using System.Text.RegularExpressions;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class MobileIdempotencyKeyTests
{
    private static readonly Regex BackendContract =
        new(
            "^[A-Za-z0-9:_-]{16,80}$",
            RegexOptions.CultureInvariant);

    [Theory]
    [InlineData(
        MobileIdempotencyOperation
            .ParcelProtectionPreparation)]
    [InlineData(
        MobileIdempotencyOperation
            .ParcelProtectionElection)]
    [InlineData(
        MobileIdempotencyOperation.Checkout)]
    public void Generated_key_matches_the_backend_contract(
        MobileIdempotencyOperation operation)
    {
        var value = MobileIdempotencyKey.Create(
            Guid.NewGuid(),
            operation);

        Assert.InRange(
            value.Length,
            16,
            MobileIdempotencyKey.MaximumLength);
        Assert.Matches(BackendContract, value);
    }

    [Fact]
    public void Each_generated_key_is_unique()
    {
        var transactionId = Guid.NewGuid();

        var first = MobileIdempotencyKey.Create(
            transactionId,
            MobileIdempotencyOperation.Checkout);
        var second = MobileIdempotencyKey.Create(
            transactionId,
            MobileIdempotencyOperation.Checkout);

        Assert.NotEqual(first, second);
    }
}
