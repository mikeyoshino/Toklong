using System.Text;
using System.Text.Json;

namespace Toklong.Shippop.Certification;

public sealed class CounterQrResponseShapeTests
{
    [Fact]
    public void Parser_records_paths_without_provider_values()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {"purchase_id":"452002","result":{"0":{
              "counter_qr":"SECRET-COUNTER-VALUE",
              "receiver_address":"99 Customer Road",
              "courier_tracking_code":"EF123456789TH"}}}
            """);

        var shape = CounterQrResponseShapeParser.Parse("confirm/", json);
        var serialized = JsonSerializer.Serialize(shape);

        Assert.Contains("$.result[].counter_qr", shape.CandidatePaths);
        Assert.Contains(shape.Fields, field =>
            field.Path == "$.result[].receiver_address" &&
            field.Kind == JsonValueKind.String);
        Assert.DoesNotContain("SECRET-COUNTER-VALUE", serialized);
        Assert.DoesNotContain("99 Customer Road", serialized);
        Assert.DoesNotContain("EF123456789TH", serialized);
        Assert.DoesNotContain("452002", serialized);
    }

    [Fact]
    public void Parser_masks_dynamic_provider_keys()
    {
        var json = Encoding.UTF8.GetBytes(
            """{"result":{"SP-PRIVATE-123":{"value":"secret"}}}""");

        var serialized = JsonSerializer.Serialize(
            CounterQrResponseShapeParser.Parse("booking/", json));

        Assert.Contains("$.result.*.value", serialized);
        Assert.DoesNotContain("SP-PRIVATE-123", serialized);
        Assert.DoesNotContain("secret", serialized);
    }

    [Fact]
    public void Parser_rejects_more_than_five_megabytes()
    {
        var bytes = new byte[(5 * 1024 * 1024) + 1];

        Assert.Throws<InvalidOperationException>(() =>
            CounterQrResponseShapeParser.Parse("confirm/", bytes));
    }

    [Theory]
    [InlineData("tracking/")]
    [InlineData("label/")]
    [InlineData("https://example.invalid/")]
    public void Parser_rejects_non_observation_endpoints(string endpoint)
    {
        var json = Encoding.UTF8.GetBytes("{}");

        Assert.Throws<InvalidOperationException>(() =>
            CounterQrResponseShapeParser.Parse(endpoint, json));
    }
}
