var isInstallingIsolation = false;
var isDeletingIsolation = false;
var arrSelectedHecps = [];
var rowCount = 1;
var discreteHecpRowCount = 1;
var listOfExistingHecpIds = [];
var allHecpsAssociated = [];
var listOfExistingDiscrete = [];

function addIsolationRow(lotoAssociatedHecps) {
    $.ajax({
        url: '/Loto/AddNewIsolationRow?count=' + rowCount,
        type: 'POST',
        contentType: 'application/json; charset=utf-8',
        data: JSON.stringify(lotoAssociatedHecps),
        async: true,
        success: function (objectResult) {
            $('#discrete-isolation-table-body').append(objectResult)
        },
        error: function () {
            toastr["error"]("Failed to Add isolation.", TOAST_OPTIONS);
        }
    });
};

function deleteIsolationRow(i) {
    $('#isolation-' + i).remove();
    rowCount--;
};

function deleteIsolationRowFromLoto(count) {
    $('#isolation-' + count).remove();
}

function deleteIsolationFromLoto(isolationId) {
    if (isolationId == 0) {
        $('#isolation-0').fadeOut();
    }
    else if (isDeletingIsolation) {
        toastr.warning("Currently deleting an isolation...please wait", "Please Wait", TOAST_OPTIONS)
    } else {
        var lotoId = $("#Id").val();
        var $isolationDeleteLoader = $('#delete-isolation-loader-' + isolationId);
        $isolationDeleteLoader.css('display', 'inline');
        isDeletingIsolation = true;

        $.ajax({
            url: '/Loto/DeleteIsolation?isolationId=' + isolationId + '&lotoId=' + lotoId,
            type: 'GET',
            dataType: 'json',
            async: true,
            success: function (objectResult) {
                if (objectResult.StatusCode == 200) {
                    //$('#isolation-' + lotoId + '-' + isolationId).fadeOut();
                    $.ajax({
                        url: "/Loto/LotoDetail?id=" + lotoId,
                        cache: false,
                        success: function (html) {
                            //$("#isolation-" + i).fadeOut();
                            $('body').html(html); // refresh content
                            toastr.success(objectResult.Value, "Deletion Successful", TOAST_OPTIONS_SHORT_TIMEOUT);
                            isDeletingIsolation = false;
                        },
                        error: function (xhr) {
                            console.log(xhr);
                            toastr.error(xhr, "Error", TOAST_OPTIONS);
                            $isolationDeleteLoader.hide();
                        }
                    });
                } else {
                    isDeletingIsolation = false;
                    toastr["error"](objectResult.Value, "Deletion Error", TOAST_OPTIONS);
                    $isolationDeleteLoader.hide();
                }
            },
            error: function () {
                toastr["error"]("Failed to delete isolation.", "Deletion Error", TOAST_OPTIONS);
                isDeletingIsolation = false;
                $isolationDeleteLoader.hide();
            }
        });
    }
}

function installIsolation(isolationId) {
    if (isInstallingIsolation) {
        toastr.warning("Currently installing an isolation...please wait", "Please Wait", TOAST_OPTIONS)
    } else {
        var $isolationSaveLoader = $('#save-isolation-loader-' + isolationId);
        $isolationSaveLoader.css('display', 'inline');
        isInstallingIsolation = true;
        var tag = $("#tag-" + isolationId).val();
        var systemCircuitId = $("#system-id-" + isolationId).val();
        var nomenclature = $("#circuit-nomenclature-" + isolationId).val();
        var lotoId = $("#Id").val();
        var lotoAssociatedId = $("#discrete-title-select-" + isolationId + " :selected").val();

        var isolationRequest = {
            LotoId: lotoId,
            InstalledByBemsId: $("#AssignedPAEBemsId").val(),
            Tag: tag,
            SystemCircuitId: systemCircuitId,
            CircuitNomenclature: nomenclature,
            LotoAssociatedId: lotoAssociatedId
        };

        $.ajax({
            url: "/Loto/InstallIsolation",
            type: 'POST',
            data: JSON.stringify(isolationRequest),
            dataType: 'json',
            contentType: 'application/json; charset=utf-8',
            error: function (xhr) {
                var errorMsg = xhr.Status == 400 ? xhr.responseText : "";
                toastr.error(errorMsg, "Failed to Save Isolation", TOAST_OPTIONS);
                $isolationSaveLoader.hide();
            },
            success: function (response) {
                $.ajax({
                    url: "/Loto/LotoDetail?id=" + lotoId,
                    cache: false,
                    success: function (html) {
                        $('body').html(html); // refresh content
                        toastr.success(response.Message, "Saved Isolation", TOAST_OPTIONS_SHORT_TIMEOUT);
                    }
                });
            },
            complete: function () {
                isInstallingIsolation = false;
            }
        });
    }
}

function unlockIsolation(isolationId) {
    $.ajax({
        url: '/Loto/UnlockIsolation?isolationId=' + isolationId,
        type: 'PUT',
        dataType: 'json',
        async: true,
        success: function (result) {
            if (result.status === HTTP.STATUS.SUCCESS) {

                var loadingGif = '<img id="edit-isolation-loading" src="../images/loading.gif" class="img-responsive" alt="Loading..." width="25" height="25" />'
                $("#edit-icon-" + isolationId).hide();
                $("#install-button-" + isolationId).append(loadingGif);

                $.ajax({
                    url: '/Loto/DiscreteUnlockedIsolationRowPartial',
                    type: 'POST',
                    data: JSON.stringify(result.data),
                    dataType: 'html',
                    contentType: 'application/json; charset=utf-8',
                    async: false,
                    success: function (html) {
                        $('#isolation-' + isolationId).hide();
                        $('#isolation-' + isolationId).html(html);
                        $('#isolation-' + isolationId).show(200);

                        $('#tag-' + isolationId).focus();
                    }
                });
            } else {
                console.log(xhr);
                toastr.error(xhr, "Error: Failed to unlock isolation.", TOAST_OPTIONS);
            }
        },
        error: function (xhr) {
            console.log(xhr);
            toastr.error(xhr, "Error: Failed to unlock isolation.", TOAST_OPTIONS);
        },
        complete: function () {
            validateLockoutButton();
        }
    });
}

function installDiscreteIsolation(i) {
    var tag = $("#tag-" + i).val();
    var systemCircuitId = $("#system-id-" + i).val();
    var nomenclature = $("#circuit-nomenclature-" + i).val();

    var isolationRequest = {
        Id: i,
        LotoId: $("#Id").val(),
        InstalledByBemsId: $("#AssignedPAEBemsId").val(),
        Tag: tag,
        SystemCircuitId: systemCircuitId,
        CircuitNomenclature: nomenclature
    };

    $.ajax({
        url: "/Loto/InstallDiscreteIsolation",
        type: 'PUT',
        data: JSON.stringify(isolationRequest),
        dataType: 'html',
        contentType: 'application/json; charset=utf-8',
        error: function (xhr) {
            console.log('Error: ' + xhr);
            toastr["error"]("Check internet connection and try again.", "Error", TOAST_OPTIONS)
        },
        success: function (isolation) {
            toastr["success"]("Saved isolation " + systemCircuitId, "Success", TOAST_OPTIONS_SHORT_TIMEOUT)

            $('#isolation-' + i).hide();
            $('#isolation-' + i).html(safeResponseFilter(isolation));
            $('#isolation-' + i).show(200);

            updateHistoryLog(isolationRequest.LotoId, true);
        },
        complete: function () {
            validateLockoutButton();
        },
        async: true,
        processData: false
    });

    return false;
}

