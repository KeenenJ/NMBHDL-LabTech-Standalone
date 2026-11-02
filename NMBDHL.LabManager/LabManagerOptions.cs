#nullable enable
namespace NMBHLabSys.LabManagerSubsystem;

/// <summary>
/// Every setting the Lab Manager subsystem reads. They all live under one
/// "LabManager" section in appsettings.json, so they can never collide with
/// settings that belong to the Doctor or Lab Technician subsystems.
/// Anything you leave out of appsettings.json keeps the default shown here.
/// </summary>
public class LabManagerOptions
{
    public const string SectionName = "LabManager";

    /// <summary>
    /// Name of the connection string to use. If it isn't found,
    /// "DefaultConnection" is used instead (the group's shared database).
    /// </summary>
    public string ConnectionStringName { get; set; } = "LabManagerConnection";

    /// <summary>Printed on the management report and in emails (AC-08-04).</summary>
    public string LaboratoryName { get; set; } = "Nelson Mandela Bay Haematology Diagnostic Laboratory";

    /// <summary>Order numbers look like ORD-2026-001 (AC-05-02).</summary>
    public string OrderNumberPrefix { get; set; } = "ORD";

    public LabManagerAuthOptions Auth { get; set; } = new();
    public LabManagerReportOptions Report { get; set; } = new();
    public LabManagerEmailOptions Email { get; set; } = new();
}

/// <summary>
/// How the subsystem works with the GROUP'S login (Home/LoginPage, cookie sign-in).
/// The Lab Manager subsystem has no login of its own.
/// </summary>
public class LabManagerAuthOptions
{
    /// <summary>The group's login page: where users are sent when nobody is signed in.</summary>
    public string LoginPath { get; set; } = "/Home/LoginPage";

    /// <summary>
    /// Role names the group's login may give a laboratory manager (its ClaimTypes.Role).
    /// Compared without regard to case or surrounding spaces. The group's login uses "Lab Manager".
    /// </summary>
    public string[] ManagerRoleNames { get; set; } = { "Lab Manager", "Laboratory Manager", "LabManager", "Manager" };

    /// <summary>
    /// Role labels this subsystem shows for staff (worked out from the LabManager / Doctor / LabTechnician tables),
    /// spelled like the group's login.
    /// </summary>
    public string ManagerRole { get; set; } = "Lab Manager";
    public string DoctorRole { get; set; } = "Doctor";
    public string TechnicianRole { get; set; } = "Lab Technician";

    /// <summary>Forgot-password links stop working after this many minutes.</summary>
    public int ResetTokenLifetimeMinutes { get; set; } = 60;
}

public class LabManagerReportOptions
{
    /// <summary>
    /// Which TestResult.Status values count as a performed test on the management report.
    /// Empty (the default) = every TestResult with a CompletionDateTime in the date range counts.
    /// Example once the Lab Technician subsystem has settled its statuses: [ "Completed", "Verified" ]
    /// </summary>
    public string[] CountedResultStatuses { get; set; } = System.Array.Empty<string>();
}

public class LabManagerEmailOptions
{
    /// <summary>
    /// "Pickup": every email is saved as an .eml file in PickupDirectory (open it with Outlook/Mail).
    ///           Best for local testing: you can read temporary passwords and reset links.
    /// "Smtp":   really sends the email through SmtpHost.
    /// "LogOnly": writes the email to the console log only.
    /// </summary>
    public string Mode { get; set; } = "Pickup";

    public string PickupDirectory { get; set; } = "LabManagerMail";

    public string SenderName { get; set; } = "NMB-HLabSys";
    public string SenderEmail { get; set; } = "nmbhlabsys@gmail.com";

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = "";

    /// <summary>Never commit this. Use: dotnet user-secrets set "LabManager:Email:SmtpPassword" "..."</summary>
    public string SmtpPassword { get; set; } = "";
}
