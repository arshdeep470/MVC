using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NToastNotify;
using OfficeOpenXml;
using Shield.Common.Constants;
using Shield.Common.Models.Common;
using Shield.Ui.App.Common;
using Shield.Ui.App.Models.CheckInModels;
using Shield.Ui.App.Models.CommonModels;
using Shield.Ui.App.Services;
using Shield.Ui.App.Translators;
using Shield.Ui.App.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using TimeZoneConverter;

namespace Shield.Ui.App.Controllers
{
    [ResponseCache(CacheProfileName = "Default")]
    [Authorize(Policy = Constants.USER)]

    public class CheckInController : Controller
    {
        #region Getters/Setters/Declarations

        private Shield.Ui.App.Models.CommonModels.CheckInRecord record = new Shield.Ui.App.Models.CommonModels.CheckInRecord();
        public Shield.Ui.App.Models.CommonModels.CheckInRecord Record
        {
            get
            {
                return record;
            }
            set
            {
                record = value;
            }
        }

        private CheckInService _checkInService;
        private AirplaneDataService _airplaneDataService;
        private UserService _userService;
        private readonly IToastNotification _toastNotification;
        private readonly CheckInTranslator _checkInTranslator;
        private SessionService _sessionService;
        private ExternalService _externalService;

        #endregion Getters/Setters/Declarations

        public CheckInController(
                                CheckInService checkInService,
                                UserService userService,
                                AirplaneDataService apService,
                                IToastNotification toastNotification,
                                CheckInTranslator checkInTranslator,
                                SessionService sessionService,
                                ExternalService externalService
                                )
        {
            _checkInService = checkInService;
            _airplaneDataService = apService;
            _userService = userService;
            _toastNotification = toastNotification;
            _checkInTranslator = checkInTranslator;
            _sessionService = sessionService;
            _externalService = externalService;
        }

        #region ActionResults

        // GET: Checkin
        [Route("[Action]/{program}/{lineNumber}")]
        public async Task<ActionResult> CheckIn(string program, string lineNumber)
        {
            // Get aircraft data from service
            var vm = await InitializeCheckInPageContent(program, lineNumber);
            if (vm == null)
            {
                return RedirectToAction("SelectLine", "Admin");
            }

            return View(vm);
        }