function validateLockoutButton() {
    // If a save icon exists on the page, then at least one isolation is unlocked and the aircraft cannot be locked out
    if (document.getElementById('save-icon') == null) {
        $('#lockout-button').prop('disabled', false);
    } else {
        $('#lockout-button').prop('disabled', 'disabled');
    }
}

function updateHistoryLog(i, convertDateTime) {
    $.ajax({
        url: "/Loto/GetTransactionLog",
        type: 'GET',
        data: { "lotoId": i },
        contentType: 'application/json; charset=utf-8',
        datatype: 'json',
        cache: false,
        error: function (xhr) {
            console.log('Error: ' + xhr);
        },
        success: function (logs) {
            $("#lotoHistoryTable").html(safeResponseFilter(logs));
            if (convertDateTime) {
                convertAllDatesToLocalTime();
            }
            else {
                convertHistoryDatesToLocalTime();
            }
        },
        async: true
    });
}

// Hide loto history table on click of "ISOLATIONS" tab
$('#isolations-tab').click(function () {
    $('#loto-history').removeClass('active');
});

function saveLoto(lotoId) {
    $.ajax({
        url: "/Loto/SaveLoto",
        type: "post",
        dataType: "html",
        data: $('#loto-job-info-form').serialize(),
        success: function (result) {
            if (result == "SUCCESS") { // Success
                window.location = "/Loto/LotoDetail?id=" + lotoId;
            } else {
                if ($(result).filter("#loto-job-info-form").length > 0) {
                    $("#loto-job-info-wrapper").html(safeResponseFilter(result));
                    toastr["error"]("Invalid Job Info. LOTO not saved.", "Error", TOAST_OPTIONS);
                    $('#isolations-wrapper').hide();
                    $('#lockout-button').prop('disabled', true);
                }
                else {
                    $("#conflictIsolationsModal").html(safeResponseFilter(result));
                    $('#conflictIsolationsModal').modal('show');
                }
            }
        },
        error: function () {
            toastr["error"]("Failed to Save LOTO", "Save Error", TOAST_OPTIONS);
        }
    });
}

function showIdentityPopupForLotoSignIn(bemsOrBadge) {
    $("#ConfirmIdentityForLotoSignIn").show();
    $("#ConfirmIdentityForLotoSignIn").addClass("show");
    if (isBadgeNumber(bemsOrBadge)) {
        hideAcknowledgeTextAndProfileDetails('#acknowledgeBemsForLotoText', '#profileDetailsForLotoSignIn', '.confirmIdentityForLotoSignIn', '16rem');
    }
    else {
        showAcknowledgeTextAndProfileDetails('#acknowledgeBemsForLotoText', '#profileDetailsForLotoSignIn', '.confirmIdentityForLotoSignIn', '28rem');
        createIdentityPopupForLoto(bemsOrBadge);
    }
}

function createIdentityPopupForLoto(bemsID) {
    $("#profileDetailsForLotoSignIn").html("");
    $("#acknowledgedBEMSForLotoSignIn").text(bemsID);
    if (bemsID !== "") {
        let script = getInsiteWidgetScript(bemsID, 'profileDetailsForLotoSignIn');
        $("#profileDetailsForLotoSignIn").append(script);
    }
}

function closeIdentityPopupForLotoSignIn() {
    $("#ConfirmIdentityForLotoSignIn").hide();
    $("#ConfirmIdentityForLotoSignIn").removeClass("show");
}

function AssignPAE(id, bemsId, gcBemsId, overrideTraining) {
    $('#overrideTrainingLotoModal').hide();
    closeIdentityPopupForLotoSignIn();

    let $signInLoader = $('#sign-in-loader-pae');
    $signInLoader.css('display', 'inline');

    let overrideLoader = $('#override-sign-in-loader');
    overrideLoader.css('display', 'inline');

    $.ajax({
        url: "/Loto/AssignPAE",
        type: 'POST',
        data: {
            lotoId: id,
            overrideTraining: overrideTraining
        },
        dataType: 'json',
        cache: false,
        success: function (result) {
            if (result.status === HTTP.STATUS.SUCCESS) {
                $.ajax({
                    url: "/Loto/LotoDetail?id=" + id,
                    cache: false,
                    success: function (html) {
                        $('body').html(safeResponseFilter(html)); // refresh content
                        toastr.success(safeResponseFilter(result.message), "PAE Signed In", TOAST_OPTIONS_SHORT_TIMEOUT);
                    }
                });
            } else if (result.status === HTTP.STATUS.NOT_MODIFIED) {
                if (result.reason === HTTP.REASON.TRAINING) {
                    let userTrainingData = result.data.userTrainingData;
                    $.ajax({
                        url: '/Loto/GetTrainingStatus?id=' + id + '&bemsId=' + bemsId + '&message=' + result.message,
                        type: 'POST',
                        data: JSON.stringify(userTrainingData),
                        contentType: 'application/json; charset=utf-8',
                        dataType: 'html',
                        success: function (response) {
                            $('#overrideTrainingLotoModal').html(response);
                            $('#OverrideTrainingLotoConfirmation').append(safeResponseFilter('<br /><br /> Shield attempted to communicate with My Learning and was unable to provide confirmation of required training (77517 and 84757). Please document a reason for an override or cancel the sign in.'));
                            $('#overrideTrainingBemsID').val(bemsId);
                            $('#gcBemsId').val(gcBemsId);
                            $('#override-employee-training').hide();
                            $('#override-employee-training-pae').show();
                            $('#OverrideTrainingPAESubmit').show();
                            $('#override-visitor-training').hide();
                            $('#OverrideTrainingVisitorSubmit').hide();
                            $('#OverrideTrainingSubmit').hide();
                            $('#overrideTrainingLotoModal').modal('show');
                        }
                    });

                } else {
                    toastr.error(result.message, "PAE Not Signed In", TOAST_OPTIONS);
                }
            } else {
                if (result.reason === HTTP.REASON.TRAINING) {
                    $('#sign-in-partial').hide(200);
                    $('#sign-in-partial').html('');

                    $.ajax({
                        url: '/Loto/OverrideTrainingPartial',
                        type: 'POST',
                        data: {
                            id: result.data.lotoId,
                            bemsId: bemsId,
                            message: result.message,
                            isTrainingDown: true
                        },
                        dataType: 'html',
                        async: true,
                        success: function (result) {
                            $('#sign-in-partial').html(result);
                            $('#sign-in-partial').show(200);
                        }
                    });
                }
                else {
                    $('#overrideTrainingLotoModal').hide();
                    toastr.error(result.message, "PAE Not Signed In", TOAST_OPTIONS);
                }
            }
        },
        error: function (xhr) {
            $('#overrideTrainingLotoModal').hide();
            toastr.error("Unable to connect to LOTO service.", "Connection Error", TOAST_OPTIONS);
        },
        complete: function () {
            $signInLoader.hide();
        },
        async: true
    });
}

