using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NToastNotify;
using OfficeOpenXml;
using Shield.Common.Models.Common;
using Shield.Common.Models.Loto;
using Shield.Common.Models.Loto.Shared;
using Shield.Common.Models.MyLearning;
using Shield.Ui.App.Common;
using Shield.Ui.App.Models.CommonModels;
using Shield.Ui.App.Models.HecpModels;
using Shield.Ui.App.Models.LotoModels;
using Shield.Ui.App.Services;
using Shield.Ui.App.Translators;
using Shield.Ui.App.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Loto = Shield.Ui.App.Models.LotoModels.Loto;

namespace Shield.Ui.App.Controllers
{


    [Authorize(Policy = Constants.USER)]
    public class LotoController : Controller
    {
        private AirplaneDataService _airplaneDataService;
        private UserService _userService;
        private LotoService _lotoService;
        private SessionService _sessionService;
        private ExternalService _externalService;
        private HecpService hecpService;

        private readonly IToastNotification _toastNotification;

        public LotoController(AirplaneDataService ads,
                                UserService us,
                                LotoService lotoService,
                                IToastNotification toastNotification,
                                SessionService sessionService,
                                ExternalService externalService,
                                HecpService hecpService)
        {
            _airplaneDataService = ads;
            _userService = us;
            _lotoService = lotoService;
            _toastNotification = toastNotification;
            _sessionService = sessionService;
            _externalService = externalService;
            this.hecpService = hecpService;
        }

        [HttpGet]
        public async Task<ActionResult> ViewLotos(string program, string lineNumber, string site, SortBy sortBy = SortBy.NEEDSLOCKOUT)
        {
            User user = _sessionService.GetUserFromSession(HttpContext);

            // LN datatype changed to int to string, so comparing with 0 for backward compatibility
            if (string.IsNullOrEmpty(site) || string.IsNullOrEmpty(program)
                                           || (string.IsNullOrEmpty(lineNumber) || lineNumber == "0"))
            {
                return RedirectToAction("Index", "Home");
            }

            _sessionService.SetString(HttpContext, "selectedSite", site);
            _sessionService.SetString(HttpContext, "selectedProgram", program);
            _sessionService.SetString(HttpContext, "selectedLineNumber", lineNumber);

            var header = await GetAircraftHeaderViewModel(program, lineNumber, "ViewLotos");

            List<Loto> lotos = await _lotoService.GetLotosByLineNumberAndModel(lineNumber, program);
            LotoDashboardViewModel vm = await LotoDashboardViewModelTranslator(lotos);
            vm.Header = header;

            if (vm.ThreeMostRecentlyEditedLotoViewModels.Any(l => l.ShowAESignOutWarning)
                || vm.ListOfLotoViewModels.Any(l => l.ShowAESignOutWarning))
            {
                _toastNotification.AddWarningToastMessage("There are AEs that have not signed out in over 9 hours.");
            }

            if (header == null)
            {
                return RedirectToAction("SelectLine", "Admin");
            }

            vm.ListOfLotoViewModels = SortLotoTiles(vm.ListOfLotoViewModels, sortBy);

            vm.SessionService = _sessionService;

            return View("LotoDashboard", vm);
        }

        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> CreateLoto(CreateLotoPartialViewModel vm)
        {
            User user = _sessionService.GetUserFromSession(HttpContext);

            var newLotoResponse = await _lotoService.Create(vm.WorkPackage, vm.Reason, vm.Site, vm.Model, vm.LineNumber, user, vm.AssociatedMinorModelIdList);

            // TODO: Need to change the if condition to handle when newLotoResponse or the status is null
            if (newLotoResponse.Status.Equals("Failed"))
            {
                _toastNotification.AddErrorToastMessage(newLotoResponse.Message);
            }
            else
            {
                _toastNotification.AddSuccessToastMessage(newLotoResponse.Message);
            }

            return RedirectToAction("ViewLotos", "Loto", new { site = vm.Site, program = vm.Model, lineNumber = vm.LineNumber });
        }

        public ActionResult LotoGridPartial(string program, string lineNumber)
        {
            return PartialView("Partials/LotoGridPartial");
        }

        [HttpGet]
        [Route("[Action]/{program}")]
        public async Task<ActionResult> CreateLotoPartial(string program)
        {
            CreateLotoPartialViewModel vm = new CreateLotoPartialViewModel
            {
                AllMinorModelsForProgram = await _airplaneDataService.GetMinorModelData(program),
                Model = program
            };
            return PartialView("Partials/CreateLotoPartial", vm);
        }

        public async Task<ActionResult> LotoDetail(int id)
        {
            LotoDetailViewModel viewModel = new LotoDetailViewModel();
            Loto loto = await _lotoService.GetLotoDetail(id);
            if (loto != null)
            {
                viewModel = await ConvertToLotoViewModel(loto);
                if (viewModel.Loto.LotoAssociatedHecps.Count > 0 && viewModel.Loto.HecpIsolationTags.Count == 0
                    && viewModel.Loto.LotoAssociatedHecps.TrueForAll(x => x.LotoIsolationsDiscreteHecp.Count == 0))
                {
                    string noIsolationWarningMessage = string.Format("The Attached HECP(s) do not have any isolations for the selected {0} for this LOTO!", (viewModel.Loto.LotoAssociatedModelDataList.Count == 0 || viewModel.Loto.LotoAssociatedModelDataList.Any(x => x.MinorModelId == null)) ? "major model" : "minor model");
                    _toastNotification.AddWarningToastMessage(noIsolationWarningMessage);
                }
            }
            else
            {
                _toastNotification.AddErrorToastMessage("Error retrieving Loto details, please try again");
                return RedirectToAction("SelectLine", "Admin");
            }

            return base.View("LotoDetail", viewModel);
        }

