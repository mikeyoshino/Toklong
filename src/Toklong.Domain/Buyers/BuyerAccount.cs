using System.Net.Mail;
using Toklong.Domain.Accounts;
using Toklong.Domain.Common;

namespace Toklong.Domain.Buyers;

public sealed class BuyerAccount
{
    private BuyerAccount() { }

    public Guid Id { get; private set; }
    public string PhoneNumber { get; private set; } = "";
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public DateTimeOffset? NameChangedAt { get; private set; }
    public string? Email { get; private set; }
    public DateTimeOffset PhoneVerifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? SavedAddressLine { get; private set; }
    public int? SavedProvinceId { get; private set; }
    public string? SavedProvinceName { get; private set; }
    public int? SavedDistrictId { get; private set; }
    public string? SavedDistrictName { get; private set; }
    public int? SavedSubdistrictId { get; private set; }
    public string? SavedSubdistrictName { get; private set; }
    public string? SavedPostalCode { get; private set; }
    public DateTimeOffset? SavedAddressUpdatedAt { get; private set; }

    public static BuyerAccount Create(
        string phoneNumber,
        AccountName name,
        string email,
        DateTimeOffset verifiedAt)
    {
        var account = new BuyerAccount
        {
            Id = Guid.NewGuid(),
            CreatedAt = verifiedAt
        };
        account.UpdateVerifiedProfile(phoneNumber, name, email, verifiedAt);
        return account;
    }

    public static BuyerAccount Create(
        string phoneNumber,
        string fullName,
        string email,
        DateTimeOffset verifiedAt) =>
        Create(
            phoneNumber,
            AccountName.CreateFromDisplayName(fullName),
            email,
            verifiedAt);

    public void UpdateVerifiedProfile(
        string phoneNumber,
        AccountName name,
        string email,
        DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        UpdatePhoneVerification(phoneNumber, verifiedAt);
        SetAccountName(name);
        Email = NormalizeEmail(email);
    }

    public void ApplyAccountName(AccountName name, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        SetAccountName(name);
        NameChangedAt = changedAt;
    }

    public void ActivateVerifiedEmail(string email)
    {
        var normalized = NormalizeEmail(email);
        if (string.Equals(Email, normalized, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว");
        Email = normalized;
    }

    public void UpdatePhoneVerification(
        string phoneNumber,
        DateTimeOffset verifiedAt)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new DomainException("หมายเลขโทรศัพท์ไม่ถูกต้อง");

        PhoneNumber = phoneNumber.Trim();
        PhoneVerifiedAt = verifiedAt;
    }

    public void UpdateSavedDeliveryAddress(
        BuyerDeliveryAddress address,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(address);
        SavedAddressLine = address.AddressLine;
        SavedProvinceId = address.ProvinceId;
        SavedProvinceName = address.ProvinceName;
        SavedDistrictId = address.DistrictId;
        SavedDistrictName = address.DistrictName;
        SavedSubdistrictId = address.SubdistrictId;
        SavedSubdistrictName = address.SubdistrictName;
        SavedPostalCode = address.PostalCode;
        SavedAddressUpdatedAt = updatedAt;
    }

    public BuyerDeliveryAddress? GetSavedDeliveryAddress() =>
        SavedProvinceId.HasValue &&
        SavedDistrictId.HasValue &&
        SavedSubdistrictId.HasValue
            ? new BuyerDeliveryAddress(
                SavedAddressLine ?? "",
                SavedProvinceId.Value,
                SavedProvinceName ?? "",
                SavedDistrictId.Value,
                SavedDistrictName ?? "",
                SavedSubdistrictId.Value,
                SavedSubdistrictName ?? "",
                SavedPostalCode ?? "")
            : null;

    private void SetAccountName(AccountName name)
    {
        FirstName = name.FirstName;
        LastName = name.LastName;
        FullName = name.DisplayName;
    }

    public static string NormalizeEmail(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length > 254 ||
            !MailAddress.TryCreate(clean, out var parsed) ||
            !string.Equals(
                parsed.Address,
                clean,
                StringComparison.OrdinalIgnoreCase))
            throw new DomainException("กรุณากรอกอีเมลให้ถูกต้อง");
        return clean;
    }
}

public sealed record BuyerDeliveryAddress
{
    public BuyerDeliveryAddress(
        string addressLine,
        int provinceId,
        string provinceName,
        int districtId,
        string districtName,
        int subdistrictId,
        string subdistrictName,
        string postalCode)
    {
        AddressLine = Required(addressLine, "บ้านเลขที่และรายละเอียดที่อยู่", 500);
        ProvinceId = Positive(provinceId, "จังหวัด");
        ProvinceName = Required(provinceName, "จังหวัด", 100);
        DistrictId = Positive(districtId, "อำเภอหรือเขต");
        DistrictName = Required(districtName, "อำเภอหรือเขต", 100);
        SubdistrictId = Positive(subdistrictId, "ตำบลหรือแขวง");
        SubdistrictName = Required(subdistrictName, "ตำบลหรือแขวง", 100);
        PostalCode = Required(postalCode, "รหัสไปรษณีย์", 5);
        if (PostalCode.Length != 5 || PostalCode.Any(character => !char.IsDigit(character)))
            throw new DomainException("รหัสไปรษณีย์ไม่ถูกต้อง");
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

    private static string Required(string value, string label, int maximumLength)
    {
        var clean = value.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            throw new DomainException($"กรุณาระบุ{label}");
        if (clean.Length > maximumLength)
            throw new DomainException($"{label}ยาวเกิน {maximumLength} ตัวอักษร");
        return clean;
    }

    private static int Positive(int value, string label)
    {
        if (value <= 0)
            throw new DomainException($"กรุณาเลือก{label}");
        return value;
    }
}
