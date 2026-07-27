using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public static class ReusableCredentialGuard
{
    private static readonly string[] ForbiddenMarkers =
    [
        "password:",
        "password=",
        "รหัสผ่าน:",
        "รหัสผ่าน=",
        "recovery code:",
        "recovery code=",
        "รหัสกู้คืน:",
        "รหัสกู้คืน=",
        "private key:",
        "private key=",
        "seed phrase:",
        "seed phrase=",
        "mnemonic:",
        "mnemonic="
    ];

    public static string Reject(string value)
    {
        if (ForbiddenMarkers.Any(marker =>
                value.Contains(
                    marker,
                    StringComparison.OrdinalIgnoreCase)))
            throw new DomainException(
                "ห้ามส่งรหัสผ่าน รหัสกู้คืน private key seed phrase หรือข้อมูลลับที่นำกลับมาใช้ได้");
        return value;
    }
}
