using ArnoxPay.Models;
namespace ArnoxPay.Data;

public class RepositoryWrapper(AppDbContext context) : IRepositoryWrapper
{
    public IRepositoryBase<Customer> Customer {
        get;
    }
    = new RepositoryBase<Customer>(context);
    public IRepositoryBase<FinancialOfficer> FinancialOfficer {
        get;
    }
    = new RepositoryBase<FinancialOfficer>(context);
    public IRepositoryBase<Accomodation> Accomodation {
        get;
    }
    = new RepositoryBase<Accomodation>(context);
    public IRepositoryBase<AccomodationImage> AccomodationImage {
        get;
    }
    = new RepositoryBase<AccomodationImage>(context);
    public IRepositoryBase<AccommodationIssue> AccommodationIssue {
        get;
    }
    = new RepositoryBase<AccommodationIssue>(context);
    public IRepositoryBase<AccommodationAssignmentRequest> AccommodationAssignmentRequest {
        get;
    }
    = new RepositoryBase<AccommodationAssignmentRequest>(context);
    public IRepositoryBase<Payment> Payment {
        get;
    }
    = new RepositoryBase<Payment>(context);
    public IRepositoryBase<Transaction> Transaction {
        get;
    }
    = new RepositoryBase<Transaction>(context);
    public IRepositoryBase<Notification> Notification {
        get;
    }
    = new RepositoryBase<Notification>(context);
    public IRepositoryBase<ServiceReview> ServiceReview {
        get;
    }
    = new RepositoryBase<ServiceReview>(context);
    public IRepositoryBase<Vehicle> Vehicle {
        get;
    }
    = new RepositoryBase<Vehicle>(context);
    public IRepositoryBase<ParkingArea> ParkingArea {
        get;
    }
    = new RepositoryBase<ParkingArea>(context);
    public IRepositoryBase<ParkingBay> ParkingBay {
        get;
    }
    = new RepositoryBase<ParkingBay>(context);
    public IRepositoryBase<ParkingPermit> ParkingPermit {
        get;
    }
    = new RepositoryBase<ParkingPermit>(context);
    public IRepositoryBase<ParkingSession> ParkingSession {
        get;
    }
    = new RepositoryBase<ParkingSession>(context);
    public void Save() => context.SaveChanges();
    public Task SaveAsync() => context.SaveChangesAsync();
}
