#nullable enable
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NMBHLabSys.LabManagerSubsystem.Infrastructure;

/// <summary>
/// Number inputs always post a dot ("13.5"), but on a computer set to South African
/// English the normal model binder expects a comma ("13,5") and rejects 13.5.
/// This binder accepts either, and is only used on the Lab Manager's decimal fields,
/// so it changes nothing for the rest of the project.
/// </summary>
public class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        string? raw = result.FirstValue?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            // Leave it null; [Required] decides whether that is an error.
            return Task.CompletedTask;
        }

        raw = raw.Replace(" ", "").Replace(',', '.');

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Enter a valid number");
        }

        return Task.CompletedTask;
    }
}
