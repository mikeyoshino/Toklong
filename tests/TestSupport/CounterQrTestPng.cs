using QRCoder;

namespace Toklong.TestSupport;

public static class CounterQrTestPng
{
    public static byte[] Create(string value = "TOKLONG-COUNTER-QR-TEST") =>
        PngByteQRCodeHelper.GetQRCode(
            value,
            QRCodeGenerator.ECCLevel.Q,
            8);
}
