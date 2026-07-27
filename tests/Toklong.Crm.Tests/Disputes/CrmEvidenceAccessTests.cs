using System.Security.Claims;
using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Transactions.ManageDisputeEvidence;
using Toklong.Crm.Authentication;
using Toklong.Crm.Disputes;
using Toklong.Crm.Persistence;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Crm.Tests.Disputes;

public sealed class CrmEvidenceAccessTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Active_admin_can_open_case_evidence_and_access_is_audited()
    {
        await using var core = CoreDatabase();
        await using var crm = CrmDatabase();
        var content =
            new byte[] { 0xff, 0xd8, 1, 2, 3, 0xff, 0xd9 };
        var transaction = DisputedTransaction();
        var evidence = transaction.RecordDisputeEvidence(
            Guid.NewGuid(),
            DisputeEvidenceParty.Buyer,
            transaction.BuyerId!.Value,
            DisputeEvidenceType.Item,
            "ภาพสภาพสินค้าหลังเปิดกล่อง",
            "evidence:test.bin",
            "image/jpeg",
            content.LongLength,
            Convert.ToHexString(SHA256.HashData(content))
                .ToLowerInvariant(),
            "crm-access-test",
            Now);
        core.Transactions.Add(transaction);
        await core.SaveChangesAsync();

        var actor = CrmUser.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "admin@example.com",
            "Admin Test",
            null,
            Now);
        var disputeCase = CrmDisputeCase.Create(
            transaction.Id,
            transaction.DisputeOpenedAt!.Value);
        crm.Users.Add(actor);
        crm.UserRoles.Add(CrmUserRole.Assign(
            actor.Id,
            CrmRoleIds.Admin,
            null,
            Now));
        crm.DisputeCases.Add(disputeCase);
        await crm.SaveChangesAsync();
        var operations = new CrmDisputeOperations(
            crm,
            new TransactionRepository(core),
            null!,
            TimeProvider.System,
            new WorkforceIdentityOptions(),
            new FixedEvidenceStore(content));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        CrmAuthenticationDefaults.UserIdClaim,
                        actor.Id.ToString())
                ],
                "test"));

        var download = await operations.DownloadEvidenceAsync(
            disputeCase.Id,
            evidence.Id,
            principal,
            "ตรวจหลักฐานเพื่อพิจารณาข้อโต้แย้ง",
            "trace-evidence-1",
            default);

        Assert.Equal(content, download.Content);
        var audit = await crm.SensitiveAccessEvents.SingleAsync();
        Assert.Equal(
            "party_dispute_evidence",
            audit.ResourceType);
        Assert.Equal(
            evidence.Id.ToString("N"),
            audit.ResourceReference);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal("trace-evidence-1", audit.CorrelationId);
    }

    [Fact]
    public async Task User_without_local_role_cannot_open_evidence()
    {
        await using var core = CoreDatabase();
        await using var crm = CrmDatabase();
        var actor = CrmUser.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "inactive@example.com",
            "No Role",
            null,
            Now);
        crm.Users.Add(actor);
        await crm.SaveChangesAsync();
        var operations = new CrmDisputeOperations(
            crm,
            new TransactionRepository(core),
            null!,
            TimeProvider.System,
            new WorkforceIdentityOptions(),
            new FixedEvidenceStore([]));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        CrmAuthenticationDefaults.UserIdClaim,
                        actor.Id.ToString())
                ],
                "test"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            operations.DownloadEvidenceAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                principal,
                "test",
                "trace",
                default));
        Assert.Empty(crm.SensitiveAccessEvents);
    }

    [Fact]
    public async Task Both_party_request_queues_exact_idempotent_notifications()
    {
        await using var core = CoreDatabase();
        await using var crm = CrmDatabase();
        var transaction = DisputedTransaction();
        core.Transactions.Add(transaction);
        await core.SaveChangesAsync();
        var actor = CrmUser.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "reviewer@example.com",
            "Reviewer Test",
            null,
            Now);
        var disputeCase = CrmDisputeCase.Create(
            transaction.Id,
            transaction.DisputeOpenedAt!.Value);
        crm.Users.Add(actor);
        crm.UserRoles.Add(CrmUserRole.Assign(
            actor.Id,
            CrmRoleIds.Admin,
            null,
            Now));
        crm.DisputeCases.Add(disputeCase);
        await crm.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<
                NotifyDisputeEvidenceRequestHandler>());
        services.AddSingleton<ITransactionRepository>(
            new TransactionRepository(core));
        services.AddSingleton<IUnitOfWork>(core);
        services.AddSingleton<IClock>(
            new FixedClock(Now));
        await using var provider =
            services.BuildServiceProvider();
        var operations = new CrmDisputeOperations(
            crm,
            new TransactionRepository(core),
            provider.GetRequiredService<ISender>(),
            new FixedTimeProvider(Now),
            new WorkforceIdentityOptions(),
            new FixedEvidenceStore([]));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        CrmAuthenticationDefaults.UserIdClaim,
                        actor.Id.ToString())
                ],
                "test"));

        await operations.RequestEvidenceAsync(
            disputeCase.Id,
            CrmCaseParty.Both,
            "รูปฉลากขนส่งและบรรจุภัณฑ์",
            principal,
            default);
        await operations.RequestEvidenceAsync(
            disputeCase.Id,
            CrmCaseParty.Both,
            "รูปฉลากขนส่งและบรรจุภัณฑ์",
            principal,
            default);

        Assert.Single(crm.EvidenceRequests);
        var messages = await core.NotificationOutbox
            .Where(item =>
                item.Template ==
                "dispute_evidence_requested")
            .ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Contains(
            messages,
            item => item.Audience == "buyer" &&
                    item.ActionDeadlineAt ==
                    Now.AddHours(48));
        Assert.Contains(
            messages,
            item => item.Audience == "seller" &&
                    item.ActionDeadlineAt ==
                    Now.AddHours(48));
        Assert.Equal(
            2,
            transaction.AuditEvents.Count(item =>
                item.Name ==
                "dispute.evidence_requested"));
    }

    private static SaleTransaction DisputedTransaction()
    {
        var transitions = new TransactionTransitionService();
        var startedAt = Now.AddHours(-4);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "+66822222222",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานได้ปกติ",
            ConditionCode.UsedGood,
            "",
            null,
            450_000,
            "mvp-th-2026-07",
            startedAt,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            startedAt.AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                startedAt.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            startedAt.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-crm-evidence",
            startedAt.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            startedAt.AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            "delivery-crm-evidence",
            "delivered",
            startedAt.AddHours(1),
            startedAt.AddHours(1),
            transitions);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            startedAt.AddHours(2),
            transitions);
        return transaction;
    }

    private static ToklongDbContext CoreDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new ToklongDbContext(options);
    }

    private static CrmDbContext CrmDatabase()
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new CrmDbContext(options);
    }

    private sealed class FixedEvidenceStore(byte[] content)
        : IDisputeEvidenceStore
    {
        public Task<StoredDisputeEvidenceFile> SaveImageAsync(
            DisputeEvidenceFileInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DisputeEvidenceFileContent> ReadAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new DisputeEvidenceFileContent(
                    content,
                    "image/jpeg"));

        public Task DeleteAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
