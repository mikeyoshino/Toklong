using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Toklong.Application;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.ExternalEvents;
using Toklong.Application.Features.Buyers;
using Toklong.Application.Features.Sellers;
using Toklong.Application.Features.Shipping.GetShippingLabel;
using Toklong.Application.Features.Transactions.GetAgreementEvidence;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Infrastructure;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;
using Toklong.Web.Components;
using Toklong.Web.Extensions;
using Toklong.Web.Services;

var builder = WebApplication.CreateBuilder(args);

ProductionConfigurationValidator.Validate(
    builder.Configuration,
    builder.Environment,
    requireMobileLinks: true,
    requirePersistentStorage: true);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHealthChecks();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "toklong.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/seller/sign-in";
        options.Events.OnRedirectToLogin = context =>
        {
            var buyerEntry =
                context.Request.Path.StartsWithSegments("/offers/create") ||
                context.Request.Path.StartsWithSegments("/buyer");
            var loginPath = buyerEntry
                ? "/buyer/sign-in"
                : options.LoginPath.Value!;
            var returnUrl =
                $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            context.Response.Redirect(
                $"{loginPath}{QueryString.Create("returnUrl", returnUrl)}");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            var buyerEntry =
                context.Request.Path.StartsWithSegments("/offers/create") ||
                context.Request.Path.StartsWithSegments("/buyer");
            var signInPath = buyerEntry
                ? "/buyer/sign-in"
                : "/seller/sign-in";
            var returnUrl =
                $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            context.Response.Redirect(
                $"{signInPath}{QueryString.Create("returnUrl", returnUrl)}");
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ApplicationRequestSender>();
var keysPath = PersistentStoragePath.Resolve(
    builder.Environment,
    builder.Configuration,
    "DataProtection:KeysPath",
    "App_Data/data-protection-keys");
Directory.CreateDirectory(keysPath);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Toklong.Web")
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

var app = builder.Build();

_ = app.Services.GetRequiredService<IThaiAddressCatalog>();
if (builder.Configuration.GetValue("Database:ApplyMigrations", true))
    await app.ApplyDatabaseMigrationsAsync();

app.UseForwardedHeaders();

app.MapGet(
    "/.well-known/apple-app-site-association",
    (IConfiguration configuration) =>
    {
        var teamId = configuration["MobileLinks:AppleTeamId"]?.Trim();
        if (string.IsNullOrWhiteSpace(teamId))
            return Results.NotFound();
        return Results.Json(new
        {
            applinks = new
            {
                details = new[]
                {
                    new
                    {
                        appIDs = new[]
                        {
                            $"{teamId}.th.co.toklong.mobile"
                        },
                        components = new[]
                        {
                            new Dictionary<string, string>
                            {
                                ["/"] = "/offer/*"
                            }
                        }
                    }
                }
            }
        });
    });
app.MapGet(
    "/.well-known/assetlinks.json",
    (IConfiguration configuration) =>
    {
        var fingerprints = configuration
            .GetSection("MobileLinks:AndroidSha256Fingerprints")
            .Get<string[]>()?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? [];
        if (fingerprints.Length == 0)
            return Results.NotFound();
        return Results.Json(new[]
        {
            new
            {
                relation = new[]
                {
                    "delegate_permission/common.handle_all_urls"
                },
                target = new
                {
                    @namespace = "android_app",
                    package_name =
                        configuration[
                            "MobileLinks:AndroidPackageName"] ??
                        "th.co.toklong.mobile",
                    sha256_cert_fingerprints = fingerprints
                }
            }
        });
    });

var repositoryLandingPath = Path.GetFullPath(
    Path.Combine(app.Environment.ContentRootPath, "..", "..", "landing.html"));
var outputLandingPath = Path.Combine(AppContext.BaseDirectory, "landing.html");
var landingPath = File.Exists(repositoryLandingPath)
    ? repositoryLandingPath
    : outputLandingPath;
if (!File.Exists(landingPath))
    throw new FileNotFoundException("The landing UI source file was not found.", landingPath);

app.Map("/api/webhooks/manual-payment", branch =>
{
    branch.Use(WebhookErrorHandlingAsync);
    branch.Run(context => HandleManualPaymentAsync(context));
});
app.Map("/api/webhooks/carrier", branch =>
{
    branch.Use(WebhookErrorHandlingAsync);
    branch.Run(context => HandleCarrierEventAsync(context));
});
app.Map("/api/webhooks/manual-payout", branch =>
{
    branch.Use(WebhookErrorHandlingAsync);
    branch.Run(context => HandleManualPayoutAsync(context));
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
var importedImagesPath = ImportedProductImageStore.ResolveStoragePath(
    app.Environment,
    app.Configuration);
Directory.CreateDirectory(importedImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(importedImagesPath),
    RequestPath = ImportedProductImageStore.RequestPath
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
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

app.MapGet(
    "/transactions/{transactionId:guid}/agreement-evidence",
    async (
        Guid transactionId,
        string? format,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken) =>
    {
        var id = Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var partyId)
            ? partyId
            : (Guid?)null;
        var role = principal.FindFirstValue(ClaimTypes.Role);
        var evidence = await sender.Send(
            new GetAgreementEvidenceQuery(
                transactionId,
                role == "Buyer" ? id : null,
                role == "Seller" ? id : null),
            cancellationToken);
        return string.Equals(
            format,
            "html",
            StringComparison.OrdinalIgnoreCase)
            ? Results.File(
                evidence.HtmlBytes,
                "text/html; charset=utf-8",
                evidence.HtmlFileName)
            : Results.File(
                evidence.JsonBytes,
                "application/json; charset=utf-8",
                evidence.JsonFileName);
    })
    .RequireAuthorization();

app.MapGet(
    "/seller/{sellerToken}/shipping-label",
    async (
        string sellerToken,
        ClaimsPrincipal principal,
        HttpResponse response,
        ITransactionRepository transactions,
        ISender sender,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(
                principal.FindFirstValue(ClaimTypes.Role),
                "Seller",
                StringComparison.Ordinal) ||
            !Guid.TryParse(
                principal.FindFirstValue(
                    ClaimTypes.NameIdentifier),
                out var sellerId))
            return Results.Forbid();
        var transaction =
            await transactions.GetBySellerTokenAsync(
                sellerToken,
                cancellationToken);
        if (transaction is null)
            return Results.NotFound();
        var html = await sender.Send(
            new GetShippingLabelQuery(
                transaction.Id,
                sellerId),
            cancellationToken);
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] =
            "sandbox; default-src 'none'; img-src data: https:; " +
            "style-src 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src https://fonts.gstatic.com";
        return Results.File(
            Encoding.UTF8.GetBytes(html),
            "text/html; charset=utf-8",
            $"TOKLONG-label-{transaction.Id:N}.html");
    })
    .RequireAuthorization();

app.MapPost("/auth/seller/otp/request", async (
    HttpContext context,
    IAntiforgery antiforgery,
    ISender sender) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var returnUrl = SafeLocalReturnUrl(form["returnUrl"], "/");
        var challenge = await sender.Send(
            new RequestSellerOtpCommand(form["phone"].ToString()),
            context.RequestAborted);
        var target = QueryString.Create(new Dictionary<string, string?>
        {
            ["challengeId"] = challenge.ChallengeId,
            ["phone"] = challenge.MaskedPhoneNumber,
            ["devCode"] = challenge.DevelopmentCode,
            ["returnUrl"] = returnUrl
        });
        return Results.Redirect($"/seller/verify-otp{target}");
    }
    catch (Exception exception)
    {
        var target = QueryString.Create("error", exception.Message);
        return Results.Redirect($"/seller/sign-in{target}");
    }
});

