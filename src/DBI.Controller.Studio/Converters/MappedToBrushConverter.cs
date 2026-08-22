using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DBI.Controller.Studio.Converters;

/// <summary>
/// bool IsMapped → brush của glyph ●/○: đã map thì xanh thành công, chưa map thì xám nhạt.
/// Lấy brush qua resource key nên tự đổi theo theme.
/// </summary>
public class MappedToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mapped = value is true;
        var key = mapped ? "SuccessColor" : "IdleColor";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Chỉ dùng một chiều.");
}