function OverrideTrainingPAE(lotoId, overrideTraining) {
    let $PAESignInLoad = $('#overridePAETraining-sign-in-loader');
    $PAESignInLoad.css('display', 'inline');

    let gcBemsId = $('#gcBemsId').val();
    gcBemsId = $.trim(gcBemsId);
    let overrideReason = $('#loto-override-training-comments-dropdown-pae option:selected').text();
    if (overrideReason == "Other") {
        overrideReason = $('#loto-override-training-comments-pae').val();
    }

    $.ajax({
        url: "/Loto/AssignPAE",
        type: 'POST',
        data: {
            lotoId: lotoId,
            gcBemsId: gcBemsId,
            overrideTraining: overrideTraining,
            reasonToOverride: overrideReason
        },
        dataType: 'json',
        cache: false,
        success: function (result) {
            if (result.status === HTTP.STATUS.SUCCESS) {
                $.ajax({
                    url: "/Loto/LotoDetail?id=" + lotoId,
                    cache: false,
                    success: function (html) {
                        $('body').html(html); // refresh content
                        toastr.success(result.message, "PAE Signed In", TOAST_OPTIONS_SHORT_TIMEOUT);
                    }
                });
            }
        },
        error: function () {
            $('#overrideTrainingLotoModal').hide();
            $PAESignInLoad.hide();
            toastr.error('Failed', "Failed to override training. Unable to signin PAE", TOAST_OPTIONS);
        },
        complete: function () {
            $('#overrideTrainingLotoModal').hide();
            $('#overrideTrainingLotoModal').modal("hide");
            $PAESignInLoad.hide();
        },
    })
}

function signInAE(id, bemsOrBadge, overrideTraining) {
    $('#overrideTrainingLotoModal').hide();
    closeIdentityPopupForLotoSignIn();

    let $signInLoader = $('#sign-in-loader');
    $signInLoader.css('display', 'inline');

    let overrideLoader = $('#override-sign-in-loader');
    overrideLoader.css('display', 'inline');

    $.ajax({
        url: "/Loto/AssignAE",
        type: 'POST',
        data: {
            lotoId: id,
            bemsOrBadge: bemsOrBadge,
            overrideTraining: overrideTraining
        },
        dataType: 'json',
        cache: false,
        success: function (result) {
            if (result.status === HTTP.STATUS.SUCCESS) {
                $.ajax({
                    url: "/Loto/LotoDetail?id=" + id,
                    cache: false,
                    success: function (html) {
                        $('body').html(safeResponseFilter(html)); // refresh content
                        toastr.success(safeResponseFilter(result.message), "AE Signed In", TOAST_OPTIONS_SHORT_TIMEOUT);
                    }
                });
            } else if (result.status === HTTP.STATUS.NOT_MODIFIED) {
                if (result.reason === HTTP.REASON.TRAINING) {
                    let userTrainingData = result.data.userTrainingData;
                    $.ajax({
                        url: '/Loto/GetTrainingStatus?id=' + id + '&bemsId=' + result.data.aeBemsId + '&message=' + result.message,
                        type: 'POST',
                        data: JSON.stringify(userTrainingData),
                        contentType: 'application/json; charset=utf-8',
                        dataType: 'html',
                        success: function (response) {
                            $('#overrideTrainingLotoModal').html(response); 
                            $('#OverrideTrainingLotoConfirmation').append(safeResponseFilter('<br /><br /> Shield attempted to communicate with My Learning and was unable to provide confirmation of required training (77517 and 84757). Please document a reason for an override or cancel the sign in.'));
                            $('#overrideTrainingBemsID').val(bemsOrBadge);
                            $('#override-employee-training').show();
                            $('#OverrideTrainingSubmit').show();
                            $('#override-visitor-training').hide();
                            $('#OverrideTrainingVisitorSubmit').hide();
                            $('#OverrideTrainingPAESubmit').hide();
                            $('#overrideTrainingLotoModal').modal('show');
                        }
                    });

                } else if (result.reason === HTTP.REASON.ALREADY_EXISTS) {
                    var warning = "User " + result.data.aeName + "(" + result.data.aeBemsId + ") is already assigned as AE in the LOTO " + result.data.lotoId + ".";
                    $('#ae-warning-partial p').text(warning);
                    $('#sign-in').hide();
                    $('#ae-warning-partial').show();
                } else {
                    toastr.error(result.message, "AE Not Signed In", TOAST_OPTIONS);
                }
            } else {
                if (result.reason === HTTP.REASON.TRAINING) {
                    $('#sign-in-partial').hide(200);
                    $('#sign-in-partial').html('');

                    $.ajax({
                        url: '/Loto/OverrideTrainingPartial',
                        type: 'POST',
                        data: {
                            id: result.data.lotoId,
                            bemsId: result.data.aeBemsId,
                            message: result.message,
                            aeName: result.AEName,
                            isTrainingDown: true
                        },
                        dataType: 'html',
                        async: true,
                        success: function (result) {
                            $('#sign-in-partial').html(result);
                            $('#sign-in-partial').show(200);
                        }
                    });
                } else {
                    $('#overrideTrainingLotoModal').hide();
                    toastr.error(result.message, "AE Not Signed In", TOAST_OPTIONS);
                }
            }
        },
        error: function (xhr) {
            $('#overrideTrainingLotoModal').hide();
            toastr.error("Unable to connect to LOTO service.", "Connection Error", TOAST_OPTIONS);
        },
        complete: function () {
            $signInLoader.hide();
        },
        async: true
    });
}

function OverrideTrainingAE(lotoId, overrideTraining) {
    let $visitorSignInLoad = $('#overrideTraining-sign-in-loader');
    $visitorSignInLoad.css('display', 'inline');

    let bemsOrBadge = $('#overrideTrainingBemsID').val();
    bemsOrBadge = $.trim(bemsOrBadge);

    let overrideReason = $('#loto-override-training-comments-dropdown option:selected').text();
    if (overrideReason == "Other") {
        overrideReason = $('#loto-override-training-comments').val();
    }

    $.ajax({
        url: "/Loto/AssignAE",
        type: 'POST',
        data: {
            lotoId: lotoId,
            bemsOrBadge: bemsOrBadge,
            overrideTraining: overrideTraining,
            reasonToOverride: overrideReason
        },
        dataType: 'json',
        cache: false,
        success: function (result) {
            if (result.status === HTTP.STATUS.SUCCESS) {
                $.ajax({
                    url: "/Loto/LotoDetail?id=" + lotoId,
                    cache: false,
                    success: function (html) {
                        $('body').html(html); // refresh content
                        toastr.success(result.message, "AE Signed In", TOAST_OPTIONS_SHORT_TIMEOUT);
                    }
                });
            } else if (result.status === HTTP.STATUS.NOT_MODIFIED && result.reason === HTTP.REASON.ALREADY_EXISTS) {
                var warning = "User " + result.data.aeName + "(" + result.data.aeBemsId + ") is already assigned as AE in the LOTO " + result.data.lotoId + ".";
                $('#ae-warning-partial p').text(warning);
                $('#sign-in').hide();
                $('#ae-warning-partial').show();
            } else {
                toastr.error(result.message, "AE Not Signed In", TOAST_OPTIONS);
            }
        },
        error: function () {
            $('#overrideTrainingLotoModal').hide();
            $visitorSignInLoad.hide();
            toastr.error('Failed', "Failed to override training. Unable to signin AE", TOAST_OPTIONS);
        },
        complete: function () {
            $('#overrideTrainingLotoModal').hide();
            $('#overrideTrainingLotoModal').modal("hide");
            $visitorSignInLoad.hide();
        },
    })
}

