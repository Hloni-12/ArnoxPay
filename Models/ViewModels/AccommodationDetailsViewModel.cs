namespace ArnoxPay.Models.ViewModels;

public class AccommodationDetailsViewModel
{
    public Accomodation Accomodation { get; set; }
    = new();
    public int OccupiedPlaces { get; set; }
    public bool HasPendingRequest { get; set; }
    public bool IsCurrentResident { get; set; }
}
