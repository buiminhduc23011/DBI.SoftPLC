using System.Globalization;
using System.Windows.Data;

namespace DBI.Controller.Studio.Converters;

/// <summary>Đảo giá trị bool để khoá nút khi một tác vụ đang chạy.</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Chỉ dùng một chiều.");
}
