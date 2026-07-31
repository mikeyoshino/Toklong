namespace Toklong.Mobile.Core;

public enum MobileIdempotencyOperation
{
    ParcelProtectionPreparation,
    ParcelProtectionElection,
    Checkout
}

public static class MobileIdempotencyKey
{
    public const int MaximumLength = 80;

    public static string Create(
        Guid transactionId,
        MobileIdempotencyOperation operation)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException(
                "ต้องมีรหัสรายการก่อนสร้าง idempotency key",
                nameof(transactionId));

        var operationCode = operation switch
        {
            MobileIdempotencyOperation
                .ParcelProtectionPreparation => "pp",
            MobileIdempotencyOperation
                .ParcelProtectionElection => "pe",
            MobileIdempotencyOperation.Checkout => "co",
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation))
        };
        var value =
            $"mobile:{transactionId:N}:{operationCode}:{Guid.NewGuid():N}";

        if (value.Length > MaximumLength)
            throw new InvalidOperationException(
                "idempotency key ยาวเกินข้อกำหนด");

        return value;
    }
}
