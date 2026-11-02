
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NMBHDL.LabTechnician.Configuration;
using NMBHDL.LabTechnician.Services;

namespace NMBHDL.LabTechnician;

/// <summary>
/// Registers services owned by the Lab Technician subsystem.
/// </summary>
public static class LabTechnicianServiceCollectionExtensions
{
    public static IServiceCollection AddLabTechnicianSubsystem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new LabTechOptions();

        // Reuse the existing Brevo email configuration.
        options.Email.Mode = "Smtp";

        options.Email.SenderName =
            configuration["EmailSettings:FromName"]
            ?? options.Email.SenderName;

        options.Email.SenderEmail =
            configuration["EmailSettings:FromEmail"]
            ?? options.Email.SenderEmail;

        options.Email.SmtpHost =
            configuration["EmailSettings:SmtpServer"]
            ?? options.Email.SmtpHost;

        options.Email.SmtpPort =
            int.TryParse(configuration["EmailSettings:Port"], out var port)
                ? port
                : options.Email.SmtpPort;

        options.Email.EnableSsl = true;

        options.Email.Username =
            configuration["EmailSettings:Username"]
            ?? options.Email.Username;

        options.Email.Password =
            configuration["EmailSettings:Password"]
            ?? options.Email.Password;

        options.LaboratoryName =
            configuration[$"{LabTechOptions.SectionName}:LaboratoryName"]
            ?? options.LaboratoryName;

        services.AddSingleton(options);
        services.AddScoped<LabTechEmailService>();
        services.AddScoped<LabTechReportPdf>();

        return services;
    }
}