        public async Task<LotoDetailViewModel> ConvertToLotoViewModel(Loto loto)
        {
            AircraftHeaderViewModel header = null;
            List<LotoTransaction> lotoLog = null;
            List<User> usersForLoto = new List<User>();
            User assignedPAE = null;
            User currentUser = _sessionService.GetUserFromSession(HttpContext);
            int aircraftGCBems = 0;
            header = await GetAircraftHeaderViewModel(loto.Model, loto.LineNumber, "LotoDetail", loto.Id);
            lotoLog = await _lotoService.GetLotoTransactionsByLotoId(loto.Id);

            if (header != null)
            {
                aircraftGCBems = header.Aircraft.AssignedGroupCoordinatorBems ?? 0;
            }

            // test that we call the user service for each ae
            foreach (AssignedAE ae in loto.ActiveAEs)
            {
                if (ae.AEBemsId != 0)
                {
                    usersForLoto.Add(await _userService.GetUserByBemsidAsync(ae.AEBemsId));
                }
                else
                {
                    usersForLoto.Add(new User { BemsId = 0, DisplayName = ae.FullName });
                }
            }

            if (loto.AssignedPAEBems != null)
            {
                assignedPAE = await _userService.GetUserByBemsidAsync(loto.AssignedPAEBems ?? default(int));
                if (assignedPAE.BemsId != 0)
                    usersForLoto.Add(assignedPAE);
            }

            foreach (Isolation iso in loto.Isolations)
            {
                var userId = iso.InstalledByBemsId;
                if (userId != null)
                {
                    usersForLoto.Add(await _userService.GetUserByBemsidAsync(userId.Value));
                }
            }

            foreach (LotoAssociatedHecps lotoAssociatedHecps in loto.LotoAssociatedHecps)
            {
                List<int> IsolationBemsIds = new List<int>();
                IsolationBemsIds.AddRange(
                    lotoAssociatedHecps.LotoIsolationsDiscreteHecp.Select(x => x.InstalledByBemsId.GetValueOrDefault())
                        .Distinct().ToList());
                IsolationBemsIds.AddRange(
                    lotoAssociatedHecps.LotoIsolationsDiscreteHecp
                        .Where(x => !IsolationBemsIds.Contains(x.RemovedByBemsId.GetValueOrDefault()))
                        .Select(x => x.RemovedByBemsId.GetValueOrDefault()).Distinct().ToList());
                foreach (int bemsId in IsolationBemsIds)
                {
                    usersForLoto.Add(await _userService.GetUserByBemsidAsync(bemsId));
                }
            }

            // Only get the User for Author signatures
            if (loto.Discrete != null)
            {
                foreach (var signature in loto.Discrete.Signatures)
                {
                    if (signature.Type == Models.DiscreteModels.Signature.AUTHOR)
                    {
                        signature.User = await _userService.GetUserByBemsidAsync(signature.BemsId);
                    }
                }
            }

            List<MinorModel> minorModelForProgramList = await _airplaneDataService.GetMinorModelData(loto.Model);

            string minorModelDisplayValue = string.Empty;
            if (loto.LotoAssociatedModelDataList?.Count > 0)
            {
                minorModelDisplayValue = loto.LotoAssociatedModelDataList?.Count > 1
                                             ? string.Empty
                                             : minorModelForProgramList?.FirstOrDefault(x => x.Id == loto.LotoAssociatedModelDataList?[0].MinorModelId)?.Name;
            }

            LotoDetailViewModel viewModel = new LotoDetailViewModel
            {
                Loto = LotoTranslator.ToLotoViewModel(
                                                        loto,
                                                        currentUser,
                                                        usersForLoto,
                                                        minorModelDisplayValue,
                                                        aircraftGCBems),
                Header = header,
                LotoTransactions = lotoLog,
                CurrentUser = new User { RoleId = currentUser.RoleId }
            };

            // get the HECP assigned to LOTO 
            List<int> hecpIdList = viewModel.Loto.LotoAssociatedHecps.Where(x => x.HecpTableId is not null)
                .Select(x => x.HecpTableId.GetValueOrDefault()).ToList();

            if (viewModel.Loto.HecpTableId != null)
            {
                hecpIdList.Add(viewModel.Loto.HecpTableId.GetValueOrDefault());
            }

            if (hecpIdList.Count > 0)
            {
                GetIsolationtagRequest getIsolationtagRequest = new GetIsolationtagRequest
                {
                    HecpIds = new List<int>(hecpIdList),
                    LotoId = viewModel.Loto.Id,
                    MinorModelIdList = viewModel.Loto.GetMinorModelIdList()
                };
                List<HecpIsolationTag> hecpIsolationsTagList =
                    await _lotoService.GetHecpIsolationTagsForLoto(getIsolationtagRequest);

                if (hecpIsolationsTagList?.Count > 0)
                {
                    viewModel.Loto.HecpIsolationTags.AddRange(hecpIsolationsTagList);
                }

                // Getting list of Published HecpIds which don't have Deactivation isolations in Published Deactivation Tables.
                List<int> hecpIdsInIsolationTagList = hecpIsolationsTagList.Select(x => x.HecpId).Distinct().ToList();
                List<int> hecpIdsWithoutIsolationTagList =
                    hecpIdList.Where(x => !hecpIdsInIsolationTagList.Contains(x)).ToList();
                List<DeactivationStep> hecpDeactivationStepList = new List<DeactivationStep>();

                // Getting and attaching current deactivation step isolations.
                foreach (int hecpId in hecpIdsWithoutIsolationTagList)
                {
                    hecpDeactivationStepList.AddRange(
                        await this.hecpService.GetHecpDeactivationSteps(hecpId, getIsolationtagRequest.MinorModelIdList));
                }

                List<HECPIsolation> hecpIsolations = new List<HECPIsolation>();

                // TODO:Optimize the code
                foreach (var hecpDeactivation in hecpDeactivationStepList)
                {
                    // HecpId is added for showing title in the UI
                    hecpDeactivation.HecpIsolations.ForEach(i => i.HecpId = hecpDeactivation.HecpId);
                    hecpIsolations.AddRange(hecpDeactivation.HecpIsolations);
                }

                viewModel.Loto.HecpIsolationTags.AddRange(HecpTranslator.GetHecpIsolationTag(hecpIsolations));

                // Getting unique isolation tags by grouping the existing isolation tags using parameters except HecpIsolationId.
                viewModel.Loto.HecpIsolationTags = viewModel.Loto.HecpIsolationTags
                    .GroupBy(
                        p => new
                        {
                            p.CircuitId,
                            p.CircuitLocation,
                            p.CircuitName,
                            p.CircuitPanel,
                            p.State
                        }).Select(g => g.First()).OrderBy(x => x.HecpIsolationId).ToList();

                foreach (var isolation in viewModel.Loto.HecpIsolationTags)
                {
                    if (isolation.InstalledByBemsId > 0)
                    {
                        if (usersForLoto.FirstOrDefault(user => user.BemsId == isolation.InstalledByBemsId) == null)
                        {
                            var user = await _userService.GetUserByBemsidAsync(isolation.InstalledByBemsId);
                            usersForLoto.Add(user);
                        }

                        string installedByDisplayName = String.IsNullOrEmpty(usersForLoto.Find(s => s.BemsId == isolation.InstalledByBemsId)?.DisplayName) ?
                                                        $"BEMSID: {isolation.InstalledByBemsId}" :
                                                        usersForLoto.Find(s => s.BemsId == isolation.InstalledByBemsId).DisplayName;

                        isolation.InstalledBy = installedByDisplayName;
                    }

                    if (isolation.UninstalledByBemsId > 0)
                    {
                        if (usersForLoto.FirstOrDefault(user => user.BemsId == isolation.UninstalledByBemsId) == null)
                        {
                            var user = await _userService.GetUserByBemsidAsync(isolation.UninstalledByBemsId);
                            usersForLoto.Add(user);
                        }

                        string unInstalledByDisplayName = String.IsNullOrEmpty(usersForLoto.Find(s => s.BemsId == isolation.UninstalledByBemsId)?.DisplayName) ?
                                                        $"BEMSID: {isolation.UninstalledByBemsId}" :
                                                        usersForLoto.Find(s => s.BemsId == isolation.UninstalledByBemsId).DisplayName;

                        isolation.UninstalledBy = unInstalledByDisplayName;
                    }
                }

                if (viewModel.Loto.Status.Description == Shield.Common.Constants.Status.NEEDS_LOCKOUT_DESCRIPTION)
                {
                    viewModel.Loto.ConflictIsolations = await _lotoService.GetConflictIsolation(
                                                            viewModel.Loto.Id,
                                                            viewModel.Loto.Program,
                                                            hecpIdList,
                                                            viewModel.Loto.LineNumber);
                }
            }

            // PAE assigned, current user is the line's GC, and the LOTO is Active
            if (assignedPAE != null
                && (currentUser.BemsId == aircraftGCBems || currentUser.RoleId == 7 || currentUser.RoleId == 8)
                && viewModel.Loto.Status.Description == Shield.Common.Constants.Status.ACTIVE_DESCRIPTION)
            {
                viewModel.SignInCardMessage = string.Empty;
            }

            // PAE assigned, current user is the line's GC, and the LOTO is not Active
            else if (assignedPAE != null && currentUser.BemsId == aircraftGCBems && viewModel.Loto.Status.Description
                     != Shield.Common.Constants.Status.ACTIVE_DESCRIPTION)
            {
                viewModel.SignInCardMessage = "The LOTO must be Locked Out and Active before AEs can be signed in.";
            }

            // PAE assigned but the current user is not the line's GC
            else if (assignedPAE != null)
            {
                viewModel.SignInCardMessage =
                    "Only this line's GC, System admin and Site admin can sign an AE into this LOTO.";
            }

            // No PAE and the current user is the line's GC
            else if (aircraftGCBems == currentUser.BemsId)
            {
                viewModel.SignInCardMessage = "In order to sign in an AE, a PAE must claim this LOTO.";
            }

            // No PAE and the current user is not the line's GC
            else
            {
                viewModel.SignInCardMessage =
                    "Only this line's GC, System admin and Site admin can sign an AE into this LOTO. A PAE must also claim this LOTO.";
            }

            foreach (var iso in viewModel.Loto.Isolations)
            {
                if (iso.RemovedByBems != null)
                {
                    iso.RemovedByDisplayName =
                        (await _userService.GetUserByBemsidAsync(iso.RemovedByBems.Value)).GetDisplayName();
                }
                else
                {
                    iso.RemovedByDisplayName = string.Empty;
                }
            }

            viewModel.SessionService = _sessionService;

            return viewModel;
        }

