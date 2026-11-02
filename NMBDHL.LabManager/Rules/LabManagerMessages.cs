#nullable enable
namespace NMBHLabSys.LabManagerSubsystem.Rules;

/// <summary>
/// The exact error texts the acceptance criteria and test cases ask for, kept in one place
/// so the wording can't drift between the controller, the models and the tests.
/// </summary>
public static class LabManagerMessages
{
    // US-LM-01
    public const string CategoryExists = "Category name already exists";                                // AC-01-03
    public const string CategoryInUse = "Category cannot be deleted as it has associated test types";   // AC-01-07

    // US-LM-02
    public const string TestNameExists = "Test name already exists";                                    // AC-02-03
    public const string MinExceedsMax = "Min value cannot exceed max value";                            // AC-02-08

    // US-LM-03
    public const string ConsumableExists = "Consumable name already exists";                            // AC-03-03
    public const string StockBelowZero = "Stock cannot go below zero";                                  // AC-03-06

    // US-LM-04
    public const string SupplierExists = "Supplier name already exists";                                // AC-04-03
    public const string InvalidEmail = "Invalid email address";                                         // AC-04-04
    public const string SupplierHasConsumables = "Supplier has associated consumables and cannot be deleted"; // AC-04-06

    // US-LM-06
    public const string HpcsaExists = "HPCSA number already registered";                               // AC-06-04
    public const string EmailInUse = "Email address already in use";                                    // AC-06-05

    // US-LM-07
    public const string NoTestType = "At least one test type must be assigned";                         // AC-07-03 / AC-07-06
    public const string SaIdExists = "ID number already registered";                                    // AC-07-04

    // US-LM-08
    public const string StartAfterEnd = "Start date cannot be after end date";                          // AC-08-03
    public const string NoTestsFound = "No tests found for the selected period";                        // AC-08-02

    // US-LM-09
    public const string InvalidCredentials = "Invalid credentials";                                     // AC-09-02
    public const string PasswordTooShort = "Password must be at least 8 characters";                    // TC-LM-09-04
}
