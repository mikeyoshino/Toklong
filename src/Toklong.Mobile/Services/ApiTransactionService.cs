using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiTransactionService(MobileApiClient api)
    : ITransactionService
{
    public async Task<BuyerCostPreview> GetBuyerCostPreviewAsync(
        long itemPriceSatang,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/pricing/buyer-protection" +
                $"?itemPriceSatang={itemPriceSatang.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)}"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<BuyerCostPreview>(
                       cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "คำนวณค่าคุ้มครองผู้ซื้อไม่สำเร็จ");
    }

    public async Task<IReadOnlyList<CarrierOption>>
        GetSupportedCarriersAsync(
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/shipping/carriers"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<IReadOnlyList<CarrierOption>>(
                       cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<AppTransaction>> GetTransactionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/transactions"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<IReadOnlyList<AppTransaction>>(
                       cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<AppTransaction?> GetTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/mobile/transactions/{transactionId}"),
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AppTransaction>(
            cancellationToken: cancellationToken);
    }

    public async Task<AgreementEvidenceFile>
        DownloadAgreementEvidenceAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/mobile/transactions/{transactionId}/agreement-evidence"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition
            ?.FileNameStar?.Trim('"') ??
            response.Content.Headers.ContentDisposition
                ?.FileName?.Trim('"') ??
            $"TOKLONG-agreement-{transactionId:N}.json";
        return new AgreementEvidenceFile(
            fileName,
            await response.Content.ReadAsByteArrayAsync(
                cancellationToken));
    }

    public async Task<ShippingLabelFile>
        DownloadShippingLabelAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/mobile/transactions/{transactionId}/shipping-label"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition
            ?.FileNameStar?.Trim('"') ??
            response.Content.Headers.ContentDisposition
                ?.FileName?.Trim('"') ??
            $"TOKLONG-label-{transactionId:N}.html";
        return new ShippingLabelFile(
            fileName,
            await response.Content.ReadAsByteArrayAsync(
                cancellationToken));
    }

    public async Task<ShippingLabelFile>
        DownloadReturnShippingLabelAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/mobile/transactions/{transactionId}/return-shipping-label"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition
            ?.FileNameStar?.Trim('"') ??
            response.Content.Headers.ContentDisposition
                ?.FileName?.Trim('"') ??
            $"TOKLONG-return-label-{transactionId:N}.html";
        return new ShippingLabelFile(
            fileName,
            await response.Content.ReadAsByteArrayAsync(
                cancellationToken));
    }

    public async Task<AppTransaction> CreateBuyerOfferAsync(
        CreateBuyerOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => CreateOfferRequest(request),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AppTransaction>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "สร้างข้อเสนอไม่สำเร็จ");
    }

    public Task<AppTransaction> SubmitTrackingAsync(
        Guid transactionId,
        string carrierCode,
        string trackingNumber,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            $"api/mobile/transactions/{transactionId}/tracking",
            new { CarrierCode = carrierCode, TrackingNumber = trackingNumber },
            cancellationToken);

    public Task<AppTransaction> SubmitDigitalHandoffAsync(
        Guid transactionId,
        string statement,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            $"api/mobile/transactions/{transactionId}/digital-handoff",
            new { Statement = statement },
            cancellationToken);

    public Task<AppTransaction> ConfirmReceiptAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            $"api/mobile/transactions/{transactionId}/confirm-receipt",
            new { },
            cancellationToken);

    public Task<AppTransaction> OpenDisputeAsync(
        Guid transactionId,
        AppDisputeReason reason,
        string statement,
        CancellationToken cancellationToken = default) =>
        PostAsync(
            $"api/mobile/transactions/{transactionId}/disputes",
            new { Reason = reason.ToString(), Statement = statement },
            cancellationToken);

    public async Task<DisputeEvidenceSummary>
        SubmitDisputeEvidenceAsync(
            Guid transactionId,
            AppDisputeEvidenceParty party,
            AppDisputeEvidenceType evidenceType,
            string description,
            DisputeEvidenceUpload file,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => CreateEvidenceRequest(
                transactionId,
                party,
                evidenceType,
                description,
                file,
                idempotencyKey),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<DisputeEvidenceSummary>(
                       cancellationToken:
                           cancellationToken)
               ?? throw new InvalidOperationException(
                   "บันทึกหลักฐานไม่สำเร็จ");
    }

    public async Task<IReadOnlyList<DisputeEvidenceSummary>>
        GetOwnDisputeEvidenceAsync(
            Guid transactionId,
            AppDisputeEvidenceParty party,
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"api/mobile/transactions/{transactionId}/dispute-evidence?party={party}"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content.ReadFromJsonAsync<
                   IReadOnlyList<DisputeEvidenceSummary>>(
                   cancellationToken: cancellationToken)
               ?? [];
    }

    private async Task<AppTransaction> PostAsync(
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
        return await response.Content.ReadFromJsonAsync<AppTransaction>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "อัปเดตรายการไม่สำเร็จ");
    }

    private static HttpRequestMessage CreateOfferRequest(
        CreateBuyerOfferRequest request)
    {
        if (!string.IsNullOrWhiteSpace(
                request.LocalPhotoPath) &&
            !File.Exists(request.LocalPhotoPath))
            throw new InvalidOperationException(
                "ไม่พบรูปที่เลือก กรุณาเลือกรูปใหม่");

        var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(request.SellerPhoneNumber),
            "sellerPhoneNumber");
        content.Add(
            new StringContent(
                request.FulfillmentType == AppFulfillmentType.Physical
                    ? "PhysicalShipment"
                    : "DigitalHandoff"),
            "fulfillmentType");
        content.Add(
            new StringContent(request.Condition.ToString()),
            "condition");
        content.Add(
            new StringContent(request.ProductName),
            "productName");
        content.Add(
            new StringContent(request.AgreementDetails),
            "agreementDetails");
        content.Add(
            new StringContent(request.KnownDefects),
            "knownDefects");
        content.Add(
            new StringContent(
                request.AmountSatang.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            "amountSatang");
        content.Add(
            new StringContent(
                request.UseSavedAddress.ToString()),
            "useSavedAddress");
        content.Add(
            new StringContent(
                request.RememberAddress.ToString()),
            "rememberAddress");
        if (request.FulfillmentType ==
                AppFulfillmentType.Physical &&
            !request.UseSavedAddress)
        {
            if (string.IsNullOrWhiteSpace(
                    request.AddressLine))
                throw new InvalidOperationException(
                    "กรุณากรอกบ้านเลขที่และรายละเอียดที่อยู่");
            content.Add(
                new StringContent(
                    request.AddressLine.Trim()),
                "addressLine");
            AddAddressPart(
                content,
                "provinceId",
                request.ProvinceId);
            AddAddressPart(
                content,
                "districtId",
                request.DistrictId);
            AddAddressPart(
                content,
                "subdistrictId",
                request.SubdistrictId);
        }
        if (!string.IsNullOrWhiteSpace(
                request.LocalPhotoPath))
        {
            var file = new StreamContent(
                File.OpenRead(request.LocalPhotoPath));
            file.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(
                    ContentType(request.LocalPhotoPath));
            content.Add(
                file,
                "photo",
                Path.GetFileName(request.LocalPhotoPath));
        }
        return new HttpRequestMessage(
            HttpMethod.Post,
            "api/mobile/offers")
        {
            Content = content
        };
    }

    private static HttpRequestMessage CreateEvidenceRequest(
        Guid transactionId,
        AppDisputeEvidenceParty party,
        AppDisputeEvidenceType evidenceType,
        string description,
        DisputeEvidenceUpload file,
        string idempotencyKey)
    {
        var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(party.ToString()),
            "party");
        content.Add(
            new StringContent(evidenceType.ToString()),
            "evidenceType");
        content.Add(
            new StringContent(description),
            "description");
        var image = new ByteArrayContent(file.Content);
        image.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                file.ContentType);
        content.Add(
            image,
            "file",
            Path.GetFileName(file.FileName));
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/mobile/transactions/{transactionId}/dispute-evidence")
        {
            Content = content
        };
        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey);
        return request;
    }

    private static void AddAddressPart(
        MultipartFormDataContent content,
        string name,
        int? value)
    {
        if (!value.HasValue || value <= 0)
            throw new InvalidOperationException(
                "กรุณาเลือกพื้นที่จัดส่งให้ครบ");
        content.Add(
            new StringContent(
                value.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            name);
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
}
