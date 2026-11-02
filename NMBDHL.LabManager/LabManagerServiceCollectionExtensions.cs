#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Data.Repositories;
using NMBHLabSys.LabManagerSubsystem.Services;

namespace NMBHLabSys.LabManagerSubsystem;

/// <summary>
/// Registers everything the Lab Manager subsystem needs with ONE line in Program.cs:
///
///     builder.Services.AddLabManagerSubsystem(builder.Configuration);
///
/// Every type registered here has a Lab Manager-specific name or lives in the
/// NMBHLabSys.LabManagerSubsystem namespace, so it can't replace or clash with
/// anything the Doctor or Lab Technician subsystems register.
/// </summary>
public static class LabManagerServiceCollectionExtensions
{
    public static IServiceCollection AddLabManagerSubsystem(this IServiceCollection services, IConfiguration configuration)
    {
        // Settings from the "LabManager" section of appsettings.json
        services.Configure<LabManagerOptions>(configuration.GetSection(LabManagerOptions.SectionName));

        // Used to read who the group's login signed in (its cookie claims). Safe if Program.cs also calls it.
        services.AddHttpContextAccessor();

        // Data access (Dapper)
        services.AddSingleton<ILabManagerDb, LabManagerDb>();
        services.AddScoped<ITestCategoryRepository, TestCategoryRepository>();
        services.AddScoped<ISampleTypeRepository, SampleTypeRepository>();
        services.AddScoped<ITestTypeRepository, TestTypeRepository>();
        services.AddScoped<IConsumableRepository, ConsumableRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IDoctorRepository, DoctorRepository>();
        services.AddScoped<ITechnicianRepository, TechnicianRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<ILabManagerData, LabManagerData>();

        // Task services: one method per user-story task (the controller only calls these)
        services.AddScoped<ILabManagerCatalogueService, LabManagerCatalogueService>();   // US-LM-01, US-LM-02
        services.AddScoped<ILabManagerInventoryService, LabManagerInventoryService>();   // US-LM-03, US-LM-04
        services.AddScoped<ILabManagerOrderService, LabManagerOrderService>();           // US-LM-05
        services.AddScoped<ILabManagerStaffService, LabManagerStaffService>();           // US-LM-06, US-LM-07
        services.AddScoped<ILabManagerReportService, LabManagerReportService>();         // dashboard, US-LM-08
        services.AddScoped<ILabManagerAccountService, LabManagerAccountService>();       // US-LM-09

        // Supporting services
        services.AddSingleton<ILabManagerPasswordService, LabManagerPasswordService>();
        services.AddSingleton<ILabManagerReportPdf, LabManagerReportPdf>();
        services.AddScoped<ILabManagerEmailService, LabManagerEmailService>();
        services.AddScoped<ILabManagerCurrentUser, LabManagerCurrentUser>();

        return services;
    }
}
