#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NMBHLabSys.LabManagerSubsystem.Infrastructure;
using NMBHLabSys.LabManagerSubsystem.Services;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

/// <summary>
/// The one controller behind every page in Views/LabManager (they all use asp-controller="LabManager").
///
/// It is deliberately thin: each action calls ONE task method on a service, then decides which page
/// or message to show. All rules, database work and emails are in the services:
///
///   LabManagerController.Catalogue.cs  -> ILabManagerCatalogueService  (US-LM-01, US-LM-02, sample types)
///   LabManagerController.Inventory.cs  -> ILabManagerInventoryService  (US-LM-03, US-LM-04)
///   LabManagerController.Orders.cs     -> ILabManagerOrderService      (US-LM-05)
///   LabManagerController.Staff.cs      -> ILabManagerStaffService      (US-LM-06, US-LM-07)
///   LabManagerController.Reports.cs    -> ILabManagerReportService     (dashboard, US-LM-08)
///   LabManagerController.Account.cs    -> ILabManagerAccountService    (US-LM-09)
///
/// [LabManagerOnly] = only users the group's login signed in as "Lab Manager" get in (AC-09-07).
/// [AutoValidateAntiforgeryToken] = every POST must carry the form's anti-forgery token.
/// </summary>
[LabManagerOnly]
[AutoValidateAntiforgeryToken]
public partial class LabManagerController : Controller
{
    private readonly ILabManagerCatalogueService _catalogue;
    private readonly ILabManagerInventoryService _inventory;
    private readonly ILabManagerOrderService _orders;
    private readonly ILabManagerStaffService _staff;
    private readonly ILabManagerReportService _reports;
    private readonly ILabManagerAccountService _account;
    private readonly ILabManagerCurrentUser _currentUser;
    private readonly LabManagerOptions _options;

    public LabManagerController(
        ILabManagerCatalogueService catalogue,
        ILabManagerInventoryService inventory,
        ILabManagerOrderService orders,
        ILabManagerStaffService staff,
        ILabManagerReportService reports,
        ILabManagerAccountService account,
        ILabManagerCurrentUser currentUser,
        IOptions<LabManagerOptions> options)
    {
        _catalogue = catalogue;
        _inventory = inventory;
        _orders = orders;
        _staff = staff;
        _reports = reports;
        _account = account;
        _currentUser = currentUser;
        _options = options.Value;
    }

    // /LabManager -> /LabManager/Dashboard
    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    // Dashboard.cshtml
    [HttpGet]
    public async Task<IActionResult> Dashboard() => View(await _reports.GetDashboardAsync());

    // =================================================================
    // Helpers shared by the partial files
    // =================================================================

    /// <summary>[User].UserID from the group's login. The access filter guarantees a manager on every non-public action.</summary>
    private int CurrentUserId => _currentUser.UserId ?? 0;

    private void SetSuccess(string message) => TempData["Success"] = message;
    private void SetError(string message) => TempData["Error"] = message;

    /// <summary>Puts a task's outcome in the green / red banners of the next page (TempData).</summary>
    private void Flash(LabManagerTaskResult result)
    {
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(result.Message)) SetSuccess(result.Message);
            if (!string.IsNullOrEmpty(result.Warning)) SetError(result.Warning);
        }
        else
        {
            SetError(result.ErrorSummary);
        }
    }

    /// <summary>
    /// Copies a task's errors into ModelState, so asp-validation-summary and asp-validation-for show them.
    /// Messages model binding already added (e.g. [Required]) are not added twice.
    /// </summary>
    private void ShowErrors(LabManagerTaskResult result)
    {
        foreach (var error in result.Errors)
        {
            bool alreadyThere = ModelState.TryGetValue(error.Field, out var entry)
                                && entry.Errors.Any(e => e.ErrorMessage == error.Message);
            if (!alreadyThere) ModelState.AddModelError(error.Field, error.Message);
        }
    }

    /// <summary>
    /// Edit forms post to /LabManager/XxxEdit/{id} and also carry a hidden XxxId field.
    /// The id in the URL wins, so the right record is saved even if the hidden field is stale.
    /// </summary>
    private static int PickId(int? routeId, int postedId) => routeId is > 0 ? routeId.Value : postedId;

    /// <summary>Full address of the group's login page, for emails.</summary>
    private string LoginUrl()
    {
        string path = _options.Auth.LoginPath.StartsWith('/') ? _options.Auth.LoginPath : "/" + _options.Auth.LoginPath;
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{path}";
    }
}
