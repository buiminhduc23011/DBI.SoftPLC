using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DBI.Controller.Studio.Converters;

/// <summary>
/// Đổi khoá brush (chuỗi) thành <see cref="Brush"/> lấy từ ResourceDictionary đang bật.
/// </summary>
/// <remarks>
/// ViewModel chỉ nói "màu này tên là <c>DangerColor</c>", không giữ mã màu — nhờ vậy đổi theme thì
/// màu tự đổi theo, và tầng lõi không phải biết gì về WPF.
/// </remarks>
public class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key)) return Brushes.Transparent;

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Chỉ dùng một chiều.");
}
