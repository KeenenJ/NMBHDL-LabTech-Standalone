#nullable enable
using NMBHLabSys.LabManagerSubsystem.Data.Repositories;

namespace NMBHLabSys.LabManagerSubsystem.Data;

/// <summary>
/// One object that gives the controller every Lab Manager repository, so the controller
/// can write _data.Suppliers.GetAllAsync() instead of taking eleven constructor parameters.
/// Each repository is still registered on its own, so you can swap or test one at a time.
/// </summary>
public interface ILabManagerData
{
    ITestCategoryRepository TestCategories { get; }
    ISampleTypeRepository SampleTypes { get; }
    ITestTypeRepository TestTypes { get; }
    IConsumableRepository Consumables { get; }
    ISupplierRepository Suppliers { get; }
    IOrderRepository Orders { get; }
    IDoctorRepository Doctors { get; }
    ITechnicianRepository Technicians { get; }
    IAccountRepository Accounts { get; }
    IReportRepository Reports { get; }
    IDashboardRepository Dashboard { get; }
}

public class LabManagerData : ILabManagerData
{
    public LabManagerData(
        ITestCategoryRepository testCategories,
        ISampleTypeRepository sampleTypes,
        ITestTypeRepository testTypes,
        IConsumableRepository consumables,
        ISupplierRepository suppliers,
        IOrderRepository orders,
        IDoctorRepository doctors,
        ITechnicianRepository technicians,
        IAccountRepository accounts,
        IReportRepository reports,
        IDashboardRepository dashboard)
    {
        TestCategories = testCategories;
        SampleTypes = sampleTypes;
        TestTypes = testTypes;
        Consumables = consumables;
        Suppliers = suppliers;
        Orders = orders;
        Doctors = doctors;
        Technicians = technicians;
        Accounts = accounts;
        Reports = reports;
        Dashboard = dashboard;
    }

    public ITestCategoryRepository TestCategories { get; }
    public ISampleTypeRepository SampleTypes { get; }
    public ITestTypeRepository TestTypes { get; }
    public IConsumableRepository Consumables { get; }
    public ISupplierRepository Suppliers { get; }
    public IOrderRepository Orders { get; }
    public IDoctorRepository Doctors { get; }
    public ITechnicianRepository Technicians { get; }
    public IAccountRepository Accounts { get; }
    public IReportRepository Reports { get; }
    public IDashboardRepository Dashboard { get; }
}
