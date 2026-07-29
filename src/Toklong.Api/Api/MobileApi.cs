using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Authentication;
using Toklong.Application.Features.Buyers;
using Toklong.Application.Features.Checkout.PreparePaymentSheet;
using Toklong.Application.Features.Offers.CreateBuyerOffer;
using Toklong.Application.Features.Offers.ExtractAgreementDraft;
using Toklong.Application.Features.Offers.RespondToBuyerOffer;
using Toklong.Application.Features.Notifications.ListNotifications;
using Toklong.Application.Features.Sales.SaveListingPhoto;
using Toklong.Application.Features.Sellers;
using Toklong.Application.Features.Shipping.GetShippingLabel;
using Toklong.Application.Features.Shipping.GetShippingQuotes;
using Toklong.Application.Features.Transactions.GetTransaction;
using Toklong.Application.Features.Transactions.GetAgreementEvidence;
using Toklong.Application.Features.Transactions.ListTransactions;
using Toklong.Application.Features.Transactions.ActOnTransaction;
using Toklong.Application.Features.Transactions.ManageDisputeEvidence;
using Toklong.Application.Pricing;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Api.Security;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Api;

public static class MobileApi
{
    public static IApplicationBuilder UseMobileApiErrors(
        this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/api/mobile"),
            branch => branch.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                    DomainException or
                    ForbiddenException or
                    NotFoundException or
                    RequestCooldownException or
                    InvalidOperationException)
                {
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Toklong.MobileApi");
                    logger.LogWarning(
                        exception,
                        "A controlled mobile API request failed");
                    var status = exception switch
                    {
                        NotFoundException =>
                            StatusCodes.Status404NotFound,
                        ForbiddenException =>
                            StatusCodes.Status403Forbidden,
                        RequestCooldownException =>
                            StatusCodes.Status429TooManyRequests,
                        DomainException =>
                            StatusCodes.Status400BadRequest,
                        InvalidOperationException =>
                            StatusCodes.Status503ServiceUnavailable,
                        _ => StatusCodes.Status400BadRequest
                    };
                    var detail =
                        exception is InvalidOperationException and
                            not DomainException
                            ? "บริการนี้ยังไม่พร้อมใช้งาน กรุณาลองใหม่ภายหลัง"
                            : exception.Message;
                    if (exception is RequestCooldownException cooldown)
                    {
                        context.Response.Headers["Retry-After"] = Math.Max(
                                1,
                                (int)Math.Ceiling(
                                    cooldown.RetryAfter.TotalSeconds))
                            .ToString(
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                    await Results.Problem(
                            statusCode: status,
                            title: "ทำรายการไม่สำเร็จ",
                            detail: detail)
                        .ExecuteAsync(context);
                }
            }));

    public static void MapMobileApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/mobile");

        api.MapPost("/auth/otp/request", RequestOtpAsync)
            .RequireRateLimiting("otp-request");
        api.MapPost("/auth/otp/verify", VerifyOtpAsync)
            .RequireRateLimiting("otp-verify");
        api.MapPost(
                "/auth/registration/complete",
                CompleteRegistrationAsync)
            .RequireRateLimiting("registration-complete");
        api.MapPost("/auth/refresh", RefreshSessionAsync)
            .RequireRateLimiting("otp-verify");

        var authenticated = api.MapGroup("")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes =
                    MobileAuthenticationDefaults.Scheme
            });

        authenticated.MapGet("/me", GetProfileAsync);
        authenticated.MapGet(
            "/me/email-change",
            GetPendingEmailChangeAsync);
        authenticated.MapPost(
                "/me/email-change",
                RequestEmailChangeAsync)
            .RequireRateLimiting("email-change-request");
        authenticated.MapPost(
                "/me/email-change/{challengeId:guid}/resend",
                ResendEmailChangeAsync)
            .RequireRateLimiting("email-change-request");
        authenticated.MapPost(
                "/me/email-change/{challengeId:guid}/verify",
                VerifyEmailChangeAsync)
            .RequireRateLimiting("email-change-verify");
        authenticated.MapPost("/auth/logout", LogoutAsync);
        authenticated.MapGet("/addresses/provinces", (
            IThaiAddressCatalog catalog) => Results.Ok(catalog.Provinces));
        authenticated.MapGet("/addresses/districts/{provinceId:int}", (
            int provinceId,
            IThaiAddressCatalog catalog) =>
            Results.Ok(catalog.GetDistricts(provinceId)));
        authenticated.MapGet("/addresses/subdistricts/{districtId:int}", (
            int districtId,
            IThaiAddressCatalog catalog) =>
            Results.Ok(catalog.GetSubdistricts(districtId)));
        authenticated.MapGet(
            "/pricing/buyer-protection",
            GetBuyerProtectionPreview);
        authenticated.MapGet(
            "/shipping/carriers",
            () => Results.Ok(
                SupportedCarrierCatalog.All.Select(
                    carrier => new MobileCarrierResponse(
                        carrier.Code,
                        carrier.DisplayName,
                        carrier.TrackingHint,
                        carrier.TrackingExample,
                        carrier.ValidationPattern,
                        carrier.ValidationMessage,
                        carrier.MaximumLength))));
        authenticated.MapGet("/transactions", ListTransactionsAsync);
        authenticated.MapGet("/notifications", ListNotificationsAsync);
        authenticated.MapPut(
            "/notification-devices/current",
            RegisterNotificationDeviceAsync);
        authenticated.MapDelete(
            "/notification-devices/current/{installationId}",
            UnregisterNotificationDeviceAsync);
        authenticated.MapGet(
            "/transactions/{transactionId:guid}",
            GetTransactionAsync);
        authenticated.MapGet(
            "/transactions/{transactionId:guid}/agreement-evidence",
            DownloadAgreementEvidenceAsync);
        authenticated.MapGet(
            "/transactions/{transactionId:guid}/shipping-label",
            DownloadShippingLabelAsync);
        authenticated.MapPost("/offers", CreateOfferAsync)
            .DisableAntiforgery();
        authenticated.MapPost(
                "/offers/extract-draft",
                ExtractAgreementDraftAsync)
            .RequireRateLimiting("ai-draft")
            .DisableAntiforgery();
        authenticated.MapGet(
            "/seller-offers/{publicToken}",
            GetSellerOfferAsync);
        authenticated.MapPost(
            "/seller-offers/{publicToken}/shipping-quotes",
            GetSellerShippingQuotesAsync);
        authenticated.MapPut(
            "/seller/payout-account",
            SaveMobileSellerPayoutAccountAsync);
        authenticated.MapGet(
            "/seller/payout-accounts",
            GetMobileSellerPayoutAccountsAsync);
        authenticated.MapPost(
            "/seller-offers/{publicToken}/accept",
            AcceptSellerOfferAsync);
        authenticated.MapPost(
            "/seller-offers/{publicToken}/decline",
            DeclineSellerOfferAsync);
        authenticated.MapPost(
            "/transactions/{transactionId:guid}/payment-sheet",
            PreparePaymentSheetAsync);
        authenticated.MapPost(
            "/transactions/{transactionId:guid}/tracking",
            SubmitTrackingAsync);
        authenticated.MapPost(
            "/transactions/{transactionId:guid}/digital-handoff",
            SubmitDigitalHandoffAsync);
        authenticated.MapPost(
            "/transactions/{transactionId:guid}/confirm-receipt",
            ConfirmReceiptAsync);
        authenticated.MapPost(
            "/transactions/{transactionId:guid}/disputes",
            OpenDisputeAsync);
        authenticated.MapPost(
                "/transactions/{transactionId:guid}/dispute-evidence",
                SubmitDisputeEvidenceAsync)
            .RequireRateLimiting("evidence-upload")
            .DisableAntiforgery();
        authenticated.MapGet(
            "/transactions/{transactionId:guid}/dispute-evidence",
            ListOwnDisputeEvidenceAsync);
        authenticated.MapGet(
            "/transactions/{transactionId:guid}/dispute-evidence/{evidenceId:guid}",
            DownloadOwnDisputeEvidenceAsync);
    }

    private static IResult GetBuyerProtectionPreview(
        long itemPriceSatang,
        ClaimsPrincipal principal,
        IPaymentFeePolicy feePolicy)
    {
        _ = PartyIds.From(principal).BuyerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์ดูค่าคุ้มครองผู้ซื้อ");
        var fees = feePolicy.GetDisclosure(itemPriceSatang);
        return Results.Ok(new MobileBuyerProtectionPreviewResponse(
            itemPriceSatang,
            fees.BuyerProtectionFeeSatang,
            fees.PlatformFeeSatang,
            fees.SellerExpectedNetSatang,
            checked(
                itemPriceSatang +
                fees.BuyerProtectionFeeSatang),
            "THB",
            fees.PolicyVersion));
    }

    private static async Task<IResult> ListNotificationsAsync(
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var notifications = await sender.Send(
            new ListNotificationsQuery(RequiredPhone(principal)),
            cancellationToken);
        return Results.Ok(notifications);
    }

    private static async Task<IResult>
        RegisterNotificationDeviceAsync(
            MobileNotificationDeviceRequest request,
            ClaimsPrincipal principal,
            IDeviceNotificationRegistrationProvider provider,
            CancellationToken cancellationToken)
    {
        var installationId =
            RequiredInstallationId(request.InstallationId);
        var platform = request.Platform.Trim().ToLowerInvariant();
        if (platform is not ("ios" or "android"))
            throw new ArgumentException(
                "แพลตฟอร์มการแจ้งเตือนไม่ถูกต้อง");
        var pushToken = request.PushToken.Trim();
        if (pushToken.Length is < 16 or > 4096)
            throw new ArgumentException(
                "รหัสอุปกรณ์สำหรับการแจ้งเตือนไม่ถูกต้อง");

        await provider.RegisterAsync(
            RequiredPhone(principal),
            installationId,
            platform,
            pushToken,
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult>
        UnregisterNotificationDeviceAsync(
            string installationId,
            ClaimsPrincipal principal,
            IDeviceNotificationRegistrationProvider provider,
            CancellationToken cancellationToken)
    {
        await provider.UnregisterAsync(
            RequiredPhone(principal),
            RequiredInstallationId(installationId),
            cancellationToken);
        return Results.NoContent();
    }

    private static string RequiredInstallationId(string value) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id.ToString("N")
            : throw new ArgumentException(
                "รหัสการติดตั้งแอปไม่ถูกต้อง");

    private static async Task<IResult> RequestOtpAsync(
        MobileOtpRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (request.Mode == MobileAuthenticationMode.SignUp &&
            (request.FullName is not null ||
             request.Email is not null))
            throw new ArgumentException(
                "กรุณายืนยันเบอร์ก่อน แล้วกรอกข้อมูลสมัครสมาชิกในขั้นถัดไป");

        var challenge = await sender.Send(
            new RequestBuyerOtpCommand(request.PhoneNumber),
            cancellationToken);
        return Results.Ok(new MobileOtpChallengeResponse(
            challenge.ChallengeId,
            challenge.MaskedPhoneNumber,
            challenge.DevelopmentCode));
    }

    private static async Task<IResult> VerifyOtpAsync(
        MobileOtpVerification request,
        ISender sender,
        MobileSessionTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (request.Mode == MobileAuthenticationMode.SignUp &&
            (request.FullName is not null ||
             request.Email is not null))
            throw new ArgumentException(
                "กรุณายืนยันเบอร์ก่อน แล้วกรอกข้อมูลสมัครสมาชิกในขั้นถัดไป");

        var result = await sender.Send(
            new VerifyMobileCodeCommand(
                request.ChallengeId,
                request.Code,
                request.Mode,
                request.InstallationId),
            cancellationToken);
        if (result.Session is not null)
        {
            var issued = await tokens.CreateAsync(
                result.Session,
                cancellationToken);
            return Results.Ok(
                new MobileOtpVerificationResponse(
                    "session",
                    ToResponse(issued),
                    null));
        }

        var registration = result.Registration!;
        return Results.Ok(
            new MobileOtpVerificationResponse(
                "registration_required",
                null,
                new MobileRegistrationRequiredResponse(
                    registration.RegistrationTicket,
                    registration.ExpiresAt,
                    registration.MaskedPhoneNumber)));
    }

    private static async Task<IResult> CompleteRegistrationAsync(
        MobileRegistrationCompletion request,
        HttpRequest httpRequest,
        ISender sender,
        MobileSessionTokenService tokens,
        ToklongDbContext database,
        CancellationToken cancellationToken)
    {
        var idempotencyKey =
            RequiredNormalizedIdempotencyKey(httpRequest);
        await using IDbContextTransaction? transaction =
            database.Database.IsRelational()
                ? await database.Database.BeginTransactionAsync(
                    cancellationToken)
                : null;

        var profile = await sender.Send(
            new CompleteMobileRegistrationCommand(
                request.RegistrationTicket,
                request.FullName,
                request.Email,
                request.TermsVersion,
                RequiredInstallationId(request.InstallationId),
                idempotencyKey),
            cancellationToken);
        var issued = await tokens.CreateAsync(
            profile,
            cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ToResponse(issued));
    }

    private static async Task<IResult> RefreshSessionAsync(
        MobileRefreshRequest request,
        MobileSessionTokenService tokens,
        CancellationToken cancellationToken)
    {
        var issued = await tokens.RefreshAsync(
            request.RefreshToken,
            cancellationToken);
        return issued is null
            ? Results.Unauthorized()
            : Results.Ok(ToResponse(issued));
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        MobileSessionTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(
                principal.FindFirstValue(
                    MobileAuthenticationDefaults.SessionIdClaim),
                out var sessionId))
            await tokens.RevokeAsync(sessionId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        BuyerProfile? buyer = null;
        SellerProfile? seller = null;
        if (ids.BuyerId.HasValue)
            buyer = await sender.Send(
                new GetBuyerProfileQuery(ids.BuyerId.Value),
                cancellationToken);
        if (ids.SellerId.HasValue)
            seller = await sender.Send(
                new GetSellerProfileQuery(ids.SellerId.Value),
                cancellationToken);

        return Results.Ok(new MobileProfileResponse(
            buyer?.FullName ?? seller?.DisplayName ?? principal.Identity?.Name ?? "",
            buyer?.PhoneNumber ?? seller?.PhoneNumber ??
            principal.FindFirstValue(ClaimTypes.MobilePhone) ?? "",
            buyer?.Email,
            buyer?.SavedDeliveryAddress?.DisplayText,
            buyer?.SavedDeliveryAddress?.ProvinceName,
            buyer?.SavedDeliveryAddress?.PostalCode,
            seller?.PayoutAccounts.FirstOrDefault()?.BankCode,
            seller?.PayoutAccounts.FirstOrDefault()?.MaskedNumber,
            buyer is not null,
            seller is not null));
    }

    private static async Task<IResult> GetPendingEmailChangeAsync(
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var pending = await sender.Send(
            new GetPendingBuyerEmailChangeQuery(
                RequiredEmailChangeBuyerId(principal)),
            cancellationToken);
        return pending is null
            ? Results.NoContent()
            : Results.Ok(ToMobileEmailChange(pending));
    }

    private static async Task<IResult> RequestEmailChangeAsync(
        MobileEmailChangeRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var pending = await sender.Send(
            new RequestBuyerEmailChangeCommand(
                RequiredEmailChangeBuyerId(principal),
                request.Email,
                request.IdempotencyKey),
            cancellationToken);
        return Results.Ok(ToMobileEmailChange(pending));
    }

    private static async Task<IResult> ResendEmailChangeAsync(
        Guid challengeId,
        MobileEmailChangeResendRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var pending = await sender.Send(
            new ResendBuyerEmailChangeCommand(
                RequiredEmailChangeBuyerId(principal),
                challengeId,
                request.IdempotencyKey),
            cancellationToken);
        return Results.Ok(ToMobileEmailChange(pending));
    }

    private static async Task<IResult> VerifyEmailChangeAsync(
        Guid challengeId,
        MobileEmailChangeVerifyRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var verified = await sender.Send(
            new VerifyBuyerEmailChangeCommand(
                RequiredEmailChangeBuyerId(principal),
                challengeId,
                request.Code,
                request.IdempotencyKey),
            cancellationToken);
        return Results.Ok(
            new MobileEmailChangeVerifiedResponse(
                verified.Email,
                verified.CompletedAt));
    }

    private static MobileEmailChangeResponse ToMobileEmailChange(
        BuyerEmailChangeView pending) =>
        new(
            pending.ChallengeId,
            pending.MaskedEmail,
            pending.ExpiresAt,
            pending.ResendAvailableAt,
            pending.RemainingAttempts);

    private static Guid RequiredEmailChangeBuyerId(
        ClaimsPrincipal principal) =>
        PartyIds.From(principal).BuyerId
        ?? throw new DomainException(
            "บัญชีนี้ไม่มีสิทธิ์เปลี่ยนอีเมล");

    private static async Task<IResult> ListTransactionsAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var transactions = await sender.Send(
            new ListPartyTransactionsQuery(
                ids.BuyerId,
                ids.SellerId,
                ids.PhoneNumber),
            cancellationToken);
        return Results.Ok(transactions.Select(
            transaction => ToMobileTransaction(
                request,
                transaction,
                ids,
                configuration)));
    }

    private static async Task<IResult> GetTransactionAsync(
        Guid transactionId,
        HttpRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var transactions = await sender.Send(
            new ListPartyTransactionsQuery(
                ids.BuyerId,
                ids.SellerId,
                ids.PhoneNumber),
            cancellationToken);
        var transaction = transactions.SingleOrDefault(
            candidate => candidate.Id == transactionId)
            ?? throw new NotFoundException("ไม่พบรายการ");
        return Results.Ok(ToMobileTransaction(
            request,
            transaction,
            ids,
            configuration));
    }

    private static async Task<IResult> DownloadAgreementEvidenceAsync(
        Guid transactionId,
        string? format,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var evidence = await sender.Send(
            new GetAgreementEvidenceQuery(
                transactionId,
                ids.BuyerId,
                ids.SellerId),
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
    }

    private static async Task<IResult> CreateOfferAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var buyerId = PartyIds.From(principal).BuyerId
            ?? throw new DomainException(
                "บัญชีนี้ยังไม่มีโปรไฟล์ผู้ซื้อ กรุณาสมัครสมาชิก");
        var form = await request.ReadFormAsync(cancellationToken);
        if (!Enum.TryParse<FulfillmentType>(
                form["fulfillmentType"],
                true,
                out var fulfillmentType))
            throw new ArgumentException("ประเภทสินค้าไม่ถูกต้อง");
        if (!Enum.TryParse<ConditionCode>(
                form["condition"],
                true,
                out var condition))
            throw new ArgumentException("กรุณาเลือกสภาพสินค้า");
        if (!long.TryParse(form["amountSatang"], out var amountSatang))
            throw new ArgumentException("ยอดเงินไม่ถูกต้อง");
        string? photoPath = null;
        var photo = form.Files.GetFile("photo");
        if (photo is not null)
        {
            if (photo.Length > 8 * 1024 * 1024)
                throw new ArgumentException(
                    "รูปต้องมีขนาดไม่เกิน 8 MB");

            await using var stream = photo.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            photoPath = await sender.Send(
                new SaveListingPhotoCommand(new ListingImageInput(
                    photo.FileName,
                    photo.ContentType,
                    memory.ToArray())),
                cancellationToken);
        }
        var useSavedAddress =
            bool.TryParse(
                form["useSavedAddress"],
                out var parsedUseSavedAddress) &&
            parsedUseSavedAddress;
        var rememberAddress =
            bool.TryParse(
                form["rememberAddress"],
                out var parsedRememberAddress) &&
            parsedRememberAddress;
        var transaction = await sender.Send(
            new CreateBuyerOfferCommand(
                buyerId,
                form["sellerPhoneNumber"].ToString(),
                fulfillmentType,
                form["productName"].ToString(),
                form["agreementDetails"].ToString(),
                condition,
                form["knownDefects"].ToString(),
                photoPath,
                amountSatang,
                useSavedAddress,
                fulfillmentType ==
                    FulfillmentType.PhysicalShipment &&
                !useSavedAddress
                    ? new OfferDeliveryAddressInput(
                        form["addressLine"].ToString(),
                        ParseRequiredInt(
                            form,
                            "provinceId",
                            "จังหวัดปลายทาง"),
                        ParseRequiredInt(
                            form,
                            "districtId",
                            "อำเภอหรือเขตปลายทาง"),
                        ParseRequiredInt(
                            form,
                            "subdistrictId",
                            "ตำบลหรือแขวงปลายทาง"))
                    : null,
                rememberAddress),
            cancellationToken);

        return Results.Created(
            $"/api/mobile/transactions/{transaction.Id}",
            ToMobileTransaction(
                request,
                transaction,
                new PartyIds(
                    buyerId,
                    null,
                    RequiredPhone(principal)),
                configuration));
    }

    private static async Task<IResult> ExtractAgreementDraftAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var images = new List<ListingImageInput>();
        foreach (var file in form.Files)
        {
            if (file.Length >
                ExtractAgreementDraftHandler.MaximumImageBytes)
                throw new ArgumentException(
                    "รูปต้องมีขนาดไม่เกิน 6 MB ต่อรูป");

            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            images.Add(new ListingImageInput(
                file.FileName,
                file.ContentType,
                memory.ToArray()));
        }

        var sessionId = principal.FindFirstValue(
            MobileAuthenticationDefaults.SessionIdClaim)
            ?? throw new DomainException(
                "เซสชันไม่ถูกต้อง กรุณาเข้าสู่ระบบใหม่");
        var safetyIdentifier = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))
            .ToLowerInvariant();
        var draft = await sender.Send(
            new ExtractAgreementDraftCommand(
                form["chatText"].ToString(),
                images,
                safetyIdentifier),
            cancellationToken);

        return Results.Ok(draft);
    }

    private static async Task<IResult> PreparePaymentSheetAsync(
        Guid transactionId,
        MobilePaymentSheetRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var buyerId = PartyIds.From(principal).BuyerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์ชำระข้อเสนอ");
        var result = await sender.Send(
            new PreparePaymentSheetCommand(
                transactionId,
                buyerId,
                request.AcceptedTerms),
            cancellationToken);
        return Results.Ok(new MobilePaymentSheetResponse(
            result.ClientSecret,
            result.PublishableKey,
            result.ReceiptEmail));
    }

    private static async Task<IResult> GetSellerOfferAsync(
        string publicToken,
        HttpRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        IPaymentFeePolicy feePolicy,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var phone = RequiredPhone(principal);
        var transaction = await sender.Send(
            new GetPublicTransactionQuery(publicToken),
            cancellationToken);
        if (!string.Equals(
                transaction.SellerContact,
                phone,
                StringComparison.Ordinal))
            throw new ForbiddenException(
                "ไม่พบข้อเสนอสำหรับบัญชีนี้");
        var seller = await sender.Send(
            new GetSellerProfileByPhoneQuery(phone),
            cancellationToken);
        var fees = feePolicy.GetDisclosure(transaction.PriceSatang);

        return Results.Ok(new MobileSellerOfferResponse(
            ToMobileTransaction(
                request,
                transaction,
                new PartyIds(
                    null,
                    seller?.Id,
                    RequiredPhone(principal)),
                configuration),
            fees.BuyerProtectionFeeSatang,
            fees.PlatformFeeSatang,
            fees.SellerExpectedNetSatang,
            fees.PolicyVersion,
            seller?.PayoutAccounts.Select(ToPayoutAccount).ToArray() ?? [],
            seller?.SavedShippingOrigin is null
                ? null
                : new MobileSavedShippingOriginResponse(
                    seller.SavedShippingOrigin.DisplayText,
                    seller.SavedShippingOrigin.ProvinceId,
                    seller.SavedShippingOrigin.ProvinceName,
                    seller.SavedShippingOrigin.DistrictId,
                    seller.SavedShippingOrigin.DistrictName,
                    seller.SavedShippingOrigin.SubdistrictId,
                    seller.SavedShippingOrigin.SubdistrictName,
                    seller.SavedShippingOrigin.PostalCode)));
    }

    private static async Task<IResult> GetSellerShippingQuotesAsync(
        string publicToken,
        MobileShippingQuoteRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var seller = await sender.Send(
            new EnsureSellerProfileCommand(
                RequiredPhone(principal)),
            cancellationToken);
        var quotes = await sender.Send(
            new GetShippingQuotesQuery(
                publicToken,
                seller.Id,
                request.UseSavedOrigin,
                request.AddressLine,
                request.ProvinceId,
                request.DistrictId,
                request.SubdistrictId,
                request.WeightGrams,
                request.WidthCentimeters,
                request.LengthCentimeters,
                request.HeightCentimeters),
            cancellationToken);
        return Results.Ok(
            quotes.Select(quote =>
                new MobileShippingQuoteResponse(
                    quote.Provider,
                    quote.QuoteReference,
                    quote.CarrierCode,
                    quote.ServiceCode,
                    quote.ServiceName,
                    quote.FeeSatang,
                    quote.ExpiresAt)));
    }

    private static async Task<IResult> SaveMobileSellerPayoutAccountAsync(
        MobileSavePayoutAccountRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        MobileSessionTokenService tokens,
        CancellationToken cancellationToken)
    {
        var phone = RequiredPhone(principal);
        var seller = await sender.Send(
            new EnsureSellerProfileCommand(phone),
            cancellationToken);
        seller = await sender.Send(
            new SaveSellerPayoutAccountCommand(
                seller.Id,
                request.AccountId,
                request.BankCode,
                request.AccountName,
                request.AccountNumber),
            cancellationToken);
        var issued = await AttachSellerSessionAsync(
            principal,
            seller,
            tokens,
            cancellationToken);
        return Results.Ok(new MobileSellerProfileUpdateResponse(
            ToResponse(issued),
            seller.PayoutAccounts.Select(ToPayoutAccount).ToArray()));
    }

    private static async Task<IResult> GetMobileSellerPayoutAccountsAsync(
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var seller = await sender.Send(
            new GetSellerProfileByPhoneQuery(
                RequiredPhone(principal)),
            cancellationToken);
        return Results.Ok(
            seller?.PayoutAccounts.Select(ToPayoutAccount).ToArray() ??
            []);
    }

    private static async Task<IResult> AcceptSellerOfferAsync(
        string publicToken,
        MobileAcceptSellerOfferRequest request,
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        ISender sender,
        MobileSessionTokenService tokens,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var seller = await sender.Send(
            new EnsureSellerProfileCommand(RequiredPhone(principal)),
            cancellationToken);
        var transaction = await sender.Send(
            new AcceptBuyerOfferCommand(
                publicToken,
                seller.Id,
                request.PayoutAccountId,
                request.TransferRightsAttested,
                request.SellerAcceptedTerms,
                request.DisclosedBuyerProtectionFeeSatang,
                request.DisclosedPlatformFeeSatang,
                request.DisclosedSellerExpectedNetSatang,
                request.DisclosedFeePolicyVersion,
                request.Shipping is null
                    ? null
                    : new SellerShippingSelectionInput(
                        request.Shipping.UseSavedOrigin,
                        request.Shipping.AddressLine,
                        request.Shipping.ProvinceId,
                        request.Shipping.DistrictId,
                        request.Shipping.SubdistrictId,
                        request.Shipping.RememberOrigin,
                        request.Shipping.WeightGrams,
                        request.Shipping.WidthCentimeters,
                        request.Shipping.LengthCentimeters,
                        request.Shipping.HeightCentimeters,
                        request.Shipping.QuoteReference,
                        request.Shipping
                            .DisclosedShippingFeeSatang)),
            cancellationToken);
        var issued = await AttachSellerSessionAsync(
            principal,
            seller,
            tokens,
            cancellationToken);

        return Results.Ok(new MobileSellerOfferActionResponse(
            ToMobileTransaction(
                httpRequest,
                transaction,
                new PartyIds(
                    null,
                    seller.Id,
                    RequiredPhone(principal)),
                configuration),
            ToResponse(issued)));
    }

    private static async Task<IResult> DeclineSellerOfferAsync(
        string publicToken,
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        ISender sender,
        MobileSessionTokenService tokens,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var seller = await sender.Send(
            new EnsureSellerProfileCommand(RequiredPhone(principal)),
            cancellationToken);
        var transaction = await sender.Send(
            new DeclineBuyerOfferCommand(publicToken, seller.Id),
            cancellationToken);
        var issued = await AttachSellerSessionAsync(
            principal,
            seller,
            tokens,
            cancellationToken);

        return Results.Ok(new MobileSellerOfferActionResponse(
            ToMobileTransaction(
                httpRequest,
                transaction,
                new PartyIds(
                    null,
                    seller.Id,
                    RequiredPhone(principal)),
                configuration),
            ToResponse(issued)));
    }

    private static async Task<IssuedMobileSession> AttachSellerSessionAsync(
        ClaimsPrincipal principal,
        SellerProfile seller,
        MobileSessionTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(
                principal.FindFirstValue(
                    MobileAuthenticationDefaults.SessionIdClaim),
                out var sessionId))
            throw new DomainException(
                "เซสชันไม่ถูกต้อง กรุณาเข้าสู่ระบบใหม่");
        return await tokens.AttachSellerAsync(
                   sessionId,
                   seller,
                   cancellationToken)
               ?? throw new DomainException(
                   "ไม่สามารถเพิ่มสิทธิ์ผู้ขายได้ กรุณาเข้าสู่ระบบใหม่");
    }

    private static string RequiredPhone(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.MobilePhone)
        ?? throw new DomainException(
            "เซสชันไม่มีเบอร์โทรศัพท์ กรุณาเข้าสู่ระบบใหม่");

    private static async Task<IResult> DownloadShippingLabelAsync(
        Guid transactionId,
        ClaimsPrincipal principal,
        ISender sender,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var sellerId = PartyIds.From(principal).SellerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์เปิดใบปะหน้า");
        var html = await sender.Send(
            new GetShippingLabelQuery(
                transactionId,
                sellerId),
            cancellationToken);
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Disposition"] =
            $"attachment; filename=\"TOKLONG-label-{transactionId:N}.html\"";
        response.Headers["Content-Security-Policy"] =
            "sandbox; default-src 'none'; img-src data: https:; " +
            "style-src 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src https://fonts.gstatic.com";
        return Results.Text(
            html,
            "text/html",
            Encoding.UTF8);
    }

    private static MobilePayoutAccountResponse ToPayoutAccount(
        SellerPayoutAccountView account) =>
        new(
            account.Id,
            account.BankCode,
            account.AccountName,
            account.MaskedNumber,
            account.IsDefault);

    private static async Task<IResult> SubmitTrackingAsync(
        Guid transactionId,
        MobileTrackingRequest request,
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var sellerId = ids.SellerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์จัดการรายการขาย");
        var transaction = await sender.Send(
            new SubmitTrackingForSellerCommand(
                transactionId,
                sellerId,
                request.CarrierCode,
                request.TrackingNumber),
            cancellationToken);
        return Results.Ok(ToMobileTransaction(
            httpRequest,
            transaction,
            ids,
            configuration));
    }

    private static async Task<IResult> SubmitDigitalHandoffAsync(
        Guid transactionId,
        MobileDigitalHandoffRequest request,
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var sellerId = ids.SellerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์จัดการรายการขาย");
        var transaction = await sender.Send(
            new SubmitDigitalHandoffForSellerCommand(
                transactionId,
                sellerId,
                request.Statement),
            cancellationToken);
        return Results.Ok(ToMobileTransaction(
            httpRequest,
            transaction,
            ids,
            configuration));
    }

    private static async Task<IResult> ConfirmReceiptAsync(
        Guid transactionId,
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var buyerId = ids.BuyerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์จัดการรายการซื้อ");
        var transaction = await sender.Send(
            new ConfirmReceiptForBuyerCommand(transactionId, buyerId),
            cancellationToken);
        return Results.Ok(ToMobileTransaction(
            httpRequest,
            transaction,
            ids,
            configuration));
    }

    private static async Task<IResult> OpenDisputeAsync(
        Guid transactionId,
        MobileDisputeRequest request,
        HttpRequest httpRequest,
        ClaimsPrincipal principal,
        ISender sender,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = PartyIds.From(principal);
        var buyerId = ids.BuyerId
            ?? throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์จัดการรายการซื้อ");
        var transaction = await sender.Send(
            new OpenDisputeForBuyerCommand(
                transactionId,
                buyerId,
                request.Reason,
                request.Statement),
            cancellationToken);
        return Results.Ok(ToMobileTransaction(
            httpRequest,
            transaction,
            ids,
            configuration));
    }

    private static async Task<IResult> SubmitDisputeEvidenceAsync(
        Guid transactionId,
        HttpRequest request,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            throw new DomainException(
                "ต้องส่งหลักฐานแบบ multipart/form-data");
        var form = await request.ReadFormAsync(
            cancellationToken);
        if (form.Files.Count != 1)
            throw new DomainException(
                "กรุณาแนบรูปหลักฐานหนึ่งรูปต่อคำขอ");
        if (!Enum.TryParse<DisputeEvidenceParty>(
                form["party"].ToString(),
                true,
                out var party))
            throw new DomainException(
                "ฝ่ายที่ส่งหลักฐานไม่ถูกต้อง");
        if (!Enum.TryParse<DisputeEvidenceType>(
                form["evidenceType"].ToString(),
                true,
                out var evidenceType))
            throw new DomainException(
                "ประเภทหลักฐานไม่ถูกต้อง");
        var idempotencyKey = request.Headers[
                "Idempotency-Key"]
            .ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException(
                "ต้องส่ง Idempotency-Key");
        var file = form.Files[0];
        if (file.Length is < 1 or > 6_000_000)
            throw new DomainException(
                "รูปหลักฐานต้องมีขนาดไม่เกิน 6 MB");
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(
            memory,
            cancellationToken);
        var ids = PartyIds.From(principal);
        var result = await sender.Send(
            new SubmitDisputeEvidenceCommand(
                transactionId,
                ids.BuyerId,
                ids.SellerId,
                party,
                evidenceType,
                form["description"].ToString(),
                idempotencyKey,
                new DisputeEvidenceFileInput(
                    Path.GetFileName(file.FileName),
                    file.ContentType,
                    memory.ToArray())),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListOwnDisputeEvidenceAsync(
        Guid transactionId,
        string party,
        ClaimsPrincipal principal,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DisputeEvidenceParty>(
                party,
                true,
                out var parsedParty))
            throw new DomainException(
                "ฝ่ายที่ส่งหลักฐานไม่ถูกต้อง");
        var ids = PartyIds.From(principal);
        var evidence = await sender.Send(
            new ListOwnDisputeEvidenceQuery(
                transactionId,
                ids.BuyerId,
                ids.SellerId,
                parsedParty),
            cancellationToken);
        return Results.Ok(evidence);
    }

    private static async Task<IResult>
        DownloadOwnDisputeEvidenceAsync(
            Guid transactionId,
            Guid evidenceId,
            string party,
            ClaimsPrincipal principal,
            ISender sender,
            HttpResponse response,
            CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DisputeEvidenceParty>(
                party,
                true,
                out var parsedParty))
            throw new DomainException(
                "ฝ่ายที่ส่งหลักฐานไม่ถูกต้อง");
        var ids = PartyIds.From(principal);
        var evidence = await sender.Send(
            new GetOwnDisputeEvidenceFileQuery(
                transactionId,
                evidenceId,
                ids.BuyerId,
                ids.SellerId,
                parsedParty),
            cancellationToken);
        response.Headers.CacheControl = "no-store";
        response.Headers["Content-Security-Policy"] =
            "default-src 'none'; sandbox";
        response.Headers["Content-Disposition"] =
            $"inline; filename=\"evidence-{evidence.EvidenceId:N}.jpg\"";
        return Results.File(
            evidence.Content,
            evidence.ContentType,
            enableRangeProcessing: false);
    }

    private static MobileTransactionResponse ToMobileTransaction(
        HttpRequest request,
        TransactionView transaction,
        PartyIds ids,
        IConfiguration configuration)
    {
        var isBuyer = ids.BuyerId.HasValue &&
                      transaction.BuyerId == ids.BuyerId;
        var role = isBuyer ? "Buyer" : "Seller";
        var counterparty = isBuyer
            ? transaction.SellerDisplayName
            : transaction.BuyerDisplayName ?? "ผู้ซื้อ";
        var deadline = transaction.State switch
        {
            TransactionState.AwaitingSellerAcceptance =>
                transaction.SellerAcceptanceDeadlineAt,
            TransactionState.SellerAcceptedAwaitingPayment or
                TransactionState.CheckoutStarted or
                TransactionState.PaymentPending =>
                transaction.BuyerPaymentDeadlineAt,
            TransactionState.PaidAwaitingShipment or
                TransactionState.PaidAwaitingDigitalDelivery =>
                transaction.ShipByAt,
            TransactionState.TrackingSubmitted or
                TransactionState.TrackingUnverified
                when transaction.IsProviderManagedShipment &&
                     !transaction.FirstCarrierScanAt.HasValue =>
                transaction.ShipByAt,
            TransactionState.DeliveredDisputeWindow =>
                transaction.DisputeWindowEndsAt,
            TransactionState.RefundPending when
                transaction.RefundProviderStatus ==
                "requires_action" =>
                    transaction.RefundActionExpiresAt,
            TransactionState.Expired when
                transaction.ExpirationReason ==
                TransactionExpirationReason.SellerDidNotRespond =>
                    transaction.SellerAcceptanceDeadlineAt,
            TransactionState.Expired when
                transaction.ExpirationReason ==
                TransactionExpirationReason.BuyerDidNotPay =>
                    transaction.BuyerPaymentDeadlineAt,
            _ => null
        };
        var updatedAt = transaction.AuditEvents
            .Select(audit => audit.CreatedAt)
            .DefaultIfEmpty(DateTimeOffset.UtcNow)
            .Max();
        var photoUrl = ResolvePublicPhotoUrl(
            transaction.PhotoUrl,
            request);

        return new MobileTransactionResponse(
            transaction.Id,
            transaction.ProductName,
            transaction.BuyerTotalSatang,
            transaction.PriceSatang,
            transaction.ShippingFeeSatang,
            transaction.Currency,
            role,
            transaction.FulfillmentType == FulfillmentType.PhysicalShipment
                ? "Physical"
                : "Digital",
            transaction.State.ToString(),
            updatedAt,
            deadline,
            counterparty,
            photoUrl,
            transaction.Description,
            transaction.TermsVersion,
            transaction.BuyerProtectionFeeSatang,
            transaction.PlatformFeeSatang,
            transaction.SellerExpectedNetSatang,
            transaction.FeePolicyVersion,
            transaction.ExpirationReason?.ToString(),
            isBuyer
                ? new Uri(
                    GetWebBaseUri(configuration),
                    $"offer/{transaction.PublicToken}").ToString()
                : transaction.State ==
                      TransactionState
                          .AwaitingSellerAcceptance &&
                  string.Equals(
                      transaction.SellerContact,
                      ids.PhoneNumber,
                      StringComparison.Ordinal)
                    ? $"toklong://offer/{transaction.PublicToken}"
                : null,
            transaction.AgreementCoreSnapshotHash,
            transaction.SellerAcceptedAt,
            transaction.BuyerAcceptedAt,
            transaction.DeliveryProvinceName,
            transaction.DeliveryPostalCode,
            isBuyer ||
            transaction.ShipByAt.HasValue
                ? transaction.DeliveryAddress
                : null,
            transaction.CarrierCode,
            transaction.ShippingServiceName,
            transaction.Condition.ToString(),
            transaction.KnownDefects,
            transaction.IsProviderManagedShipment,
            transaction.TrackingNumber,
            transaction.IsProviderManagedShipment &&
            transaction.ShippingConfirmedAt.HasValue,
            transaction.ShipByAt,
            transaction.FirstCarrierScanAt,
            transaction.RefundProviderStatus,
            transaction.RefundActionRequiredAt,
            transaction.RefundActionExpiresAt,
            transaction.RefundInstructionsSentAt,
            transaction.CreatedAt);
    }

    private static string? ResolvePublicPhotoUrl(
        string? value,
        HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim();
        var requestBase = new Uri(
            $"{request.Scheme}://{request.Host}/");

        // A leading slash is a web root-relative path. Uri.TryCreate treats it
        // as an absolute file URI, which makes iOS look inside the app bundle.
        if (candidate.StartsWith(
                "/",
                StringComparison.Ordinal))
            return new Uri(
                requestBase,
                candidate.TrimStart('/')).ToString();

        if (Uri.TryCreate(
                candidate,
                UriKind.Absolute,
                out var absolute))
            return absolute.Scheme.Equals(
                       Uri.UriSchemeHttp,
                       StringComparison.OrdinalIgnoreCase) ||
                   absolute.Scheme.Equals(
                       Uri.UriSchemeHttps,
                       StringComparison.OrdinalIgnoreCase)
                ? absolute.ToString()
                : null;

        return new Uri(requestBase, candidate).ToString();
    }

    private static Uri GetWebBaseUri(IConfiguration configuration)
    {
        var value = configuration["PublicUrls:WebBaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่า PublicUrls:WebBaseUrl เป็น HTTPS");
        return uri.AbsoluteUri.EndsWith(
            "/",
            StringComparison.Ordinal)
            ? uri
            : new Uri($"{uri.AbsoluteUri}/");
    }

    private static string FirstLine(string value) =>
        value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "รายการ";

    private static int ParseRequiredInt(
        IFormCollection form,
        string field,
        string label) =>
        int.TryParse(
            form[field],
            out var value) &&
        value > 0
            ? value
            : throw new ArgumentException(
                $"กรุณาเลือก{label}");

    private static MobileSessionResponse ToResponse(
        IssuedMobileSession issued) =>
        new(
            issued.AccessToken,
            issued.RefreshToken,
            issued.AccessTokenExpiresAt,
            issued.DisplayName,
            issued.PhoneNumber,
            issued.CanBuy,
            issued.CanSell);

    private static string RequiredNormalizedIdempotencyKey(
        HttpRequest request)
    {
        var value = request.Headers["Idempotency-Key"].ToString();
        return value.Length == 32 &&
               Guid.TryParseExact(value, "N", out var id) &&
               id != Guid.Empty
            ? id.ToString("N")
            : throw new ArgumentException(
                "Idempotency-Key ไม่ถูกต้อง");
    }

    private sealed record PartyIds(
        Guid? BuyerId,
        Guid? SellerId,
        string PhoneNumber)
    {
        public static PartyIds From(ClaimsPrincipal principal) =>
            new(
                Parse(principal, MobileAuthenticationDefaults.BuyerIdClaim),
                Parse(principal, MobileAuthenticationDefaults.SellerIdClaim),
                RequiredPhone(principal));

        private static Guid? Parse(
            ClaimsPrincipal principal,
            string claimType) =>
            Guid.TryParse(
                principal.FindFirstValue(claimType),
                out var value)
                ? value
                : null;
    }
}