        [HttpPost]
        [Authorize(Policy = Shield.Common.Constants.Permission.CREATE_LOTO)]
        public async Task<ActionResult> AssignPAE(int lotoId,int gcBemsId, bool overrideTraining, string reasonToOverride)
        {
            ActionResult result;
            User user = _sessionService.GetUserFromSession(HttpContext);
            try
            {
                HTTPResponseWrapper<Loto> response = await _lotoService.AssignPAE(user.BemsId, lotoId, user.GetDisplayName(), overrideTraining, reasonToOverride, gcBemsId);
                var json = JsonConvert.SerializeObject(response, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    PreserveReferencesHandling = PreserveReferencesHandling.None
                });
                return Content(json, "application/json");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                result = Ok(new HTTPResponseWrapper<Loto>
                {
                    Status = Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED,
                    Reason = Shield.Common.Constants.ShieldHttpWrapper.Reason.EXCEPTION_OCCURRED,
                    Message = "Unable to sign in user as PAE.",
                    Data = new Loto()
                });
            }
            return result;
        }

        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> SaveLoto(LotoViewModel model)
        {
            if (ModelState.IsValid)
            {
                List<HecpIsolationTag> savedHecpIsolationTags = new List<HecpIsolationTag>();
                var conflictIsolationList = new List<HecpIsolationTag>();

                // Checking for conflict isolations in the attached HECPs
                List<int> savedHecpIdList = model.LotoAssociatedHecps.Where(x => x.HecpTableId is not null && x.Id != 0)
                    .Select(x => x.HecpTableId.GetValueOrDefault()).ToList();

                if (model.HecpTableId is not null)
                {
                    savedHecpIdList.Add(model.HecpTableId.GetValueOrDefault());
                }

                List<int> unSavedHecpList = model.LotoAssociatedHecps.Where(x => x.HecpTableId is not null && x.Id == 0)
                    .Select(x => x.HecpTableId.GetValueOrDefault()).ToList();
                List<int?> MinorModelList = model.MinorModelIdList != null ?
                                            model.MinorModelIdList.Split(",").Select(int.Parse).ToList().Cast<int?>().ToList()
                                            : Enumerable.Empty<int?>().ToList();
                if (savedHecpIdList.Count > 0)
                {
                    savedHecpIsolationTags = await GetIsolationsTagsByHecpIds(
                                                 savedHecpIdList,
                                                 model.Id,
                                                 MinorModelList
                                                 );
                }

                List<HecpIsolationTag> hecpIsolationTagsToBeSaved =
                    await GetIsolationsTagsByHecpIds(unSavedHecpList, model.Id, MinorModelList);
                foreach (var iso in hecpIsolationTagsToBeSaved)
                {
                    if (!string.IsNullOrEmpty(iso.CircuitId))
                    {
                        conflictIsolationList.AddRange(
                            savedHecpIsolationTags.Where(
                                    l => ((string.IsNullOrEmpty(l.CircuitId)
                                               ? string.Empty
                                               : l.CircuitId.Trim().ToUpper())
                                          == iso.CircuitId.Trim().ToUpper()) && l.State != iso.State
                                                                             && l.CircuitLocation == iso.CircuitLocation
                                                                             && l.CircuitPanel == iso.CircuitPanel)
                                .ToList());

                        conflictIsolationList.AddRange(
                            hecpIsolationTagsToBeSaved.Where(
                                l => l.HecpId != iso.HecpId
                                     && ((string.IsNullOrEmpty(l.CircuitId)
                                              ? string.Empty
                                              : l.CircuitId.Trim().ToUpper()) == iso.CircuitId.Trim().ToUpper())
                                     && l.State != iso.State && l.CircuitLocation == iso.CircuitLocation
                                     && l.CircuitPanel == iso.CircuitPanel).ToList());
                    }
                    else if (!string.IsNullOrEmpty(iso.CircuitName))
                    {
                        conflictIsolationList.AddRange(
                            savedHecpIsolationTags.Where(
                                l => ((string.IsNullOrEmpty(l.CircuitName)
                                           ? string.Empty
                                           : l.CircuitName.Trim().ToUpper()) == iso.CircuitName.Trim().ToUpper())
                                     && l.State != iso.State && l.CircuitLocation == iso.CircuitLocation
                                     && l.CircuitPanel == iso.CircuitPanel).ToList());

                        conflictIsolationList.AddRange(
                            hecpIsolationTagsToBeSaved.Where(
                                l => l.HecpId != iso.HecpId
                                     && ((string.IsNullOrEmpty(l.CircuitName)
                                              ? string.Empty
                                              : l.CircuitName.Trim().ToUpper()) == iso.CircuitName.Trim().ToUpper())
                                     && l.State != iso.State && l.CircuitLocation == iso.CircuitLocation
                                     && l.CircuitPanel == iso.CircuitPanel).ToList());
                    }
                }

                if (conflictIsolationList.Count == 0)
                {
                    // Saving LOTO
                    var response = await _lotoService.Update(LotoTranslator.ToModel(model));

                    if (response.Status == Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED)
                    {
                        model.HasValidJobInfoData = LotoTranslator.HasValidJobInfoData(model);
                        return PartialView("Partials/LotoJobInfoPartial", model);
                    }
                    else
                    {
                        return new ObjectResult(Shield.Common.Constants.ShieldHttpWrapper.Status.SUCCESS)
                        {
                            StatusCode = 200
                        }; // Success
                    }
                }
                else
                {
                    List<ConflictHecpIsolationsViewModel> conflictedHecpisolations =
                        new List<ConflictHecpIsolationsViewModel>();
                    foreach (HecpIsolationTag hecpIsolationTag in conflictIsolationList)
                    {
                        conflictedHecpisolations.Add(
                            new ConflictHecpIsolationsViewModel
                            {
                                CircuitId = hecpIsolationTag.CircuitId,
                                CircuitLocation = hecpIsolationTag.CircuitLocation,
                                CircuitName = hecpIsolationTag.CircuitName,
                                CircuitPanel = hecpIsolationTag.CircuitPanel,
                                ConflictHecpName =
                                        model.LotoAssociatedHecps.Where(x => x.HecpTableId == hecpIsolationTag.HecpId)
                                            .Select(x => x.HECPTitle).FirstOrDefault(),
                                IsLocked = hecpIsolationTag.IsLocked,
                                LotoId = hecpIsolationTag.LotoId,
                                State = hecpIsolationTag.State,
                            });
                    }

                    return PartialView("Partials/ConflictIsolationsPartial", conflictedHecpisolations);
                }
            }
            else
            {
                model.HasValidJobInfoData = LotoTranslator.HasValidJobInfoData(model);
                return PartialView("Partials/LotoJobInfoPartial", model);
            }
        }

