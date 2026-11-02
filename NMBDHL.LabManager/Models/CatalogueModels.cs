#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NMBHLabSys.LabManagerSubsystem.Infrastructure;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Models;

// =====================================================================
// Shared helper: one option in a dropdown or checkbox list.
// =====================================================================
public class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// =====================================================================
// US-LM-01  Test categories
//   TestCategoryIndex.cshtml   -> TestCategoryIndexModel
//   TestCategoryCreate.cshtml  -> TestCategoryFormModel
//   TestCategoryEdit.cshtml    -> TestCategoryFormModel
// =====================================================================
public class TestCategoryRow
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string? Description { get; set; }
    public int TestTypeCount { get; set; }
}

public class TestCategoryIndexModel
{
    public List<TestCategoryRow> Categories { get; set; } = new();
}

/// <summary>Posted by TestCategoryCreate and TestCategoryEdit: CategoryId, CategoryName, Description.</summary>
public class TestCategoryFormModel
{
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Category name is required")]
    [StringLength(150, ErrorMessage = "Category name can be at most 150 characters")]
    public string? CategoryName { get; set; }

    [StringLength(500, ErrorMessage = "Description can be at most 500 characters")]
    public string? Description { get; set; }
}

// =====================================================================
// Sample types (AC-02-09: the maintained list a test type's sample is checked against)
//   SampleTypeIndex.cshtml -> SampleTypeIndexModel (the add form posts SampleTypeName)
// =====================================================================
public class SampleTypeRow
{
    public int SampleTypeId { get; set; }
    public string SampleTypeName { get; set; } = "";
    public int TestTypeCount { get; set; }
}

public class SampleTypeIndexModel
{
    public List<SampleTypeRow> SampleTypes { get; set; } = new();

    /// <summary>The add form's field.</summary>
    public string? SampleTypeName { get; set; }
}

public class SampleTypeFormModel
{
    [Required(ErrorMessage = "Sample type name is required")]
    [StringLength(100, ErrorMessage = "Sample type name can be at most 100 characters")]
    public string? SampleTypeName { get; set; }
}

// =====================================================================
// US-LM-02  Test types
//   TestTypeIndex.cshtml  -> TestTypeIndexModel
//   TestTypeCreate.cshtml -> TestTypeFormModel
//   TestTypeEdit.cshtml   -> TestTypeFormModel
//   TestTypeDetail.cshtml -> TestTypeDetailModel
// =====================================================================
public class TestTypeRow
{
    public int TestTypeId { get; set; }
    public string TestName { get; set; } = "";
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public int? SampleTypeId { get; set; }
    public string SampleTypeName { get; set; } = "";
    public string Units { get; set; } = "";
    public decimal? NormalRangeMin { get; set; }
    public decimal? NormalRangeMax { get; set; }
    public int? TurnaroundTime { get; set; }
    public List<string> ConsumableNames { get; set; } = new();

    /// <summary>"12.0 – 17.0" like the list shows.</summary>
    public string NormalRangeText => NormalRangeMin.HasValue || NormalRangeMax.HasValue
        ? $"{NormalRangeMin:0.0##} – {NormalRangeMax:0.0##}"
        : "";
}

public class TestTypeIndexModel
{
    public List<TestTypeRow> TestTypes { get; set; } = new();

    /// <summary>Fills the "All Categories" filter dropdown.</summary>
    public List<LookupItem> Categories { get; set; } = new();
}

/// <summary>
/// Posted by TestTypeCreate and TestTypeEdit:
/// TestTypeId, TestName, CategoryId, SampleTypeId, Units, NormalRangeMin, NormalRangeMax,
/// TurnaroundTime and one "Consumables" value per ticked checkbox.
/// </summary>
public class TestTypeFormModel : IValidatableObject
{
    public const string RangeMessage = LabManagerMessages.MinExceedsMax;

    public int TestTypeId { get; set; }

    [Required(ErrorMessage = "Test name is required")]
    [StringLength(200, ErrorMessage = "Test name can be at most 200 characters")]
    public string? TestName { get; set; }

    [Required(ErrorMessage = "Select a category")]
    public int? CategoryId { get; set; }

    [Required(ErrorMessage = "Select a sample type")]
    public int? SampleTypeId { get; set; }

    [Required(ErrorMessage = "Units of measurement are required")]
    [StringLength(50, ErrorMessage = "Units can be at most 50 characters")]
    public string? Units { get; set; }

    [Required(ErrorMessage = "Normal range minimum is required")]
    [ModelBinder(typeof(InvariantDecimalModelBinder))]
    public decimal? NormalRangeMin { get; set; }

    [Required(ErrorMessage = "Normal range maximum is required")]
    [ModelBinder(typeof(InvariantDecimalModelBinder))]
    public decimal? NormalRangeMax { get; set; }

    [Required(ErrorMessage = "Turnaround time is required")]
    [Range(1, 100000, ErrorMessage = "Turnaround time must be at least 1 minute")]
    public int? TurnaroundTime { get; set; }

    /// <summary>IDs of the ticked consumables (many-to-many, AC-02-05).</summary>
    public List<int> Consumables { get; set; } = new();

    // ----- dropdown / checkbox data for the page (not posted) -----
    public List<LookupItem> CategoryOptions { get; set; } = new();
    public List<LookupItem> SampleTypeOptions { get; set; } = new();
    public List<LookupItem> ConsumableOptions { get; set; } = new();

    /// <summary>AC-02-08 / TC-LM-02-03.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NormalRangeMin.HasValue && NormalRangeMax.HasValue && NormalRangeMin > NormalRangeMax)
            yield return new ValidationResult(RangeMessage, new[] { nameof(NormalRangeMax) });
    }

    public bool IsConsumableSelected(int consumableId) => Consumables.Contains(consumableId);
}

public class TestTypeDetailModel
{
    public TestTypeRow TestType { get; set; } = new();

    /// <summary>AC-02-07 / AC-07-07 / TC-LM-02-05: everyone qualified to perform this test.</summary>
    public List<TechnicianRow> Technicians { get; set; } = new();

    public int TechnicianCount => Technicians.Count;
    public bool HasTechnicians => Technicians.Any();
}
