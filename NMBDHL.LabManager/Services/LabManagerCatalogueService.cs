#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Data.Repositories;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>
/// US-LM-01 test categories, US-LM-02 test types, and the sample type list (AC-02-09).
/// One method per task. Each "change" method validates, saves and returns a LabManagerTaskResult.
/// </summary>
public interface ILabManagerCatalogueService
{
    // ---- US-LM-01 ----
    Task<TestCategoryIndexModel> GetCategoryListAsync();                             // AC-01-01
    Task<TestCategoryFormModel?> GetCategoryAsync(int categoryId);                   // AC-01-05
    Task<LabManagerTaskResult> AddCategoryAsync(TestCategoryFormModel form);         // AC-01-02/03/04
    Task<LabManagerTaskResult> EditCategoryAsync(int categoryId, TestCategoryFormModel form);   // AC-01-06
    Task<LabManagerTaskResult> DeleteCategoryAsync(int categoryId);                  // AC-01-07

    // ---- sample types (AC-02-09) ----
    Task<SampleTypeIndexModel> GetSampleTypeListAsync();
    Task<LabManagerTaskResult> AddSampleTypeAsync(SampleTypeFormModel form);
    Task<LabManagerTaskResult> DeleteSampleTypeAsync(int sampleTypeId);

    // ---- US-LM-02 ----
    Task<TestTypeIndexModel> GetTestTypeListAsync();                                 // AC-02-01
    Task<TestTypeFormModel?> GetTestTypeFormAsync(int testTypeId);
    Task<TestTypeFormModel> FillTestTypeOptionsAsync(TestTypeFormModel form);
    Task<TestTypeDetailModel?> GetTestTypeDetailAsync(int testTypeId);               // AC-02-07
    Task<LabManagerTaskResult> AddTestTypeAsync(TestTypeFormModel form);             // AC-02-02..06/08/09
    Task<LabManagerTaskResult> EditTestTypeAsync(int testTypeId, TestTypeFormModel form);
    Task<LabManagerTaskResult> DeleteTestTypeAsync(int testTypeId);
}

public class LabManagerCatalogueService : ILabManagerCatalogueService
{
    private readonly ILabManagerData _data;
    public LabManagerCatalogueService(ILabManagerData data) { _data = data; }

    // =================================================================
    // US-LM-01  Test categories
    // =================================================================
    public async Task<TestCategoryIndexModel> GetCategoryListAsync() =>
        new() { Categories = await _data.TestCategories.GetAllAsync() };

    public Task<TestCategoryFormModel?> GetCategoryAsync(int categoryId) => _data.TestCategories.GetByIdAsync(categoryId);