public sealed record MobileOtpRequest(
    string PhoneNumber,
    MobileAuthenticationMode Mode,
    string? FullName,
    string? Email);

public sealed record MobileOtpChallengeResponse(
    string ChallengeId,
    string MaskedPhoneNumber,
    string? DevelopmentCode);

public sealed record MobileOtpVerification(
    string ChallengeId,
    string Code,
    MobileAuthenticationMode Mode,
    string? FullName,
    string? Email,
    string? InstallationId);

public sealed record MobileOtpVerificationResponse(
    string Outcome,
    MobileSessionResponse? Session,
    MobileRegistrationRequiredResponse? Registration);

public sealed record MobileRegistrationRequiredResponse(
    string RegistrationTicket,
    DateTimeOffset ExpiresAt,
    string MaskedPhoneNumber);

public sealed record MobileRegistrationCompletion(
    string RegistrationTicket,
    string FullName,
    string Email,
    string TermsVersion,
    string InstallationId);

public sealed record MobileSessionResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string DisplayName,
    string PhoneNumber,
    bool CanBuy,
    bool CanSell);

public sealed record MobileRefreshRequest(string RefreshToken);

public sealed record MobileProfileResponse(
    string DisplayName,
    string PhoneNumber,
    string? Email,
    string? SavedAddress,
    string? SavedDeliveryProvinceName,
    string? SavedDeliveryPostalCode,
    string? PayoutBankCode,
    string? PayoutMaskedNumber,
    bool CanBuy,
    bool CanSell);