        public async Task<AircraftHeaderViewModel> InitializeCheckInPageContent(string program, string lineNumber)
        {
            User currentUser = _sessionService.GetUserFromSession(this.HttpContext) ?? new User();

            Shield.Ui.App.Models.CommonModels.Aircraft airplaneResult = await _airplaneDataService.GetAirplaneByModelLineNumberAsync(program, lineNumber);
            var viewModel = new AircraftHeaderViewModel();

            if (airplaneResult == null)
            {
                return null;
            }

            int assignedBCBems = airplaneResult.AssignedBargeCoordinatorBems ?? 0;
            var assignedBC = await _userService.GetUserByBemsidAsync(assignedBCBems);

            int assignedGCBems = airplaneResult.AssignedGroupCoordinatorBems ?? 0;
            var assignedGC = await _userService.GetUserByBemsidAsync(assignedGCBems);

            viewModel.BCAssignedAircraft = await _airplaneDataService.GetListOfAircraftByBCBems(assignedBCBems);
            viewModel.IsCurrentUserTheBC = (assignedBCBems != 0) &&
                                           (assignedBCBems == currentUser.BemsId);
            viewModel.BargeCoordinator = assignedBC ?? new User();
            viewModel.GroupCoordinator = assignedGC;
            viewModel.Aircraft = airplaneResult;
            viewModel.IsCurrentUserBC = currentUser.IsBC();
            viewModel.IsCurrentUserGC = currentUser.IsGC();
            viewModel.controller = "CheckIn";
            viewModel.action = "GoToCheckin";
            viewModel.SessionService = _sessionService;

            bool aircraftHasGC = airplaneResult.AssignedGroupCoordinatorBems != null && airplaneResult.AssignedGroupCoordinatorBems != 0;

            // TODO: delete this section after Loto/Discrete pilot
            // If pilot program and site, requre a GC to claim the aircraft before check-in
            //if (Helpers.GetLOTODiscretePilotLimitations(airplaneResult.Site, program) == null)
            //{
            // GC has claimed the line, and the current user is the BC OR no one is logged in
            if (aircraftHasGC && (viewModel.IsCurrentUserTheBC || currentUser.BemsId == 0))
            {
                viewModel.CheckInMessage = null;
            }
            // Aircraft has a GC and the current user is not the plane's BC
            else if (aircraftHasGC)
            {
                viewModel.CheckInMessage = "CC needs to Log In and Claim Line";
            }
            // Current user is the aircraft's BC, but a GC has not claimed the line
            else if (viewModel.IsCurrentUserTheBC)
            {
                viewModel.CheckInMessage = "A GC needs to claim the line";
            }
            // Aircraft does not have a GC and the current user is not the aircraft's BC
            else
            {
                viewModel.CheckInMessage = "CC needs to Log In and Claim Line and GC needs to Claim Line";
            }
            //}
            // Only check if user is the BC when not LOTO pilot
            //else
            //{
            //    // If the current user is the BC or no one is logged in
            //    if (viewModel.IsCurrentUserTheBC || currentUser.BemsId == 0)
            //    {
            //        viewModel.CheckInMessage = null;
            //    }
            //    // Aircraft has a GC and the current user is not the plane's BC
            //    else
            //    {
            //        viewModel.CheckInMessage = "BC needs to Log In and Claim Line";
            //    }
            //}

            return viewModel;
        }

        public async Task<ActionResult> CheckInPartial(string site, string model, string lineNumber, int bemsId, string pin = null)
        {
            site = HttpUtility.HtmlDecode(site);
            IList<WorkArea> workAreas = await _airplaneDataService.GetActiveWorkAreasAsync(site, model);

            CheckInPartialViewModel viewModel = _checkInTranslator.GetDefaultCheckInPartialViewModel(lineNumber, site, model, workAreas);
            viewModel.CurrentUser = GetCurrentUserFromViewModel(viewModel);
            viewModel.assignedBCBems = bemsId;
            viewModel.assignedCCPin = pin;
            return PartialView("Partials/CheckInPartial", viewModel);
        }

