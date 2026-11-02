#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Services;

namespace NMBHLabSys.LabManagerSubsystem.Infrastructure;

/// <summary>Put on an action to let people in without being signed in (Forgot Password etc.).</summary>
[AttributeUsage(AttributeTargets.Method)]
public class LabManagerPublicAttribute : Attribute { }

/// <summary>Put on the controller: every action then runs LabManagerAccessFilter first.</summary>
public class LabManagerOnlyAttribute : TypeFilterAttribute
{
    public LabManagerOnlyAttribute() : base(typeof(LabManagerAccessFilter)) { }
}

/// <summary>
/// Access to the Lab Manager pages, decided by the GROUP'S login (AC-09-07 / TC-LM-09-06):
///   * not signed in                          -> the group's login page (and back here after signing in)
///   * signed in with a role that isn't Lab Manager -> AccessDenied
///   * signed in as Lab Manager               -> in. If the user has no dbo.LabManager row yet, it is added,
///                                               because [Order].LabManagerID needs it.
/// Nothing else is checked: no separate Lab Manager login, session or forced password change.
/// It also stops the browser caching manager pages, so after Logout the Back button can't show them (TC-LM-09-05).
/// </summary>
public class LabManagerAccessFilter : IAsyncActionFilter
{
    private readonly ILabManagerCurrentUser _currentUser;
    private readonly ILabManagerData _data;
    private readonly LabManagerAuthOptions _auth;
    private readonly IWebHostEnvironment _env;

    public LabManagerAccessFilter(ILabManagerCurrentUser currentUser, ILabManagerData data,
                                  IOptions<LabManagerOptions> options, IWebHostEnvironment env)
    {
        _currentUser = currentUser;
        _data = data;
        _auth = options.Value.Auth;
        _env = env;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var response = context.HttpContext.Response;
        response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["Expires"] = "0";

        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (action?.MethodInfo.GetCustomAttributes(typeof(LabManagerPublicAttribute), true).Any() == true)
        {
            await next();
            return;
        }

        // 1. Signed in through the group's login?
        int? userId = _currentUser.UserId;
        if (userId is null)
        {
            context.Result = new RedirectResult(LoginPathWithReturnUrl(context.HttpContext));
            return;
        }

        // 2. The group's login decided the role
        if (!IsManagerRole(_currentUser.Role))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "LabManager", null);
            return;
        }

        // 3. The manager's [User] row, read from the database this subsystem uses
        var user = await _data.Accounts.GetUserByIdAsync(userId.Value);
        string? loginEmail = _currentUser.Email;
        if (user is null || (loginEmail is not null && !SameEmail(loginEmail, user.Email)))
        {
            // Only happens when the login and this subsystem read different databases
            context.Result = _env.IsDevelopment()
                ? DevMessage($"The group's login signed in {loginEmail ?? "a user"} (UserID {userId}), but in the database the Lab Manager " +
                             (user is null ? $"subsystem reads there is no user with UserID {userId}. " : $"subsystem reads, UserID {userId} is {user.Email}. ") +
                             "Both must use the same database: remove ConnectionStrings:LabManagerConnection from appsettings.json " +
                             "(and the ConnectionStrings__LabManagerConnection environment variable, if you set one).")
                : new RedirectResult(_auth.LoginPath);
            return;
        }

        // 4. Make sure the dbo.LabManager row exists ([Order].LabManagerID points at it)
        if (user.LabManagerId is null)
            await _data.Accounts.EnsureLabManagerRecordAsync(user.UserId);

        // The sidebar can show this instead of the hard-coded name: @ViewData["LabManagerName"]
        if (context.Controller is Controller controller)
        {
            controller.ViewData["LabManagerName"] = string.IsNullOrEmpty(user.FullName) ? user.Email : user.FullName;
            controller.ViewData["LabManagerUserId"] = user.UserId;
        }

        await next();
    }

    /// <summary>The group's login page; its ReturnUrl brings the manager back to the page they asked for.</summary>
    private string LoginPathWithReturnUrl(HttpContext http)
    {
        string returnUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
        return $"{_auth.LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    private bool IsManagerRole(string? role) =>
        !string.IsNullOrWhiteSpace(role)
        && (_auth.ManagerRoleNames ?? Array.Empty<string>()).Any(n => string.Equals(n?.Trim(), role.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool SameEmail(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static ContentResult DevMessage(string text) => new()
    {
        StatusCode = 500,
        ContentType = "text/plain; charset=utf-8",
        Content = text
    };
}