app.MapPost("/auth/buyer/otp/request", async (
    HttpContext context,
    IAntiforgery antiforgery,
    ISender sender) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var returnUrl = SafeLocalReturnUrl(form["returnUrl"], "/offers/create");
        var mode = BuyerAuthMode(form["mode"]);
        var fullName = mode == "sign-up"
            ? form["fullName"].ToString().Trim()
            : "";
        var email = mode == "sign-up"
            ? BuyerAccount.NormalizeEmail(form["email"].ToString())
            : "";
        if (mode == "sign-up" &&
            fullName.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries).Length < 2)
            throw new ArgumentException(
                "กรุณากรอกชื่อและนามสกุลตอนสมัครสมาชิก");
        var challenge = await sender.Send(
            new RequestBuyerOtpCommand(form["phone"].ToString()),
            context.RequestAborted);
        var target = QueryString.Create(new Dictionary<string, string?>
        {
            ["challengeId"] = challenge.ChallengeId,
            ["phone"] = challenge.MaskedPhoneNumber,
            ["mode"] = mode,
            ["fullName"] = fullName,
            ["email"] = email,
            ["devCode"] = challenge.DevelopmentCode,
            ["returnUrl"] = returnUrl
        });
        return Results.Redirect($"/buyer/verify-otp{target}");
    }
    catch (Exception exception)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var mode = BuyerAuthMode(form["mode"]);
        var target = QueryString.Create(new Dictionary<string, string?>
        {
            ["returnUrl"] = SafeLocalReturnUrl(
                form["returnUrl"], "/offers/create"),
            ["error"] = exception.Message
        });
        return Results.Redirect(
            $"{(mode == "sign-up" ? "/buyer/sign-up" : "/buyer/sign-in")}{target}");
    }
});