function onReturn(){
    $('#ae-warning-partial').hide();
    $('#sign-in').show();
}

function signInVisitorLotoModal(id, name, overrideTraining) {

    if (name == '') {
        toastr.error("Please enter visitor's name", "Invalid Name", TOAST_OPTIONS);
        return;
    }
    else {
        $.ajax({
            url: '/Loto/GetTrainingStatus?id=' + id ,
            type: 'POST',
            contentType: 'application/json; charset=utf-8',
            dataType: 'html',
            success: function (response) {
                $('#acknowledge-briefing-done-visitor').prop('checked', false);
                $('#overrideTrainingLotoModal').html(response); 
                $('#OverrideTrainingLotoConfirmation').append(safeResponseFilter('Complete form X36106 for Non-Boeing Authorized Employees prior to signing in'));
                $('#OverrideTrainingLotoConfirmation').append(safeResponseFilter('<br /><br /><br />'));
                $('#OverrideTrainingLotoConfirmation').show();
                $('#OverrideTrainingPAESubmit').hide();
                $('#OverrideTrainingSubmit').hide();
                $('#override-visitor-training').show();
                $('#OverrideTrainingVisitorSubmit').show();
                $('#overrideTrainingLotoModal').modal('show');
            }
        });
    }
}

function signInVisitor(id, name, overrideTraining) {
    var $visitorSignInLoader = $('#visitor-sign-in-loader');
    $visitorSignInLoader.css('display', 'inline');
    var $visitorSignInLoad = $('#overrideVisitorTraining-sign-in-loader');
    $visitorSignInLoad.css('display', 'inline');

    $.ajax({
        url: "/Loto/AssignVisitor",
        type: 'POST',
        data: {
            lotoId: id,
            visitorName: name,
            overrideTraining: true
        },
        dataType: 'json',
        cache: false,
        success: function (result) {
            if (result.status === HTTP.STATUS.SUCCESS) {
                $.ajax({
                    url: "/Loto/LotoDetail?id=" + id,
                    cache: false,
                    success: function (html) {
                        $('body').html(html); // refresh content
                        toastr.success(result.message, "Visitor Signed In", TOAST_OPTIONS_SHORT_TIMEOUT);
                    }
                });
            } else {
                toastr.error(result.message, "Visitor Not Signed In", TOAST_OPTIONS);
            }
        },
        error: function (xhr) {
            toastr.error("Unable to connect to LOTO service.", "Connection Error", TOAST_OPTIONS);
        },
        complete: function () {
            $('#overrideTrainingLotoModal').modal("hide");
            $visitorSignInLoad.hide();
            $visitorSignInLoader.hide();
        },
        async: true
    });
}

function toggleOtherTextBox(type) {
    if (type == 'employee') {
        let dropdown = $('#loto-override-training-comments-dropdown');
        let otherTextBox = $('#loto-override-training-comments');
        if (dropdown.val() === "other") {
            otherTextBox.show();
        } else {
            otherTextBox.hide();
            otherTextBox.val('');
        }
    }
    else if (type == 'PAEemployee') {
        let dropdown = $('#loto-override-training-comments-dropdown-pae');
        let otherTextBox = $('#loto-override-training-comments-pae');
        if (dropdown.val() === "other") {
            otherTextBox.show();
        } else {
            otherTextBox.hide();
            otherTextBox.val('');
        }
    }
}
function validateOverrideTraining(AEtype) {
    if (AEtype == 'employee') {
        let dropdownValue = $('#loto-override-training-comments-dropdown').val();
        let textboxValue = $.trim($('#loto-override-training-comments').val());
        let checked = $('#acknowledge-briefing-done').prop('checked');

        let isValidSelection = (dropdownValue === 'other' && textboxValue !== '') ||
            (dropdownValue !== 'other' && dropdownValue !== '');

        if (checked && isValidSelection) {
            $('#OverrideTrainingSubmit').attr('disabled', false);
        } else {
            $('#OverrideTrainingSubmit').attr('disabled', true);
        }
    }
    else if (AEtype == 'PAEemployee') {
        let dropdownValue = $('#loto-override-training-comments-dropdown-pae').val();
        let textboxValue = $.trim($('#loto-override-training-comments-pae').val());
        let checked = $('#acknowledge-briefing-done-pae').prop('checked');
        let isValidSelection = (dropdownValue === 'other' && textboxValue !== '') ||
            (dropdownValue !== 'other' && dropdownValue !== '');
        if (checked && isValidSelection) {
            $('#OverrideTrainingPAESubmit').attr('disabled', false);
        } else {
            $('#OverrideTrainingPAESubmit').attr('disabled', true);
        }
    }
    else {
        if ($('#acknowledge-briefing-done-visitor').prop('checked')) {
            $('#OverrideTrainingVisitorSubmit').attr('disabled', false);
        }
        else {
            $('#OverrideTrainingVisitorSubmit').attr('disabled', true);
        }
    }
}

function signOutAe(bemsOrBadge, name, id, idToDelete) {

    var $signOutLoader = $('#sign-out-loader');
    $signOutLoader.css('display', 'inline');

    $.ajax({
        url: "/Loto/SignOutAE",
        type: 'POST',
        data: {
            lotoId: id,
            bemsOrBadge: bemsOrBadge,
            idToDelete: idToDelete,
            aeName: name
        },
        datatype: 'json',
        cache: false,
        success: function (result) {
            $.ajax({
                url: "/Loto/LotoDetail?id=" + id,
                cache: false,
                success: function (html) {
                    $('body').html(html); // refresh content
                    toastr.success(result, "AE Signed Out", TOAST_OPTIONS_SHORT_TIMEOUT)
                }
            });
            $('#signOutAEModal').modal('hide');
        },
        error: function (xhr) {
            console.log(xhr);
            if (xhr.Status == 500) {
                toastr.error("You do not have permission to sign out AEs. Are you logged into Shield?", "Not Allowed", TOAST_OPTIONS);

            } else {
                toastr.error(xhr.responseText, "AE Not Signed Out", TOAST_OPTIONS);
            }
            $signOutLoader.hide();
            closeAeSignoutModal();
        },
        async: true
    });
}

function checkIsolation(isolationRowNumber) {
    let isActive = $("#system-id-" + isolationRowNumber).val().trim() != "" &&
        $("#tag-" + isolationRowNumber).val().trim() != "" &&
        $("#circuit-nomenclature-" + isolationRowNumber).val().trim() != "" &&
        $("#discrete-title-select-" + isolationRowNumber + " :selected").text().trim() !== "" &&
        $("#AssignedPAEBemsId").val() != "";
    if (!isActive) {
        $("#install-button-" + isolationRowNumber).attr("disabled", "disabled");
    }
    else {
        $("#install-button-" + isolationRowNumber).removeAttr("disabled");
    }
}

function checkIsolationForDiscreteLoto(isolationRowNumber) {
    var isActive = $("#tag-" + isolationRowNumber).val().trim() != "" && $("#AssignedPAEBemsId").val() != "";
    if (!isActive) {
        $("#install-button-" + isolationRowNumber).attr("disabled", "disabled");
    } else {
        $("#install-button-" + isolationRowNumber).removeAttr("disabled");
    }
}

