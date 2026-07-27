using System.Text.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionSerializationTests
{
    [Fact]
    public void Mobile_api_string_enums_deserialize_into_transaction()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "productName": "กล้อง",
              "amountSatang": 450000,
              "currency": "THB",
              "role": "Buyer",
              "fulfillmentType": "Physical",
              "state": "AwaitingSellerAcceptance",
              "updatedAt": "2026-07-25T04:00:00Z",
              "actionDeadline": null,
              "counterpartyName": "ผู้ขาย"
            }
            """;

        var transaction = JsonSerializer.Deserialize<AppTransaction>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(transaction);
        Assert.Equal(AppTransactionRole.Buyer, transaction.Role);
        Assert.Equal(
            AppFulfillmentType.Physical,
            transaction.FulfillmentType);
    }
}
