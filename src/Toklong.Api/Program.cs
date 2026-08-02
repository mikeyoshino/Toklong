using System.Threading.RateLimiting;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Toklong.Api.Api;
using Toklong.Api.Security;
using Toklong.Api.Services;
using Toklong.Application;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Infrastructure;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;

var migrateOnly = args.Any(argument =>
    string.Equals(
        argument,
        "--migrate-only",
        StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder(args);

ProductionConfigurationValidator.Validate(
    builder.Configuration,
    builder.Environment,
    requireMobileLinks: false,
    requirePersistentStorage: true);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter()));
builder.Services.AddAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
var demoSimulation =
    DevelopmentDemoSimulationOptions.From(
        builder.Configuration,
        builder.Environment);
builder.Services.AddSingleton(demoSimulation);
if (demoSimulation.Enabled)
    builder.Services.AddHostedService<
        DevelopmentDemoSimulationWorker>();
builder.Services.AddSingleton<
    PendingRegistrationCleanupWorker>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService(provider =>
        provider.GetRequiredService<
            PendingRegistrationCleanupWorker>());

var keysPath = Path.GetFullPath(
    PersistentStoragePath.Resolve(
        builder.Environment,
        builder.Configuration,
        "DataProtection:KeysPath",
        "App_Data/data-protection-keys"));
Directory.CreateDirectory(keysPath);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Toklong.MobileApi")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
var dataProtectionCertificate =
    DataProtectionCertificateLoader.Load(builder.Configuration);
if (dataProtectionCertificate is not null)
    dataProtection.ProtectKeysWithCertificate(
        dataProtectionCertificate);