function toggleEditState(shouldEdit) {
    if (shouldEdit) {
        $('#work-package').prop('disabled', '');
        $('#reason').prop('disabled', '');
        $('.btn-delete-hecp').prop('disabled', '');
        $('#save-button').prop('hidden', '');
        $('#edit-hecp-button').hide();
    }
}

function signOutModal(aeName, bemsOrBadge, lotoId, Id) {
    $('.modal #sign-out-message').html('Are you sure you want to sign ' + aeName + ' out of this LOTO?');
    $('.modal #sign-out-button').click(function () {
        signOutAe(bemsOrBadge, aeName, lotoId, Id);
    });
    $('#signOutAEModal').modal('show');
};

function closeAeSignoutModal() {
    $('.modal #sign-out-button').off("click");
    $('#signOutAEModal').modal('hide');
}

function signOutAEByBemsOrBadge(lotoId) {
    var bemsOrBadge = $('#sign-out-card #bemsIdOrBadge-input').val();
    if (!!bemsOrBadge) {
        signOutModal("", bemsOrBadge, lotoId);
    }
}

function getSearchHecpsModal() {
    if (!$('#edit-hecp-button').is(":hidden")) {
        toastr.warning("Please Enable Editing to Select HECP", TOAST_OPTIONS);
        return false;
    }
    clearHecpModalValues();
    $('#searchHecpsModal').modal('show');
    getSearchPublishedHecpResultData(1);
}

function getDiscreteHecpsModal() {
    if (!$('#edit-hecp-button').is(":hidden")) {
        toastr.warning("Please Enable Editing to Add HECP", TOAST_OPTIONS);
        return false;
    }
    $('#addDiscreteHecpsModal').modal('show');
}

function addDiscreteHecpRow() {
    $.get('/Loto/AddNewDiscreteHecpRow?count=' + discreteHecpRowCount, function (data) {
        $('#discreteHecpsAdded tbody').append(data);
        discreteHecpRowCount++;
        validateDiscreteLoto();
    });
}
function validateBoeingAESignInButton() {
    if ($("#bemsIdOrBadge-input").val().trim() === '') {
        disableFilterButton(".boeing-loto-sign-in#sign-in-button");
    }
    else {
        enableFilterButton(".boeing-loto-sign-in#sign-in-button");
    }
}
function validateVisitorAESignInButton() {
    if ($("#visitorName-input").val().trim() === '') {
        disableFilterButton(".visitor-loto-sign-in#sign-in-button");
    }
    else {
        enableFilterButton(".visitor-loto-sign-in#sign-in-button");
    }
}

function deleteDiscreteHecpRow(i) {
    $('#discrete-hecp-' + i).remove();
    discreteHecpRowCount--;
    if ($('.discrete-hecp-row').length === 0) {
        disableFilterButton("#add-discrete-hecp-button");
    }
    else {
        validateDiscreteLoto();
    }
};

function hideIsolationTabandLockoutButton() {
    if ($('.multiple-hecp-list').length === 0) {
        $('#isolations').hide();
        $('#LockoutButtonBlock').hide();
    }
}

function enableDisableAddHecpButton() {
    ($('.hecpCheckbox:checked').length > 0) ? $('#add-button').removeAttr("disabled") : $('#add-button').attr('disabled', true);
}

function deleteLotoAssociatedHecps(hecpIndex) {
    $.ajax({
        url: '/Loto/RemoveHecpFromLoto/' + hecpIndex,
        type: 'post',
        data: $('#loto-job-info-form').serialize(),
        success: function (response) {
            $("#loto-job-info-wrapper").html(safeResponseFilter(response));
        },
        error: function () {
            toastr["error"]("Error removing HECP", "Error", TOAST_OPTIONS);
        },
        complete: function () {
            toggleEditState(true);
            hideIsolationTabandLockoutButton();
        }
    });

    if ($('.btn-delete-hecp').length !== 1) {
        toastr.warning("Please save HECP/s before proceeding to add the isolation tags.", TOAST_OPTIONS);
    }
}

function getSearchPublishedHecpResultData(pageNumber) {
    $('#searchComponent').prop('disabled', true);
    $('#resetComponent').prop('disabled', true);
    let isEngineered = $('#hecpType').val();
    let nameText = (($('#txtHecpName').val() == undefined) || ($('#txtHecpName').val() == "")) ? "" : $('#txtHecpName').val().trim();
    let ataText = (($('#txtHecpAta').val() == undefined) || ($('#txtHecpAta').val() == "")) ? "" : $('#txtHecpAta').val().trim();
    let site = $('#Site').val();
    let program = $('#Program').val();
    let minorModelIds = $('#MinorModelIdList').val() === "" ? '' : $('#MinorModelIdList').val().split(',');
    let nameParam = nameText !== "" ? nameText : "";
    let ataParam = ataText !== "" ? ataText : "";
    let existingHecpIdList = '';
    $.each($(".hecpid"), function (index, item) {
        existingHecpIdList += item.value + ',';
    });
    if (existingHecpIdList.length > 0) {
        existingHecpIdList = existingHecpIdList.slice(0, -1);
    }
    $('#program-loading').show();
    $('#lotoHecpContainer').hide();
    let hasValueInLotoSearchHecp = $('.loto-search-hecp').filter(function () {
        return this.value.trim() !== '';
    }).length > 0;

    $.ajax({
        url: '/Loto/GetPublishedHecpsForLoto?site=' + $.trim(site) + '&program=' + $.trim(program) + '&title=' + nameParam + '&ata=' + ataParam + '&minorModelIdList=' + encodeURIComponent(JSON.stringify(minorModelIds)) + "&hecpIdList=" + encodeURIComponent(existingHecpIdList) + "&pageNumber=" + pageNumber + "&isEngineered=" + isEngineered,
        type: 'GET',
        success: function (response) {
            $('#lotoHecpContainer').show();
            $('#lotoHecpContainer').html(response);
            if ($('#dtSearchResult tbody tr').length > 0) {
                arrSelectedHecps.forEach(function (hecp) {
                    let checkbox = $('#hecp-check-' + hecp.HecpTableId);
                    if (checkbox.length) {
                        checkbox.prop('checked', true);
                    }
                });
                $('#dtSearchResult').show();
                $('#lblNoData').hide();
            } else {
                $('#lotoHecpContainer').hide();
                $('#lblNoData').show();
            }
            $('#program-loading').hide();
            $('#resetComponent').prop('disabled', !hasValueInLotoSearchHecp);
        },
        error: function (e) {
            console.log(e);
            toastr.error("Error fetching search result.", "Error");
            $('#resetComponent').prop('disabled', !hasValueInLotoSearchHecp);
        }
    });
}

function resetFiltersInLoto() {
    let nameText = document.getElementById("txtHecpName");
    let ataText = document.getElementById("txtHecpAta"); 
    let isEngineered = document.getElementById("hecpType");

    nameText.value = "";
    ataText.value = "";
    isEngineered.value = "";
    getSearchPublishedHecpResultData(1);

}
function clearHecpModalValues() {
    $('#txtHecpName').val('');
    $('#txtHecpAta').val('');
    $('#dtSearchResult tbody').html('');
}

