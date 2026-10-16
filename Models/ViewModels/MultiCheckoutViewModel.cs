using ArnoxPay.Models;

namespace ArnoxPay.Models.ViewModels;

public class MultiCheckoutViewModel
{
    public List<SelectableCharge> Charges { get; set; } = new();
    public string? Notes { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Customer accommodation label for utility line items.</summary>
    public string? AccommodationDisplay { get; set; }
    public int? AccommodationId { get; set; }

    public decimal? ElectricityPricePerKwh { get; set; }
    public decimal? WaterPricePerKl { get; set; }
    public decimal MaxElectricityUnits { get; set; } = 5000m;
    public decimal MaxWaterUnits { get; set; } = 50m;
}

public class SelectableCharge
{
    public PaymentType PaymentType { get; set; }
    public string DisplayName { get; set; } = "";
    public decimal Amount { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public string? OutstandingChargeKey { get; set; }
    public bool Selected { get; set; }
    public bool AlreadyPaid { get; set; }

    /// <summary>True for Electricity / Water — amount is calculated from units × unit price.</summary>
    public bool RequiresUnits { get; set; }

    /// <summary>Number of units (kWh or kL).</summary>
    public decimal? Quantity { get; set; }

    public decimal? UnitPrice { get; set; }
    public string? UnitOfMeasure { get; set; }
    public string? AccommodationLabel { get; set; }
    public int? AccommodationId { get; set; }
}

public class CardPaymentFormModel
{
    public int PaymentId { get; set; }
    public string CardholderName { get; set; } = "";
    public string CardNumber { get; set; } = "";
    public string ExpiryMonth { get; set; } = "";
    public string ExpiryYear { get; set; } = "";
    public string Cvv { get; set; } = "";
    public decimal Amount { get; set; }
    public string? TransactionReference { get; set; }
}

public class CampusFundViewModel
{
    public decimal CurrentBalance { get; set; }
    public decimal RequiredAmount { get; set; }
    public int? ReturnPaymentId { get; set; }
    public decimal TransferAmount { get; set; }
    public string? CardholderName { get; set; }
    public string? CardNumber { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? Cvv { get; set; }
}
