using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class ToklongDbContext(DbContextOptions<ToklongDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<SaleTransaction> Transactions => Set<SaleTransaction>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ExternalEvent> ExternalEvents => Set<ExternalEvent>();
    public DbSet<ActivationRiskEvent> ActivationRiskEvents => Set<ActivationRiskEvent>();
    public DbSet<SellerAccount> Sellers => Set<SellerAccount>();
    public DbSet<SellerPayoutAccount> SellerPayoutAccounts => Set<SellerPayoutAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transaction = modelBuilder.Entity<SaleTransaction>();
        transaction.ToTable("transactions");
        transaction.HasKey(x => x.Id);
        transaction.HasIndex(x => x.PublicToken).IsUnique();
        transaction.HasIndex(x => x.SellerAccessToken).IsUnique();
        transaction.HasIndex(x => x.BuyerAccessToken).IsUnique();
        transaction.HasIndex(x => x.PaymentReference).IsUnique();
        transaction.Property(x => x.State).HasConversion<string>().HasMaxLength(40);
        transaction.Property(x => x.FulfillmentType).HasConversion<string>().HasMaxLength(30);
        transaction.Property(x => x.Condition).HasConversion<string>().HasMaxLength(30);
        transaction.Property(x => x.DisputeReason).HasConversion<string>().HasMaxLength(40);
        transaction.Property(x => x.Currency).HasMaxLength(3);
        transaction.Property(x => x.ProductName).HasMaxLength(180);
        transaction.Property(x => x.Category).HasMaxLength(80);
        transaction.Property(x => x.SellerDisplayName).HasMaxLength(120);
        transaction.Property(x => x.SellerContact).HasMaxLength(180);
        transaction.Property(x => x.PayoutBankCode).HasMaxLength(20);
        transaction.Property(x => x.PayoutAccountName).HasMaxLength(160);
        transaction.Property(x => x.PayoutAccountNumber).HasMaxLength(15);
        transaction.Property(x => x.BuyerDisplayName).HasMaxLength(120);
        transaction.Property(x => x.BuyerContact).HasMaxLength(180);
        transaction.Property(x => x.PublicToken).HasMaxLength(64);
        transaction.Property(x => x.SellerAccessToken).HasMaxLength(64);
        transaction.Property(x => x.BuyerAccessToken).HasMaxLength(64);
        transaction.Property(x => x.TermsVersion).HasMaxLength(40);
        transaction.Property(x => x.PaymentProvider).HasMaxLength(40);
        transaction.Property(x => x.PaymentReference).HasMaxLength(100);
        transaction.Property(x => x.CarrierCode).HasMaxLength(40);
        transaction.Property(x => x.TrackingNumber).HasMaxLength(120);
        transaction.Property(x => x.DeliveryEventId).HasMaxLength(160);
        transaction.Property(x => x.DigitalDeliveryStatement).HasMaxLength(500);
        transaction.Property(x => x.DigitalManualReviewReference).HasMaxLength(160);
        transaction.Property(x => x.PayoutReference).HasMaxLength(160);
        transaction.Property(x => x.ProductSnapshotHash).HasMaxLength(64);
        transaction.Property(x => x.Version).IsConcurrencyToken();
        transaction.Ignore(x => x.BuyerTotalSatang);
        transaction.HasMany(x => x.AuditEvents).WithOne().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasMany(x => x.ExternalEvents).WithOne().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        transaction.Navigation(nameof(SaleTransaction.AuditEvents)).UsePropertyAccessMode(PropertyAccessMode.Field);
        transaction.Navigation(nameof(SaleTransaction.ExternalEvents)).UsePropertyAccessMode(PropertyAccessMode.Field);

        var audit = modelBuilder.Entity<AuditEvent>();
        audit.ToTable("audit_events");
        audit.HasKey(x => x.Id);
        audit.HasIndex(x => new { x.TransactionId, x.IdempotencyKey }).IsUnique();
        audit.Property(x => x.ActorRole).HasConversion<string>().HasMaxLength(40);
        audit.Property(x => x.FromState).HasConversion<string>().HasMaxLength(40);
        audit.Property(x => x.ToState).HasConversion<string>().HasMaxLength(40);
        audit.Property(x => x.Name).HasMaxLength(120);
        audit.Property(x => x.ActorId).HasMaxLength(160);
        audit.Property(x => x.CorrelationId).HasMaxLength(160);
        audit.Property(x => x.IdempotencyKey).HasMaxLength(200);

        var externalEvent = modelBuilder.Entity<ExternalEvent>();
        externalEvent.ToTable("external_events");
        externalEvent.HasKey(x => x.Id);
        externalEvent.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        externalEvent.Property(x => x.Provider).HasMaxLength(80);
        externalEvent.Property(x => x.EventId).HasMaxLength(160);
        externalEvent.Property(x => x.EventType).HasMaxLength(100);

        var riskEvent = modelBuilder.Entity<ActivationRiskEvent>();
        riskEvent.ToTable("activation_risk_events");
        riskEvent.HasKey(x => x.Id);
        riskEvent.Property(x => x.EventType).HasMaxLength(120);
        riskEvent.Property(x => x.ReasonCode).HasMaxLength(80);
        riskEvent.Property(x => x.Category).HasMaxLength(80);

        var seller = modelBuilder.Entity<SellerAccount>();
        seller.ToTable("sellers");
        seller.HasKey(x => x.Id);
        seller.HasIndex(x => x.PhoneNumber).IsUnique();
        seller.Property(x => x.PhoneNumber).HasMaxLength(16);
        seller.Property(x => x.DisplayName).HasMaxLength(120);
        seller.HasMany(x => x.PayoutAccounts)
            .WithOne()
            .HasForeignKey(x => x.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
        seller.Navigation(nameof(SellerAccount.PayoutAccounts))
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        var payout = modelBuilder.Entity<SellerPayoutAccount>();
        payout.ToTable("seller_payout_accounts");
        payout.HasKey(x => x.Id);
        payout.HasIndex(x => new { x.SellerId, x.AccountNumber });
        payout.Property(x => x.BankCode).HasMaxLength(20);
        payout.Property(x => x.AccountName).HasMaxLength(160);
        payout.Property(x => x.AccountNumber).HasMaxLength(15);

        transaction.HasIndex(x => x.SellerId);
    }
}
