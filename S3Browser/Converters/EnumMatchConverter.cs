using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace S3Browser.Converters;

/// <summary>
/// enum 値を bool または Visibility に変換する。ConverterParameter に enum 名を文字列で渡す。
/// RadioButton.IsChecked と StackPanel.Visibility の双方で再利用できる。
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var match = value is not null && parameter is string s &&
                    string.Equals(value.ToString(), s, StringComparison.Ordinal);
        if (targetType == typeof(Visibility))
            return match ? Visibility.Visible : Visibility.Collapsed;
        return match;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string s && targetType.IsEnum)
            return Enum.Parse(targetType, s);
        return Binding.DoNothing;
    }
}
