using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Tests.Disputes;

public sealed class CrmDisputeWorkflowTests
{
    [Fact]
    public void Recommendation_requires_a_different_approver()
    {
        var userId = Guid.NewGuid();
        var action = CrmResolutionAction.Recommend(
            Guid.NewGuid(),
            CrmResolutionOutcome.FullRefund,
            "MATERIALLY_NOT_AS_DESCRIBED",
            "ภาพก่อนส่งและวิดีโอเปิดกล่องสอดคล้องกัน",
            userId,
            DateTimeOffset.UtcNow);

        var error = Assert.Throws<InvalidOperationException>(
            () => action.Approve(
                userId,
                DateTimeOffset.UtcNow));

        Assert.Contains("ห้ามอนุมัติ", error.Message);
        Assert.Equal(
            CrmResolutionActionStatus.PendingApproval,
            action.Status);
    }

    [Fact]
    public void Approved_resolution_is_applied_only_once()
    {
        var action = CrmResolutionAction.Recommend(
            Guid.NewGuid(),
            CrmResolutionOutcome.FullPayout,
            "ITEM_NOT_RECEIVED",
            "หลักฐานการส่งมอบตรวจสอบได้",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        action.Approve(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        action.MarkApplied(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(
            () => action.MarkApplied(
                DateTimeOffset.UtcNow));
        Assert.Equal(
            CrmResolutionActionStatus.Applied,
            action.Status);
    }

    [Fact]
    public void Recommendation_can_be_returned_with_a_reason()
    {
        var action = CrmResolutionAction.Recommend(
            Guid.NewGuid(),
            CrmResolutionOutcome.FullRefund,
            "WRONG_ITEM",
            "ต้องตรวจข้อมูลเพิ่ม",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        action.ReturnForMoreWork(
            Guid.NewGuid(),
            "ขอภาพฉลากขนส่งและภาพสินค้าทั้งชิ้น",
            DateTimeOffset.UtcNow);

        Assert.Equal(
            CrmResolutionActionStatus.Returned,
            action.Status);
        Assert.NotNull(action.ReturnedAt);
        Assert.Contains(
            "ฉลากขนส่ง",
            action.ReturnedReason);
    }

    [Fact]
    public void Evidence_deadline_is_exactly_48_elapsed_hours()
    {
        var now = new DateTimeOffset(
            2026, 7, 26, 8, 30, 0, TimeSpan.Zero);
        var request = CrmEvidenceRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CrmCaseParty.Both,
            "ภาพสินค้าและบรรจุภัณฑ์",
            now);

        Assert.Equal(now.AddHours(48), request.DueAt);
    }

    [Fact]
    public async Task Operational_records_are_append_only()
    {
        await using var database = Database();
        var note = CrmCaseNote.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "บันทึกทดสอบ",
            DateTimeOffset.UtcNow);
        database.CaseNotes.Add(note);
        await database.SaveChangesAsync();

        database.Entry(note).State = EntityState.Deleted;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
    }

    [Theory]
    [InlineData("password: hunter2")]
    [InlineData("รหัสผ่าน=123456")]
    [InlineData("private key: secret")]
    [InlineData("seed phrase: one two three")]
    public void Reusable_credentials_are_rejected(
        string unsafeText)
    {
        Assert.Throws<Toklong.Domain.Common.DomainException>(
            () => CrmCaseNote.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                unsafeText,
                DateTimeOffset.UtcNow));
    }

    private static CrmDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new CrmDbContext(options);
    }
}
