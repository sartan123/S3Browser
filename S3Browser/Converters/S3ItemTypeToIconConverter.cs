using System.Globalization;
using System.Windows.Data;
using S3Browser.Models;

namespace S3Browser.Converters;

public sealed class S3ItemTypeToIconConverter : IValueConverter
{
    // Segoe MDL2 Assets glyph code points
    private static readonly string Cloud = char.ConvertFromUtf32(0xE753);
    private static readonly string Folder = char.ConvertFromUtf32(0xE8B7);
    private static readonly string Up = char.ConvertFromUtf32(0xE74A);
    private static readonly string Document = char.ConvertFromUtf32(0xE8A5);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is S3ItemType type ? type switch
        {
            S3ItemType.Bucket => Cloud,
            S3ItemType.Folder => Folder,
            S3ItemType.ParentFolder => Up,
            S3ItemType.File => Document,
            _ => string.Empty
        } : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
