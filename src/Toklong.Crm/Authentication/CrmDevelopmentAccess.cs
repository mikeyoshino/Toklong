using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Disputes;
using Toklong.Crm.Persistence;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Crm.Authentication;

public static class CrmDevelopmentAccess
{
    public const string EnabledKey =
        "DevelopmentAccess:Enabled";

    private const string TenantId =
        "00000000-0000-0000-0000-000000000100";

    private static readonly DevelopmentAccount[] Accounts =
    [
        new(
            "admin",
            "00000000-0000-0000-0000-000000000101",
            "admin.local@toklong.test",
            "Admin Local",
            CrmRoleIds.Admin),
        new(
            "superadmin",
            "00000000-0000-0000-0000-000000000102",
            "superadmin.local@toklong.test",
            "SuperAdmin Local",
            CrmRoleIds.SuperAdmin),
        new(
            "approver",
            "00000000-0000-0000-0000-000000000103",
            "approver.local@toklong.test",
            "SuperAdmin Approver Local",
            CrmRoleIds.SuperAdmin)
    ];

    public static bool IsEnabled(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        environment.IsDevelopment() &&
        configuration.GetValue<bool>(EnabledKey);

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled(environment, configuration))
            return;

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<CrmDbContext>();
        var now = scope.ServiceProvider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow();

        foreach (var account in Accounts)
        {
            var user = await database.Users.SingleOrDefaultAsync(
                item =>
                    item.EntraTenantId == TenantId &&
                    item.EntraObjectId == account.ObjectId,
                cancellationToken);
            if (user is null)
            {
                user = CrmUser.Create(
                    TenantId,
                    account.ObjectId,
                    account.Email,
                    account.DisplayName,
                    null,
                    now);
                database.Users.Add(user);
                database.AuthEvents.Add(
                    CrmAuthEvent.Create(
                        user.Id,
                        "account.development_seeded",
                        CrmSubjectReference.Hash(
                            TenantId,
                            account.ObjectId),
                        $"crm-development-seed:{user.Id:N}",
                        now));
            }

            if (!await database.UserRoles.AnyAsync(
                    item =>
                        item.UserId == user.Id &&
                        item.RoleId == account.RoleId,
                    cancellationToken))
                database.UserRoles.Add(
                    CrmUserRole.Assign(
                        user.Id,
                        account.RoleId,
                        null,
                        now));
        }