app.MapPost("/auth/buyer/otp/verify", async (
    HttpContext context,
    IAntiforgery antiforgery,
    ISender sender) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var returnUrl = SafeLocalReturnUrl(
        form["returnUrl"], "/offers/create");
    var mode = BuyerAuthMode(form["mode"]);
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var buyer = mode == "sign-up"
            ? await sender.Send(new RegisterBuyerCommand(
                form["challengeId"].ToString(),
                form["code"].ToString(),
                form["fullName"].ToString(),
                form["email"].ToString()), context.RequestAborted)
            : await sender.Send(new VerifyBuyerOtpCommand(
                form["challengeId"].ToString(),
                form["code"].ToString()), context.RequestAborted);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, buyer.Id.ToString()),
            new Claim(ClaimTypes.Name, buyer.FullName),
            new Claim(ClaimTypes.MobilePhone, buyer.PhoneNumber),
            new Claim(ClaimTypes.Role, "Buyer")
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });
        return Results.Redirect(returnUrl);
    }
    catch (Exception exception)
    {
        var target = QueryString.Create(new Dictionary<string, string?>
        {
            ["challengeId"] = form["challengeId"],
            ["phone"] = form["phone"],
            ["mode"] = mode,
            ["fullName"] = form["fullName"],
            ["devCode"] = form["devCode"],
            ["returnUrl"] = returnUrl,
            ["error"] = exception.Message
        });
        return Results.Redirect($"/buyer/verify-otp{target}");
    }
});

app.MapPost("/auth/seller/otp/verify", async (
    HttpContext context,
    IAntiforgery antiforgery,
    ISender sender) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var returnUrl = SafeLocalReturnUrl(form["returnUrl"], "/");
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var seller = await sender.Send(new VerifySellerOtpCommand(
            form["challengeId"].ToString(),
            form["code"].ToString()), context.RequestAborted);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, seller.Id.ToString()),
            new Claim(ClaimTypes.Name, seller.DisplayName),
            new Claim(ClaimTypes.MobilePhone, seller.PhoneNumber),
            new Claim(ClaimTypes.Role, "Seller")
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });
        return Results.Redirect(returnUrl);
    }
    catch (Exception exception)
    {
        var target = QueryString.Create(new Dictionary<string, string?>
        {
            ["challengeId"] = form["challengeId"],
            ["devCode"] = form["devCode"],
            ["returnUrl"] = returnUrl,
            ["error"] = exception.Message
        });
        return Results.Redirect($"/seller/verify-otp{target}");
    }
});

