using System.Globalization;
using System.Windows.Data;

namespace DBI.Controller.Studio.Converters;

/// <summary>
/// bool IsMapped → tên resource-key màu (SuccessColor/IdleColor). Dùng kèm
/// <see cref="ThemeBrushConverter"/> trong MultiBinding để glyph map đổi màu theo theme live.
/// </summary>
public class MappedToKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "SuccessColor" : "IdleColor";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Chỉ dùng một chiều.");
}
