namespace ArnoxPay.Models;

/// <summary>
/// Configurable fee and tariff settings. Values should be maintained by administrators.
/// Missing values are flagged so they are not silently invented.
/// </summary>
public class FeeSettings
{
    /// <summary>Electricity tariff in ZAR per kWh. Null if not configured.</summary>
    public decimal? ElectricityPricePerKwh { get; set; } = 2.50m;

    /// <summary>Water tariff in ZAR per kilolitre. Null if not configured.</summary>
    public decimal? WaterPricePerKl { get; set; } = 18.00m;

    /// <summary>Fee for the first registered vehicle (normally R0).</summary>
    public decimal FirstVehicleFee { get; set; } = 0m;

    /// <summary>Fee for the second and subsequent vehicles. Null if not yet approved.</summary>
    public decimal? SubsequentVehicleFee { get; set; } = 350.00m;

    /// <summary>Parking permit application fee (required to be free).</summary>
    public decimal ParkingPermitApplicationFee { get; set; } = 0m;

    /// <summary>Maximum allowed electricity units per single purchase.</summary>
    public decimal MaxElectricityUnits { get; set; } = 5000m;

    /// <summary>Maximum allowed water units (kL) per single purchase.</summary>
    public decimal MaxWaterUnits { get; set; } = 50m;
}
