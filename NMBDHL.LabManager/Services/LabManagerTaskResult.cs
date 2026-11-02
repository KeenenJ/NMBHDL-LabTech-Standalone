#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>One validation problem. Field "" means it belongs to the whole form, not one input.</summary>
public record LabManagerTaskError(string Field, string Message);

/// <summary>
/// What every task method returns (AddCategoryAsync, AdjustStockAsync, PlaceOrdersAsync, ...).
/// The controller turns it into page messages; another caller (a test, an API, another subsystem)
/// can read it directly. Nothing in here knows about web pages.
/// </summary>
public class LabManagerTaskResult
{
    public List<LabManagerTaskError> Errors { get; } = new();

    /// <summary>The record the task was about doesn't exist (any more).</summary>
    public bool NotFound { get; protected set; }

    /// <summary>Shown in the green banner when the task worked.</summary>
    public string? Message { get; set; }

    /// <summary>The task worked, but something after it didn't (e.g. the email couldn't be sent).</summary>
    public string? Warning { get; set; }

    public bool Succeeded => !NotFound && Errors.Count == 0;

    public bool HasErrorFor(string field) => Errors.Any(e => e.Field == field);

    public LabManagerTaskResult AddError(string field, string message)
    {
        if (!Errors.Any(e => e.Field == field && e.Message == message))
            Errors.Add(new LabManagerTaskError(field, message));
        return this;
    }

    public LabManagerTaskResult AddError(string message) => AddError("", message);

    /// <summary>Error text for the red banner when a task has no form to show the errors next to.</summary>
    public string ErrorSummary => string.Join(" ", Errors.Select(e => e.Message).Distinct());

    public static LabManagerTaskResult Ok(string message) => new() { Message = message };

    public static LabManagerTaskResult Missing(string message)
    {
        var result = new LabManagerTaskResult { NotFound = true };
        result.Errors.Add(new LabManagerTaskError("", message));
        return result;
    }

    public static LabManagerTaskResult Failed(string message) => new LabManagerTaskResult().AddError(message);

    /// <summary>
    /// Runs the model's [Required] / [ValidEmail] / [StringLength] ... attributes and its Validate method,
    /// so a task is safe to call even when the data didn't come through MVC model binding.
    /// </summary>
    public static LabManagerTaskResult FromAnnotations(object model)
    {
        var result = new LabManagerTaskResult();
        var problems = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), problems, validateAllProperties: true);

        foreach (var problem in problems)
        {
            var fields = problem.MemberNames.Any() ? problem.MemberNames : new[] { "" };
            foreach (var field in fields)
                result.AddError(field, problem.ErrorMessage ?? "Invalid value");
        }
        return result;
    }
}

/// <summary>A task result that also hands back a value (a new ID, a file, ...).</summary>
public class LabManagerTaskResult<T> : LabManagerTaskResult
{
    public T? Value { get; set; }

    public static LabManagerTaskResult<T> From(LabManagerTaskResult result, T? value = default)
    {
        var typed = new LabManagerTaskResult<T> { Value = value, Message = result.Message, Warning = result.Warning, NotFound = result.NotFound };
        foreach (var e in result.Errors) typed.Errors.Add(e);
        return typed;
    }
}