if (builder.Configuration.GetValue<bool>(
        "ReverseProxy:TrustForwardedHeaders"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
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
builder.Services.AddScoped<MobileSessionTokenService>();
builder.Services.AddSingleton<StripeWebhookEventParser>();
builder.Services
    .AddAuthentication(MobileAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, MobileBearerAuthenticationHandler>(
        MobileAuthenticationDefaults.Scheme,
        _ => { });
var rateLimiterPartitionSecret =
    RandomNumberGenerator.GetBytes(32);
var otpRequestPermitLimit = builder.Configuration.GetValue(
    "RateLimits:OtpRequestPermitLimit",
    5);
var otpVerifyPermitLimit = builder.Configuration.GetValue(
    "RateLimits:OtpVerifyPermitLimit",
    10);
var registrationCompletePermitLimit =
    builder.Configuration.GetValue(
        "RateLimits:RegistrationCompletePermitLimit",
        10);
var emailChangeRequestPermitLimit =
    builder.Configuration.GetValue(
        "RateLimits:EmailChangeRequestPermitLimit",
        5);
var emailChangeVerifyPermitLimit =
    builder.Configuration.GetValue(
        "RateLimits:EmailChangeVerifyPermitLimit",
        10);
var nameChangeRequestPermitLimit =
    builder.Configuration.GetValue(
        "RateLimits:NameChangeRequestPermitLimit",
        5);
var nameChangeVerifyPermitLimit =
    builder.Configuration.GetValue(
        "RateLimits:NameChangeVerifyPermitLimit",
        10);
var nameChangeRequestWindowSeconds =
    builder.Environment.IsEnvironment("Testing")
        ? Math.Clamp(
            builder.Configuration.GetValue(
                "RateLimits:NameChangeRequestWindowSeconds",
                60),
            1,
            60)
        : 60;
var nameChangeVerifyWindowSeconds =
    builder.Environment.IsEnvironment("Testing")
        ? Math.Clamp(
            builder.Configuration.GetValue(
                "RateLimits:NameChangeVerifyWindowSeconds",
                600),
            1,
            600)
        : 600;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments(
                "/api/mobile/me/name-change"))
            return;

        var maximumRetryAfterSeconds = context.HttpContext.Request.Path.Value
            ?.EndsWith("/verify", StringComparison.Ordinal) == true
            ? nameChangeVerifyWindowSeconds
            : nameChangeRequestWindowSeconds;
        var retryAfterSeconds =
            context.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out var leaseRetryAfter) &&
            leaseRetryAfter > TimeSpan.Zero
                ? Math.Clamp(
                    (int)Math.Ceiling(
                        leaseRetryAfter.TotalSeconds),
                    1,
                    maximumRetryAfterSeconds)
                : maximumRetryAfterSeconds;
        context.HttpContext.Response.Headers["Retry-After"] =
            retryAfterSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        await Results.Json(
                new
                {
                    title = "ทำรายการไม่สำเร็จ",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "ทำรายการบ่อยเกินไป กรุณาลองใหม่ภายหลัง",
                    code = "name_change_rate_limited",
                    retryAfterSeconds
                },
                statusCode: StatusCodes.Status429TooManyRequests,
                contentType: "application/problem+json")
            .ExecuteAsync(context.HttpContext);
    };
    options.AddPolicy("otp-request", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = otpRequestPermitLimit,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("otp-verify", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = otpVerifyPermitLimit,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("registration-complete", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = registrationCompletePermitLimit,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("ai-draft", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("evidence-upload", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("counter-qr", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            AuthenticatedAccountRateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("email-change-request", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthenticatedBuyerRateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = emailChangeRequestPermitLimit,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("email-change-verify", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthenticatedBuyerRateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = emailChangeVerifyPermitLimit,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("name-change-request", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthenticatedAccountRateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = nameChangeRequestPermitLimit,
                Window = TimeSpan.FromSeconds(
                    nameChangeRequestWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("name-change-verify", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            AuthenticatedAccountRateLimitKey(
                context,
                rateLimiterPartitionSecret),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = nameChangeVerifyPermitLimit,
                Window = TimeSpan.FromSeconds(
                    nameChangeVerifyWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (migrateOnly)
{
    await ApplyDatabaseMigrationsAsync(app);
    return;
}

_ = app.Services.GetRequiredService<IThaiAddressCatalog>();
if (builder.Configuration.GetValue("Database:ApplyMigrations", true))
    await ApplyDatabaseMigrationsAsync(app);

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api/mobile"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
var importedImagesPath = ImportedProductImageStore.ResolveStoragePath(
    app.Environment,
    app.Configuration);
Directory.CreateDirectory(importedImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(importedImagesPath),
    RequestPath = ImportedProductImageStore.RequestPath,
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            "private,max-age=300";
        context.Context.Response.Headers["X-Content-Type-Options"] =
            "nosniff";
    }
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMobileApiErrors();
app.MapMobileApi();
app.MapStripeWebhook();
app.MapInternalOperationsApi();
app.MapHealthChecks("/health/live");
app.MapGet(
    "/health/ready",
    async (
        ToklongDbContext database,
        CancellationToken cancellationToken) =>
        await database.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready" })
            : Results.Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title: "database unavailable"));

await app.RunAsync();

static string RateLimitKey(
    HttpContext context,
    byte[] secret)
{
    var address = context.Connection
        .RemoteIpAddress?.GetAddressBytes() ?? [];
    return Convert.ToHexString(
        HMACSHA256.HashData(secret, address));
}

static string AuthenticatedBuyerRateLimitKey(
    HttpContext context,
    byte[] secret)
{
    var buyerId = context.User.FindFirst(
        MobileAuthenticationDefaults.BuyerIdClaim)?.Value
        ?? "no-buyer";
    return $"{buyerId}:{RateLimitKey(context, secret)}";
}

static string AuthenticatedAccountRateLimitKey(
    HttpContext context,
    byte[] secret)
{
    var claimedPhone = context.User.FindFirstValue(
        ClaimTypes.MobilePhone);
    string accountKey;
    try
    {
        accountKey = ThaiMobilePhone.Normalize(claimedPhone ?? "");
    }
    catch (ArgumentException)
    {
        accountKey = "no-account";
    }
    return $"{accountKey}:{RateLimitKey(context, secret)}";
}

static async Task ApplyDatabaseMigrationsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ToklongDbContext>();
    await database.Database.MigrateAsync();
}

public partial class Program;
