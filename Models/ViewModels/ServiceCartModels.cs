using ArnoxPay.Models;

namespace ArnoxPay.Models.ViewModels;

/// <summary>One line in the customer's multi-service cart (session-backed).</summary>
public class CartLine
{
    public string LineId { get; set; } = Guid.NewGuid().ToString("N");
    public PaymentType PaymentType { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public decimal Amount { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? UnitOfMeasure { get; set; }
    public int? AccommodationId { get; set; }
    public string? AccommodationLabel { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public int? VehiclePosition { get; set; }
    public string? OutstandingChargeKey { get; set; }
    public string? Notes { get; set; }
}

public class UtilityPurchaseForm
{
    public int? AccommodationId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class RentPaymentForm
{
    public int? AccommodationId { get; set; }
    public string? BillingPeriod { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public class ParkingPaymentForm
{
    public int VehicleId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public class VehicleFeePaymentForm
{
    public int VehicleId { get; set; }
    public string? Notes { get; set; }
}

public class ServiceHubViewModel
{
    public string? AccommodationDisplay { get; set; }
    public int VehicleCount { get; set; }
    public int CartCount { get; set; }
    public decimal CartTotal { get; set; }
    public decimal? ElectricityTariff { get; set; }
    public decimal? WaterTariff { get; set; }
    public decimal SubsequentVehicleFee { get; set; }
}
