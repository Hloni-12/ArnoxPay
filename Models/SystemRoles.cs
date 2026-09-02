namespace ArnoxPay.Models;

public static class SystemRoles
{
    public const string Administrator = "Administrator";
    public const string FinancialOfficer = "Financial Officer";
    public const string Customer = "Customer";
    public static readonly string[] All = [Administrator, FinancialOfficer, Customer];
}
