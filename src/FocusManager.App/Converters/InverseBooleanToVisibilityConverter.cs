using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FocusManager.App.Converters;

/// <summary>
/// 「是」→ 隐藏，「否」→ 显示。用来做「没有项目时才显示提示语」这类效果。
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;

        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
