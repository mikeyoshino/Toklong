using Toklong.Domain.Common;
using Toklong.Domain.Accounts;

namespace Toklong.Domain.Sellers;

public sealed class SellerAccount
{
    private readonly List<SellerPayoutAccount> _payoutAccounts = [];

    private SellerAccount() { }

    public Guid Id { get; private set; }
    public string PhoneNumber { get; private set; } = "";
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset? NameChangedAt { get; private set; }
    public DateTimeOffset PhoneVerifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? SavedShippingAddressLine { get; private set; }
    public int? SavedShippingProvinceId { get; private set; }
    public string? SavedShippingProvinceName { get; private set; }
    public int? SavedShippingDistrictId { get; private set; }
    public string? SavedShippingDistrictName { get; private set; }
    public int? SavedShippingSubdistrictId { get; private set; }
    public string? SavedShippingSubdistrictName { get; private set; }
    public string? SavedShippingPostalCode { get; private set; }
    public DateTimeOffset? SavedShippingAddressUpdatedAt { get; private set; }
    public IReadOnlyCollection<SellerPayoutAccount> PayoutAccounts => _payoutAccounts;

    public static SellerAccount Create(
        string phoneNumber,
        DateTimeOffset verifiedAt,
        AccountName? name = null)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new DomainException("หมายเลขโทรศัพท์ไม่ถูกต้อง");

        var seller = new SellerAccount
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber.Trim(),
            PhoneVerifiedAt = verifiedAt,
            CreatedAt = verifiedAt
        };
        if (name is null)
        {
            seller.SetLegacyDisplayName($"ผู้ขาย {phoneNumber[^4..]}");
        }
        else
        {
            seller.SetAccountName(name);
        }
        return seller;
    }

    public static SellerAccount Create(
        string phoneNumber,
        DateTimeOffset verifiedAt,
        string? displayName)
    {
        var seller = Create(phoneNumber, verifiedAt, (AccountName?)null);
        if (!string.IsNullOrWhiteSpace(displayName))
            seller.UpdateDisplayName(displayName);
        return seller;
    }

    public void MarkPhoneVerified(DateTimeOffset verifiedAt) =>
        PhoneVerifiedAt = verifiedAt;

    public void UpdateDisplayName(string displayName)
    {
        var normalized = AccountName.NormalizeLegacyDisplayName(displayName);
        if (!normalized.Contains(' '))
        {
            SetLegacyDisplayName(normalized);
            return;
        }

        SetAccountName(AccountName.MaterializeLegacyDisplayName(normalized));
    }

    public void ApplyAccountName(AccountName name, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        SetAccountName(name);
        NameChangedAt = changedAt;
    }

    public void UpdateSavedShippingOrigin(
        SellerShippingOriginAddress address,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(address);
        SavedShippingAddressLine = address.AddressLine;
        SavedShippingProvinceId = address.ProvinceId;
        SavedShippingProvinceName = address.ProvinceName;
        SavedShippingDistrictId = address.DistrictId;
        SavedShippingDistrictName = address.DistrictName;
        SavedShippingSubdistrictId = address.SubdistrictId;
        SavedShippingSubdistrictName = address.SubdistrictName;
        SavedShippingPostalCode = address.PostalCode;
        SavedShippingAddressUpdatedAt = updatedAt;
    }

    public SellerShippingOriginAddress? GetSavedShippingOrigin() =>
        SavedShippingProvinceId.HasValue &&
        SavedShippingDistrictId.HasValue &&
        SavedShippingSubdistrictId.HasValue
            ? new SellerShippingOriginAddress(
                SavedShippingAddressLine ?? "",
                SavedShippingProvinceId.Value,
                SavedShippingProvinceName ?? "",
                SavedShippingDistrictId.Value,
                SavedShippingDistrictName ?? "",
                SavedShippingSubdistrictId.Value,
                SavedShippingSubdistrictName ?? "",
                SavedShippingPostalCode ?? "")
            : null;

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

    private void SetAccountName(AccountName name)
    {
        FirstName = name.FirstName;
        LastName = name.LastName;
        DisplayName = name.DisplayName;
    }

    private void SetLegacyDisplayName(string displayName)
    {
        FirstName = "";
        LastName = "";
        DisplayName = AccountName.NormalizeLegacyDisplayName(displayName);
    }
}

public sealed record SellerShippingOriginAddress
{
    public SellerShippingOriginAddress(
        string addressLine,
        int provinceId,
        string provinceName,
        int districtId,
        string districtName,
        int subdistrictId,
        string subdistrictName,
        string postalCode)
    {
        AddressLine = Required(addressLine, "บ้านเลขที่และรายละเอียดต้นทาง", 500);
        ProvinceId = Positive(provinceId, "จังหวัดต้นทาง");
        ProvinceName = Required(provinceName, "จังหวัดต้นทาง", 100);
        DistrictId = Positive(districtId, "อำเภอหรือเขตต้นทาง");
        DistrictName = Required(districtName, "อำเภอหรือเขตต้นทาง", 100);
        SubdistrictId = Positive(subdistrictId, "ตำบลหรือแขวงต้นทาง");
        SubdistrictName = Required(subdistrictName, "ตำบลหรือแขวงต้นทาง", 100);
        PostalCode = Required(postalCode, "รหัสไปรษณีย์ต้นทาง", 5);
        if (PostalCode.Length != 5 ||
            PostalCode.Any(character => !char.IsDigit(character)))
            throw new DomainException("รหัสไปรษณีย์ต้นทางไม่ถูกต้อง");
    }

    public string AddressLine { get; }
    public int ProvinceId { get; }
    public string ProvinceName { get; }
    public int DistrictId { get; }
    public string DistrictName { get; }
    public int SubdistrictId { get; }
    public string SubdistrictName { get; }
    public string PostalCode { get; }

    public string ToDisplayText() =>
        $"{AddressLine} ตำบล/แขวง {SubdistrictName} อำเภอ/เขต {DistrictName} จังหวัด {ProvinceName} {PostalCode}";

    private static string Required(
        string value,
        string label,
        int maximumLength)
    {
        var clean = value.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            throw new DomainException($"กรุณาระบุ{label}");
        if (clean.Length > maximumLength)
            throw new DomainException(
                $"{label}ยาวเกิน {maximumLength} ตัวอักษร");
        return clean;
    }

    private static int Positive(int value, string label)
    {
        if (value <= 0)
            throw new DomainException($"กรุณาเลือก{label}");
        return value;
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