        private async Task<List<HecpIsolationTag>> GetIsolationsTagsByHecpIds(List<int> hecpIdList, int lotoId, List<int?> minorModelIdList)
        {
            List<HecpIsolationTag> hecpIsolationTags = new List<HecpIsolationTag>();

            if (hecpIdList != null && hecpIdList.Count > 0)
            {
                GetIsolationtagRequest getIsolationtagRequest = new GetIsolationtagRequest
                {
                    HecpIds = new List<int>(hecpIdList),
                    LotoId = lotoId,
                    MinorModelIdList = minorModelIdList
                };
                List<HecpIsolationTag> hecpIsolationsTagList = await _lotoService.GetHecpIsolationTagsForLoto(getIsolationtagRequest);

                if (hecpIsolationsTagList != null && hecpIsolationsTagList.Count > 0)
                {
                    hecpIsolationTags.AddRange(hecpIsolationsTagList);
                }

                // Getting list of Published HecpIds which don't have Deactivation isolations in Published Deactivation Tables.
                List<int> hecpidsInIsolationtaglist = hecpIsolationsTagList.Select(x => x.HecpId).Distinct().ToList();
                List<int> hecpIdsWithoutIsolationTags = hecpIdList.Where(x => !hecpidsInIsolationtaglist.Contains(x)).ToList();
                List<DeactivationStep> hecpDeactivations = new List<DeactivationStep>();

                // Getting and attaching current deactivation step isolations.
                foreach (int hecpId in hecpIdsWithoutIsolationTags)
                {
                    hecpDeactivations.AddRange(await this.hecpService.GetHecpDeactivationSteps(hecpId, getIsolationtagRequest.MinorModelIdList));
                }

                List<HECPIsolation> hecpIsolations = new List<HECPIsolation>();

                foreach (var hecpDeactivation in hecpDeactivations)
                {
                    // HecpId is added for showing title in the UI
                    hecpDeactivation.HecpIsolations.ForEach(i => i.HecpId = hecpDeactivation.HecpId);
                    hecpIsolations.AddRange(hecpDeactivation.HecpIsolations);
                }

                hecpIsolationTags.AddRange(HecpTranslator.GetHecpIsolationTag(hecpIsolations));
            }

            return hecpIsolationTags;
        }

        [HttpGet]

        // [Route("DeleteLoto/{lotoId}")]
        public async Task<string> DeleteLoto(int lotoId)
        {
            User currentUser = _sessionService.GetUserFromSession(HttpContext);

            if (currentUser.Role.Name == "Group Coordinator" || currentUser.Role.Name == "System Admin"
                                                             || currentUser.Role.Name == "Site Admin")
            {
                var response = await _lotoService.DeleteLotoById(lotoId);

                if (response != null)
                {
                    return JsonConvert.SerializeObject(response);
                }
                else
                {
                    _toastNotification.AddErrorToastMessage("Error deleting the Loto");
                    return JsonConvert.SerializeObject(
                        new ObjectResult("Failed to delete isolation.") { StatusCode = 500 });
                }
            }

            _toastNotification.AddErrorToastMessage(
                "Error deleting the Loto, User does not have permission to delete loto");

            return JsonConvert.SerializeObject(new ObjectResult("Failed to delete isolation.") { StatusCode = 500 });
        }

        [HttpGet]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<string> DeleteIsolation(int isolationId, int lotoId)
        {
            var response = await _lotoService.DeleteIsolation(isolationId, lotoId);

            if (response != null)
            {
                return JsonConvert.SerializeObject(response);
            }
            else
            {
                return JsonConvert.SerializeObject(new ObjectResult("Failed to delete isolation.") { StatusCode = 500 });
            }
        }

        [HttpPost]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public ActionResult AddNewIsolationRow([FromBody] List<LotoAssociatedHecps> lotoAssociatedHecps, int count)
        {
            ViewData["IsCurrentUserTheAssignedPAE"] = true;
            ViewData["count"] = count;
            return PartialView("Partials/InstallIsolationFormPartial", lotoAssociatedHecps);
        }

        [HttpGet]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public ActionResult AddNewDiscreteHecpRow(int count)
        {
            ViewData["IsCurrentUserTheAssignedPAE"] = true;

            return PartialView("Partials/DiscreteHecpRowPartial", count);
        }

        [HttpPost]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> InstallIsolation([FromBody] InstallIsolationRequestWithId request)
        {
            ActionResult result;
            try
            {
                User user = await _userService.GetUserByBemsidAsync(request.InstalledByBemsId);
                var lotoServiceResponse = await _lotoService.InstallIsolation(request, user);
                if (lotoServiceResponse.Status.Equals(Shield.Common.Constants.ShieldHttpWrapper.Status.SUCCESS))
                {
                    result = new OkObjectResult(lotoServiceResponse);
                }
                else
                {
                    result = new BadRequestObjectResult(lotoServiceResponse.Message);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                result = new BadRequestObjectResult("Unable to install isolation with System/Circuit ID " + request.SystemCircuitId);
            }

            return result;
        }

        [HttpPut]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> InstallDiscreteIsolation([FromBody] InstallIsolationRequest request)
        {
            try
            {
                User user = await _userService.GetUserByBemsidAsync(request.InstalledByBemsId);

                Isolation result = await _lotoService.InstallDiscreteIsolation(request, user);

                IsolationViewModel vm = IsolationTranslator.ToViewModel(result, user);
                vm.IsEditable = true;

                if (result == null)
                {
                    throw new Exception("Discrete isolation was null");
                }

                return PartialView("Partials/DiscreteIsolationRowPartial", vm);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return null;
            }
        }

        [HttpPut]
        [Authorize(Policy = Shield.Common.Constants.Permission.CREATE_LOTO)]
        public async Task<HTTPResponseWrapper<Isolation>> UnlockIsolation(int isolationId)
        {
            try
            {
                return await _lotoService.UnlockIsolation(isolationId);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);

                return new HTTPResponseWrapper<Isolation>
                {
                    Status = Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED,
                    Message = "Failed to unlock isolation.",
                    Reason = e.Message
                };
            }
        }

