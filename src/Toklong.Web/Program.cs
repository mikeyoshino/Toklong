using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Security.Claims;
using System.Text.Json;
using Toklong.Application;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.ExternalEvents;
using Toklong.Application.Features.Sellers;
using Toklong.Domain.Common;
using Toklong.Infrastructure;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;
using Toklong.Web.Components;
using Toklong.Web.Extensions;
using Toklong.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "toklong.seller";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/seller/sign-in";
    });
builder.Services.AddAuthorization();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ApplicationRequestSender>();
builder.Services.AddHostedService<ReleaseDeadlineWorker>();

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
var importedImagesPath = Path.Combine(
    app.Environment.ContentRootPath,
    ImportedProductImageStore.StorageFolder);
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

app.MapPost("/auth/seller/otp/request", async (
    HttpContext context,
    IAntiforgery antiforgery,
    ISender sender) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var returnUrl = SafeLocalReturnUrl(form["returnUrl"], "/sales/create");
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

app.MapPost("/auth/seller/otp/verify", async (
    HttpContext context,
    IAntiforgery antiforgery,
    ISender sender) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var returnUrl = SafeLocalReturnUrl(form["returnUrl"], "/sales/create");
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
        new RecordCarrierEventCommand(request.TransactionId, request.EventId, request.EventType, request.OccurredAt),
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

public sealed record CarrierEvent(Guid TransactionId, string EventId, string EventType, DateTimeOffset OccurredAt)
{
    public string SignaturePayload() =>
        $"carrier|{TransactionId:N}|{EventId}|{EventType.ToLowerInvariant()}|{OccurredAt.ToUnixTimeSeconds()}";
}

public sealed record ManualPayoutEvent(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt)
{
    public string SignaturePayload() =>
        $"payout|{TransactionId:N}|{EventId}|{ConfirmedAt.ToUnixTimeSeconds()}";
}

public partial class Program;
