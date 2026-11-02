#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

public partial class LabManagerController
{
    // =================================================================
    // US-LM-08  Generate Management Reports (PDF)
    // =================================================================

    /// <summary>
    /// Reports.cshtml. The form uses method="get":
    ///   /LabManager/Reports                                          -> empty form
    ///   /LabManager/Reports?StartDate=2026-01-01&amp;EndDate=2026-03-31  -> report on screen (TC-LM-08-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Reports(DateTime? startDate, DateTime? endDate)
    {
        // Model binding already rejected values that aren't dates; don't run the report then
        if (!ModelState.IsValid || (startDate is null && endDate is null))
            return View(new Models.ReportsModel { StartDate = startDate, EndDate = endDate, LaboratoryName = _options.LaboratoryName });

        var result = await _reports.RunReportAsync(startDate, endDate);
        if (!result.Succeeded) ShowErrors(result);   // AC-08-03 "Start date cannot be after end date"
        return View(result.Value);
    }

    // AC-08-05 / TC-LM-08-04: "Download PDF" (asp-action="ReportsPdf" asp-route-startDate asp-route-endDate)
    [HttpGet]
    public async Task<IActionResult> ReportsPdf(DateTime? startDate, DateTime? endDate)
    {
        var result = await _reports.BuildReportPdfAsync(startDate, endDate);
        if (!result.Succeeded)
        {
            Flash(result);
            return RedirectToAction(nameof(Reports));
        }
        return File(result.Value.Pdf, "application/pdf", result.Value.FileName);
    }
}
