namespace ArnoxPay.Models.ViewModels;

public class DashboardViewModel
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Preferred first/display name for personalized greeting (from authenticated user).</summary>
    public string FirstName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
    public int ActiveCustomers { get; set; }
    public int ActiveFinancialOfficers { get; set; }
    public int PendingPayments { get; set; }
    public int AcceptedPayments { get; set; }
    public decimal AcceptedTotal { get; set; }
    public List<Payment> RecentPayments { get; set; } = [];
}