function onCancelDiscreteHecp() {
    $('#discreteHecpsAdded tbody').html('');
    disableFilterButton("#add-discrete-hecp-button");
}

function onCancelSearchHecp() {
    $('#searchComponent').prop('disabled', true);
    disableFilterButton("#add-button");
}

function addMultipleHecpExisting(lotoId) {

    listOfExistingHecpIds = [];
    allHecpsAssociated = [];
    $('input[id^="hecp-id-"]').each(function (index, item) {
        listOfExistingHecpIds.push(item.value);
    });

    $('.multiple-hecp-list tr.selected-hecp-row').each(function (item, v) {
        let hecpId = v.children[3].children[0].value;
        let lotoAssociatedHecpId = v.children[3].children[1].value;
        let hecpTitle = v.children[0].getElementsByClassName('form-control jobinfo-data-field')[0].value;
        let ata = v.children[1].getElementsByClassName('form-control jobinfo-data-field')[0].value;
        let revision = v.children[2].getElementsByClassName('form-control jobinfo-data-field')[0].value;
        allHecpsAssociated.push({ LotoId: lotoId, HecpTableId: hecpId, HecpTitle: hecpTitle, Ata: ata, HECPRevisionLetter: revision, Id: lotoAssociatedHecpId });
    });
}

function addMultipleDiscreteHecp(lotoId) {
    listOfExistingDiscrete = [];
    $('.discrete-row').each(function (index, item) {
        let discreteTitle = item.children[0].getElementsByClassName("multiple-discrete-title")[0].value;
        let discreteAta = item.children[1].getElementsByClassName("multiple-discrete-ata")[0].value;
        let dicreteRevision = item.children[2].getElementsByClassName("multiple-discrete-revisionletter")[0].value;
        let lotoAssociatedHecpId = item.children[3].children[0].value;
        listOfExistingDiscrete.push({
            HecpTitle: discreteTitle,
            Ata: discreteAta,
            HECPRevisionLetter: dicreteRevision,
            Id: lotoAssociatedHecpId
        });
    });

    $('#discreteHecpsAdded tbody>tr').each(function (i, item) {
        {
            let index = i + 1;
            let hecpTitle = $("#hecp-title-" + index)[0].value.trim();
            let ata = $("#ata-" + index)[0].value.trim();
            let revision = $("#hecp-revision-letter-" + index)[0].value.trim();
            if (!listOfExistingDiscrete.some(i => i.HecpTitle === hecpTitle && i.Ata === ata && i.HECPRevisionLetter === revision)) {
                arrSelectedHecps.push({ LotoId: lotoId, HecpTableId: null, HecpTitle: hecpTitle, Ata: ata, HECPRevisionLetter: revision, LineNumber: null });
            }
        }
    });
    discreteHecpRowCount = 1;
    $('#addDiscreteHecpsModal').modal('hide');
}

function storeSelectedHecp(checkbox) {
    let lotoId = $("#Id").val().trim();
    let row = $(checkbox).closest('tr');
    let hecpId = $(checkbox).val();
    let hecpTitle = row.find('.hecpDetails').text().trim();
    let ata = row.find('.ata').text().trim();
    let revision = row.find('.revision').text().trim();
    let lineNumber = $('.lineNumber').text().trim();

    if (checkbox.checked) {
        if (!arrSelectedHecps.some(hecp => hecp.HecpTableId === hecpId)) {
            arrSelectedHecps.push({ LotoId: lotoId, HecpTableId: hecpId, HecpTitle: hecpTitle, Ata: ata, HECPRevisionLetter: revision, LineNumber: lineNumber });
        }
    } else {
        arrSelectedHecps = arrSelectedHecps.filter(hecp => hecp.HecpTableId !== hecpId);
    }
}

function unique(array) {
    return $.grep(array, function (el, index) {
        return index === $.inArray(el, array);
    });
}

function addMultipleHecps(lotoId) {
    addMultipleHecpExisting(lotoId);
    addMultipleDiscreteHecp(lotoId);
    var uniqueSelectedHecps = unique(arrSelectedHecps);
    uniqueSelectedHecps = uniqueSelectedHecps.concat(allHecpsAssociated).concat(listOfExistingDiscrete);

    $.ajax({
        url: "/Loto/RenderSelectedHecps",
        type: 'POST',
        contentType: 'application/json; charset=utf-8',
        data: JSON.stringify(uniqueSelectedHecps),
        success: function (objectResult) {
            $('#loto-job-info-wrapper').html(safeResponseFilter(objectResult));
            arrSelectedHecps = [];
        },
        error: function (e) {
            console.log(e);
            toastr["error"]("Failed to add HECP or Discrete", "Add HECP or Discrete Error", TOAST_OPTIONS);
        },
        complete: function () {
            toggleEditState(true);
            toastr.warning("Please save HECP/s before proceeding to add the isolation tags.", TOAST_OPTIONS);
        }
    });

    $('#searchHecpsModal').modal('hide');
    clearHecpModalValues();
}

function viewHecpDetails(hecpId) {
    if (hecpId > 0) {
        var navigateToURL = '/Hecp/ViewDetails/?hecpId=' + hecpId + '&targetStep= 8';
        //window.location.href = navigateToURL;
        window.open(navigateToURL);

    }
}

function selectHecpForLoto(hecpId, hecpTitle, revision, ata) {
    if (hecpId > 0) {
        $('#HecpTableId').val(hecpId);
        $('#hecp-title').val(hecpTitle).trigger("change");
        $('#hecp-ata').val(ata).trigger("change");
        $('#hecp-revisionLetter').val(revision).trigger("change");
    }
    $('#searchHecpsModal').modal('hide');
    clearHecpModalValues();
}

/* Start : Isolation tags */

function installIsolationTags(lotoId) {
    if (isInstallingIsolation) {
        toastr.warning("Currently installing an isolation...please wait", "Please Wait", TOAST_OPTIONS)
    }
    else {
        if (!validateIsolationForm()) {
            var isolationTags = new Array();
            var myTab = document.getElementById("isolation-tag-table");

            // LOOP THROUGH EACH ROW OF THE TABLE AFTER HEADER.
            for (i = 1; i < myTab.rows.length; i++) {
                var m = i - 1;
                var tagValue = $('#tag-isolation-' + m).val();

                // GET THE CELLS COLLECTION OF THE CURRENT ROW.
                var objCells = myTab.rows.item(i).cells;
                var isolationTag = {
                    LotoId: lotoId,
                    CircuitId: objCells.item(0).innerHTML.trim(),
                    CircuitName: objCells.item(1).innerHTML.trim(),
                    CircuitPanel: objCells.item(2).innerHTML.trim(),
                    CircuitLocation: objCells.item(3).innerHTML.trim(),
                    State: document.getElementById('isolation-circuit-state-' + m).innerHTML.trim(),
                    HecpId: document.getElementById('isolation-hecpid-' + m).innerHTML.trim(),
                    HecpIsolationId: document.getElementById('isolation-hecp-isolation-id-' + m).innerHTML.trim(),
                    IsLocked: !tagValue || 0 === tagValue.length ? 0 : 1,
                    InstalledByBemsId: document.getElementById('isolation-user-bemsid-' + m).innerHTML.trim(),
                    InstallDateTime: document.getElementById('isolation-install-date-' + m).innerHTML.trim(),
                    Tag: tagValue
                };
                isolationTags.push(isolationTag);
            }
            $.ajax({
                url: "/HECP/InstallIsolationTags",
                type: 'POST',
                data: JSON.stringify(isolationTags),
                dataType: 'json',
                contentType: 'application/json; charset=utf-8',
                error: function (xhr) {
                    var errorMsg = xhr.Status == 400 ? xhr.responseText : "";
                    toastr.error(errorMsg, "Failed to Save Isolation", TOAST_OPTIONS);
                },
                success: function (response) {
                    $.ajax({
                        url: "/Loto/LotoDetail?id=" + lotoId,
                        cache: false,
                        success: function (html) {
                            $('body').html(html); // refresh content
                            toastr.success(" ", "Saved Isolation", TOAST_OPTIONS_SHORT_TIMEOUT);
                        }
                    });
                },
                complete: function () {
                    isInstallingIsolation = false;
                }
            });
        }

    }

    function validateIsolationForm() {
        var isIsolationFormValid = false;
        var tagValue = "";
        var tagIndex = 0;
        var myTab = document.getElementById("isolation-tag-table");
        for (i = 1; i < myTab.rows.length; i++) {
            var tagIndex = i - 1;
            tagValue = $('#tag-isolation-' + tagIndex).val().trim();
            if ((!tagValue || 0 === tagValue.length)) {
                document.getElementById("errorTagisolation").style.visibility = "visible";
                return true;
            }
        }
        document.getElementById("errorTagisolation").style.visibility = "hidden";
        return false;
    }

    /* End : Isolation tags */
}