        if (database.ChangeTracker.HasChanges())
            await database.SaveChangesAsync(cancellationToken);
    }

    public static async Task<IResult> LoginAsync(
        string? account,
        string? returnUrl,
        HttpContext context,
        IDbContextFactory<CrmDbContext> databaseFactory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var safeReturnUrl = CrmReturnUrl.Safe(returnUrl);
        if (string.IsNullOrWhiteSpace(account))
            return LoginPage(safeReturnUrl);

        var selected = Accounts.SingleOrDefault(item =>
            string.Equals(
                item.Key,
                account,
                StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            return Results.BadRequest(
                new { error = "Unknown development account." });

        await using var database =
            await databaseFactory.CreateDbContextAsync(
                cancellationToken);
        var user = await database.Users.SingleOrDefaultAsync(
            item =>
                item.EntraTenantId == TenantId &&
                item.EntraObjectId == selected.ObjectId,
            cancellationToken);
        if (user is null || !user.IsActive)
            return Results.Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "Development CRM account is unavailable.");

        var roles = await (
                from userRole in database.UserRoles
                join role in database.Roles
                    on userRole.RoleId equals role.Id
                where userRole.UserId == user.Id
                select role.Name)
            .ToArrayAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new("tid", TenantId),
            new("oid", selected.ObjectId),
            new(
                CrmAuthenticationDefaults.UserIdClaim,
                user.Id.ToString("N")),
            new(
                CrmAuthenticationDefaults.AuthenticatedAtClaim,
                now.ToUnixTimeSeconds().ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
        };
        claims.AddRange(roles.Select(role =>
            new Claim(
                CrmAuthenticationDefaults.RoleClaim,
                role)));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                CrmAuthenticationDefaults.CookieScheme));
        await context.SignInAsync(
            CrmAuthenticationDefaults.CookieScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                ExpiresUtc = now.AddHours(8)
            });

        database.AuthEvents.Add(
            CrmAuthEvent.Create(
                user.Id,
                "sign_in.development",
                CrmSubjectReference.Hash(
                    TenantId,
                    selected.ObjectId),
                context.TraceIdentifier,
                now));
        await database.SaveChangesAsync(cancellationToken);
        return Results.Redirect(safeReturnUrl);
    }

    public static async Task<Guid> SeedDemoDisputeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled(environment, configuration))
            throw new InvalidOperationException(
                "CRM demo dispute seeding is available only when DevelopmentAccess is enabled.");

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var transaction = await database.Transactions
            .Include(item => item.AuditEvents)
            .Include(item => item.AgreementAcceptances)
            .Include(item => item.ExternalEvents)
            .Include(item => item.Notifications)
            .Include(item => item.DisputeEvidence)
            .Where(item =>
                item.State ==
                    TransactionState.DeliveredDisputeWindow &&
                item.ProductName.Contains("ทดสอบ"))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No delivered Local test transaction is available for a demo dispute.");
        var now = scope.ServiceProvider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow();
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "เคสตัวอย่างสำหรับทดสอบ CRM: สินค้าที่ได้รับมีสภาพไม่ตรงกับรายละเอียด",
            now,
            scope.ServiceProvider
                .GetRequiredService<TransactionTransitionService>());
        await database.SaveChangesAsync(cancellationToken);
        return transaction.Id;
    }

    public static async Task<DevelopmentRefundApproval>
        ApplyDemoFullRefundAsync(
            IServiceProvider services,
            IConfiguration configuration,
            IHostEnvironment environment,
            Guid transactionId,
            CancellationToken cancellationToken = default)
    {
        EnsureDevelopmentAccess(configuration, environment);
        await using var scope = services.CreateAsyncScope();
        var operations = scope.ServiceProvider
            .GetRequiredService<CrmDisputeOperations>();
        var database = scope.ServiceProvider
            .GetRequiredService<CrmDbContext>();
        var admin = await PrincipalAsync(
            database,
            "admin",
            scope.ServiceProvider
                .GetRequiredService<TimeProvider>(),
            cancellationToken);
        var recommender = await PrincipalAsync(
            database,
            "superadmin",
            scope.ServiceProvider
                .GetRequiredService<TimeProvider>(),
            cancellationToken);
        var approver = await PrincipalAsync(
            database,
            "approver",
            scope.ServiceProvider
                .GetRequiredService<TimeProvider>(),
            cancellationToken);
        var disputeCase = (await operations.GetQueueAsync(
                admin,
                cancellationToken))
            .SingleOrDefault(item =>
                item.TransactionId == transactionId)
            ?? throw new InvalidOperationException(
                "ไม่พบเคสข้อโต้แย้งสำหรับรายการทดสอบ");
        await operations.ClaimAsync(
            disputeCase.CaseId,
            admin,
            cancellationToken);
        await operations.AddNoteAsync(
            disputeCase.CaseId,
            "ตรวจสอบ Local Stripe refund sandbox ตามหลักฐานรายการและยอด immutable แล้ว",
            admin,
            cancellationToken);
        await operations.RecommendAsync(
            disputeCase.CaseId,
            CrmResolutionOutcome.FullRefund,
            "MATERIALLY_NOT_AS_DESCRIBED",
            "อนุมัติคืนเงินเต็มจำนวนเพื่อทดสอบ Stripe refund sandbox โดยไม่ใช้เงินจริง",
            recommender,
            cancellationToken);
        var action = await database.ResolutionActions
            .SingleAsync(
                item =>
                    item.CaseId == disputeCase.CaseId &&
                    item.Status ==
                        CrmResolutionActionStatus.PendingApproval,
                cancellationToken);
        await operations.ApproveAndApplyAsync(
            disputeCase.CaseId,
            action.Id,
            approver,
            cancellationToken);

        var transaction = await scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>()
            .Transactions
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == transactionId,
                cancellationToken);
        if (transaction.State != TransactionState.RefundPending)
            throw new InvalidOperationException(
                "CRM approval did not move the transaction to RefundPending.");
        return new DevelopmentRefundApproval(
            disputeCase.CaseId,
            action.Id);
    }

    public static async Task VerifyDemoFullRefundAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        EnsureDevelopmentAccess(configuration, environment);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<CrmDbContext>();
        var admin = await PrincipalAsync(
            database,
            "admin",
            scope.ServiceProvider
                .GetRequiredService<TimeProvider>(),
            cancellationToken);
        _ = await scope.ServiceProvider
            .GetRequiredService<CrmDisputeOperations>()
            .GetQueueAsync(admin, cancellationToken);
        var disputeCase = await database.DisputeCases
            .AsNoTracking()
            .SingleAsync(
                item => item.TransactionId == transactionId,
                cancellationToken);
        var action = await database.ResolutionActions
            .AsNoTracking()
            .SingleAsync(
                item => item.CaseId == disputeCase.Id,
                cancellationToken);
        var transaction = await scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>()
            .Transactions
            .AsNoTracking()
            .Include(item => item.AuditEvents)
            .Include(item => item.ExternalEvents)
            .Include(item => item.Notifications)
            .SingleAsync(
                item => item.Id == transactionId,
                cancellationToken);
        var requiredAuditEvents = new[]
        {
            "dispute.review_started",
            "dispute.resolved_for_buyer",
            "refund.instruction_created",
            "refund.confirmed"
        };
        var requiredNotifications = new[]
        {
            "refund_started",
            "dispute_resolved_for_buyer",
            "refund_confirmed"
        };
        if (transaction.State != TransactionState.Refunded ||
            string.IsNullOrWhiteSpace(transaction.RefundReference) ||
            !transaction.RefundConfirmedAt.HasValue ||
            action.Status != CrmResolutionActionStatus.Applied ||
            disputeCase.Status != CrmDisputeCaseStatus.Closed ||
            requiredAuditEvents.Any(required =>
                transaction.AuditEvents.All(item =>
                    item.Name != required)) ||
            requiredNotifications.Any(required =>
                transaction.Notifications.All(item =>
                    item.Template != required)) ||
            transaction.ExternalEvents.All(item =>
                item.Provider != "stripe" ||
                item.EventType != "refund.succeeded"))
            throw new InvalidOperationException(
                "Local Stripe refund audit verification failed.");
    }

    private static async Task<ClaimsPrincipal> PrincipalAsync(
        CrmDbContext database,
        string accountKey,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var account = Accounts.Single(item =>
            string.Equals(
                item.Key,
                accountKey,
                StringComparison.Ordinal));
        var user = await database.Users
            .AsNoTracking()
            .SingleAsync(
                item =>
                    item.EntraTenantId == TenantId &&
                    item.EntraObjectId == account.ObjectId,
                cancellationToken);
        var claims = new[]
        {
            new Claim(
                CrmAuthenticationDefaults.UserIdClaim,
                user.Id.ToString("N")),
            new Claim(
                CrmAuthenticationDefaults.AuthenticatedAtClaim,
                timeProvider.GetUtcNow()
                    .ToUnixTimeSeconds()
                    .ToString(
                        System.Globalization.CultureInfo
                            .InvariantCulture))
        };
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                CrmAuthenticationDefaults.CookieScheme));
    }

    private static void EnsureDevelopmentAccess(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!IsEnabled(environment, configuration))
            throw new InvalidOperationException(
                "CRM refund sandbox commands require DevelopmentAccess.");
    }

    private static IResult LoginPage(string returnUrl)
    {
        var encodedReturnUrl = HtmlEncoder.Default.Encode(
            Uri.EscapeDataString(returnUrl));
        return Results.Content(
            $$"""
              <!doctype html>
              <html lang="th">
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <title>เข้า TOKLONG CRM — Local</title>
                <style>
                  *{box-sizing:border-box}
                  body{font-family:"Noto Sans Thai",system-ui,sans-serif;max-width:38rem;margin:7vh auto;padding:0 1.25rem;background:radial-gradient(600px circle at 100% 0,rgba(89,184,255,.16),transparent 48%),#fbfdff;color:#101828;line-height:1.65}
                  main{background:white;border:1px solid #e4eaf1;border-radius:22px;padding:2rem;box-shadow:0 18px 55px rgba(37,74,122,.1)}
                  h1{margin:.2rem 0 .6rem;font-size:clamp(1.8rem,7vw,2.6rem);letter-spacing:-.04em}
                  a{display:block;margin:.75rem 0;padding:.85rem 1rem;border:1px solid transparent;border-radius:13px;background:#2b7fff;color:white;font-weight:750;text-align:center;text-decoration:none;box-shadow:0 10px 25px rgba(43,127,255,.2)}
                  p{color:#475467}.warning{padding:.7rem .8rem;color:#8a5100;border-radius:12px;background:#fff4dc;font-size:.82rem}
                </style>
              </head>
              <body>
                <main>
                  <h1>TOKLONG CRM — Local</h1>
                  <p class="warning">หน้านี้เปิดเฉพาะ Development และไม่ใช้แทน Entra ID ใน Production</p>
                  <a href="/auth/login?account=admin&amp;returnUrl={{encodedReturnUrl}}">เข้าเป็น Admin ผู้ตรวจเคส</a>
                  <a href="/auth/login?account=superadmin&amp;returnUrl={{encodedReturnUrl}}">เข้าเป็น SuperAdmin ผู้เสนอผล</a>
                  <a href="/auth/login?account=approver&amp;returnUrl={{encodedReturnUrl}}">เข้าเป็น SuperAdmin ผู้อนุมัติอีกคน</a>
                </main>
              </body>
              </html>
              """,
            "text/html; charset=utf-8");
    }

    private sealed record DevelopmentAccount(
        string Key,
        string ObjectId,
        string Email,
        string DisplayName,
        Guid RoleId);
}

public sealed record DevelopmentRefundApproval(
    Guid CaseId,
    Guid ActionId);