        [HttpPost]
        [Authorize(Policy = Shield.Common.Constants.Permission.CREATE_LOTO)]
        public ActionResult DiscreteUnlockedIsolationRowPartial([FromBody] Isolation isolation)
        {
            IsolationViewModel vm = IsolationTranslator.ToViewModel(isolation, new User());
            vm.IsEditable = true;

            return PartialView("Partials/DiscreteUnlockedIsolationRowPartial", vm);
        }

        [HttpPost]
        public async Task<ActionResult> GetTrainingStatus(int id, int bemsId,string message,[FromBody] List<MyLearningDataResponse> userTrainingData)
        {
            LotoAEManagementViewModel vm = new LotoAEManagementViewModel();

            vm.UserTrainingData = userTrainingData;
            User user = await _userService.GetUserByBemsidAsync(bemsId);
            vm.AEName = user.DisplayName;
            vm.LotoId = id;
            vm.Message = message;

            return PartialView("Partials/LotoOverridePartial", vm);
        }

        [HttpPost]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> AssignAE(int lotoId, string bemsOrBadge, bool overrideTraining, string reasonToOverride)
        {
            ActionResult response;

            try
            {
                HTTPResponseWrapper<User> user = await GetUserFromBemsOrBadge(bemsOrBadge);

                if (user.Data.BemsId != 0)
                {
                    User currentUser = _sessionService.GetUserFromSession(HttpContext);
                    if (string.IsNullOrWhiteSpace(reasonToOverride))
                    {
                        reasonToOverride = string.Empty;
                    }

                    AssignedAE aeToAssign = new AssignedAE
                    {
                        AEName = user.Data.GetDisplayName(),
                        AEBemsId = user.Data.BemsId,
                        LotoId = lotoId
                    };

                    var lotoDetail = await _lotoService.GetLotoDetail(lotoId);
                    bool isAlreadySignedIn = lotoDetail?.ActiveAEs != null && lotoDetail.ActiveAEs.Any(ae => ae.AEBemsId == user.Data.BemsId);
                    if(isAlreadySignedIn)
                    {
                        return Ok(new HTTPResponseWrapper<AssignedAE>
                        {
                            Status = Shield.Common.Constants.ShieldHttpWrapper.Status.NOT_MODIFIED,
                            Reason = Shield.Common.Constants.ShieldHttpWrapper.Reason.ALREADY_EXISTS,
                            Data = aeToAssign
                        });
                    }

                    HTTPResponseWrapper<AssignedAE> result = await _lotoService.AssignAE(aeToAssign, overrideTraining, currentUser.DisplayName, currentUser.BemsId, reasonToOverride);
                    string json = JsonConvert.SerializeObject(result, new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        PreserveReferencesHandling = PreserveReferencesHandling.None
                    });
                    return Content(json, "application/json");
                }
                else
                {
                    response = Ok(new HTTPResponseWrapper<AssignedAE>
                    {
                        Status = Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED,
                        Reason = Shield.Common.Constants.ShieldHttpWrapper.Reason.INVALID_USER,
                        Message = user.Message,
                        Data = new AssignedAE()
                    });
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                response = Ok(new HTTPResponseWrapper<AssignedAE>
                {
                    Status = Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED,
                    Reason = Shield.Common.Constants.ShieldHttpWrapper.Reason.EXCEPTION_OCCURRED,
                    Message = "Unable to sign in user as AE.",
                    Data = new AssignedAE()
                });
            }

            return response;
        }

        [HttpPost]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> AssignVisitor(int lotoId, string visitorName, bool overrideTraining)
        {
            ActionResult response;

            try
            {
                User currentUser = _sessionService.GetUserFromSession(HttpContext);

                AssignedAE aeToAssign = new AssignedAE
                {
                    AEName = visitorName,
                    AEBemsId = 0,
                    LotoId = lotoId
                };

                var result = await _lotoService.AssignVisitor(aeToAssign, true, currentUser.DisplayName, currentUser.BemsId);

                return Ok(result);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                response = Ok(new HTTPResponseWrapper<AssignedAE>
                {
                    Status = Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED,
                    Reason = Shield.Common.Constants.ShieldHttpWrapper.Reason.EXCEPTION_OCCURRED,
                    Message = "Unable to sign in Visitor.",
                    Data = null
                });
            }

            return response;
        }

        [HttpPost]
        public async Task<ActionResult> OverrideTrainingPartial(int id, int bemsId, string message, string aeName, bool isTrainingDown)
        {
            User user = await _userService.GetUserByBemsidAsync(bemsId);
            return PartialView("Partials/OverrideTrainingPartial", new LotoAEManagementViewModel
            {
                LotoId=id,
                BemsId = bemsId,
                Message = message,
                AEName = user.DisplayName,
                IsTrainingDown = isTrainingDown
            });
        }

        [HttpGet]
        public async Task<ActionResult> GetTransactionLog(int lotoId)
        {
            var transactions = await _lotoService.GetLotoTransactionsByLotoId(lotoId);

            return PartialView("Partials/LotoHistoryTable", transactions);
        }

        private async Task<LotoDashboardViewModel> LotoDashboardViewModelTranslator(List<Loto> lotos)
        {

            List<Loto> sortedLotos = lotos.OrderByDescending(d => d.DateUpdated).ToList();

            List<LotoTileViewModel> recentlyUpdated = await LoadLotoViewModels(sortedLotos.Take(3).ToList());
            List<LotoTileViewModel> restOfLotos = await LoadLotoViewModels(sortedLotos.Skip(3).ToList());
            restOfLotos = SortLotos(restOfLotos);

            LotoDashboardViewModel vm = new LotoDashboardViewModel
            {
                ThreeMostRecentlyEditedLotoViewModels = recentlyUpdated,
                ListOfLotoViewModels = restOfLotos
            };

            return vm;
        }

        private async Task<List<LotoTileViewModel>> LoadLotoViewModels(List<Loto> lotos)
        {
            List<LotoTileViewModel> lotoViewModels = new List<LotoTileViewModel>();

            // Get all users and then filter in memory.This is faster than calling the service every time
            List<User> allUsers = await _userService.GetUsersAsync();
            List<MinorModel> minorModelForProgramList =
                lotos.Count > 0
                    ? await _airplaneDataService.GetMinorModelData(lotos.FirstOrDefault().Model)
                    : Enumerable.Empty<MinorModel>().ToList();

            foreach (Loto loto in lotos)
            {
                string minorModelDisplayValue = string.Empty;

                if (loto.LotoAssociatedModelDataList?.Count > 0)
                {
                    minorModelDisplayValue = loto.LotoAssociatedModelDataList?.Count > 1
                                                 ? string.Empty
                                                 : minorModelForProgramList?.FirstOrDefault(x => x.Id == loto.LotoAssociatedModelDataList?[0].MinorModelId)?.Name;
                }

                User assignedPAE = allUsers.Find(
                    x => x.BemsId == (loto.AssignedPAEBems ?? default(int)));
                lotoViewModels.Add(LotoTileTranslator.ToViewModel(loto, assignedPAE, minorModelDisplayValue));
            }

            return lotoViewModels;
        }

