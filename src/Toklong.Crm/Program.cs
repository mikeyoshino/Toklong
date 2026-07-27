using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Toklong.Crm.Authentication;
using Toklong.Crm.Accounts;
using Toklong.Crm.Components;
using Toklong.Crm.Disputes;
using Toklong.Crm.Persistence;
using Toklong.Application;
using Toklong.Infrastructure;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;

var migrateOnly = args.Any(argument =>
    string.Equals(
        argument,
        "--migrate-only",
        StringComparison.OrdinalIgnoreCase));
var bootstrapSuperAdmin = args.Any(argument =>
    string.Equals(
        argument,
        "--bootstrap-super-admin",
        StringComparison.OrdinalIgnoreCase));
var bootstrapSecondSuperAdmin = args.Any(argument =>
    string.Equals(
        argument,
        "--bootstrap-second-super-admin",
        StringComparison.OrdinalIgnoreCase));
var seedDemoDispute = args.Any(argument =>
    string.Equals(
        argument,
        "--seed-demo-dispute",
        StringComparison.OrdinalIgnoreCase));
var applyDemoFullRefund = args.Any(argument =>
    string.Equals(
        argument,
        "--apply-demo-full-refund",
        StringComparison.OrdinalIgnoreCase));
var verifyDemoFullRefund = args.Any(argument =>
    string.Equals(
        argument,
        "--verify-demo-full-refund",
        StringComparison.OrdinalIgnoreCase));
if (bootstrapSuperAdmin && bootstrapSecondSuperAdmin)
    throw new InvalidOperationException(
        "เลือก bootstrap CRM ได้ครั้งละหนึ่งโหมด");
var builder = WebApplication.CreateBuilder(args);

var workforce = WorkforceIdentityOptions.From(
    builder.Configuration);
workforce.Validate(builder.Environment);
DisputeEvidenceStoreOptions.ValidateConfiguration(
    builder.Configuration,
    builder.Environment);

builder.Services.AddSingleton(workforce);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CrmDisputeOperations>();
builder.Services.AddScoped<CrmAccountOperations>();

builder.Services.AddDbContextFactory<CrmDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "ToklongDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__ef_migrations_history",
            "crm")));

var configuredKeysPath =
    builder.Configuration["DataProtection:KeysPath"];
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(configuredKeysPath) ||
     !Path.IsPathFullyQualified(configuredKeysPath)))
    throw new InvalidOperationException(
        "Production CRM DataProtection:KeysPath must be an absolute persistent path.");
var keysPath = Path.GetFullPath(
    PersistentStoragePath.Resolve(
        builder.Environment,
        builder.Configuration,
        "DataProtection:KeysPath",
        "App_Data/crm-data-protection-keys"));
Directory.CreateDirectory(keysPath);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Toklong.Crm")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
var dataProtectionCertificate =
    DataProtectionCertificateLoader.Load(
        builder.Configuration);
if (!builder.Environment.IsDevelopment() &&
    dataProtectionCertificate is null)
    throw new InvalidOperationException(
        "Production CRM requires a Data Protection certificate from secret storage.");
if (dataProtectionCertificate is not null)
    dataProtection.ProtectKeysWithCertificate(
        dataProtectionCertificate);

builder.Services.AddSingleton<CrmTicketStore>();
builder.Services.AddSingleton<
    IPostConfigureOptions<CookieAuthenticationOptions>,
    CrmCookiePostConfigureOptions>();
builder.Services.AddScoped<CrmOpenIdConnectEvents>();

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        CrmAuthenticationDefaults.CookieScheme;
    options.DefaultSignInScheme =
        CrmAuthenticationDefaults.CookieScheme;
    options.DefaultChallengeScheme = workforce.IsConfigured
        ? CrmAuthenticationDefaults.OpenIdConnectScheme
        : CrmAuthenticationDefaults.CookieScheme;
});
authentication.AddCookie(
    CrmAuthenticationDefaults.CookieScheme,
    options =>
    {
        options.Cookie.Name = "toklong.crm.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/access-denied";
    });
if (workforce.IsConfigured)
{
    authentication.AddOpenIdConnect(
        CrmAuthenticationDefaults.OpenIdConnectScheme,
        options =>
        {
            options.Authority =
                $"https://login.microsoftonline.com/{workforce.TenantId}/v2.0";
            options.ClientId = workforce.ClientId;
            options.ClientSecret = workforce.ClientSecret;
            options.CallbackPath = workforce.CallbackPath;
            options.ResponseType = "code";
            options.SignInScheme =
                CrmAuthenticationDefaults.CookieScheme;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.MapInboundClaims = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    NameClaimType = "name",
                    ValidateIssuer = true,
                    ValidateAudience = true
                };
            options.EventsType =
                typeof(CrmOpenIdConnectEvents);
        });
}

builder.Services.AddAuthorization(
    CrmAuthorization.Configure);