function toggleHecpSearchButton(inputField) {
    var value = $('.loto-search-hecp').filter(function () {
        return (this.value != undefined && this.value.trim() != '');
    });
    // Disable Search button
    if (value.length > 0) {
            $('#searchComponent').prop('disabled', false);
        }
    else {
        $('#searchComponent').prop('disabled', true);
    }
}

/* Start : Conflict LOCKOUT */
function getLockOutModal(conflictIsolationCount) {
    if (conflictIsolationCount > 0) {
        $('#conflictLockoutModal').modal('show');
    }
    else {
        $('#lockoutModal').modal('show');
    }
};

/* End : Conflict LOCKOUT */

$('#delete-a-loto').hover(function () {
    $(this).css('background-color', 'red');

})

$('#delete-a-loto').mouseleave(function () {
    $(this).css('background-color', 'black');
})

function showDeleteLotoModal() {
    var decodedhecpName = $('#work-package').val();
    //var decodedhecpName = workPackage;
    $('#lblDeleteLotoConfirmation').html("");
    $('#lblDeleteLotoConfirmation').html("Do you want to delete Loto <b>'" + decodedhecpName + "</b>'? Once deleted, the Loto can not be recovered.");
    $('#deleteLotoModal').modal('show');
}

function deleteLoto(lotoId, Site, Program, LineNumber) {
    var WorkPackage = $('#work-package').val();
    $('btn-primary').attr('background-color', 'red');
    console.log('onclick strarted!!');
    var uri = window.location.origin + '/Loto/DeleteLoto/' + lotoId;
    var uri2 = "https://localhost:5001/Loto/DeleteLoto/?lotoId=1166";

    $.ajax({
        url: '/Loto/DeleteLoto?lotoId=' + lotoId,
        type: 'GET',
        dataType: 'json',
        async: true,
        success: function (objectResult) {
            console.log("success");
            if (objectResult.StatusCode == 200) {
                $('#deleteLotoModal').modal('hide');
                toastr.success("Successfully Deleted LOTO '" + WorkPackage + "'", "Deletion Successful", TOAST_OPTIONS);
                setTimeout(function () {
                    window.location.href = window.location.origin + '/Loto/ViewLotos?program=' + Program + '&lineNumber=' + LineNumber + '&site=' + Site;
                }, 1000);
                return;
            }
            else if (objectResult.StatusCode == 500) {
                $('#deleteLotoModal').modal('hide');
                toastr.error("An error occured while deleting the Loto", "Error", TOAST_OPTIONS)
            }
        },
        error: function (objectResult) {
            $('#deleteLotoModal').modal('hide');
            toastr.error("A error occured while deleting the LOTO", "Error", TOAST_OPTIONS);
        }
    })
}

function createViewHecpsLink(id, program) {
    var anchor = $('#hecpLink');
    console.log(anchor.prop("href"));
    console.log($('#hecpLink').attr("href"));
    link = anchor.attr('href');
    console.log("let it begin");
    link = window.location.origin + "/HECP/ViewFilteredHecps?program=" + program + "&hecpName=&siteName=&ataChapterNumber=&ataChapterTitle=&affectedSystem=&hecpStatusName=&isPublishedList=true&pageNumber=1";
    anchor.attr("href", link);
    console.log(link);

}


function printWindow() {
    var tags = $('.lotoIsolationTag');
    var labelTags = $('.lotoIsolationTagLabel');
    if (tags.length != 0) {
        tags.each(function () {
            $(this).css("display", "none");
        });
    }
    if (labelTags.length != 0) {
        labelTags.each(function () {
            $(this).css("display", "block");
        });
    }

    window.print();

    if (labelTags.length != 0) {
        labelTags.each(function () {
            $(this).css("display", "none");
        });
    }
    if (tags.length != 0) {
        tags.each(function () {
            $(this).css("display", "block");
        });
    }
}

//To Validate work package name and save in LotoJobInfoPartial
function validateWorkPackageAndSave(id) {
    let workPackage = $("#work-package").val();
    let workError = $("#work-package-error")[0];
    const workPackageExp = new RegExp("^[a-zA-Z0-9& , -]*$");
    workError.innerHTML = "";
    if (workPackage.trim() === "" || !workPackageExp.test(workPackage)) {
        workError.innerHTML = "Invalid name format - Valid name can contain Alphanumeric characters, '&' , '-' ',' ";
        return;
    }
    else if (validateLotoJobInfo()) {
        saveLoto(id);
        return;
    }
}

//To Validate Mandatory Fields in LotoJobInfoPartial
function validateLotoJobInfo() {
    let reason = $("#reason").val();
    let editReason = $("#edit-reason").val();
    if (reason === "" || editReason === "") {
        toastr.error("Please fill all required fields.", "Error");
        return false;
    }
    else {
        return true;
    }
}

function uniqueDiscreteHecp() {
    let arrDiscreteHecp = [];
    $('.discrete-hecp-row').each(function (i, item) {
        let hecpTitle = item.children[0].getElementsByClassName("discrete-hecp-title")[0].value;
        let hecpLetter = item.children[2].getElementsByClassName("discrete-hecp-letter")[0].value;
        arrDiscreteHecp.push({
            HecpTitle: hecpTitle,
            HecpRevisionLetter: hecpLetter
        });
    })
    if (arrDiscreteHecp.length > 1) {
        let resArr = [];
        arrDiscreteHecp.filter(function (item) {
            let i = resArr.findIndex(x => (x.HecpTitle.toLowerCase() == item.HecpTitle.toLowerCase() && x.HecpRevisionLetter.toLowerCase() == item.HecpRevisionLetter.toLowerCase()));
            if (i <= -1) {
                resArr.push(item);
            }
            return null;
        });
        if (resArr.length !== arrDiscreteHecp.length) {
            toastr.error("There are some duplicate HECPs. Please remove duplicates", "Error");

            return false;
        }
        else
            return true;
    }
    else
        return true;
}

