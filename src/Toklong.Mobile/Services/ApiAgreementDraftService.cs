using System.Net.Http.Headers;
using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiAgreementDraftService(MobileApiClient api)
    : IAgreementDraftService
{
    public async Task<AgreementDraft> ExtractAsync(
        string chatText,
        IReadOnlyList<string> localImagePaths,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => CreateRequest(chatText, localImagePaths),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content.ReadFromJsonAsync<AgreementDraft>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "AI ไม่ได้ส่งข้อมูลร่างกลับมา");
    }

    private static HttpRequestMessage CreateRequest(
        string chatText,
        IReadOnlyList<string> localImagePaths)
    {
        var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(chatText?.Trim() ?? ""),
            "chatText");
        foreach (var path in localImagePaths)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    "ไม่พบรูปที่เลือก กรุณาเลือกรูปใหม่");
            var file = new StreamContent(File.OpenRead(path));
            file.Headers.ContentType = new MediaTypeHeaderValue(
                ContentType(path));
            content.Add(file, "images", Path.GetFileName(path));
        }

        return new HttpRequestMessage(
            HttpMethod.Post,
            "api/mobile/offers/extract-draft")
        {
            Content = content
        };
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
}
