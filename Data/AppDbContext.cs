using ArnoxPay.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<FinancialOfficer> FinancialOfficers => Set<FinancialOfficer>();
    public DbSet<Accomodation> Accomodations => Set<Accomodation>();
    public DbSet<AccomodationImage> AccomodationImages => Set<AccomodationImage>();
    public DbSet<AccommodationIssue> AccommodationIssues => Set<AccommodationIssue>();
    public DbSet<AccommodationAssignmentRequest> AccommodationAssignmentRequests => Set<AccommodationAssignmentRequest>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ServiceReview> ServiceReviews => Set<ServiceReview>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ParkingArea> ParkingAreas => Set<ParkingArea>();
    public DbSet<ParkingBay> ParkingBays => Set<ParkingBay>();
    public DbSet<ParkingPermit> ParkingPermits => Set<ParkingPermit>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();
    public DbSet<PaymentItem> PaymentItems => Set<PaymentItem>();
    public DbSet<CampusAccount> CampusAccounts => Set<CampusAccount>();
    public DbSet<CampusAccountTransfer> CampusAccountTransfers => Set<CampusAccountTransfer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        }
        );

        builder.Entity<Customer>(e =>
        {
            e.Property(x => x.UserNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.UserNumber).IsUnique();

            e.HasIndex(x => x.AppUserId).IsUnique();
            e.HasIndex(x => x.UniversityNumber).IsUnique().HasFilter("[UniversityNumber] IS NOT NULL");
            e.HasOne(x => x.AppUser).WithOne(x => x.Customer).HasForeignKey<Customer>(x => x.AppUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Accomodation).WithMany(x => x.Residents).HasForeignKey(x => x.AccomodationId).OnDelete(DeleteBehavior.SetNull);
        }
        );

        builder.Entity<FinancialOfficer>(e =>
        {
            e.HasIndex(x => x.AppUserId).IsUnique();
            e.HasIndex(x => x.EmployeeNumber).IsUnique();
            e.HasOne(x => x.AppUser).WithOne(x => x.FinancialOfficer).HasForeignKey<FinancialOfficer>(x => x.AppUserId).OnDelete(DeleteBehavior.Restrict);
        }
        );

        builder.Entity<Accomodation>(e =>
        {
            e.HasIndex(x => x.UnitNumber).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        }
        );

        builder.Entity<AccomodationImage>(e =>
        {
            e.HasOne(x => x.Accomodation).WithMany(x => x.Images).HasForeignKey(x => x.AccomodationId).OnDelete(DeleteBehavior.Cascade);
        }
        );

        builder.Entity<AccommodationIssue>(e =>
        {
            e.HasOne(x => x.Accomodation).WithMany(x => x.Issues).HasForeignKey(x => x.AccomodationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany(x => x.AccommodationIssues).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        }
        );

        builder.Entity<AccommodationAssignmentRequest>(e =>
        {
            e.HasOne(x => x.Customer).WithMany(x => x.AccommodationAssignmentRequests).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Accomodation).WithMany().HasForeignKey(x => x.AccomodationId).OnDelete(DeleteBehavior.Restrict);
        }
        );

        builder.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.TransactionReference).HasMaxLength(40);
            e.HasIndex(x => x.TransactionReference);
            e.Property(x => x.PaymentMethod).HasMaxLength(40);
            e.Property(x => x.FailureReason).HasMaxLength(300);
            e.Property(x => x.AccountReference).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.Customer).WithMany(x => x.Payments).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FinancialOfficer).WithMany(x => x.ReviewedPayments).HasForeignKey(x => x.FinancialOfficerId).OnDelete(DeleteBehavior.SetNull);
        }
        );

        builder.Entity<Transaction>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Payment).WithOne(x => x.Transaction).HasForeignKey<Transaction>(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ProcessedByFinancialOfficer).WithMany().HasForeignKey(x => x.ProcessedByFinancialOfficerId).OnDelete(DeleteBehavior.SetNull);
        }
        );

        builder.Entity<Notification>(e =>
        {
            e.HasOne(x => x.Customer).WithMany(x => x.Notifications).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        }
        );

        builder.Entity<ServiceReview>(e =>
        {
            e.HasOne(x => x.Customer).WithMany(x => x.ServiceReviews).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        }
        );

        builder.Entity<Vehicle>(e =>
        {
            e.HasIndex(x => x.RegistrationNumber).IsUnique();
            e.HasOne(x => x.Customer).WithMany(x => x.Vehicles).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        }
        );

        builder.Entity<ParkingArea>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        }
        );

        builder.Entity<ParkingBay>(e =>
        {
            e.HasIndex(x => new { x.ParkingAreaId, x.BayNumber }).IsUnique();
            e.HasOne(x => x.ParkingArea).WithMany(x => x.Bays).HasForeignKey(x => x.ParkingAreaId).OnDelete(DeleteBehavior.Cascade);
        }
        );

        builder.Entity<ParkingPermit>(e =>
        {
            e.HasIndex(x => x.PermitNumber).IsUnique();
            e.Property(x => x.Fee).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Customer).WithMany(x => x.ParkingPermits).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vehicle).WithMany(x => x.ParkingPermits).HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        }
        );

        builder.Entity<ParkingSession>(e =>
        {
            e.Property(x => x.Fee).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Vehicle).WithMany(x => x.ParkingSessions).HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ParkingBay).WithMany(x => x.Sessions).HasForeignKey(x => x.ParkingBayId).OnDelete(DeleteBehavior.Restrict);
        }
        );

        builder.Entity<PaymentItem>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Quantity).HasColumnType("decimal(18,4)");
            e.Property(x => x.UnitPrice).HasColumnType("decimal(18,4)");
            e.HasIndex(x => x.OutstandingChargeKey);
            e.HasOne(x => x.Payment).WithMany(x => x.Items).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Accomodation).WithMany().HasForeignKey(x => x.AccomodationId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<CampusAccount>(e =>
        {
            e.Property(x => x.Balance).HasColumnType("decimal(18,2)");
            e.HasIndex(x => x.CustomerId).IsUnique();
            e.HasOne(x => x.Customer).WithOne(x => x.CampusAccount).HasForeignKey<CampusAccount>(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CampusAccountTransfer>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.BalanceBefore).HasColumnType("decimal(18,2)");
            e.Property(x => x.BalanceAfter).HasColumnType("decimal(18,2)");
            e.HasIndex(x => x.TransferReference).IsUnique();
            e.HasOne(x => x.CampusAccount).WithMany(x => x.Transfers).HasForeignKey(x => x.CampusAccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