public sealed record MobileEmailChangeRequest(
    string Email,
    string IdempotencyKey);

public sealed record MobileEmailChangeResendRequest(
    string IdempotencyKey);

public sealed record MobileEmailChangeVerifyRequest(
    string Code,
    string IdempotencyKey);

public sealed record MobileEmailChangeResponse(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public sealed record MobileEmailChangeVerifiedResponse(
    string Email,
    DateTimeOffset CompletedAt);

public sealed record MobileBuyerProtectionPreviewResponse(
    long ItemPriceSatang,
    long BuyerProtectionFeeSatang,
    long PlatformFeeSatang,
    long SellerExpectedNetSatang,
    long TotalBeforeShippingSatang,
    string Currency,
    string FeePolicyVersion);

public sealed record MobilePayoutAccountResponse(
    Guid Id,
    string BankCode,
    string AccountName,
    string MaskedNumber,
    bool IsDefault);

public sealed record MobileSellerOfferResponse(
    MobileTransactionResponse Transaction,
    long BuyerProtectionFeeSatang,
    long PlatformFeeSatang,
    long SellerExpectedNetSatang,
    string FeePolicyVersion,
    IReadOnlyList<MobilePayoutAccountResponse> PayoutAccounts,
    MobileSavedShippingOriginResponse? SavedShippingOrigin);

public sealed record MobileSavedShippingOriginResponse(
    string DisplayText,
    int ProvinceId,
    string ProvinceName,
    int DistrictId,
    string DistrictName,
    int SubdistrictId,
    string SubdistrictName,
    string PostalCode);

public sealed record MobileSavePayoutAccountRequest(
    Guid? AccountId,
    string BankCode,
    string AccountName,
    string AccountNumber);

public sealed record MobileSellerProfileUpdateResponse(
    MobileSessionResponse Session,
    IReadOnlyList<MobilePayoutAccountResponse> PayoutAccounts);

public sealed record MobileAcceptSellerOfferRequest(
    Guid PayoutAccountId,
    bool TransferRightsAttested,
    bool SellerAcceptedTerms,
    long DisclosedBuyerProtectionFeeSatang,
    long DisclosedPlatformFeeSatang,
    long DisclosedSellerExpectedNetSatang,
    string DisclosedFeePolicyVersion,
    MobileSellerShippingSelectionRequest? Shipping);

public sealed record MobileSellerShippingSelectionRequest(
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    bool RememberOrigin,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string QuoteReference,
    long DisclosedShippingFeeSatang);

public sealed record MobileShippingQuoteRequest(
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters);

public sealed record MobileShippingQuoteResponse(
    string Provider,
    string QuoteReference,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long FeeSatang,
    DateTimeOffset ExpiresAt);

public sealed record MobileNotificationDeviceRequest(
    string InstallationId,
    string Platform,
    string PushToken);

public sealed record MobileSellerOfferActionResponse(
    MobileTransactionResponse Transaction,
    MobileSessionResponse Session);

public sealed record MobileTransactionResponse(
    Guid Id,
    string ProductName,
    long AmountSatang,
    long ItemPriceSatang,
    long ShippingFeeSatang,
    string Currency,
    string Role,
    string FulfillmentType,
    string State,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActionDeadline,
    string CounterpartyName,
    string? PhotoUrl,
    string AgreementDetails,
    string TermsVersion,
    long BuyerProtectionFeeSatang,
    long PlatformFeeSatang,
    long SellerExpectedNetSatang,
    string FeePolicyVersion,
    string? ExpirationReason,
    string? SellerInvitationUrl,
    string? AgreementCoreSnapshotHash,
    DateTimeOffset? SellerAcceptedAt,
    DateTimeOffset? BuyerAcceptedAt,
    string? DeliveryProvinceName,
    string? DeliveryPostalCode,
    string? DeliveryAddress,
    string? CarrierCode,
    string? ShippingServiceName,
    string Condition,
    string KnownDefects,
    bool ShippingManagedByProvider,
    string? TrackingNumber,
    bool ShippingLabelAvailable,
    DateTimeOffset? ShipByAt,
    DateTimeOffset? FirstCarrierScanAt,
    string? RefundProviderStatus,
    DateTimeOffset? RefundActionRequiredAt,
    DateTimeOffset? RefundActionExpiresAt,
    DateTimeOffset? RefundInstructionsSentAt,
    DateTimeOffset CreatedAt);

public sealed record MobilePaymentSheetRequest(bool AcceptedTerms);

public sealed record MobilePaymentSheetResponse(
    string ClientSecret,
    string PublishableKey,
    string ReceiptEmail);

public sealed record MobileTrackingRequest(
    string CarrierCode,
    string TrackingNumber);

public sealed record MobileCarrierResponse(
    string Code,
    string DisplayName,
    string TrackingHint,
    string TrackingExample,
    string ValidationPattern,
    string ValidationMessage,
    int MaximumLength);

public sealed record MobileDigitalHandoffRequest(string Statement);

public sealed record MobileDisputeRequest(
    DisputeReason Reason,
    string Statement);
