
namespace NMBHDL.LabTechnician.Configuration;

/// Configuration settings used exclusively by the Lab Technician subsystem.

public sealed class LabTechOptions
{
    public const string SectionName = "LabTech";

    public string LaboratoryName { get; set; } = "NMBH Laboratory";

    public EmailOptions Email { get; set; } = new();

    public sealed class EmailOptions
    {
        /// LogOnly logs messages, Pickup saves messages locally,
        /// and Smtp sends messages through a configured SMTP server.
        public string Mode { get; set; } = "Pickup";

        public string PickupDirectory { get; set; } = "LabTechMail";

        public string SenderName { get; set; } = "NMBH Laboratory";

        public string SenderEmail { get; set; } = "";

        public string SmtpHost { get; set; } = "";

        public int SmtpPort { get; set; } = 587;

        public bool EnableSsl { get; set; } = true;

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";
    }
}
