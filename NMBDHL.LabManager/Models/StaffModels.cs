#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NMBHLabSys.LabManagerSubsystem.Infrastructure;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Models;

// =====================================================================
// US-LM-06  Doctors
//   DoctorIndex.cshtml  -> DoctorIndexModel
//   DoctorCreate.cshtml -> DoctorFormModel
//   DoctorEdit.cshtml   -> DoctorFormModel
// =====================================================================
public class DoctorRow
{
    public int DoctorId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string HPCSANumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;

    public string FullName => $"Dr {Name} {Surname}";
    public string ListName => $"{Surname}, {Name}";

    /// <summary>"active" / "password" for data-status (the page's filter).</summary>
    public string StatusKey => MustChangePassword ? "password" : "active";
    public string StatusLabel => MustChangePassword ? "Must Change Password" : "Active";
}

public class DoctorIndexModel
{
    public List<DoctorRow> Doctors { get; set; } = new();
}

/// <summary>Posted by DoctorCreate and DoctorEdit: DoctorId, Name, Surname, HPCSANumber, Email, ContactNumber.</summary>
public class DoctorFormModel
{
    public int DoctorId { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name can be at most 100 characters")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Surname is required")]
    [StringLength(100, ErrorMessage = "Surname can be at most 100 characters")]
    public string? Surname { get; set; }

    [Required(ErrorMessage = "HPCSA number is required")]
    public string? HPCSANumber { get; set; }

    [Required(ErrorMessage = "Email address is required")]
    [ValidEmail]
    [StringLength(255, ErrorMessage = "Email can be at most 255 characters")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Contact number is required")]
    public string? ContactNumber { get; set; }

    // ----- shown on the edit page only -----
    public bool MustChangePassword { get; set; }
    public string StatusLabel => MustChangePassword ? "Must Change Password" : "Active";
}

// =====================================================================
// US-LM-07  Lab technicians
//   TechnicianIndex.cshtml -> TechnicianIndexModel
//       its "Add Technician" dialog posts TechnicianFormModel
//   TechnicianEdit.cshtml  -> TechnicianFormModel
// =====================================================================
public class TechnicianRow
{
    public int TechnicianId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string SAIDNumber { get; set; } = "";
    public string EmployeeNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>AC-07-01: the test types this technician may perform.</summary>
    public List<LookupItem> TestTypes { get; set; } = new();

    public string FullName => $"{Name} {Surname}";
    public string ListName => $"{Surname}, {Name}";
    public string SAIDMasked => IdentityRules.MaskSaId(SAIDNumber);
    public string StatusKey => MustChangePassword ? "password" : "active";
    public string StatusLabel => MustChangePassword ? "Must Change Password" : "Active";
}

public class TechnicianIndexModel
{
    public List<TechnicianRow> Technicians { get; set; } = new();

    /// <summary>Checkboxes in the "Add Technician" dialog.</summary>
    public List<LookupItem> TestTypeOptions { get; set; } = new();
}

/// <summary>
/// Posted by the Add Technician dialog and TechnicianEdit:
/// TechnicianId, Name, Surname, SAIDNumber, EmployeeNumber, Email, ContactNumber
/// and one "TestTypes" value per ticked checkbox.
/// </summary>
public class TechnicianFormModel
{
    public const string NoTestTypeMessage = LabManagerMessages.NoTestType;

    public int TechnicianId { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name can be at most 100 characters")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Surname is required")]
    [StringLength(100, ErrorMessage = "Surname can be at most 100 characters")]
    public string? Surname { get; set; }

    [Required(ErrorMessage = "SA ID number is required")]
    public string? SAIDNumber { get; set; }

    [Required(ErrorMessage = "Employee number is required")]
    [StringLength(20, ErrorMessage = "Employee number can be at most 20 characters")]
    public string? EmployeeNumber { get; set; }

    [Required(ErrorMessage = "Email address is required")]
    [ValidEmail]
    [StringLength(255, ErrorMessage = "Email can be at most 255 characters")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Contact number is required")]
    public string? ContactNumber { get; set; }

    /// <summary>IDs of the ticked test types (AC-07-02 / AC-07-03 / AC-07-06).</summary>
    public List<int> TestTypes { get; set; } = new();

    // ----- for the page (not posted) -----
    public List<LookupItem> TestTypeOptions { get; set; } = new();
    public bool MustChangePassword { get; set; }
    public string StatusLabel => MustChangePassword ? "Must Change Password" : "Active";

    public bool IsTestTypeSelected(int testTypeId) => TestTypes.Contains(testTypeId);
}