    public async Task<LabManagerTaskResult> AddCategoryAsync(TestCategoryFormModel form)
    {
        var result = await ValidateCategoryAsync(form, excludeCategoryId: 0);
        if (!result.Succeeded) return result;

        try
        {
            await _data.TestCategories.CreateAsync(form.CategoryName!, form.Description);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.CategoryName), LabManagerMessages.CategoryExists);
        }
        return LabManagerTaskResult.Ok($"Category '{form.CategoryName}' was added.");
    }

    public async Task<LabManagerTaskResult> EditCategoryAsync(int categoryId, TestCategoryFormModel form)
    {
        form.CategoryId = categoryId;
        var result = await ValidateCategoryAsync(form, excludeCategoryId: categoryId);
        if (!result.Succeeded) return result;

        try
        {
            if (!await _data.TestCategories.UpdateAsync(categoryId, form.CategoryName!, form.Description))
                return LabManagerTaskResult.Missing("That category no longer exists.");
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.CategoryName), LabManagerMessages.CategoryExists);
        }
        return LabManagerTaskResult.Ok($"Category '{form.CategoryName}' was updated.");
    }

    public async Task<LabManagerTaskResult> DeleteCategoryAsync(int categoryId) =>
        await _data.TestCategories.DeleteAsync(categoryId) switch
        {
            DeleteResult.Deleted => LabManagerTaskResult.Ok("Category deleted."),
            DeleteResult.InUse => LabManagerTaskResult.Failed(LabManagerMessages.CategoryInUse),
            _ => LabManagerTaskResult.Missing("That category no longer exists.")
        };

    private async Task<LabManagerTaskResult> ValidateCategoryAsync(TestCategoryFormModel form, int excludeCategoryId)
    {
        form.CategoryName = LmText.Clean(form.CategoryName);
        form.Description = LmText.CleanOrNull(form.Description);

        var result = LabManagerTaskResult.FromAnnotations(form);    // AC-01-04 blank name
        if (!result.HasErrorFor(nameof(form.CategoryName))
            && await _data.TestCategories.NameExistsAsync(form.CategoryName, excludeCategoryId))
            result.AddError(nameof(form.CategoryName), LabManagerMessages.CategoryExists);   // AC-01-03
        return result;
    }

    // =================================================================
    // Sample types
    // =================================================================
    public async Task<SampleTypeIndexModel> GetSampleTypeListAsync() =>
        new() { SampleTypes = await _data.SampleTypes.GetAllAsync() };

    public async Task<LabManagerTaskResult> AddSampleTypeAsync(SampleTypeFormModel form)
    {
        form.SampleTypeName = LmText.Clean(form.SampleTypeName);
        var result = LabManagerTaskResult.FromAnnotations(form);
        if (!result.HasErrorFor(nameof(form.SampleTypeName)) && await _data.SampleTypes.NameExistsAsync(form.SampleTypeName))
            result.AddError(nameof(form.SampleTypeName), "Sample type already exists");
        if (!result.Succeeded) return result;

        try
        {
            await _data.SampleTypes.CreateAsync(form.SampleTypeName);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.SampleTypeName), "Sample type already exists");
        }
        return LabManagerTaskResult.Ok($"Sample type '{form.SampleTypeName}' was added.");
    }

    public async Task<LabManagerTaskResult> DeleteSampleTypeAsync(int sampleTypeId) =>
        await _data.SampleTypes.DeleteAsync(sampleTypeId) switch
        {
            DeleteResult.Deleted => LabManagerTaskResult.Ok("Sample type deleted."),
            DeleteResult.InUse => LabManagerTaskResult.Failed("Sample type cannot be deleted as it is used by test types"),
            _ => LabManagerTaskResult.Missing("That sample type no longer exists.")
        };

    // =================================================================
    // US-LM-02  Test types
    // =================================================================
    public async Task<TestTypeIndexModel> GetTestTypeListAsync() => new()
    {
        TestTypes = await _data.TestTypes.GetAllAsync(),
        Categories = await _data.TestCategories.GetLookupAsync()
    };

    public async Task<TestTypeFormModel?> GetTestTypeFormAsync(int testTypeId)
    {
        var row = await _data.TestTypes.GetByIdAsync(testTypeId);
        if (row is null) return null;

        return await FillTestTypeOptionsAsync(new TestTypeFormModel
        {
            TestTypeId = row.TestTypeId,
            TestName = row.TestName,
            CategoryId = row.CategoryId,
            SampleTypeId = row.SampleTypeId,
            Units = row.Units,
            NormalRangeMin = row.NormalRangeMin,
            NormalRangeMax = row.NormalRangeMax,
            TurnaroundTime = row.TurnaroundTime,
            Consumables = await _data.TestTypes.GetConsumableIdsAsync(testTypeId)
        });
    }

    public async Task<TestTypeFormModel> FillTestTypeOptionsAsync(TestTypeFormModel form)
    {
        form.CategoryOptions = await _data.TestCategories.GetLookupAsync();
        form.SampleTypeOptions = await _data.SampleTypes.GetLookupAsync();
        form.ConsumableOptions = await _data.Consumables.GetLookupAsync();
        return form;
    }

    public async Task<TestTypeDetailModel?> GetTestTypeDetailAsync(int testTypeId)
    {
        var row = await _data.TestTypes.GetByIdAsync(testTypeId);
        if (row is null) return null;
        return new TestTypeDetailModel
        {
            TestType = row,
            Technicians = await _data.TestTypes.GetAssignedTechniciansAsync(testTypeId)
        };
    }

    public async Task<LabManagerTaskResult> AddTestTypeAsync(TestTypeFormModel form)
    {
        var result = await ValidateTestTypeAsync(form, excludeTestTypeId: 0);
        if (!result.Succeeded) return result;

        try
        {
            await _data.TestTypes.CreateAsync(form);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.TestName), LabManagerMessages.TestNameExists);
        }

        return LabManagerTaskResult.Ok($"Test type '{form.TestName}' was added" +
            (form.Consumables.Count > 0 ? $" with {form.Consumables.Count} consumable(s)." : "."));
    }

    public async Task<LabManagerTaskResult> EditTestTypeAsync(int testTypeId, TestTypeFormModel form)
    {
        form.TestTypeId = testTypeId;
        var result = await ValidateTestTypeAsync(form, excludeTestTypeId: testTypeId);
        if (!result.Succeeded) return result;

        try
        {
            if (!await _data.TestTypes.UpdateAsync(testTypeId, form))
                return LabManagerTaskResult.Missing("That test type no longer exists.");
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.TestName), LabManagerMessages.TestNameExists);
        }
        return LabManagerTaskResult.Ok($"Test type '{form.TestName}' was updated.");
    }

    public async Task<LabManagerTaskResult> DeleteTestTypeAsync(int testTypeId) =>
        await _data.TestTypes.DeleteAsync(testTypeId) switch
        {
            DeleteResult.Deleted => LabManagerTaskResult.Ok("Test type deleted."),
            DeleteResult.InUse => LabManagerTaskResult.Failed("Test type cannot be deleted because test requests or results use it."),
            _ => LabManagerTaskResult.Missing("That test type no longer exists.")
        };

    private async Task<LabManagerTaskResult> ValidateTestTypeAsync(TestTypeFormModel form, int excludeTestTypeId)
    {
        form.TestName = LmText.Clean(form.TestName);
        form.Units = LmText.Clean(form.Units);
        form.Consumables = (form.Consumables ?? new()).Where(c => c > 0).Distinct().ToList();

        // AC-02-06 blank fields and AC-02-08 min > max
        var result = LabManagerTaskResult.FromAnnotations(form);

        // AC-02-03
        if (!result.HasErrorFor(nameof(form.TestName)) && form.TestName.Length > 0
            && await _data.TestTypes.NameExistsAsync(form.TestName, excludeTestTypeId))
            result.AddError(nameof(form.TestName), LabManagerMessages.TestNameExists);

        // AC-02-04: exactly one, real category
        if (form.CategoryId is int categoryId && !await _data.TestCategories.ExistsAsync(categoryId))
            result.AddError(nameof(form.CategoryId), "Select a category from the list");

        // AC-02-09: the sample type must be on the maintained list
        if (form.SampleTypeId is int sampleTypeId && !await _data.SampleTypes.ExistsAsync(sampleTypeId))
            result.AddError(nameof(form.SampleTypeId), "Select a sample type from the maintained list");

        // AC-02-05: any number of consumables, each must exist
        if (form.Consumables.Count > 0)
        {
            var found = await _data.Consumables.GetByIdsAsync(form.Consumables);
            if (found.Count != form.Consumables.Count)
                result.AddError(nameof(form.Consumables), "One or more selected consumables no longer exist");
        }
        return result;
    }
}

/// <summary>Small text helpers shared by the services.</summary>
internal static class LmText
{
    public static string Clean(string? value) => (value ?? "").Trim();
    public static string? CleanOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