function validateDiscreteLoto(id) {
    if ($(".discrete-hecp-title").toArray().some((el) => $(el).val().trim() === "") || $(".discrete-hecp-letter").toArray().some((el) => $(el).val().trim() === "")) {
        $('#add-discrete-hecp-button').attr('disabled', true);
        return;
    }
    if (!uniqueDiscreteHecp()) {
        $('#add-discrete-hecp-button').attr('disabled', true);
        return;
    }
    else {
        $('#add-discrete-hecp-button').removeAttr('disabled');
        return;
    }
}

function showTransferDialog() {
    $('#transferCommentModal').modal('show');
}

function validateTransferComments() {
    if ($("#transfer-comments").val().trim().length > 0) {
        $('#transfer-button-popup').removeAttr('disabled');
    }
    else {
        $('#transfer-button-popup').attr('disabled', 'disabled');
    }
}

function clearTransferComment() {
    $("#transfer-comments").val('');
    $('#transfer-button-popup').attr('disabled', 'disabled');
}

function validateRequiredField(element) {
    if (element.value.trim() === null || element.value.trim().length === 0) {
        if (element.getAttribute('id') == 'minor-model-select') {
            $('#minor-model-select').css({ borderBottom: "2px solid #dc3545" });
        }
        else {
            document.querySelector('label[for=' + element.getAttribute('id') + ']').className += " less_space";
            $('#' + element.getAttribute('id')).addClass('invalid');
        }
        $('#' + element.getAttribute('id') + '-required').removeAttr("hidden");
        return false;
    }
    return validateWorkPackage();
}

function removeInvalidClass(element) {
    if (element.getAttribute('id') == 'minor-model-select') {
        $('#minor-model-select').css({ borderBottom: "1px solid #9e9e9e" });
    }
    else {
        $('#' + element.getAttribute('id')).removeClass('invalid');
    }
    $('#' + element.getAttribute('id') + '-required').attr("hidden", "hidden");
}

function validateCreateLotoForm() {
    let createLotoForm = document.forms["CreateLotoForm"];
    removeInvalidClass(createLotoForm["work-package"]);
    removeInvalidClass(createLotoForm["reason"]);
    var formElementList = [createLotoForm["work-package"], createLotoForm["reason"]];
    if (typeof (createLotoForm["minor-model-select"]) !== "undefined") {
        removeInvalidClass(createLotoForm["minor-model-select"]);
        formElementList.push(createLotoForm["minor-model-select"]);
    }
    return validateSubmittedValues(formElementList);
}

function validateSubmittedValues(formElementList) {
    let isValid = true;
    for (let i = 0; i < formElementList.length; i++) {
        isValid = validateRequiredField(formElementList[i]) && isValid;
    }
    return isValid;
}

function validateWorkPackage() {
    let wp = new RegExp("^[a-zA-Z0-9& , -]*$");
    if (!wp.test($('#work-package').val().trim()) || $('#work-package').val().trim().length == 0) {
        document.getElementById("work-package-error").innerHTML = "Invalid name format - Valid name can contain Alphanumeric characters, '&' , '-' , ',' ";
        return false;
    }
    else if ($('#edit-work-package').length > 0 && (!wp.test($('#edit-work-package').val().trim()) || $('#edit-work-package').val().trim().length == 0)) {
        document.getElementById("edit-work-package-error").innerHTML = "Invalid name format - Valid name can contain Alphanumeric characters, '&' , '-' , ',' ";
        return false;
    }
    else {
        document.getElementById("work-package-error").innerHTML = "";
        return true;
    }
}

//Funtion to edit and update work package and reason when loto is active.
function showEditButton() {
    $('#editJobInfo').data('isEditOpen', false)
    $('#editJobInfo').removeClass("fa-close");
    $('#editJobInfo').addClass("fa-pencil");
    $('#editJobInfo').css({ color: "#0000FF" });
    $('.btn-save').attr("disabled", "disabled");
    $('#saveJobInfo').css({ color: "darkslategrey" });
}

function showCancelButton() {
    $('#editJobInfo').data('isEditOpen', true);
    $('#editJobInfo').removeClass("fa-pencil");
    $('#editJobInfo').addClass("fa-close");
    $('#editJobInfo').css({ color: "red" });
    $('.btn-save').removeAttr("disabled");
    $('#saveJobInfo').css({ color: "blue" });
}
function editWorkPackageAndReason() {
    //to reset the value of error message
    if ($("#edit-work-package-error").text() !== "") {
        $("#edit-work-package-error").text('');
    }
    if ($('#editJobInfo').hasClass("fa-pencil")) {
        showCancelButton();
        $('.edit-packagetextbox').each(function () { //onclick of edit show the text fields to add wp and reason
            $(".show-textbox").removeAttr("hidden");
            $(this).attr("disabled", false);
            $(this).data('initialValue', $(this).val());
        });
    }
    else {
        showEditButton();
        $('.edit-packagetextbox').each(function () { //onclick of close hide the text fields
            $(".show-textbox").attr("hidden", true)
            $(this).attr("disabled", true)
            $(this).val($(this).data('initialValue'));
        });
    }
}

function saveWorkPackageAndReason(lotoId) {
    if (!($('#editJobInfo').data("isEditOpen"))) {
        return false;
    }

    // if reason is added append it to the existing one else send the previous one
    let reason = $("#edit-reason").val().trim() === "" ? $.trim($('.packagetextbox')[1].value) : $.trim($('.packagetextbox')[1].value) + ", " + $.trim($('.edit-packagetextbox')[1].value);

    //check if work package and reason have valid input
    if (validateWorkPackage()) {
        let updatedJobInfo = {
            LotoId: lotoId,
            WorkPackage: $.trim($('.packagetextbox')[0].value) + ", " + $.trim($('.edit-packagetextbox')[0].value),
            Reason: reason,
            CreatedByBemsId: $("#AssignedPAEBemsId").val(),
        };
        showEditButton();

        $.ajax({
            url: "/LOTO/UpdateLotoJobInfo",
            type: 'PUT',
            data: JSON.stringify(updatedJobInfo),
            dataType: 'html',
            contentType: 'application/json; charset=utf-8',
            error: function (xhr) {
                let errorMsg = xhr.Status == 400 ? xhr.responseText : "";
                toastr.error(errorMsg, "Failed to Update Work Package and reason", TOAST_OPTIONS);
            },
            success: function (response) {
                $('#loto-job-info-form').html(safeResponseFilter(response)); // refresh content
                toastr.success("Updated Work Package and Reason", "Success");
                $('.edit-packagetextbox').each(function () {
                    $(this).attr("disabled", true)
                    $(".show-textbox").attr("hidden", true)
                });
                updateHistoryLog(lotoId ,false); // brings back the updated logs and reloads loto log history partial
            },
            complete: function () {
                $('.edit-packagetextbox').each(function () {
                    $(this).attr("disabled", true);
                    $(".show-textbox").attr("hidden", true);
                })
            }
        });
    }
    //if the input is not valid show the error and keep the buttons enabled
    else {
        showCancelButton();
        $('.edit-packagetextbox').each(function () {
            $(".show-textbox").removeAttr("hidden");
            $(this).attr("disabled", false); 
        });       
    }
}