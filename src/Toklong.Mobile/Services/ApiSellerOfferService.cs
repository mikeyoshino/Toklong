using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiSellerOfferService(
    MobileApiClient api,
    IMobileSessionStore sessionStore) : ISellerOfferService
{
    public async Task<IReadOnlyList<MobilePayoutAccount>>
        GetPayoutAccountsAsync(
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/seller/payout-accounts"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<
                       IReadOnlyList<MobilePayoutAccount>>(
                       cancellationToken: cancellationToken) ??
               [];
    }

    public async Task<SellerOfferInvitation> GetAsync(
        string publicToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/mobile/seller-offers/{Uri.EscapeDataString(publicToken)}"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<SellerOfferInvitation>(
            cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException(
            "ไม่พบข้อมูลข้อเสนอ");
    }

    public async Task<IReadOnlyList<MobileShippingQuote>>
        GetShippingQuotesAsync(
            string publicToken,
            SellerShippingQuoteRequest request,
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                $"api/mobile/seller-offers/{Uri.EscapeDataString(publicToken)}/shipping-quotes")
            {
                Content = JsonContent.Create(request)
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<
                       IReadOnlyList<MobileShippingQuote>>(
                       cancellationToken: cancellationToken) ??
               [];
    }

    public async Task<IReadOnlyList<MobilePayoutAccount>> SavePayoutAccountAsync(
        Guid? accountId,
        string bankCode,
        string accountName,
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                "api/mobile/seller/payout-account")
            {
                Content = JsonContent.Create(new
                {
                    AccountId = accountId,
                    BankCode = bankCode,
                    AccountName = accountName,
                    AccountNumber = accountNumber
                })
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content
            .ReadFromJsonAsync<SellerProfileUpdateResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "บันทึกบัญชีรับเงินไม่สำเร็จ");
        await SaveSessionAsync(result.Session);
        return result.PayoutAccounts;
    }

    public async Task<AppTransaction> AcceptAsync(
        string publicToken,
        Guid payoutAccountId,
        bool transferRightsAttested,
        bool sellerAcceptedTerms,
        long disclosedBuyerProtectionFeeSatang,
        long disclosedPlatformFeeSatang,
        long disclosedSellerExpectedNetSatang,
        string disclosedFeePolicyVersion,
        SellerShippingSelection? shipping,
        CancellationToken cancellationToken = default)
    {
        var result = await PostActionAsync(
            $"api/mobile/seller-offers/{Uri.EscapeDataString(publicToken)}/accept",
            new
            {
                PayoutAccountId = payoutAccountId,
                TransferRightsAttested = transferRightsAttested,
                SellerAcceptedTerms = sellerAcceptedTerms,
                DisclosedBuyerProtectionFeeSatang =
                    disclosedBuyerProtectionFeeSatang,
                DisclosedPlatformFeeSatang = disclosedPlatformFeeSatang,
                DisclosedSellerExpectedNetSatang =
                    disclosedSellerExpectedNetSatang,
                DisclosedFeePolicyVersion = disclosedFeePolicyVersion,
                Shipping = shipping
            },
            cancellationToken);
        return result.Transaction;
    }

    public async Task<AppTransaction> DeclineAsync(
        string publicToken,
        CancellationToken cancellationToken = default)
    {
        var result = await PostActionAsync(
            $"api/mobile/seller-offers/{Uri.EscapeDataString(publicToken)}/decline",
            new { },
            cancellationToken);
        return result.Transaction;
    }

    private async Task<SellerOfferActionResponse> PostActionAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body)
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content
            .ReadFromJsonAsync<SellerOfferActionResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "ตอบข้อเสนอไม่สำเร็จ");
        await SaveSessionAsync(result.Session);
        return result;
    }

    private Task SaveSessionAsync(SessionResponse session) =>
        sessionStore.SaveAsync(new StoredMobileSession(
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt));

    private sealed record SessionResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private sealed record SellerProfileUpdateResponse(
        SessionResponse Session,
        IReadOnlyList<MobilePayoutAccount> PayoutAccounts);

    private sealed record SellerOfferActionResponse(
        AppTransaction Transaction,
        SessionResponse Session);
}
