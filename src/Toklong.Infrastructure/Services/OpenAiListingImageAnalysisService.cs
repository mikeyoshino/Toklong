using System.Text.Json;
using OpenAI.Chat;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Services;

public sealed record ListingAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-5.6-luna";
}

public sealed class OpenAiListingImageAnalysisService(ListingAiOptions options)
    : IListingImageAnalysisService
{
    private static readonly BinaryData OutputSchema = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "productName": { "type": "string" },
            "description": { "type": "string" },
            "knownDefects": { "type": "string" },
            "priceBaht": { "type": ["number", "null"] },
            "category": {
              "type": "string",
              "enum": [
                "กล้องและอุปกรณ์",
                "รองเท้าและเสื้อผ้า",
                "กระเป๋าและแฟชั่น",
                "ของสะสม",
                "อิเล็กทรอนิกส์",
                "งานอดิเรกและของใช้"
              ]
            },
            "condition": {
              "type": "string",
              "enum": ["New", "UsedGood", "UsedDefects"]
            },
            "confidence": {
              "type": "string",
              "enum": ["high", "medium", "low"]
            },
            "extractedFields": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": [
            "productName",
            "description",
            "knownDefects",
            "priceBaht",
            "category",
            "condition",
            "confidence",
            "extractedFields"
          ],
          "additionalProperties": false
        }
        """);

    public async Task<ListingImageAnalysis> AnalyzeAsync(
        IReadOnlyList<ListingImageInput> images,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("ยังไม่ได้ตั้งค่า OpenAI API key");

        try
        {
            var content = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart("""
                    อ่านรูปสินค้าและ screenshot ประกาศทั้งหมด แล้วสร้างข้อมูลร่างสำหรับขายสินค้าที่จับต้องได้ในประเทศไทย

                    กฎ:
                    - ใช้เฉพาะข้อความและสิ่งที่เห็นในภาพ ห้ามเดายี่ห้อ รุ่น ราคา ความแท้ หรืออุปกรณ์ที่ไม่มีหลักฐาน
                    - หากไม่เห็นราคาให้ priceBaht เป็น null
                    - knownDefects ต้องมีเฉพาะรอย ตำหนิ หรือความเสียหายที่เห็นหรือมีข้อความระบุ หากไม่พบให้เป็นสตริงว่าง
                    - description เขียนภาษาไทยแบบเป็นกลาง กระชับ และห้ามกล่าวว่า “ไม่มีตำหนิ” จากภาพเพียงอย่างเดียว
                    - condition เป็น New เฉพาะเมื่อข้อความระบุว่าใหม่, UsedDefects เมื่อมีตำหนิชัดเจน, นอกนั้น UsedGood
                    - extractedFields ใส่ชื่อ field ที่อ่านได้จริง เช่น ชื่อสินค้า ราคา รายละเอียด ตำหนิ
                    - confidence เป็น low เมื่อชื่อหรือประเภทสินค้าไม่ชัดเจน
                    """)
            };

            foreach (var image in images)
            {
                var normalized = await ProductImageProcessor.NormalizeAsync(
                    image, cancellationToken);
                content.Add(ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(normalized),
                    "image/jpeg",
                    ChatImageDetailLevel.High));
            }

            var client = new ChatClient(options.Model, options.ApiKey);
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "คุณช่วยถอดข้อมูลประกาศสินค้าจากภาพเป็นข้อมูลร่างเท่านั้น ผู้ขายต้องตรวจและยืนยันเอง"),
                new UserChatMessage(content)
            };
            var requestOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "toklong_listing_draft",
                    OutputSchema,
                    "ข้อมูลร่างสินค้าจากภาพสำหรับให้ผู้ขายตรวจสอบ",
                    jsonSchemaIsStrict: true),
                MaxOutputTokenCount = 1200
            };

            ChatCompletion completion = await client.CompleteChatAsync(
                messages, requestOptions, cancellationToken);
            var json = completion.Content.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("AI ไม่ได้ส่งข้อมูลสินค้ากลับมา");

            return ParseResponse(json);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "AI ยังวิเคราะห์รูปไม่ได้ กรุณาลองใหม่หรือลดจำนวนรูป", exception);
        }
    }

    public static ListingImageAnalysis ParseResponse(string json)
    {
        var value = JsonSerializer.Deserialize<AiListingResponse>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("รูปแบบข้อมูลจาก AI ไม่ถูกต้อง");
        var condition = Enum.TryParse<ConditionCode>(value.Condition, out var parsed)
            ? parsed
            : ConditionCode.UsedGood;

        return new ListingImageAnalysis(
            Limit(value.ProductName, 180),
            Limit(value.Description, 2000),
            Limit(value.KnownDefects, 1000),
            value.PriceBaht is > 0 ? decimal.Round(value.PriceBaht.Value, 2) : null,
            AllowedCategory(value.Category),
            condition,
            value.Confidence is "high" or "medium" ? value.Confidence : "low",
            value.ExtractedFields
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray());
    }

    private static string AllowedCategory(string value) => value switch
    {
        "กล้องและอุปกรณ์" or "รองเท้าและเสื้อผ้า" or "กระเป๋าและแฟชั่น"
            or "ของสะสม" or "อิเล็กทรอนิกส์" => value,
        _ => "งานอดิเรกและของใช้"
    };
    private static string Limit(string? value, int length)
    {
        var clean = value?.Trim() ?? "";
        return clean.Length <= length ? clean : clean[..length].TrimEnd();
    }

    private sealed record AiListingResponse(
        string ProductName,
        string Description,
        string KnownDefects,
        decimal? PriceBaht,
        string Category,
        string Condition,
        string Confidence,
        IReadOnlyList<string> ExtractedFields);
}