app.MapGet("/", () => Results.File(landingPath, "text/html; charset=utf-8"));
app.MapGet("/landing-theme.css", async () =>
{
    var html = await File.ReadAllTextAsync(landingPath);
    const string openTag = "<style>";
    const string closeTag = "</style>";
    var start = html.IndexOf(openTag, StringComparison.Ordinal) + openTag.Length;
    var end = html.IndexOf(closeTag, start, StringComparison.Ordinal);
    return Results.Text(html[start..end], "text/css; charset=utf-8");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

static string SafeLocalReturnUrl(string? value, string fallback) =>
    !string.IsNullOrWhiteSpace(value) &&
    value.StartsWith('/') &&
    !value.StartsWith("//", StringComparison.Ordinal)
        ? value
        : fallback;

static string BuyerAuthMode(string? value) =>
    string.Equals(value, "sign-up", StringComparison.Ordinal)
        ? "sign-up"
        : "sign-in";

static bool TryVerify(string payload, HttpRequest request, IWebhookSignatureVerifier verifier) =>
    request.Headers.TryGetValue("X-Toklong-Signature", out var signature) &&
    verifier.Verify(payload, signature.ToString());

static async Task WebhookErrorHandlingAsync(HttpContext context, Func<Task> next)
{
    try
    {
        await next();
    }
    catch (JsonException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
    catch (NotFoundException)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }
    catch (DomainException)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
    }
    catch (DbUpdateException)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
    }
}

static async Task HandleManualPaymentAsync(HttpContext context)
{
    if (!HttpMethods.IsPost(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }
    var request = await JsonSerializer.DeserializeAsync<ManualPaymentEvent>(
        context.Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted);
    var verifier = context.RequestServices.GetRequiredService<IWebhookSignatureVerifier>();
    if (request is null || !TryVerify(request.SignaturePayload(), context.Request, verifier))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    var sender = context.RequestServices.GetRequiredService<ISender>();
    var result = await sender.Send(
        new ConfirmManualPaymentCommand(request.TransactionId, request.EventId, request.ConfirmedAt),
        context.RequestAborted);
    await Results.Ok(new { result.AlreadyProcessed, state = result.Transaction.State.ToString() }).ExecuteAsync(context);
}

static async Task HandleCarrierEventAsync(HttpContext context)
{
    if (!HttpMethods.IsPost(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }
    var request = await JsonSerializer.DeserializeAsync<CarrierEvent>(
        context.Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted);
    var verifier = context.RequestServices.GetRequiredService<IWebhookSignatureVerifier>();
    if (request is null || !TryVerify(request.SignaturePayload(), context.Request, verifier))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    var sender = context.RequestServices.GetRequiredService<ISender>();
    var result = await sender.Send(
        new RecordCarrierEventCommand(
            request.TransactionId,
            request.EventId,
            request.EventType,
            request.OccurredAt,
            request.CarrierCode,
            request.TrackingNumber),
        context.RequestAborted);
    await Results.Ok(new { result.AlreadyProcessed, state = result.Transaction.State.ToString() }).ExecuteAsync(context);
}

static async Task HandleManualPayoutAsync(HttpContext context)
{
    if (!HttpMethods.IsPost(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }
    var request = await JsonSerializer.DeserializeAsync<ManualPayoutEvent>(
        context.Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted);
    var verifier = context.RequestServices.GetRequiredService<IWebhookSignatureVerifier>();
    if (request is null || !TryVerify(request.SignaturePayload(), context.Request, verifier))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    var sender = context.RequestServices.GetRequiredService<ISender>();
    var result = await sender.Send(
        new ConfirmManualPayoutCommand(request.TransactionId, request.EventId, request.ConfirmedAt),
        context.RequestAborted);
    await Results.Ok(new { result.AlreadyProcessed, state = result.Transaction.State.ToString() }).ExecuteAsync(context);
}

public sealed record ManualPaymentEvent(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt)
{
    public string SignaturePayload() =>
        $"payment|{TransactionId:N}|{EventId}|{ConfirmedAt.ToUnixTimeSeconds()}";
}

public sealed record CarrierEvent(
    Guid TransactionId,
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string CarrierCode,
    string TrackingNumber)
{
    public string SignaturePayload() =>
        $"carrier|{TransactionId:N}|{EventId}|" +
        $"{EventType.ToLowerInvariant()}|" +
        $"{CarrierCode.Trim().ToUpperInvariant()}|" +
        $"{new string(TrackingNumber.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray())}|" +
        $"{OccurredAt.ToUnixTimeSeconds()}";
}

public sealed record ManualPayoutEvent(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt)
{
    public string SignaturePayload() =>
        $"payout|{TransactionId:N}|{EventId}|{ConfirmedAt.ToUnixTimeSeconds()}";
}

public partial class Program;