        private List<LotoTileViewModel> SortLotos(List<LotoTileViewModel> lotos)
        {
            List<LotoTileViewModel> allLotos = new List<LotoTileViewModel>();

            // Ensure order: status(1.Needs Lockout, 2.Active, 3.Transfer, 4.Complete)
            // Needs Lockout
            allLotos.AddRange(
                lotos.Where(loto => loto.Status.Description.Equals("NeedsLockout")).ToList()
                    .OrderByDescending(d => d.DateUpdated));

            // Active
            allLotos.AddRange(
                lotos.Where(loto => loto.Status.Description.Equals("Active")).ToList()
                    .OrderByDescending(d => d.DateUpdated));

            // Transfer
            allLotos.AddRange(
                lotos.Where(loto => loto.Status.Description.Equals("Transfer")).ToList()
                    .OrderByDescending(d => d.DateUpdated));

            // Complete
            allLotos.AddRange(
                lotos.Where(loto => loto.Status.Description.Equals("Completed")).ToList()
                    .OrderByDescending(d => d.DateUpdated));

            return allLotos;
        }

        public async Task<ActionResult> SignOutAE(int lotoId, string bemsOrBadge, int idToDelete, string aeName)
        {
            HTTPResponseWrapper<User> user = await GetUserFromBemsOrBadge(bemsOrBadge);
            ObjectResult returnResult = BadRequest("Failed to sign out AE.");

            if (bemsOrBadge != "0")
            {
                if (user != null && user.Data.BemsId != 0)
                {
                    User currentUser = _sessionService.GetUserFromSession(HttpContext);

                    if (currentUser == null || currentUser.BemsId == 0)
                    {
                        return new BadRequestObjectResult("This session has timed out. Please refresh the page and try again.");
                    }

                    var result = await _lotoService.SignOutAE(lotoId, user.Data.BemsId, user.Data.DisplayName, currentUser.BemsId, currentUser.GetDisplayName());
                    if (result)
                    {
                        returnResult = Ok($"Signed out AE {user.Data.DisplayName}.");
                    }
                }
                else if (user.Data.BemsId == 0)
                {
                    return new BadRequestObjectResult(user.Message);
                }
            }
            else
            {
                User currentUser = _sessionService.GetUserFromSession(HttpContext);

                if (currentUser == null || currentUser.BemsId == 0)
                {
                    return new BadRequestObjectResult("This session has timed out. Please refresh the page and try again.");
                }

                var result = await _lotoService.SignOutVisitor(lotoId, idToDelete, aeName, currentUser.BemsId, currentUser.GetDisplayName());
                if (result)
                {
                    returnResult = Ok("Signed out Non Boeing AE.");
                }
            }


            return returnResult;
        }

        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> UninstallIsolationsAndCompleteLoto(int lotoId, string site)
        {
            User currentUser = _sessionService.GetUserFromSession(HttpContext);
            var result = await _lotoService.UninstallIsolationsAndCompleteLoto(lotoId, currentUser.BemsId, currentUser.GetDisplayName());

            if (result != null && result.Status.Description == "Completed")
            {
                _toastNotification.AddSuccessToastMessage("Completed LOTO and signed-out PAE.");
                return RedirectToAction("ViewLotos", new { program = result.Model, lineNumber = result.LineNumber, site = site });
            }
            else
            {
                _toastNotification.AddErrorToastMessage("Failed to Complete LOTO");
                return RedirectToAction("LotoDetail", new { id = lotoId });
            }

        }

        public async Task<ActionResult> GetAllInProgressLotosWithIsolations(string program, string lineNumber)
        {
            List<Loto> lotos = await _lotoService.GetAllInProgressLotosWithIsolations(program, lineNumber);
            List<DiscreteIsolationViewModel> discreteIsolationList = new List<DiscreteIsolationViewModel>();
            foreach (Loto loto in lotos)
            {
                if (loto.Isolations.Count > 0)
                {
                    foreach (Isolation iso in loto.Isolations)
                    {
                        discreteIsolationList.Add(new DiscreteIsolationViewModel
                        {
                            DiscreteTitle = loto.HECPTitle,
                            LotoTitle = loto.WorkPackage,
                            LotoIsolationsDiscreteHecp = new LotoIsolationsDiscreteHecp
                            {
                                SystemCircuitId = iso.SystemCircuitId,
                                CircuitNomenclature = iso.CircuitNomenclature,
                                Tag = iso.Tag
                            }
                        });
                    }
                }

                if (loto.LotoAssociatedHecps.Any(x => x.HecpTableId is null))
                {
                    List<LotoIsolationsDiscreteHecp> lotoIsolationsDiscreteHecps = loto.LotoAssociatedHecps.SelectMany(x => x.LotoIsolationsDiscreteHecp).ToList();
                    foreach (LotoIsolationsDiscreteHecp iso in lotoIsolationsDiscreteHecps)
                    {
                        discreteIsolationList.Add(new DiscreteIsolationViewModel
                        {
                            DiscreteTitle = loto.LotoAssociatedHecps.Where(x => x.Id == iso.LotoAssociatedId).Select(y => y.HECPTitle).FirstOrDefault(),
                            LotoTitle = loto.WorkPackage,
                            LotoIsolationsDiscreteHecp = new LotoIsolationsDiscreteHecp
                            {
                                SystemCircuitId = iso.SystemCircuitId,
                                CircuitNomenclature = iso.CircuitNomenclature,
                                Tag = iso.Tag
                            }
                        });
                    }
                }
            }

            Dictionary<int, string> hecpTitleIsolationTag = GetHecpTitleForIsolationTags(lotos);
            Dictionary<int, string> lotoTitleIsolationTag = GetLotoTitleForIsolationTags(lotos);

            ActiveIsolationViewModel activeIsolationViewModel = new ActiveIsolationViewModel
            {
                HecpIsolationTags = lotos.Where(x => x.HecpIsolationTag != null).SelectMany(x => x.HecpIsolationTag).OrderBy(x => x.HecpId).ToList(),
                DiscreteIsolations = discreteIsolationList.OrderBy(x => x.DiscreteTitle).ToList(),
                LineNumber = lineNumber,
                Model = program,
                HecpTitleIsolationTag = hecpTitleIsolationTag,
                LotoTitleIsolationTag = lotoTitleIsolationTag
            };

            return View("ActiveIsolationView", activeIsolationViewModel);
        }

        public async Task<List<ActiveIsolationTagViewModel>> FilterIsolations(string program, string lineNumber, string id = null, string name = null, string panel = null, string location = null)
        {
            List<Loto> lotos = await _lotoService.GetAllInProgressLotosWithIsolations(program, lineNumber);

            // Filter for isolations in view isolations screen
            lotos = lotos.Where(lot => lot.HecpIsolationTag != null).ToList();

            if (name != null)
            {
                lotos.ForEach(lot =>
                {
                    lot.HecpIsolationTag = lot.HecpIsolationTag.Where(hit => hit.CircuitName != null && hit.CircuitName.ToUpper().Contains(name.ToUpper())).ToList();
                });
            }

            if (panel != null)
            {
                lotos.ForEach(lot =>
                {
                    lot.HecpIsolationTag = lot.HecpIsolationTag.Where(hit => hit.CircuitPanel != null && hit.CircuitPanel.ToUpper().Contains(panel.ToUpper())).ToList();
                });
            }

            if (location != null)
            {
                lotos.ForEach(lot =>
                {
                    lot.HecpIsolationTag = lot.HecpIsolationTag.Where(hit => hit.CircuitLocation != null && hit.CircuitLocation.ToUpper().Contains(location.ToUpper())).ToList();
                });
            }

            if (id != null)
            {
                lotos.ForEach(lot =>
                {
                    lot.HecpIsolationTag = lot.HecpIsolationTag.Where(hit => hit.CircuitId != null && hit.CircuitId.ToUpper().Contains(id.ToUpper())).ToList();
                });
            }

            Dictionary<int, string> hecpTitleIsolationTag = GetHecpTitleForIsolationTags(lotos);

            List<HecpIsolationTag> hecpIsolationTags = lotos.Where(x => x.HecpIsolationTag != null).SelectMany(x => x.HecpIsolationTag).ToList();

            List<ActiveIsolationTagViewModel> activeIsolationTags = new List<ActiveIsolationTagViewModel>();
            hecpIsolationTags.ForEach(x => activeIsolationTags.Add(
                new ActiveIsolationTagViewModel
                {
                    CircuitId = x.CircuitId,
                    CircuitLocation = x.CircuitLocation,
                    CircuitName = x.CircuitName,
                    CircuitPanel = x.CircuitPanel,
                    State = x.State,
                    Tag = x.Tag,
                    HecpTitle = hecpTitleIsolationTag.FirstOrDefault(y => y.Key == x.HecpId).Value,
                }));

            return activeIsolationTags;
        }

