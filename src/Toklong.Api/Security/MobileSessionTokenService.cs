using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Authentication;
using Toklong.Application.Features.Sellers;
using Toklong.Domain.Authentication;

namespace Toklong.Api.Security;

public static class MobileAuthenticationDefaults
{
    public const string Scheme = "MobileBearer";
    public const string BuyerIdClaim = "toklong_buyer_id";
    public const string SellerIdClaim = "toklong_seller_id";
    public const string SessionIdClaim = "toklong_session_id";
}

public sealed record IssuedMobileSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string DisplayName,
    string PhoneNumber,
    bool CanBuy,
    bool CanSell);

public sealed class MobileSessionTokenService(
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    IMobileSessionRepository sessions,
    IMobileSessionAccountNameReader accountNames,
    IUnitOfWork unitOfWork,
    IAccountPhoneTransactionManager phoneTransactions)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AccessLifetime =
        TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime =
        TimeSpan.FromDays(30);
    private readonly IDataProtector protector = dataProtectionProvider
        .CreateProtector("Toklong.MobileAccessToken.v1");

    public async Task<IssuedMobileSession> CreateAsync(
        MobileSessionProfile profile,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var phone = ThaiMobilePhone.Normalize(
            profile.PhoneNumber);
        await using var phoneTransaction =
            await phoneTransactions.BeginAsync(
                phone,
                cancellationToken);
        var currentNames = await ReadCurrentNamesAsync(
            profile.BuyerId,
            profile.SellerId,
            cancellationToken);
        var displayName = SelectDisplayName(
            profile.BuyerId,
            profile.SellerId,
            currentNames,
            phone);
        var refreshToken = NewRefreshToken();
        var session = MobileSession.Create(
            profile.BuyerId,
            profile.SellerId,
            displayName,
            phone,
            Hash(refreshToken),
            now,
            now.Add(RefreshLifetime));
        await sessions.AddAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await phoneTransaction.CommitAsync(cancellationToken);
        return Issue(session, refreshToken, now);
    }

    public async Task<IssuedMobileSession?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var now = timeProvider.GetUtcNow();
        var session = await sessions.GetByRefreshTokenHashAsync(
            Hash(refreshToken),
            cancellationToken);
        if (session is null || !session.IsActive(now))
            return null;

        var replacement = NewRefreshToken();
        session.RotateRefreshToken(Hash(replacement), now);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        return Issue(session, replacement, now);
    }

    public async Task RevokeAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetByIdAsync(
            sessionId,
            cancellationToken);
        if (session is null)
            return;
        session.Revoke(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IssuedMobileSession?> AttachSellerAsync(
        Guid sessionId,
        SellerProfile seller,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var phone = ThaiMobilePhone.Normalize(
            seller.PhoneNumber);
        await using var phoneTransaction =
            await phoneTransactions.BeginAsync(
                phone,
                cancellationToken);
        var session = await sessions.GetByIdAsync(
            sessionId,
            cancellationToken);
        if (session is null || !session.IsActive(now))
            return null;
        var currentNames = await ReadCurrentNamesAsync(
            session.BuyerId,
            seller.Id,
            cancellationToken);
        var currentSeller = currentNames.Seller;
        if (currentSeller is null ||
            !string.Equals(
                ThaiMobilePhone.Normalize(
                    currentSeller.PhoneNumber),
                phone,
                StringComparison.Ordinal))
            return null;
        var displayName = SelectDisplayName(
            session.BuyerId,
            currentSeller.AccountId,
            currentNames,
            phone);

        var replacement = NewRefreshToken();
        session.AttachSeller(
            currentSeller.AccountId,
            currentSeller.PhoneNumber,
            displayName,
            now);
        session.UpdateDisplayName(displayName);
        session.RotateRefreshToken(Hash(replacement), now);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await phoneTransaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        return Issue(session, replacement, now);
    }

    private async Task<CurrentNames> ReadCurrentNamesAsync(
        Guid? buyerId,
        Guid? sellerId,
        CancellationToken cancellationToken)
    {
        if (!buyerId.HasValue && !sellerId.HasValue)
            throw new InvalidOperationException(
                "Mobile session must reference an account.");
        var buyer = buyerId.HasValue
            ? await accountNames.GetBuyerAsync(
                buyerId.Value,
                cancellationToken)
            : null;
        var seller = sellerId.HasValue
            ? await accountNames.GetSellerAsync(
                sellerId.Value,
                cancellationToken)
            : null;
        return new CurrentNames(buyer, seller);
    }

    private static string SelectDisplayName(
        Guid? buyerId,
        Guid? sellerId,
        CurrentNames current,
        string normalizedPhone)
    {
        if (buyerId.HasValue && current.Buyer is null)
            throw new InvalidOperationException(
                "Buyer account is unavailable.");
        if (sellerId.HasValue && current.Seller is null)
            throw new InvalidOperationException(
                "Seller account is unavailable.");
        if (current.Buyer is not null)
            EnsurePhone(normalizedPhone, current.Buyer.PhoneNumber);
        if (current.Seller is not null)
            EnsurePhone(normalizedPhone, current.Seller.PhoneNumber);
        if (current.Buyer is not null &&
            current.Seller is not null &&
            !NamesMatch(current.Buyer, current.Seller))
            throw new InvalidOperationException(
                "Account roles have inconsistent names.");
        return current.Buyer?.DisplayName ??
               current.Seller!.DisplayName;
    }

    private static bool NamesMatch(
        MobileSessionAccountName buyer,
        MobileSessionAccountName seller) =>
        string.Equals(
            buyer.FirstName,
            seller.FirstName,
            StringComparison.Ordinal) &&
        string.Equals(
            buyer.LastName,
            seller.LastName,
            StringComparison.Ordinal) &&
        string.Equals(
            buyer.DisplayName,
            seller.DisplayName,
            StringComparison.Ordinal);

    private static void EnsurePhone(
        string expected,
        string actual)
    {
        if (!string.Equals(
                expected,
                ThaiMobilePhone.Normalize(actual),
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Account phone does not match the session.");
    }

    private sealed record CurrentNames(
        MobileSessionAccountName? Buyer,
        MobileSessionAccountName? Seller);

    public async Task<ClaimsPrincipal?> ValidateAccessAsync(
        string token,
        CancellationToken cancellationToken)
    {
        MobileAccessTicket? ticket;
        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(token);
            var json = protector.Unprotect(protectedBytes);
            ticket = JsonSerializer.Deserialize<MobileAccessTicket>(
                json,
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is CryptographicException or
            FormatException or
            JsonException)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (ticket is null || ticket.ExpiresAt <= now)
            return null;
        var session = await sessions.GetByIdAsync(
            ticket.SessionId,
            cancellationToken);
        if (session is null ||
            !session.IsActive(now) ||
            session.BuyerId != ticket.BuyerId ||
            session.SellerId != ticket.SellerId)
            return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, session.DisplayName),
            new(ClaimTypes.MobilePhone, session.PhoneNumber),
            new(
                MobileAuthenticationDefaults.SessionIdClaim,
                session.Id.ToString("N"))
        };
        if (session.BuyerId.HasValue)
        {
            claims.Add(new Claim(
                MobileAuthenticationDefaults.BuyerIdClaim,
                session.BuyerId.Value.ToString("N")));
            claims.Add(new Claim(ClaimTypes.Role, "Buyer"));
        }
        if (session.SellerId.HasValue)
        {
            claims.Add(new Claim(
                MobileAuthenticationDefaults.SellerIdClaim,
                session.SellerId.Value.ToString("N")));
            claims.Add(new Claim(ClaimTypes.Role, "Seller"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            MobileAuthenticationDefaults.Scheme));
    }

    private IssuedMobileSession Issue(
        MobileSession session,
        string refreshToken,
        DateTimeOffset now)
    {
        var expiresAt = now.Add(AccessLifetime);
        var ticket = new MobileAccessTicket(
            session.Id,
            session.BuyerId,
            session.SellerId,
            expiresAt);
        var json = JsonSerializer.SerializeToUtf8Bytes(ticket, JsonOptions);
        var accessToken = WebEncoders.Base64UrlEncode(
            protector.Protect(json));
        return new IssuedMobileSession(
            accessToken,
            refreshToken,
            expiresAt,
            session.DisplayName,
            session.PhoneNumber,
            session.BuyerId.HasValue,
            session.SellerId.HasValue);
    }

    private static string NewRefreshToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record MobileAccessTicket(
        Guid SessionId,
        Guid? BuyerId,
        Guid? SellerId,
        DateTimeOffset ExpiresAt);
}
