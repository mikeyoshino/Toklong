using Toklong.Domain.Common;

namespace Toklong.Domain.Sellers;

public sealed class SellerAccount
{
    private readonly List<SellerPayoutAccount> _payoutAccounts = [];

    private SellerAccount() { }

    public Guid Id { get; private set; }
    public string PhoneNumber { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset PhoneVerifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<SellerPayoutAccount> PayoutAccounts => _payoutAccounts;

    public static SellerAccount Create(
        string phoneNumber,
        DateTimeOffset verifiedAt)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new DomainException("หมายเลขโทรศัพท์ไม่ถูกต้อง");

        return new SellerAccount
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber.Trim(),
            DisplayName = $"ผู้ขาย {phoneNumber[^4..]}",
            PhoneVerifiedAt = verifiedAt,
            CreatedAt = verifiedAt
        };
    }

    public void MarkPhoneVerified(DateTimeOffset verifiedAt) =>
        PhoneVerifiedAt = verifiedAt;

    public SellerPayoutAccount SavePayoutAccount(
        Guid? accountId,
        string bankCode,
        string accountName,
        string accountNumber,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("กรุณากรอกข้อมูลบัญชีรับเงินให้ครบ");

        var existing = accountId.HasValue
            ? _payoutAccounts.SingleOrDefault(x => x.Id == accountId.Value)
            : null;
        if (accountId.HasValue && existing is null)
            throw new DomainException("ไม่พบบัญชีรับเงินที่ต้องการแก้ไข");
        var normalized = string.IsNullOrWhiteSpace(accountNumber) && existing is not null
            ? existing.AccountNumber
            : new string(accountNumber.Where(char.IsDigit).ToArray());
        if (normalized.Length is < 10 or > 15)
            throw new DomainException("เลขบัญชีต้องเป็นตัวเลข 10–15 หลัก");

        if (existing is not null)
        {
            existing.Update(bankCode, accountName, normalized, now);
            return existing;
        }

        var account = SellerPayoutAccount.Create(
            Id, bankCode, accountName, normalized, _payoutAccounts.Count == 0, now);
        _payoutAccounts.Add(account);
        return account;
    }
}

public sealed class SellerPayoutAccount
{
    private SellerPayoutAccount() { }

    public Guid Id { get; private set; }
    public Guid SellerId { get; private set; }
    public string BankCode { get; private set; } = "";
    public string AccountName { get; private set; } = "";
    public string AccountNumber { get; private set; } = "";
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static SellerPayoutAccount Create(
        Guid sellerId,
        string bankCode,
        string accountName,
        string accountNumber,
        bool isDefault,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            BankCode = bankCode.Trim().ToUpperInvariant(),
            AccountName = accountName.Trim(),
            AccountNumber = accountNumber,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now
        };

    internal void Update(
        string bankCode,
        string accountName,
        string accountNumber,
        DateTimeOffset now)
    {
        BankCode = bankCode.Trim().ToUpperInvariant();
        AccountName = accountName.Trim();
        AccountNumber = accountNumber;
        UpdatedAt = now;
    }

    public string MaskedNumber =>
        AccountNumber.Length <= 4
            ? AccountNumber
            : $"•••• ••{AccountNumber[^4..]}";
}