        private static Dictionary<int, string> GetHecpTitleForIsolationTags(List<Loto> lotos)
        {
            Dictionary<int, string> hecpTitleIsolationTag = new Dictionary<int, string>();

            foreach (Loto loto in lotos)
            {
                if (loto.HecpIsolationTag != null && loto.HecpIsolationTag.Count > 0)
                {
                    if (loto.HecpTableId != null)
                    {
                        hecpTitleIsolationTag.TryAdd(loto.HecpTableId.GetValueOrDefault(), loto.HECPTitle);
                    }
                    else if (loto.LotoAssociatedHecps.Count > 0)
                    {
                        List<LotoAssociatedHecps> nonDiscreteLotoAssociatedHecps = loto.LotoAssociatedHecps.Where(x => x.HecpTableId != null).ToList();
                        nonDiscreteLotoAssociatedHecps.ForEach(x => hecpTitleIsolationTag.TryAdd(x.HecpTableId.GetValueOrDefault(), x.HECPTitle));
                    }
                }

            }

            return hecpTitleIsolationTag;
        }

        private static Dictionary<int, string> GetLotoTitleForIsolationTags(List<Loto> lotos)
        {
            Dictionary<int, string> lotoTitleIsolationTag = new Dictionary<int, string>();

            foreach (Loto loto in lotos)
            {
                if (loto.HecpIsolationTag != null && loto.HecpIsolationTag.Count > 0)
                {
                    lotoTitleIsolationTag.TryAdd(loto.Id, loto.WorkPackage);
                }
            }

            return lotoTitleIsolationTag;
        }