if (builder.Configuration.GetValue<bool>(
        "ReverseProxy:TrustForwardedHeaders"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(
        options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedHost |
                ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
}

var app = builder.Build();

if (migrateOnly ||
    builder.Configuration.GetValue(
        "Database:ApplyMigrations",
        false))
    await ApplyDatabaseMigrationsAsync(app);

if (migrateOnly)
    return;

await CrmDevelopmentAccess.SeedAsync(
    app.Services,
    app.Configuration,
    app.Environment);

if (seedDemoDispute)
{
    var transactionId =
        await CrmDevelopmentAccess.SeedDemoDisputeAsync(
            app.Services,
            app.Configuration,
            app.Environment);
    Console.WriteLine(
        $"Seeded Local CRM demo dispute for transaction {transactionId:D}.");
    return;
}
if (applyDemoFullRefund || verifyDemoFullRefund)
{
    if (!Guid.TryParse(
            app.Configuration[
                "DevelopmentRefundTest:TransactionId"],
            out var transactionId))
        throw new InvalidOperationException(
            "DevelopmentRefundTest:TransactionId is required.");
    if (applyDemoFullRefund)
    {
        var result =
            await CrmDevelopmentAccess.ApplyDemoFullRefundAsync(
                app.Services,
                app.Configuration,
                app.Environment,
                transactionId);
        Console.WriteLine(
            $"Applied Local CRM full-refund approval for case {result.CaseId:D}, action {result.ActionId:D}, transaction {transactionId:D}.");
    }
    else
    {
        await CrmDevelopmentAccess.VerifyDemoFullRefundAsync(
            app.Services,
            app.Configuration,
            app.Environment,
            transactionId);
        Console.WriteLine(
            $"Verified Local CRM full-refund audit for transaction {transactionId:D}.");
    }
    return;
}

if (bootstrapSuperAdmin)
{
    await CrmBootstrapper.BootstrapInitialSuperAdminAsync(
        app.Services,
        app.Configuration);
    return;
}
if (bootstrapSecondSuperAdmin)
{
    await CrmBootstrapper
        .BootstrapSecondSuperAdminAsync(
            app.Services,
            app.Configuration);
    return;
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] =
        "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] =
        "no-referrer";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet(
        "/auth/login",
        async (
            string? account,
            string? returnUrl,
            HttpContext context,
            WorkforceIdentityOptions identity,
            IDbContextFactory<CrmDbContext> databaseFactory,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            if (!identity.IsConfigured)
            {
                if (CrmDevelopmentAccess.IsEnabled(
                        app.Environment,
                        app.Configuration))
                    return await CrmDevelopmentAccess.LoginAsync(
                        account,
                        returnUrl,
                        context,
                        databaseFactory,
                        timeProvider,
                        cancellationToken);
                return Results.Problem(
                    statusCode:
                        StatusCodes.Status503ServiceUnavailable,
                    title:
                        "ยังไม่ได้ตั้งค่า Microsoft Entra ID สำหรับ CRM");
            }
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = SafeLocalReturnUrl(
                        returnUrl)
                },
                [
                    CrmAuthenticationDefaults
                        .OpenIdConnectScheme
                ]);
        })
    .AllowAnonymous();
app.MapPost(
        "/auth/logout",
        async (
            HttpContext context,
            IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await context.SignOutAsync(
                CrmAuthenticationDefaults.CookieScheme);
            return Results.Redirect("/");
        })
    .RequireAuthorization();

app.MapHealthChecks("/health/live")
    .AllowAnonymous();
app.MapGet(
        "/health/ready",
        async (
            IDbContextFactory<CrmDbContext> databaseFactory,
            CancellationToken cancellationToken) =>
        {
            await using var database =
                await databaseFactory.CreateDbContextAsync(
                    cancellationToken);
            return await database.Database.CanConnectAsync(
                    cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.Problem(
                    statusCode:
                        StatusCodes
                            .Status503ServiceUnavailable,
                    title: "database unavailable");
        })
    .AllowAnonymous();
app.MapGet(
        "/evidence/{caseId:guid}/{evidenceId:guid}",
        async (
            Guid caseId,
            Guid evidenceId,
            HttpContext context,
            CrmDisputeOperations operations,
            CancellationToken cancellationToken) =>
        {
            var evidence =
                await operations.DownloadEvidenceAsync(
                    caseId,
                    evidenceId,
                    context.User,
                    "ตรวจหลักฐานเพื่อพิจารณาข้อโต้แย้ง",
                    context.TraceIdentifier,
                    cancellationToken);
            context.Response.Headers.CacheControl =
                "no-store";
            context.Response.Headers[
                "Content-Security-Policy"] =
                "default-src 'none'; sandbox";
            context.Response.Headers[
                "Content-Disposition"] =
                $"inline; filename=\"evidence-{evidenceId:N}.jpg\"";
            return Results.File(
                evidence.Content,
                evidence.ContentType,
                enableRangeProcessing: false);
        })
    .RequireAuthorization(CrmPolicies.DisputeReader);
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

static string SafeLocalReturnUrl(string? returnUrl)
    => CrmReturnUrl.Safe(returnUrl);

static async Task ApplyDatabaseMigrationsAsync(
    WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    if (app.Environment.IsDevelopment())
    {
        var coreDatabase = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        await coreDatabase.Database.MigrateAsync();
    }
    var database = scope.ServiceProvider
        .GetRequiredService<CrmDbContext>();
    await database.Database.MigrateAsync();
}

public partial class Program;
