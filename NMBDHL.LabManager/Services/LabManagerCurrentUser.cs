#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>
/// Answers "who is logged in?" for the Lab Manager pages, using ONLY what the group's login
/// (HomeController.Login) puts in its sign-in cookie:
///   ClaimTypes.NameIdentifier = [User].UserID,  ClaimTypes.Email,  ClaimTypes.Role ("Lab Manager", ...).
/// The Lab Manager subsystem has no login of its own.
/// </summary>
public interface ILabManagerCurrentUser
{
    /// <summary>[User].UserID of the signed-in user, or null when nobody is signed in.</summary>
    int? UserId { get; }

    /// <summary>The role the group's login gave the user, e.g. "Lab Manager".</summary>
    string? Role { get; }

    /// <summary>The email the group's login put in the cookie.</summary>
    string? Email { get; }
}

public class LabManagerCurrentUser : ILabManagerCurrentUser
{
    private readonly IHttpContextAccessor _http;

    public LabManagerCurrentUser(IHttpContextAccessor http) => _http = http;

    /// <summary>The signed-in user, or null.</summary>
    private ClaimsPrincipal? SignedIn =>
        _http.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public int? UserId =>
        int.TryParse(SignedIn?.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : null;

    public string? Role => Clean(SignedIn?.FindFirstValue(ClaimTypes.Role));

    public string? Email => Clean(SignedIn?.FindFirstValue(ClaimTypes.Email));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
