#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace NMBHLabSys.LabManagerSubsystem.Infrastructure;

/// <summary>
/// Email check used on every Lab Manager form. The built-in [EmailAddress] accepts
/// "name@clinic" (no dot in the domain); this one needs name@domain.tld.
/// The default message is the exact AC-04-04 text: "Invalid email address".
/// Blank values are left to [Required].
/// </summary>
public partial class ValidEmailAttribute : ValidationAttribute
{
    public ValidEmailAttribute() : base(Rules.LabManagerMessages.InvalidEmail) { }

    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || EmailRegex().IsMatch(s.Trim());

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailRegex();
}
