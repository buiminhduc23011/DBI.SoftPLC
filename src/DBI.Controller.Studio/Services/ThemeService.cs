using System.Collections.ObjectModel;
using System.Windows;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Studio.Services;

/// <summary>
/// Đổi theme bằng cách swap <c>MergedDictionaries</c>.
/// </summary>
/// <remarks>
/// ⚠️ Cách cũ — theme token dạng chuỗi trên ViewModel rồi <c>OnPropertyChanged</c> bảy property mỗi
/// lần đổi — <b>không theme được AvalonDock</b>. Spike 2026-07-27 xác nhận đây là điều kiện cần,
/// không phải tuỳ chọn: panel của AvalonDock chỉ nghe <c>DynamicResource</c>.
/// </remarks>
public class ThemeService : IThemeSwitcher
{
    private const string LightUri = "Themes/Light.xaml";
    private const string DarkUri = "Themes/Dark.xaml";

    private readonly Collection<ResourceDictionary> _target;
    private ResourceDictionary? _current;

    /// <param name="target">
    /// Nơi cắm bảng màu. Mặc định là <c>Application.Current.Resources.MergedDictionaries</c>.
    /// </param>
    public ThemeService(Collection<ResourceDictionary>? target = null)
    {
        _target = target
            ?? Application.Current?.Resources.MergedDictionaries
            ?? throw new InvalidOperationException("Chưa có Application để gắn theme.");

        // App.xaml đã nạp sẵn Light.xaml cho design-time. Nhận nó làm theme hiện tại thay vì cắm
        // thêm cái thứ hai — hai bảng màu cùng lúc thì cái nào thắng phụ thuộc thứ tự, rất khó lần.
        _current = FindTheme();
        IsDark = _current is not null && IsDarkDictionary(_current);
    }

    public bool IsDark { get; private set; }

    public void Apply(bool dark)
    {
        var next = new ResourceDictionary
        {
            Source = new Uri(dark ? DarkUri : LightUri, UriKind.Relative)
        };

        // Cắm cái mới TRƯỚC rồi mới gỡ cái cũ: gỡ trước sẽ có một khoảnh khắc không brush nào
        // phân giải được và WPF vẽ ra cửa sổ trắng nhấp nháy.
        _target.Insert(0, next);

        if (_current is not null) _target.Remove(_current);

        _current = next;
        IsDark = dark;

        // Báo cho mọi MultiBinding gắn ThemeVersion đánh giá lại — binding-qua-converter tra
        // brush mới từ resources (IValueConverter thường giữ instance cũ sau swap).
        ThemeVersion.Instance.RaiseAll();
    }

    private ResourceDictionary? FindTheme() =>
        _target.FirstOrDefault(d => d.Source is not null && IsThemeDictionary(d));

    private static bool IsThemeDictionary(ResourceDictionary dictionary) =>
        Contains(dictionary, LightUri) || Contains(dictionary, DarkUri);

    private static bool IsDarkDictionary(ResourceDictionary dictionary) => Contains(dictionary, DarkUri);

    private static bool Contains(ResourceDictionary dictionary, string fragment) =>
        dictionary.Source?.OriginalString.Replace('\\', '/')
            .Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
}
