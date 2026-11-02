#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NMBHLabSys.LabManagerSubsystem.Infrastructure;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Services;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

public partial class LabManagerController
{
    // =================================================================
    // US-LM-09  Login and Security  +  Profile pages
    // =================================================================

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var profile = await _account.GetProfileAsync(CurrentUserId);
        return profile is null ? RedirectToAction(nameof(Logout)) : View(profile);
    }

    [HttpGet]
    public async Task<IActionResult> EditProfile()
    {
        var form = await _account.GetEditProfileAsync(CurrentUserId);
        return form is null ? RedirectToAction(nameof(Logout)) : View(form);
    }

    [HttpPost]
    public async Task<IActionResult> EditProfile(EditProfileModel form)
    {
        var result = await _account.EditProfileAsync(CurrentUserId, form);
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        // Profile.cshtml has no message banner, so no TempData here (it would pop up on a later page)
        return RedirectToAction(nameof(Profile));
    }

    // ChangePassword.cshtml
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordModel());

    // AC-09-04 / TC-LM-09-04
    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel form)
    {
        var result = await _account.ChangePasswordAsync(CurrentUserId, form);
        if (result.NotFound) return RedirectToAction(nameof(Logout));
        if (!result.Succeeded) { ShowErrors(result); return View(new ChangePasswordModel()); }   // never send passwords back

        Flash(result);                       // the dashboard shows TempData, the profile page doesn't
        return RedirectToAction(nameof(Dashboard));
    }

    // ForgotPassword.cshtml (no login needed)
    [HttpGet, LabManagerPublic]
    public IActionResult ForgotPassword() => View(new ForgotPasswordModel());

    // AC-09-03 / TC-LM-09-03
    [HttpPost, LabManagerPublic]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordModel form)
    {
        var result = await _account.RequestPasswordResetAsync(form,
            token => Url.Action(nameof(ResetPassword), "LabManager", new { token }, Request.Scheme)!);

        if (!result.Succeeded && result.Errors.Exists(e => e.Field != "")) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(ForgotPassword));
    }

    // ResetPassword.cshtml, opened from the emailed link: /LabManager/ResetPassword?token=...
    [HttpGet, LabManagerPublic]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        if (!await _account.IsResetTokenValidAsync(token))
        {
            SetError(LabManagerAccountService.ResetLinkInvalidMessage);
            return RedirectToAction(nameof(ForgotPassword));
        }

        // The page's hidden ResetToken field is empty until the view is bound to the model,
        // so the token is also kept (encrypted) in TempData for the POST.
        TempData[ResetTokenKey] = token;
        return View(new ResetPasswordModel { ResetToken = token });
    }

    [HttpPost, LabManagerPublic]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel form)
    {
        string? token = !string.IsNullOrWhiteSpace(form.ResetToken) ? form.ResetToken : TempData[ResetTokenKey] as string;

        var result = await _account.ResetPasswordAsync(token, form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(ForgotPassword)); }
        if (!result.Succeeded)
        {
            ShowErrors(result);
            TempData[ResetTokenKey] = token;   // keep it for the next attempt
            return View(new ResetPasswordModel { ResetToken = token });
        }

        Flash(result);
        return Redirect(_options.Auth.LoginPath);
    }

    private const string ResetTokenKey = "LabManagerResetToken";

    // AccessDenied.cshtml (AC-09-07 / TC-LM-09-06)
    [HttpGet, LabManagerPublic]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = (int)HttpStatusCode.Forbidden;
        return View();
    }

    // AC-09-05 / TC-LM-09-05: sign out of the group's login (its cookie) and go to the login page
    [HttpGet, HttpPost, LabManagerPublic]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        if (HttpContext.RequestServices.GetService<IAuthenticationSchemeProvider>() is { } schemes
            && await schemes.GetDefaultSignOutSchemeAsync() is not null)
        {
            await HttpContext.SignOutAsync();
        }

        return Redirect(_options.Auth.LoginPath);
    }
}
