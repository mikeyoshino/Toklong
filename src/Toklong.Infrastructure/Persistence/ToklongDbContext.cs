using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Buyers;
using Toklong.Domain.Authentication;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;
using Toklong.Domain.Notifications;

namespace Toklong.Infrastructure.Persistence;

public sealed class ToklongDbContext(DbContextOptions<ToklongDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<SaleTransaction> Transactions => Set<SaleTransaction>();
    public DbSet<FinancialRetentionRecord>
        FinancialRetentionRecords =>
        Set<FinancialRetentionRecord>();
    public DbSet<RetentionFileDeletion>
        RetentionFileDeletions =>
        Set<RetentionFileDeletion>();
    public DbSet<AgreementAcceptance> AgreementAcceptances =>
        Set<AgreementAcceptance>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ExternalEvent> ExternalEvents => Set<ExternalEvent>();
    public DbSet<ActivationRiskEvent> ActivationRiskEvents => Set<ActivationRiskEvent>();
    public DbSet<BuyerAccount> Buyers => Set<BuyerAccount>();
    public DbSet<BuyerEmailChangeChallenge>
        BuyerEmailChangeChallenges =>
        Set<BuyerEmailChangeChallenge>();
    public DbSet<BuyerEmailChangeAuditEvent>
        BuyerEmailChangeAuditEvents =>
        Set<BuyerEmailChangeAuditEvent>();
    public DbSet<SellerAccount> Sellers => Set<SellerAccount>();
    public DbSet<SellerPayoutAccount> SellerPayoutAccounts => Set<SellerPayoutAccount>();
    public DbSet<MobileSession> MobileSessions => Set<MobileSession>();
    public DbSet<PendingMobileRegistration> PendingMobileRegistrations =>
        Set<PendingMobileRegistration>();
    public DbSet<MobileAccountTermsAcceptance>
        MobileAccountTermsAcceptances =>
        Set<MobileAccountTermsAcceptance>();
    public DbSet<NotificationOutboxMessage> NotificationOutbox =>
        Set<NotificationOutboxMessage>();
    public DbSet<DisputeEvidence> DisputeEvidence =>
        Set<DisputeEvidence>();

    public override int SaveChanges(
        bool acceptAllChangesOnSuccess)
    {
        EnsureAcceptancesAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAcceptancesAreAppendOnly();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transaction = modelBuilder.Entity<SaleTransaction>();
        transaction.ToTable("transactions");
        transaction.HasKey(x => x.Id);
        transaction.HasIndex(x => x.PublicToken).IsUnique();
        transaction.HasIndex(x => x.SellerAccessToken).IsUnique();
        transaction.HasIndex(x => x.BuyerAccessToken).IsUnique();
        transaction.HasIndex(x => x.PaymentReference).IsUnique();
        transaction.HasIndex(x => new
        {
            x.RetentionExpiresAt,
            x.LegalHoldPlacedAt
        });
        transaction.Property(x => x.State).HasConversion<string>().HasMaxLength(40);
        transaction.Property(x => x.InitiatorRole).HasConversion<string>().HasMaxLength(20);
        transaction.Property(x => x.FulfillmentType).HasConversion<string>().HasMaxLength(30);
        transaction.Property(x => x.Condition).HasConversion<string>().HasMaxLength(30);
        transaction.Property(x => x.DisputeReason).HasConversion<string>().HasMaxLength(40);
        transaction.Property(x => x.ExpirationReason).HasConversion<string>().HasMaxLength(40);
        transaction.Property(x => x.TrackingVerificationStatus)
            .HasConversion<string>()
            .HasMaxLength(30);
        transaction.Property(x => x.PayoutReleaseReason)
            .HasConversion<string>()
            .HasMaxLength(40);
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
        transaction.Property(x => x.DeliveryProvinceName).HasMaxLength(100);
        transaction.Property(x => x.DeliveryDistrictName)
            .HasMaxLength(100);
        transaction.Property(x => x.DeliverySubdistrictName)
            .HasMaxLength(100);
        transaction.Property(x => x.DeliveryPostalCode).HasMaxLength(5);
        transaction.Property(x => x.DeliveryAddressLine)
            .HasMaxLength(500);
        transaction.Property(x => x.ShippingOriginAddressLine)
            .HasMaxLength(500);
        transaction.Property(x => x.ShippingOriginProvinceName)
            .HasMaxLength(100);
        transaction.Property(x => x.ShippingOriginDistrictName)
            .HasMaxLength(100);
        transaction.Property(x => x.ShippingOriginSubdistrictName)
            .HasMaxLength(100);
        transaction.Property(x => x.ShippingOriginPostalCode)
            .HasMaxLength(5);
        transaction.Property(x => x.ShippingQuoteProvider)
            .HasMaxLength(80);
        transaction.Property(x => x.ShippingQuoteReference)
            .HasMaxLength(160);
        transaction.Property(x => x.ShippingServiceCode)
            .HasMaxLength(80);
        transaction.Property(x => x.ShippingServiceName)
            .HasMaxLength(160);
        transaction.Property(x => x.ShippingPurchaseReference)
            .HasMaxLength(160);
        transaction.Property(x => x.ShippingProviderTrackingCode)
            .HasMaxLength(120);
        transaction.Property(x => x.ShippingCourierTrackingCode)
            .HasMaxLength(120);
        transaction.Property(x => x.ShippingLastProviderStatus)
            .HasMaxLength(40);
        transaction.Property(x => x.LegalHoldReference)
            .HasMaxLength(160);
        transaction.Property(x => x.LegalHoldReason)
            .HasMaxLength(500);
        transaction.Property(x => x.PublicToken).HasMaxLength(64);
        transaction.Property(x => x.SellerAccessToken).HasMaxLength(64);
        transaction.Property(x => x.BuyerAccessToken).HasMaxLength(64);
        transaction.Property(x => x.TermsVersion).HasMaxLength(40);
        transaction.Property(x => x.PaymentProvider).HasMaxLength(40);
        transaction.Property(x => x.PaymentReference).HasMaxLength(100);
        transaction.Property(x => x.FeePolicyVersion).HasMaxLength(80);
        transaction.Property(x => x.CarrierCode).HasMaxLength(40);
        transaction.Property(x => x.TrackingNumber).HasMaxLength(120);
        transaction.Property(x => x.DeliveryEventId).HasMaxLength(160);
        transaction.Property(x => x.DigitalDeliveryStatement).HasMaxLength(500);
        transaction.Property(x => x.DigitalManualReviewReference).HasMaxLength(160);
        transaction.Property(x => x.DisputeResolutionReference).HasMaxLength(160);
        transaction.Property(x => x.PayoutReference).HasMaxLength(160);
        transaction.Property(x => x.PayoutProvider).HasMaxLength(80);
        transaction.Property(x => x.RefundReference).HasMaxLength(160);
        transaction.Property(x => x.RefundProviderStatus)
            .HasMaxLength(40);
        transaction.Property(x => x.ProductSnapshotHash).HasMaxLength(64);
        transaction.Property(x => x.AgreementCoreSnapshotHash)
            .HasMaxLength(64);
        transaction.Property(x => x.TermsSnapshotHash).HasMaxLength(64);
        transaction.Property(x => x.Version).IsConcurrencyToken();
        transaction.HasMany(x => x.AuditEvents).WithOne().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasMany(x => x.AgreementAcceptances)
            .WithOne()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
        transaction.HasMany(x => x.ExternalEvents).WithOne().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasMany(x => x.Notifications).WithOne().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasMany(x => x.DisputeEvidence)
            .WithOne()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
        transaction.Navigation(nameof(SaleTransaction.AuditEvents)).UsePropertyAccessMode(PropertyAccessMode.Field);
        transaction.Navigation(nameof(SaleTransaction.AgreementAcceptances))
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        transaction.Navigation(nameof(SaleTransaction.ExternalEvents)).UsePropertyAccessMode(PropertyAccessMode.Field);
        transaction.Navigation(nameof(SaleTransaction.Notifications)).UsePropertyAccessMode(PropertyAccessMode.Field);
        transaction.Navigation(nameof(SaleTransaction.DisputeEvidence))
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        var financialRetention =
            modelBuilder.Entity<FinancialRetentionRecord>();
        financialRetention.ToTable(
            "financial_retention_records");
        financialRetention.HasKey(x => x.TransactionId);
        financialRetention.HasIndex(
            x => x.FinancialRetentionExpiresAt);
        financialRetention.Property(x => x.TerminalState)
            .HasConversion<string>()
            .HasMaxLength(40);
        financialRetention.Property(x => x.Currency)
            .HasMaxLength(3);
        financialRetention.Property(x => x.PaymentProvider)
            .HasMaxLength(40);
        financialRetention.Property(x => x.PaymentReference)
            .HasMaxLength(100);
        financialRetention.Property(x => x.RefundReference)
            .HasMaxLength(160);
        financialRetention.Property(x => x.PayoutProvider)
            .HasMaxLength(80);
        financialRetention.Property(x => x.PayoutReference)
            .HasMaxLength(160);

        var retentionFileDeletion =
            modelBuilder.Entity<RetentionFileDeletion>();
        retentionFileDeletion.ToTable(
            "retention_file_deletions");
        retentionFileDeletion.HasKey(
            x => x.Id);
        retentionFileDeletion.HasIndex(
            x => new
            {
                x.TransactionId,
                x.FileReference
            }).IsUnique();
        retentionFileDeletion.Property(
                x => x.FileReference)
            .HasMaxLength(500);
        retentionFileDeletion.HasIndex(
            x => x.QueuedAt);

        var disputeEvidence =
            modelBuilder.Entity<DisputeEvidence>();
        disputeEvidence.ToTable("dispute_evidence");
        disputeEvidence.HasKey(x => x.Id);
        disputeEvidence.HasIndex(x => new
        {
            x.TransactionId,
            x.Party,
            x.IdempotencyKey
        }).IsUnique();
        disputeEvidence.HasIndex(x => new
        {
            x.TransactionId,
            x.Party,
            x.SubmittedAt
        });
        disputeEvidence.Property(x => x.Party)
            .HasConversion<string>()
            .HasMaxLength(20);
        disputeEvidence.Property(x => x.EvidenceType)
            .HasConversion<string>()
            .HasMaxLength(40);
        disputeEvidence.Property(x => x.Description)
            .HasMaxLength(1000);
        disputeEvidence.Property(x => x.StorageReference)
            .HasMaxLength(200);
        disputeEvidence.Property(x => x.ContentType)
            .HasMaxLength(80);
        disputeEvidence.Property(x => x.Sha256)
            .HasMaxLength(64);
        disputeEvidence.Property(x => x.IdempotencyKey)
            .HasMaxLength(100);

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

        var acceptance = modelBuilder.Entity<AgreementAcceptance>();
        acceptance.ToTable("agreement_acceptances");
        acceptance.HasKey(x => x.Id);
        acceptance.HasIndex(x => new
        {
            x.TransactionId,
            x.Role
        }).IsUnique();
        acceptance.HasIndex(x => new
        {
            x.TransactionId,
            x.IdempotencyKey
        }).IsUnique();
        acceptance.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(20);
        acceptance.Property(x => x.VerifiedPhoneNumber)
            .HasMaxLength(180);
        acceptance.Property(x => x.AuthenticationMethod)
            .HasMaxLength(40);
        acceptance.Property(x => x.AgreementCoreSnapshotHash)
            .HasMaxLength(64);
        acceptance.Property(x => x.TermsVersion)
            .HasMaxLength(40);
        acceptance.Property(x => x.TermsSnapshotHash)
            .HasMaxLength(64);
        acceptance.Property(x => x.CorrelationId)
            .HasMaxLength(160);
        acceptance.Property(x => x.IdempotencyKey)
            .HasMaxLength(200);

        var externalEvent = modelBuilder.Entity<ExternalEvent>();
        externalEvent.ToTable("external_events");
        externalEvent.HasKey(x => x.Id);
        externalEvent.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        externalEvent.Property(x => x.Provider).HasMaxLength(80);
        externalEvent.Property(x => x.EventId).HasMaxLength(160);
        externalEvent.Property(x => x.EventType).HasMaxLength(100);

        var notification = modelBuilder.Entity<NotificationOutboxMessage>();
        notification.ToTable("notification_outbox");
        notification.HasKey(x => x.Id);
        notification.HasIndex(x => new
        {
            x.SentAt,
            x.AvailableAt
        });
        notification.Property(x => x.Audience).HasMaxLength(20);
        notification.Property(x => x.Recipient).HasMaxLength(180);
        notification.Property(x => x.Template).HasMaxLength(80);
        notification.Property(x => x.Detail).HasMaxLength(2000);
        notification.Property(x => x.ProviderReference).HasMaxLength(160);

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
        seller.Property(x => x.SavedShippingAddressLine)
            .HasMaxLength(500);
        seller.Property(x => x.SavedShippingProvinceName)
            .HasMaxLength(100);
        seller.Property(x => x.SavedShippingDistrictName)
            .HasMaxLength(100);
        seller.Property(x => x.SavedShippingSubdistrictName)
            .HasMaxLength(100);
        seller.Property(x => x.SavedShippingPostalCode)
            .HasMaxLength(5);
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

        var buyer = modelBuilder.Entity<BuyerAccount>();
        buyer.ToTable("buyers");
        buyer.HasKey(x => x.Id);
        buyer.HasIndex(x => x.PhoneNumber).IsUnique();
        buyer.Property(x => x.PhoneNumber).HasMaxLength(16);
        buyer.Property(x => x.FullName).HasMaxLength(120);
        buyer.Property(x => x.Email).HasMaxLength(254);
        buyer.Property(x => x.SavedAddressLine).HasMaxLength(500);
        buyer.Property(x => x.SavedProvinceName).HasMaxLength(100);
        buyer.Property(x => x.SavedDistrictName).HasMaxLength(100);
        buyer.Property(x => x.SavedSubdistrictName).HasMaxLength(100);
        buyer.Property(x => x.SavedPostalCode).HasMaxLength(5);

        var buyerEmailChangeChallenge =
            modelBuilder.Entity<BuyerEmailChangeChallenge>();
        buyerEmailChangeChallenge.ToTable(
            "buyer_email_change_challenges");
        buyerEmailChangeChallenge.HasKey(x => x.Id);
        buyerEmailChangeChallenge.HasIndex(x => x.BuyerId)
            .IsUnique()
            .HasFilter(
                "\"Status\" IN ('PendingSend', 'Active')");
        buyerEmailChangeChallenge.HasIndex(x => new
        {
            x.BuyerId,
            x.RequestIdempotencyKey
        }).IsUnique();
        buyerEmailChangeChallenge.HasIndex(x => x.ExpiresAt);
        buyerEmailChangeChallenge.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(24);
        buyerEmailChangeChallenge.Property(x => x.PendingEmail)
            .HasMaxLength(254);
        buyerEmailChangeChallenge.Property(x => x.MaskedPendingEmail)
            .HasMaxLength(254);
        buyerEmailChangeChallenge.Property(x => x.CodeDigest)
            .HasMaxLength(64);
        buyerEmailChangeChallenge.Property(
                x => x.RequestIdempotencyKey)
            .HasMaxLength(32);
        buyerEmailChangeChallenge.Property(
            x => x.SourceChallengeId);
        buyerEmailChangeChallenge.Property(
                x => x.VerificationIdempotencyKey)
            .HasMaxLength(32);
        buyerEmailChangeChallenge.Property(x => x.Version)
            .IsConcurrencyToken();
        buyerEmailChangeChallenge.HasOne<BuyerAccount>()
            .WithMany()
            .HasForeignKey(x => x.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        var buyerEmailChangeAudit =
            modelBuilder.Entity<BuyerEmailChangeAuditEvent>();
        buyerEmailChangeAudit.ToTable(
            "buyer_email_change_audit_events");
        buyerEmailChangeAudit.HasKey(x => x.Id);
        buyerEmailChangeAudit.HasIndex(x => x.ChallengeId);
        buyerEmailChangeAudit.Property(x => x.Name)
            .HasMaxLength(100);
        buyerEmailChangeAudit.Property(x => x.DestinationHash)
            .HasMaxLength(64);
        buyerEmailChangeAudit.Property(x => x.MaskedDestination)
            .HasMaxLength(254);
        buyerEmailChangeAudit.Property(x => x.Result)
            .HasMaxLength(100);
        buyerEmailChangeAudit.HasOne<BuyerAccount>()
            .WithMany()
            .HasForeignKey(x => x.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        transaction.HasIndex(x => x.SellerId);
        transaction.HasIndex(x => x.BuyerId);
        transaction.HasIndex(x => x.ShippingProviderTrackingCode)
            .IsUnique();
        transaction.HasOne<BuyerAccount>()
            .WithMany()
            .HasForeignKey(x => x.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        var mobileSession = modelBuilder.Entity<MobileSession>();
        mobileSession.ToTable("mobile_sessions");
        mobileSession.HasKey(x => x.Id);
        mobileSession.HasIndex(x => x.RefreshTokenHash).IsUnique();
        mobileSession.HasIndex(x => x.BuyerId);
        mobileSession.HasIndex(x => x.SellerId);
        mobileSession.Property(x => x.DisplayName).HasMaxLength(120);
        mobileSession.Property(x => x.PhoneNumber).HasMaxLength(16);
        mobileSession.Property(x => x.RefreshTokenHash).HasMaxLength(64);
        mobileSession.Property(x => x.Version).IsConcurrencyToken();

        var pendingRegistration =
            modelBuilder.Entity<PendingMobileRegistration>();
        pendingRegistration.ToTable("pending_mobile_registrations");
        pendingRegistration.HasKey(x => x.Id);
        pendingRegistration.HasIndex(x => x.TicketHash).IsUnique();
        pendingRegistration.HasIndex(x => x.ExpiresAt);
        pendingRegistration.Property(x => x.TicketHash).HasMaxLength(64);
        pendingRegistration.Property(x => x.PhoneNumber).HasMaxLength(16);
        pendingRegistration.Property(x => x.InstallationId)
            .HasMaxLength(32);
        pendingRegistration.Property(x => x.CompletionIdempotencyKey)
            .HasMaxLength(32);
        pendingRegistration.Property(x => x.Version).IsConcurrencyToken();
        pendingRegistration.HasOne<BuyerAccount>()
            .WithMany()
            .HasForeignKey(x => x.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        var accountTermsAcceptance =
            modelBuilder.Entity<MobileAccountTermsAcceptance>();
        accountTermsAcceptance.ToTable(
            "mobile_account_terms_acceptances");
        accountTermsAcceptance.HasKey(x => x.Id);
        accountTermsAcceptance.HasIndex(x => new
        {
            x.BuyerId,
            x.TermsVersion
        }).IsUnique();
        accountTermsAcceptance.HasIndex(x => x.IdempotencyKey)
            .IsUnique();
        accountTermsAcceptance.Property(x => x.TermsVersion)
            .HasMaxLength(40);
        accountTermsAcceptance.Property(x => x.InstallationId)
            .HasMaxLength(32);
        accountTermsAcceptance.Property(x => x.IdempotencyKey)
            .HasMaxLength(32);
        accountTermsAcceptance.HasOne<BuyerAccount>()
            .WithMany()
            .HasForeignKey(x => x.BuyerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void EnsureAcceptancesAreAppendOnly()
    {
        var deletedTransactionIds = ChangeTracker
            .Entries<SaleTransaction>()
            .Where(entry =>
                entry.State == EntityState.Deleted)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();
        if (ChangeTracker
            .Entries<AgreementAcceptance>()
            .Any(entry =>
                entry.State == EntityState.Modified ||
                entry.State == EntityState.Deleted &&
                !deletedTransactionIds.Contains(
                    entry.Entity.TransactionId)))
            throw new InvalidOperationException(
                "Agreement acceptance records are append-only.");

        if (ChangeTracker
            .Entries<MobileAccountTermsAcceptance>()
            .Any(entry =>
                entry.State is
                    EntityState.Modified or
                    EntityState.Deleted))
            throw new InvalidOperationException(
                "Mobile account terms acceptance records are append-only.");

        if (ChangeTracker
            .Entries<BuyerEmailChangeAuditEvent>()
            .Any(entry =>
                entry.State is
                    EntityState.Modified or
                    EntityState.Deleted))
            throw new InvalidOperationException(
                "Buyer email change audit records are append-only.");
    }
}
