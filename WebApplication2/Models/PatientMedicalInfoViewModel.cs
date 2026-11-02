
namespace WebApplication2.Models;

public sealed class PatientMedicalInfoViewModel
{
    public List<PatientConditionInfo> Conditions { get; set; } = new();
    public List<PatientAllergyInfo> Allergies { get; set; } = new();
}

public sealed class PatientConditionInfo
{
    public string Condition { get; set; } = "";
    public bool Chronic { get; set; }
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class PatientAllergyInfo
{
    public string Allergy { get; set; } = "";
    public string Reaction { get; set; } = "";
    public string Severity { get; set; } = "";
    public bool Emergency { get; set; }
}
