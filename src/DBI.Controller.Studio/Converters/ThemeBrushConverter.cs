using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DBI.Controller.Studio.Converters;

/// <summary>
/// Converter hai-nguồn cho binding màu theo theme: values = [resource-key chuỗi, theme-version int].
/// Tra brush từ Application resources tại thời điểm Convert — khi <see cref="Services.ThemeVersion"/>
/// tăng, WPF đánh giá lại MultiBinding và hàm này chạy lại với brush mới (fix: IValueConverter
/// thường trả instance brush cụ thể nên giữ màu cũ sau swap theme).
/// </summary>
public class ThemeBrushConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 1 || values[0] is not string key || string.IsNullOrEmpty(key))
            return Brushes.Transparent;

        return System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Chỉ dùng một chiều.");
}
