using ArnoxPay.Models;
namespace ArnoxPay.Data;

public interface IRepositoryWrapper
{
    IRepositoryBase<Customer> Customer {
        get;
    }
    IRepositoryBase<FinancialOfficer> FinancialOfficer {
        get;
    }
    IRepositoryBase<Accomodation> Accomodation {
        get;
    }
    IRepositoryBase<AccomodationImage> AccomodationImage {
        get;
    }
    IRepositoryBase<AccommodationIssue> AccommodationIssue {
        get;
    }
    IRepositoryBase<AccommodationAssignmentRequest> AccommodationAssignmentRequest {
        get;
    }
    IRepositoryBase<Payment> Payment {
        get;
    }
    IRepositoryBase<Transaction> Transaction {
        get;
    }
    IRepositoryBase<Notification> Notification {
        get;
    }
    IRepositoryBase<ServiceReview> ServiceReview {
        get;
    }
    IRepositoryBase<Vehicle> Vehicle {
        get;
    }
    IRepositoryBase<ParkingArea> ParkingArea {
        get;
    }
    IRepositoryBase<ParkingBay> ParkingBay {
        get;
    }
    IRepositoryBase<ParkingPermit> ParkingPermit {
        get;
    }
    IRepositoryBase<ParkingSession> ParkingSession {
        get;
    }
    void Save();
    Task SaveAsync();
}