        /// <summary>
        /// Post a Shield.Ui.App.Models.CommonModels.User Checking in
        /// </summary>
        public async Task<ActionResult> CheckInUser(CheckInPartialViewModel vm)
        {
            try
            {
                vm.site = HttpUtility.HtmlDecode(vm.site);
                ViewData["UserType"] = vm.personType;
                if (vm.WorkAreaIdString != null)
                {
                    var idList = vm.WorkAreaIdString.Split(',').ToList();
                    vm.workArea = idList.Select(int.Parse).ToList();
                }
                vm.WorkAreas = await _airplaneDataService.GetActiveWorkAreasAsync(vm.site, vm.program);

                if (ModelState.IsValid)
                {
                    vm.CurrentUser = GetCurrentUserFromViewModel(vm);
                    // If no one is logged in, BC should scan Badge or Enter the correct PIN for check-in to continue
                    if (vm.CurrentUser == null || vm.CurrentUser.BemsId == 0)
                    {
                        string badge = vm.ConfirmingBCBadge;
                        string pin = vm.ConfirmingCCPin;

                        if (badge == null && pin == null)
                        {
                            _toastNotification.AddErrorToastMessage("Person Not Checked In. Scan The Correct Badge or Enter the correct PIN of This Line's CC.");
                            return PartialView("Partials/CheckInPartial", vm);
                        }

                        HTTPResponseWrapper<bool> isValidResponse = badge != null ? await _externalService.IsValidBadge(vm.assignedBCBems, badge): await _userService.IsValidPin(vm.assignedBCBems, pin);

                        if (!isValidResponse.Data)
                        {
                            _toastNotification.AddErrorToastMessage(isValidResponse.Message);
                            return PartialView("Partials/CheckInPartial", vm);
                        }
                    }

                    CheckInRecord rec = _checkInTranslator.GetCheckInRecordFromViewModel(vm, DateTime.UtcNow, "Check In", false);

                    HTTPResponseWrapper<CheckInRecord> response = new HTTPResponseWrapper<CheckInRecord>();
                    if (vm.checkOutNeededFlag)
                    {
                        var checkOutResponse = await _checkInService.PostCheckOutUserByBems(vm.bemsId);
                        if (checkOutResponse.Status == Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED)
                        {
                            return PartialView("../Shared/Error/ErrorPartial", checkOutResponse.Message);
                        }

                        vm.checkOutNeededFlag = false;
                    }

                    //Getting training data when the CC didn't override or proceeded after varifying trianing data
                    if (!vm.overrideTraining && !vm.trainingConfirmation)
                    {
                        List<string> courseCodeList = new List<string>
                        {
                            TrainingCourses.AIRCRAFT_HAZARDOUS_ENERGY_AWARENESS_TRAINING_FOR_AFFECTED_PERSONS,
                            TrainingCourses.AIRCRAFT_HAZARDOUS_ENERGY_CONTROL
                        };
                        TrainingInfo trainingInfo = await _externalService.GetMyLearningDataAsync(rec.BemsId, rec.BadgeNumber,courseCodeList);
                        //show error message when there is no BemsId for the given BadgeData
                        if (trainingInfo?.BemsId == 0 && rec.Name == null)
                        {
                            _toastNotification.AddErrorToastMessage("Unable to Find Badge Data For This Badge.Please Try Using BEMSID.");
                            return PartialView("Partials/CheckInPartial", vm);
                        }

                        bool isLototrainingDone = trainingInfo.MyLearningDataResponse.Where(x => x.CertCode == TrainingCourses.AIRCRAFT_HAZARDOUS_ENERGY_CONTROL).Select(y => y.IsTrainingValid).FirstOrDefault();
                        bool showTrainingStatusPopup = trainingInfo != null && (trainingInfo.MyLearningDataResponse.All(x => !x.IsTrainingValid) || !isLototrainingDone);
                        if (showTrainingStatusPopup)
                        {
                            // Show override button when none of the training are completed.
                            if (trainingInfo.MyLearningDataResponse.Count != 0 && trainingInfo.MyLearningDataResponse.All(x => !x.IsTrainingValid))
                            {
                                vm.overrideTraining = true;
                                Console.WriteLine("Override training popup is shown");
                            }
                            // Show proceed popup button when Loto training is not done.
                            else
                            {
                                vm.trainingConfirmation = true;
                                Console.WriteLine("Training confirmation with proceed button popup is shown");
                            }
                            User user = trainingInfo.BemsId != 0 ? await _userService.GetUserByBemsidAsync(trainingInfo.BemsId) : new Models.CommonModels.User();
                            vm.UserTrainingData = trainingInfo.MyLearningDataResponse;
                            vm.recordDisplayName = trainingInfo.BemsId != 0 ? user.DisplayName : rec.Name;
                            return PartialView("Partials/TrainingStatusPartial", vm);
                        }
                    }

                    response = await _checkInService.PostCheckinAsync(rec);

                    if (response == null)
                    {
                        ViewBag.Status = "Failed";
                        ViewBag.Message = "Unable to reach Check In Service, please try again.";
                        return PartialView("Partials/CheckInPartial", vm);
                    }
                    else if (response.Status.Equals(Shield.Common.Constants.ShieldHttpWrapper.Status.SUCCESS))
                    {
                        ViewBag.Status = "Success";
                        ViewBag.Message = response.Message;
                        CheckInPartialViewModel newVM = _checkInTranslator.GetNewCheckInPartialVMAfterSuccess(vm);
                        return PartialView("Partials/CheckInPartial", newVM);
                    }
                    else if (response.Status.Equals(Shield.Common.Constants.ShieldHttpWrapper.Status.NOT_MODIFIED))
                    {
                        vm.checkOutNeededFlag = true;
                        vm.bemsId = response.Data.BemsId;
                        ViewData["ResponseMessage"] = response.Message;
                        return PartialView("Partials/CheckOutCheckInPartial", vm);
                    }
                    else
                    {
                        _toastNotification.AddErrorToastMessage(response.Message);
                        return PartialView("Partials/CheckInPartial", vm);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }

            return PartialView("Partials/CheckInPartial", vm);
        }

        [HttpGet]
        [Route("CheckIn/GetCheckedInPersonnelData/{program}/{lineNumber}/{isCurrentUserTheBc}")]
        [ResponseCache(CacheProfileName = "Default")]
        public virtual async Task<ActionResult> GetCheckedInPersonnelData(string program, string lineNumber, bool isCurrentUserTheBc = false)
        {
            var records = await _checkInService.GetCheckedInUsersByProgramAndLineNumber(program, lineNumber);

            CheckedInPersonnelTableViewModel vm = new CheckedInPersonnelTableViewModel
            {
                SessionService = _sessionService,
                CheckInRecords = records.OrderByDescending(rec => rec.CheckInDate).ToList()
            };

            ViewData["IsCurrentUserTheBc"] = isCurrentUserTheBc;

            return PartialView("Partials/CheckedInPersonnelTable", vm);
        }

        [HttpGet]
        [Route("[Controller]/[Action]/{navToAction}/{navToController}/{proceedText}/{title}")]
        //[Route("[Controller]/[Action]")]
        public async Task<ActionResult> GetCheckInReportModal(string navToAction = null, string navToController = null, string proceedText = null, string title = null)
        {
            ManageCheckInReportModalViewModel vm = new ManageCheckInReportModalViewModel();

            vm.Sites = await _airplaneDataService.GetAllSitesAsync();
            vm.Sites = vm.Sites.OrderBy(s => s.Name).ToList();
            vm.Sites = vm.Sites.Select(
                s => new Site()
                {
                    SiteId = s.SiteId,
                    Name = s.Name,
                    Programs = s.Programs.OrderBy(p => p.Name).ToList()
                }).ToList();

            vm.Action = navToAction;
            vm.Controller = navToController;
            vm.ProceedText = proceedText;
            vm.Title = title;

            vm.SelectedSite = _sessionService.GetString(HttpContext, "selectedSite");

            return PartialView("../Home/Partials/ManageCheckInReportModalPartial", vm);
        }

        [HttpGet]
        public async Task<ActionResult> ViewCheckinReportsForSite(string site, string selectedProgram, string selectLineNumber, int selectedWorkAreaId, string fromDateString, string toDateString, int bemsId, string timezone, string managerName, string managerBemsId, int pageNumber = 1)
        {
            PagingWrapper<CheckInReport> checkInReportPagingWrapper = new PagingWrapper<CheckInReport>();
            var CheckInReport = new CheckInReportViewModel();
            string selectedWorkArea = string.Empty;
            try
            {
                var program = _sessionService.GetString(HttpContext, "selectedProgram");
                _sessionService.SetString(HttpContext, "selectedSite", site);

                ViewData["site"] = site;
                TempData["site"] = site;

                if (program == null)
                {
                    _sessionService.SetString(HttpContext, "selectedProgram", "setProgram");
                }

                if (selectedWorkAreaId != 0)
                {
                    selectedWorkArea = await _airplaneDataService.GetWorkAreaById(selectedWorkAreaId);
                }

                //checkInReportPagingWrapper = await _checkInService.GetCheckInReportBySite(site, pageNumber);
                var fromDate = !String.IsNullOrEmpty(fromDateString) ? _checkInService.GetFromDateInUniversalTime(fromDateString, timezone) : DateTime.MinValue;
                var toDate = !String.IsNullOrEmpty(toDateString) ? _checkInService.GetToDateInUniversalTime(toDateString, timezone) : DateTime.MinValue;

                checkInReportPagingWrapper = await _checkInService.GetFilteredCheckInReport(new CheckInReport
                {
                    Program = selectedProgram,
                    LineNumber = selectLineNumber,
                    BemsId = bemsId,
                    WorkAreaId = selectedWorkAreaId,
                    Site = site,
                    PageNumber = pageNumber,
                    FromDate = fromDate,
                    ToDate = toDate,
                    ManagerBemsId = Convert.ToInt32(managerBemsId),
                    ManagerName = managerName
                });

                List<CheckInReport> checkInReportList = checkInReportPagingWrapper.Data;
                checkInReportList = checkInReportList.OrderByDescending(c => c.Date).ToList();
                checkInReportPagingWrapper.Data = checkInReportList;

                CheckInReport.CheckInReports = checkInReportPagingWrapper;
                var listProgram = await _airplaneDataService.GetActiveAirplaneBySiteAsync(site);
                CheckInReport.Programs = listProgram.OrderBy(s => s.Model).Select(s => s.Model).Distinct().ToList();
                CheckInReport.LineNumbers = listProgram.Where(l => l.Model == selectedProgram).Select(s => s.LineNumber).ToList();
                CheckInReport.WorkAreas = (site != null && selectedProgram != null) ? await _airplaneDataService.GetWorkAreasAsync(site, selectedProgram) : new List<WorkArea>();
                CheckInReport.SelectedWorkArea = selectedWorkArea;
                CheckInReport.SelectedWorkAreaId = selectedWorkAreaId;
                CheckInReport.SelectLineNumber = selectLineNumber;
                CheckInReport.SelectProgram = selectedProgram;
                CheckInReport.FromDate = fromDateString;
                CheckInReport.ToDate = toDateString;
                CheckInReport.BemsId = bemsId;
                CheckInReport.initialPageLoad = true;
                CheckInReport.Timezone = timezone;
                CheckInReport.ManagerBemsId = Convert.ToInt32(managerBemsId);//do testing and check
                CheckInReport.ManagerName = managerName;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                checkInReportPagingWrapper = new PagingWrapper<CheckInReport>();
                CheckInReport.CheckInReports = checkInReportPagingWrapper;
            }

            return View("CheckInReport", CheckInReport);
        }

        [HttpPost]
        public async Task<ActionResult> FilterCheckinReportsForSite(CheckInReportViewModel checkInReportModel)
        {
            PagingWrapper<CheckInReport> checkInReportPagingWrapper = new PagingWrapper<CheckInReport>();

            try
            {
                var site = _sessionService.GetString(HttpContext, "selectedSite");

                if (site == null)
                {
                    site = TempData.Peek("site").ToString();
                }
                ViewData["site"] = site;
                var fromDate = !String.IsNullOrEmpty(checkInReportModel.FromDate) ? _checkInService.GetFromDateInUniversalTime(checkInReportModel.FromDate, checkInReportModel.Timezone) : DateTime.MinValue;
                var toDate = !String.IsNullOrEmpty(checkInReportModel.ToDate) ? _checkInService.GetToDateInUniversalTime(checkInReportModel.ToDate, checkInReportModel.Timezone) : DateTime.MinValue;


                if (checkInReportModel.SelectedWorkAreaId != 0)
                {
                    checkInReportModel.SelectedWorkArea = await _airplaneDataService.GetWorkAreaById(checkInReportModel.SelectedWorkAreaId);
                }

                checkInReportPagingWrapper = await _checkInService.GetFilteredCheckInReport(new CheckInReport
                {
                    Program = checkInReportModel.SelectProgram,
                    LineNumber = checkInReportModel.SelectLineNumber,
                    BemsId = checkInReportModel.BemsId,
                    WorkArea = checkInReportModel.SelectedWorkArea,
                    WorkAreaId = checkInReportModel.SelectedWorkAreaId,
                    Site = site,
                    PageNumber = 1,
                    FromDate = fromDate,
                    ToDate = toDate,
                    ManagerBemsId = checkInReportModel.ManagerBemsId,
                    ManagerName = checkInReportModel.ManagerName
                });

                List<CheckInReport> checkInReportList = checkInReportPagingWrapper.Data;
                checkInReportList = checkInReportList.OrderByDescending(c => c.Date).ToList();
                checkInReportPagingWrapper.Data = checkInReportList;
                checkInReportModel.CheckInReports = checkInReportPagingWrapper;
                var listProgram = await _airplaneDataService.GetActiveAirplaneBySiteAsync(site);
                checkInReportModel.Programs = listProgram.OrderBy(s => s.Model).Select(s => s.Model).Distinct().ToList();
                checkInReportModel.LineNumbers = listProgram.Where(l => l.Model == checkInReportModel.SelectProgram).Select(s => s.LineNumber).ToList();
                if (checkInReportModel.LineNumbers == null)
                    checkInReportModel.LineNumbers = new List<string>();
                if (checkInReportModel.WorkAreas.Count == 0 && checkInReportModel.SelectProgram != null)
                    checkInReportModel.WorkAreas = await GetWorkAreasAsync(site, checkInReportModel.SelectProgram);
                checkInReportModel.initialPageLoad = false;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                checkInReportPagingWrapper = new PagingWrapper<CheckInReport>();
            }

            return View("CheckInReport", checkInReportModel);
        }

        [HttpGet]
        public async Task<ActionResult> ExportReportToExcel(string program, string lineNumber, string bemsId, int workAreaId, string fromDateString, string toDateString, string timezone, string managerBemsId, string managerName)
        {
            //param int workareaid
            var workArea = "";
            List<CheckInReport> checkInReport = new List<CheckInReport>();

            try
            {
                var site = _sessionService.GetString(HttpContext, "selectedSite");

                if (site == null)
                {
                    site = TempData.Peek("site").ToString();
                }
                ViewData["site"] = site;

                var fromDate = !String.IsNullOrEmpty(fromDateString) ? _checkInService.GetFromDateInUniversalTime(fromDateString, timezone) : DateTime.MinValue;
                var toDate = !String.IsNullOrEmpty(toDateString) ? _checkInService.GetToDateInUniversalTime(toDateString, timezone) : DateTime.MinValue;

                if (workAreaId != 0)
                {
                    workArea = await _airplaneDataService.GetWorkAreaById(workAreaId);
                }
                checkInReport = await _checkInService.GetFilteredCheckInReportForExcel(new CheckInReport
                {
                    Program = program,
                    LineNumber = lineNumber,
                    BemsId = bemsId == null ? 0 : int.Parse(bemsId),
                    WorkArea = workArea,
                    WorkAreaId = workAreaId,
                    Site = site,
                    FromDate = fromDate,
                    ToDate = toDate,
                    ManagerBemsId = Convert.ToInt32(managerBemsId),
                    ManagerName = managerName
                });

                TimeZoneInfo zone = TZConvert.GetTimeZoneInfo(timezone);
                List<CheckInReport> checkInReportList = SwitchTimeZone(checkInReport, zone);
                checkInReportList = checkInReportList.OrderByDescending(c => c.Date).ToList();

                MemoryStream memStream = GetExcelFile(checkInReportList);

                return File(memStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "CheckInReport" + DateTime.Now.ToString("MMddyyyyhhmm", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }

            return new ObjectResult("Unable to export Excel file") { StatusCode = 500 };
        }

        public List<CheckInReport> SwitchTimeZone(List<CheckInReport> checkInReports, TimeZoneInfo zone)
        {

            checkInReports.ForEach(chk =>
            {
                chk.Date = TimeZoneInfo.ConvertTimeFromUtc(chk.Date, zone);
            });
            return checkInReports;
        }
        #endregion ActionResults

        #region Helping Methods

        /// <summary>
        /// Get the user role and direct to GCCheckIn or NonGCCheckIn
        /// </summary>
        /// <param name="program"></param>
        /// <param name="lineNumber"></param>
        /// <param name="site"></param>
        /// <returns></returns>
        public ActionResult GoToCheckin(string program, string lineNumber, string site)
        {
            if (!String.IsNullOrEmpty(program) && !String.IsNullOrEmpty(lineNumber) && !String.IsNullOrEmpty(site))
            {
                _sessionService.SetString(HttpContext, "selectedSite", site);
                _sessionService.SetString(HttpContext, "selectedProgram", program);
                _sessionService.SetString(HttpContext, "selectedLineNumber", lineNumber);

                //User currentUser = _sessionService.GetUserFromSession(HttpContext);

                return RedirectToAction("CheckIn", new { program = program, lineNumber = lineNumber });
            }
            else
            {
                return RedirectToAction("Index", "Home");
            }
        }

        [Route("[Action]/{site}/{recordId}")]
        public async Task<ActionResult> CheckOut(string site, string program, string lineNumber, int recordId)
        {
            var checkOutResponse = await _checkInService.PostCheckOutAsync(recordId);
            if (checkOutResponse != null)
            {
                if (checkOutResponse.Status == Shield.Common.Constants.ShieldHttpWrapper.Status.FAILED)
                {
                    return new ObjectResult(checkOutResponse.Message) { StatusCode = 500 };
                }
                else
                {
                    return new ObjectResult(checkOutResponse.Message) { StatusCode = 200 };
                }
            }
            else
            {
                return new ObjectResult("Unable to check out user, please try again.") { StatusCode = 500 };
            }
        }

        [Route("[Action]")]
        public async Task<ActionResult> CheckOutByBemsOrBadge(string site, string program, string lineNumber, string bemsOrBadgeNumber)
        {
            if (string.IsNullOrWhiteSpace(bemsOrBadgeNumber))
            {
                return new ObjectResult("Please Scan Badge or Type BEMSID.") { StatusCode = 400 };
            }
            Models.CheckInModels.CheckOutRequest req = new Models.CheckInModels.CheckOutRequest()
            {
                Program = program,
                LineNumber = lineNumber,
                BemsOrBadgeNumber = bemsOrBadgeNumber
            };

            if (Helpers.IsBadgeNumber(req.BemsOrBadgeNumber))
            {
                req.BemsOrBadgeNumber = Helpers.ParseBadgeNumber(req.BemsOrBadgeNumber);
            }

            HTTPResponseWrapper<CheckInRecord> checkOutResponse = await _checkInService.PostCheckOutRequestAsync(req);

            if (checkOutResponse != null)
            {
                if (checkOutResponse.Status == Shield.Common.Constants.ShieldHttpWrapper.Status.SUCCESS)
                {
                    return new ObjectResult("Successfully " + checkOutResponse.Message) { StatusCode = 200 };
                }
                else
                {
                    return new ObjectResult(checkOutResponse.Message) { StatusCode = 500 };
                }
            }
            else
            {
                return new ObjectResult("Cannot reach Check-In Service.") { StatusCode = 500 };
            }
        }

        [HttpGet]
        public async Task<ActionResult> GetAreasForProgram(string program, string site)
        {
            IList<WorkArea> workAreas = await _airplaneDataService.GetWorkAreasAsync(site, program);

            return new ObjectResult(workAreas) { StatusCode = 200 };
        }

        private async Task<IList<WorkArea>> GetWorkAreasAsync(string site, string model)
        {
            return await _airplaneDataService.GetWorkAreasAsync(site, model);
        }

        private User GetCurrentUserFromViewModel(CheckInPartialViewModel vm)
        {
            if (vm.CurrentUser == null)
            {
                return _sessionService.GetUserFromSession(HttpContext) ?? new User();
            }

            return vm.CurrentUser;
        }

        [HttpGet]
        [Route("[Action]/Program/{program}/LineNumber/{lineNumber}/PageNumber/{pageNumber}")]
        public virtual async Task<ActionResult> GetCheckInHistory(string program, string lineNumber, int pageNumber)
        {
            PagingWrapper<Shield.Ui.App.Models.CheckInModels.CheckInTransaction> checkInPagingWrapper = new PagingWrapper<Shield.Ui.App.Models.CheckInModels.CheckInTransaction>();

            try
            {
                checkInPagingWrapper = await _checkInService.GetCheckInHistoryByProgramAndLineNumber(program, lineNumber, pageNumber);

                List<Shield.Ui.App.Models.CheckInModels.CheckInTransaction> checkInTransactionList = checkInPagingWrapper.Data;
                checkInTransactionList = checkInTransactionList.OrderByDescending(c => c.Date).ToList();
                checkInPagingWrapper.Data = checkInTransactionList;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                checkInPagingWrapper = new PagingWrapper<Shield.Ui.App.Models.CheckInModels.CheckInTransaction>();
            }

            return View("CheckInHistory", checkInPagingWrapper);
        }

        /// <summary>
        /// The is bc bems id used.
        /// </summary>
        /// <param name="confirmingBCBadge">
        /// The confirming bc badge.
        /// </param>
        /// <param name="assignedBCBems">
        /// The assigned bc bems.
        /// </param>
        /// <returns>
        /// The <see cref="bool"/>.
        /// </returns>
        private bool IsBcBemsIdUsed(string confirmingBCBadge, int assignedBCBems)
        {
            if (!string.IsNullOrEmpty(confirmingBCBadge))
            {
                // Remove any "." and empty space
                confirmingBCBadge = confirmingBCBadge.Replace(".", string.Empty).Trim();
                int.TryParse(confirmingBCBadge, out int confirmingBCId);
                if (assignedBCBems == confirmingBCId)
                {
                    return true;
                }
            }

            return false;
        }

        private MemoryStream GetExcelFile(List<CheckInReport> checkInReportList)
        {
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("CheckInReport");
                var range = worksheet.Cells[1, 1, 1, 9];
                range.Style.Font.Bold = true;
                worksheet.Cells[1, 1].Value = "Date";
                worksheet.Cells[1, 2].Value = "Program";
                worksheet.Cells[1, 3].Value = "LineNumber";
                worksheet.Cells[1, 4].Value = "Name";
                worksheet.Cells[1, 5].Value = "BEMS ID";
                worksheet.Cells[1, 6].Value = "Work Area";
                worksheet.Cells[1, 7].Value = "Activity";
                worksheet.Cells[1, 8].Value = "Manager Name";
                worksheet.Cells[1, 9].Value = "Manager BEMS ID";

                for (int i = 0; i < checkInReportList.Count; i++)
                {
                    worksheet.Cells[i + 2, 1].Value = checkInReportList[i].Date.ToString();
                    worksheet.Cells[i + 2, 2].Value = checkInReportList[i].Program;
                    worksheet.Cells[i + 2, 3].Value = checkInReportList[i].LineNumber;
                    worksheet.Cells[i + 2, 4].Value = checkInReportList[i].Name;
                    worksheet.Cells[i + 2, 5].Value = checkInReportList[i].BemsId;
                    worksheet.Cells[i + 2, 6].Value = checkInReportList[i].WorkArea;
                    worksheet.Cells[i + 2, 7].Value = checkInReportList[i].Activity;
                    worksheet.Cells[i + 2, 8].Value = checkInReportList[i].ManagerName;
                    worksheet.Cells[i + 2, 9].Value = checkInReportList[i].ManagerBemsId;
                }

                return new MemoryStream(package.GetAsByteArray());
            }
        }
        #endregion Helping Methods
    }
}