        [HttpGet]
        public async Task<ActionResult> ExportIsolationsToExcel(string program, string lineNumber, string id = null, string name = null, string panel = null, string location = null)
        {
            List<ActiveIsolationTagViewModel> activeIsolationTags = new List<ActiveIsolationTagViewModel>();

            try
            {
                activeIsolationTags = await FilterIsolations(program, lineNumber, id, name, panel, location);

                MemoryStream memStream = GetExcelFile(activeIsolationTags);

                return File(memStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ActiveIsolationsList" + DateTime.Now.ToString("MMddyyyyhhmm", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }

            return new ObjectResult("Unable to export Excel file") { StatusCode = 500 };
        }

        private MemoryStream GetExcelFile(List<ActiveIsolationTagViewModel> activeIsolationTags)
        {
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("ActiveIsolations");
                var range = worksheet.Cells[1, 1, 1, 7];
                range.Style.Font.Bold = true;
                worksheet.Cells[1, 1].Value = "Hecp Title";
                worksheet.Cells[1, 2].Value = "ID";
                worksheet.Cells[1, 3].Value = "Name";
                worksheet.Cells[1, 4].Value = "Panel/Tool";
                worksheet.Cells[1, 5].Value = "Location";
                worksheet.Cells[1, 6].Value = "Tag";
                worksheet.Cells[1, 7].Value = "Set to";

                int rowCount = 2;
                foreach (var activeIsolationTag in activeIsolationTags)
                {
                    worksheet.Cells[rowCount, 1].Value = activeIsolationTag.HecpTitle;
                    worksheet.Cells[rowCount, 2].Value = activeIsolationTag.CircuitId;
                    worksheet.Cells[rowCount, 3].Value = activeIsolationTag.CircuitName;
                    worksheet.Cells[rowCount, 4].Value = activeIsolationTag.CircuitPanel;
                    worksheet.Cells[rowCount, 5].Value = activeIsolationTag.CircuitLocation;
                    worksheet.Cells[rowCount, 6].Value = activeIsolationTag.Tag;
                    worksheet.Cells[rowCount, 7].Value = activeIsolationTag.State;
                    rowCount++;
                }

                return new MemoryStream(package.GetAsByteArray());
            }
        }

        public async Task<ActionResult> GetPublishedHecpsForLoto(string site, string program, string title, string ata, string minorModelIdList, string hecpIdList, int pageNumber = 1, bool? isEngineered = null)
        {
            try
            {
                List<int> minorModels = JsonConvert.DeserializeObject<List<int>>(minorModelIdList) ?? Enumerable.Empty<int>().ToList();
                List<int> existingHecpIds = string.IsNullOrEmpty(hecpIdList) ? new List<int>() : hecpIdList.Split(',').Select(int.Parse).ToList();

                PagingWrapper<Hecp> hecpList = await _lotoService.GetPublishedHecpsForLoto(site, program, title, ata, minorModels, pageNumber, isEngineered);
                hecpList.Data = hecpList.Data.Where(h => !existingHecpIds.Contains(h.Id)).ToList();

                hecpList.Data.ForEach(h =>
                {
                    h.Ata = (string.Concat(h.HecpATAChapters.Select(a => a.HecpATAChapterMaster.HecpAtaNumber + ", "))).TrimEnd().TrimEnd(',');
                });

                return PartialView("Partials/LotoJobInfoHecpPartial", hecpList);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

            return null;
        }

        private async Task<AircraftHeaderViewModel> GetAircraftHeaderViewModel(string program, string lineNumber, string action, int lotoId = 0)
        {
            int currentUserBemsId = _sessionService.GetUserFromSession(HttpContext).BemsId;
            ViewBag.CurrentUserBems = currentUserBemsId;

            // Get Aircraft Info and set to VM
            Aircraft aircraft = await _airplaneDataService.GetAirplaneByModelLineNumberAsync(program, lineNumber);

            if (aircraft == null)
            {
                return null;
            }

            var assignedGC = new User();
            if (aircraft.AssignedGroupCoordinatorBems != 0 && aircraft.AssignedGroupCoordinatorBems != null)
            {
                // Get Assigned GC Profile and set to VM
                assignedGC = await _userService.GetUserByBemsidAsync((int)aircraft.AssignedGroupCoordinatorBems);
            }

            // Create VM
            AircraftHeaderViewModel model = new AircraftHeaderViewModel { Aircraft = aircraft, GroupCoordinator = assignedGC, controller = "Loto", action = "ViewLotos", lotoId = lotoId };
            model.IsCurrentUserGC = _sessionService.GetIsCurrentUserGC(this.HttpContext);
            if (model.IsCurrentUserGC)
            {
                model.IsCurrentUserTheGC = model.GroupCoordinator.BemsId == _sessionService.GetUserFromSession(this.HttpContext).BemsId;
                model.GCAssignedAircraft = await _airplaneDataService.GetListOfAircraftByGCBems(model.GroupCoordinator.BemsId);
            }

            return model;
        }

        #region status change

        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> Lockout(int lotoId, string program, string lineNumber, string site)
        {
            User currentUser = _sessionService.GetUserFromSession(HttpContext);
            StatusChangeRequest changeRequest = new StatusChangeRequest
            {
                Id = lotoId,
                BemsId = currentUser.BemsId,
                DisplayName = currentUser.GetDisplayName()
            };
            bool result = await _lotoService.Lockout(changeRequest);

            if (result)
            {
                _toastNotification.AddSuccessToastMessage("Set LOTO to Active.");

                return RedirectToAction("ViewLotos", new { program = program, lineNumber = lineNumber, site = site });
            }
            else
            {
                _toastNotification.AddErrorToastMessage("Loto failed to Lockout");
            }

            return RedirectToAction("LotoDetail", new { id = lotoId });

        }

        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<ActionResult> Transfer(int lotoId, string program, string lineNumber, string site, string comment)
        {
            User currentUser = _sessionService.GetUserFromSession(HttpContext);
            StatusChangeRequest changeRequest = new StatusChangeRequest
            {
                Id = lotoId,
                BemsId = currentUser.BemsId,
                DisplayName = currentUser.GetDisplayName(),
                Comment = comment
            };
            bool result = await _lotoService.Transfer(changeRequest);
            if (result)
            {
                _toastNotification.AddSuccessToastMessage("Transferred LOTO and signed-out PAE.");
            }
            else
            {
                _toastNotification.AddErrorToastMessage("Failed to Transfer Loto");
            }

            return RedirectToAction("ViewLotos", "Loto", new { program = program, lineNumber = lineNumber, site = site });
        }

        [HttpGet]
        [Authorize(Policy = Constants.USER)]
        public async Task<int> GetLotoCount(string lineNumber, string program)
        {
            List<Loto> lotos = await _lotoService.GetLotosByLineNumberAndModel(lineNumber, program);
            lotos = lotos.Where(x => !x.Status.Description.ToLower().Equals("completed")).ToList();
            return lotos.Count;
        }

        #endregion status change

        private async Task<HTTPResponseWrapper<User>> GetUserFromBemsOrBadge(string bemsOrBadge)
        {
            HTTPResponseWrapper<User> responseWrapper = new HTTPResponseWrapper<User>();
            if (Helpers.IsBadgeNumber(bemsOrBadge))
            {
                bemsOrBadge = Helpers.ParseBadgeNumber(bemsOrBadge);
            }

            HTTPResponseWrapper<int> bemsWrapper = await _externalService.GetBemsIdFromBemsIdOrBadgeNumber(bemsOrBadge);
            int userBems = bemsWrapper.Data;
            if (userBems == 0)
            {
                responseWrapper.Message = bemsWrapper.Message;
                responseWrapper.Data = new User
                {
                    BemsId = 0
                };
            }
            else
            {
                responseWrapper.Message = bemsWrapper.Message;
                responseWrapper.Data = await _userService.GetUserByBemsidAsync(userBems);
            }

            return responseWrapper;
        }

        private List<LotoTileViewModel> SortLotoTiles(List<LotoTileViewModel> lotoTiles, SortBy sortBy)
        {
            List<LotoTileViewModel> sortedLotoTiles = new List<LotoTileViewModel>();

            if (sortBy == SortBy.NEEDSLOCKOUT)
            {
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.NEEDS_LOCKOUT_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.ACTIVE_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.TRANSFER_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.COMPLETED_DESCRIPTION.Equals(disc.Status.Description)).ToList());
            }
            else if (sortBy == SortBy.COMPLETED)
            {
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.COMPLETED_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.ACTIVE_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.TRANSFER_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.NEEDS_LOCKOUT_DESCRIPTION.Equals(disc.Status.Description)).ToList());
            }
            else if (sortBy == SortBy.ACTIVE)
            {
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.ACTIVE_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.TRANSFER_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.NEEDS_LOCKOUT_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.COMPLETED_DESCRIPTION.Equals(disc.Status.Description)).ToList());
            }
            else if (sortBy == SortBy.TRANSFERRED)
            {
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.TRANSFER_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.ACTIVE_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.NEEDS_LOCKOUT_DESCRIPTION.Equals(disc.Status.Description)).ToList());
                sortedLotoTiles.AddRange(lotoTiles.Where(disc => Shield.Common.Constants.Status.COMPLETED_DESCRIPTION.Equals(disc.Status.Description)).ToList());
            }

            return sortedLotoTiles;
        }

        [HttpGet]
        [Authorize(Policy = Constants.AUTHENTICATED_USER)]
        public async Task<HTTPResponseWrapper<bool>> IsHecpDeletable(int hecpId)
        {
            try
            {
                return await _lotoService.IsHecpDeletable(hecpId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new HTTPResponseWrapper<bool>
                {
                    Data = false,
                    Message = "Failed to check if the HECP is deletable.",
                    Status = Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED
                };
            }
        }

        [HttpPost]
        public async Task<IActionResult> RenderSelectedHecps([FromBody] LotoAssociatedHecps[] selectedHecps)
        {
            LotoDetailViewModel viewModel = new LotoDetailViewModel();
            Loto loto = await _lotoService.GetLotoDetail(selectedHecps[0].LotoId);
            if (loto != null)
            {
                viewModel = await ConvertToLotoViewModel(loto);
                viewModel.Loto.LotoAssociatedHecps = selectedHecps.ToList();
            }
            else
            {
                _toastNotification.AddErrorToastMessage("Error adding selected HECPs, please try again.");
                return RedirectToAction("SelectLine", "Admin");
            }

            return PartialView("Partials/LotoJobInfoPartial", viewModel.Loto);
        }

        [HttpPost("[controller]/[action]/{hecpIndex}")]
        public PartialViewResult RemoveHecpFromLoto(LotoViewModel vm, int hecpindex)
        {
            try
            {
                if (vm.LotoAssociatedHecps.Count >= hecpindex)
                {
                    vm.LotoAssociatedHecps.RemoveAt(hecpindex);
                }
            }
            catch (Exception ex)
            {
                Console.Error.Write(ex.Message);
                _toastNotification.AddErrorToastMessage("Error removing HECP");
            }

            return PartialView("Partials/LotoJobInfoPartial", vm);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateLotoJobInfo([FromBody] Models.CommonModels.CreateLotoRequest lotoJobInfo)
        {
            LotoDetailViewModel viewModel = new();
            try
            {
                User user = await _userService.GetUserByBemsidAsync(lotoJobInfo.CreatedByBemsId);
                lotoJobInfo.CreatedByName = user.GetDisplayName();

                HTTPResponseWrapper<Loto> response = await _lotoService.UpdateLotoJobInfo(lotoJobInfo);
                if (response.Status == Shield.Common.Constants.ShieldHttpWrapper.Status.SUCCESS)
                {
                    Loto loto = await _lotoService.GetLotoDetail(lotoJobInfo.LotoId);
                    if (loto != null)
                    {
                        viewModel = await ConvertToLotoViewModel(loto);
                    }
                }
                return PartialView("Partials/LotoJobInfoPartial", viewModel.Loto);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return BadRequest("Error updating Work Package and Reason.");
            }
        }
    }
}
