using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Authentication;

namespace Toklong.Crm.Persistence;

public sealed class CrmDbContext(
    DbContextOptions<CrmDbContext> options)
    : DbContext(options)
{
    public DbSet<CrmUser> Users => Set<CrmUser>();
    public DbSet<CrmRole> Roles => Set<CrmRole>();
    public DbSet<CrmUserRole> UserRoles => Set<CrmUserRole>();
    public DbSet<CrmSession> Sessions => Set<CrmSession>();
    public DbSet<CrmAuthEvent> AuthEvents => Set<CrmAuthEvent>();
    public DbSet<CrmDisputeCase> DisputeCases =>
        Set<CrmDisputeCase>();
    public DbSet<CrmCaseEvent> CaseEvents =>
        Set<CrmCaseEvent>();
    public DbSet<CrmCaseNote> CaseNotes =>
        Set<CrmCaseNote>();
    public DbSet<CrmEvidenceRequest> EvidenceRequests =>
        Set<CrmEvidenceRequest>();
    public DbSet<CrmResolutionAction> ResolutionActions =>
        Set<CrmResolutionAction>();
    public DbSet<CrmRoleChangeRequest> RoleChangeRequests =>
        Set<CrmRoleChangeRequest>();
    public DbSet<CrmAccountEvent> AccountEvents =>
        Set<CrmAccountEvent>();
    public DbSet<CrmCaseAssignment> CaseAssignments =>
        Set<CrmCaseAssignment>();
    public DbSet<CrmSensitiveAccessEvent>
        SensitiveAccessEvents => Set<CrmSensitiveAccessEvent>();

    public override int SaveChanges(
        bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnlyRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAppendOnlyRecords();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("crm");

        var user = modelBuilder.Entity<CrmUser>();
        user.ToTable("users");
        user.HasKey(item => item.Id);
        user.HasIndex(item => new
        {
            item.EntraTenantId,
            item.EntraObjectId
        }).IsUnique();
        user.HasIndex(item => item.Email).IsUnique();
        user.Property(item => item.EntraTenantId)
            .HasMaxLength(36);
        user.Property(item => item.EntraObjectId)
            .HasMaxLength(36);
        user.Property(item => item.Email)
            .HasMaxLength(254);
        user.Property(item => item.DisplayName)
            .HasMaxLength(160);
        user.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
        user.Property(item => item.Version)
            .IsConcurrencyToken();

        var role = modelBuilder.Entity<CrmRole>();
        role.ToTable("roles");
        role.HasKey(item => item.Id);
        role.HasIndex(item => item.Name).IsUnique();
        role.Property(item => item.Name).HasMaxLength(40);
        role.HasData(
            new
            {
                Id = CrmRoleIds.Admin,
                Name = CrmRoles.Admin
            },
            new
            {
                Id = CrmRoleIds.SuperAdmin,
                Name = CrmRoles.SuperAdmin
            });

        var userRole = modelBuilder.Entity<CrmUserRole>();
        userRole.ToTable("user_roles");
        userRole.HasKey(item => new
        {
            item.UserId,
            item.RoleId
        });
        userRole.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        userRole.HasOne<CrmRole>()
            .WithMany()
            .HasForeignKey(item => item.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        var session = modelBuilder.Entity<CrmSession>();
        session.ToTable("sessions");
        session.HasKey(item => item.Id);
        session.HasIndex(item => item.TicketHash).IsUnique();
        session.HasIndex(item => new
        {
            item.UserId,
            item.RevokedAt,
            item.ExpiresAt
        });
        session.Property(item => item.TicketHash)
            .HasMaxLength(64);
        session.Property(item => item.Version)
            .IsConcurrencyToken();
        session.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var authEvent = modelBuilder.Entity<CrmAuthEvent>();
        authEvent.ToTable("auth_events");
        authEvent.HasKey(item => item.Id);
        authEvent.HasIndex(item => item.CreatedAt);
        authEvent.Property(item => item.Name)
            .HasMaxLength(120);
        authEvent.Property(item => item.SubjectReferenceHash)
            .HasMaxLength(64);
        authEvent.Property(item => item.CorrelationId)
            .HasMaxLength(160);
        authEvent.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        var disputeCase = modelBuilder.Entity<CrmDisputeCase>();
        disputeCase.ToTable("dispute_cases");
        disputeCase.HasKey(item => item.Id);
        disputeCase.HasIndex(item => item.TransactionId)
            .IsUnique();
        disputeCase.HasIndex(item => item.CaseNumber)
            .IsUnique();
        disputeCase.HasIndex(item => new
        {
            item.Status,
            item.OpenedAt
        });
        disputeCase.Property(item => item.CaseNumber)
            .HasMaxLength(40);
        disputeCase.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(30);
        disputeCase.Property(item => item.Version)
            .IsConcurrencyToken();
        disputeCase.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.AssignedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var caseEvent = modelBuilder.Entity<CrmCaseEvent>();
        caseEvent.ToTable("case_events");
        caseEvent.HasKey(item => item.Id);
        caseEvent.HasIndex(item => item.IdempotencyKey)
            .IsUnique();
        caseEvent.HasIndex(item => new
        {
            item.CaseId,
            item.CreatedAt
        });
        caseEvent.Property(item => item.Name)
            .HasMaxLength(120);
        caseEvent.Property(item => item.MetadataJson)
            .HasMaxLength(4000);
        caseEvent.Property(item => item.IdempotencyKey)
            .HasMaxLength(160);
        caseEvent.HasOne<CrmDisputeCase>()
            .WithMany()
            .HasForeignKey(item => item.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        caseEvent.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var caseNote = modelBuilder.Entity<CrmCaseNote>();
        caseNote.ToTable("case_notes");
        caseNote.HasKey(item => item.Id);
        caseNote.HasIndex(item => new
        {
            item.CaseId,
            item.CreatedAt
        });
        caseNote.Property(item => item.Body)
            .HasMaxLength(4000);
        caseNote.HasOne<CrmDisputeCase>()
            .WithMany()
            .HasForeignKey(item => item.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        caseNote.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var evidenceRequest =
            modelBuilder.Entity<CrmEvidenceRequest>();
        evidenceRequest.ToTable("evidence_requests");
        evidenceRequest.HasKey(item => item.Id);
        evidenceRequest.HasIndex(item => new
        {
            item.CaseId,
            item.DueAt
        });
        evidenceRequest.Property(item => item.Party)
            .HasConversion<string>()
            .HasMaxLength(20);
        evidenceRequest.Property(item => item.RequiredEvidence)
            .HasMaxLength(2000);
        evidenceRequest.HasOne<CrmDisputeCase>()
            .WithMany()
            .HasForeignKey(item => item.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        evidenceRequest.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var resolution =
            modelBuilder.Entity<CrmResolutionAction>();
        resolution.ToTable("resolution_actions");
        resolution.ToTable(
            table => table.HasCheckConstraint(
                "CK_resolution_actions_distinct_approver",
                "\"ApprovedByUserId\" IS NULL OR \"ApprovedByUserId\" <> \"RecommendedByUserId\""));
        resolution.HasKey(item => item.Id);
        resolution.HasIndex(item => item.IdempotencyKey)
            .IsUnique();
        resolution.HasIndex(item => new
        {
            item.CaseId,
            item.Status
        });
        resolution.Property(item => item.Outcome)
            .HasConversion<string>()
            .HasMaxLength(20);
        resolution.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(30);
        resolution.Property(item => item.ReasonCode)
            .HasMaxLength(80);
        resolution.Property(item => item.Rationale)
            .HasMaxLength(4000);
        resolution.Property(item => item.ReviewReference)
            .HasMaxLength(80);
        resolution.Property(item => item.ReturnedReason)
            .HasMaxLength(2000);
        resolution.Property(item => item.IdempotencyKey)
            .HasMaxLength(160);
        resolution.Property(item => item.Version)
            .IsConcurrencyToken();
        resolution.HasOne<CrmDisputeCase>()
            .WithMany()
            .HasForeignKey(item => item.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        resolution.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.RecommendedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        resolution.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        resolution.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.ReturnedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var roleChange =
            modelBuilder.Entity<CrmRoleChangeRequest>();
        roleChange.ToTable("role_change_requests");
        roleChange.ToTable(
            table => table.HasCheckConstraint(
                "CK_role_change_requests_distinct_approver",
                "\"ApprovedByUserId\" IS NULL OR \"ApprovedByUserId\" <> \"RequestedByUserId\""));
        roleChange.HasKey(item => item.Id);
        roleChange.HasIndex(item => new
        {
            item.TargetUserId,
            item.RoleId,
            item.Status
        });
        roleChange.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(30);
        roleChange.Property(item => item.Version)
            .IsConcurrencyToken();
        roleChange.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);
        roleChange.HasOne<CrmRole>()
            .WithMany()
            .HasForeignKey(item => item.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        roleChange.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        roleChange.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var accountEvent =
            modelBuilder.Entity<CrmAccountEvent>();
        accountEvent.ToTable("account_events");
        accountEvent.HasKey(item => item.Id);
        accountEvent.HasIndex(item => new
        {
            item.TargetUserId,
            item.CreatedAt
        });
        accountEvent.Property(item => item.Name)
            .HasMaxLength(120);
        accountEvent.Property(item => item.MetadataJson)
            .HasMaxLength(2000);
        accountEvent.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);
        accountEvent.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var assignment =
            modelBuilder.Entity<CrmCaseAssignment>();
        assignment.ToTable("case_assignments");
        assignment.HasKey(item => item.Id);
        assignment.HasIndex(item => new
        {
            item.CaseId,
            item.AssignedAt
        });
        assignment.Property(item => item.Reason)
            .HasMaxLength(500);
        assignment.HasOne<CrmDisputeCase>()
            .WithMany()
            .HasForeignKey(item => item.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        assignment.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.AssigneeUserId)
            .OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var sensitiveAccess =
            modelBuilder.Entity<CrmSensitiveAccessEvent>();
        sensitiveAccess.ToTable("sensitive_access_events");
        sensitiveAccess.HasKey(item => item.Id);
        sensitiveAccess.HasIndex(item => new
        {
            item.CaseId,
            item.CreatedAt
        });
        sensitiveAccess.Property(item => item.ResourceType)
            .HasMaxLength(80);
        sensitiveAccess.Property(item => item.ResourceReference)
            .HasMaxLength(160);
        sensitiveAccess.Property(item => item.Purpose)
            .HasMaxLength(500);
        sensitiveAccess.Property(item => item.CorrelationId)
            .HasMaxLength(160);
        sensitiveAccess.HasOne<CrmDisputeCase>()
            .WithMany()
            .HasForeignKey(item => item.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        sensitiveAccess.HasOne<CrmUser>()
            .WithMany()
            .HasForeignKey(item => item.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void EnsureAppendOnlyRecords()
    {
        if (ChangeTracker.Entries()
            .Any(entry =>
                (entry.Entity is CrmAuthEvent or
                    CrmUserRole or
                    CrmCaseEvent or
                    CrmCaseNote or
                    CrmEvidenceRequest or
                    CrmAccountEvent or
                    CrmCaseAssignment or
                    CrmSensitiveAccessEvent) &&
                entry.State is EntityState.Modified or
                    EntityState.Deleted))
            throw new InvalidOperationException(
                "CRM audit, note, and evidence request records are append-only.");
    }
}
