using System.Globalization;

namespace Toklong.Mobile.Converters;

public sealed class ByteArrayImageSourceConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
            return null;
        var copy = bytes.ToArray();
        return ImageSource.FromStream(
            () => new MemoryStream(copy, writable: false));
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
