using System.Xml.Linq;

namespace DBI.Controller.Tests;

/// <summary>
/// Canh theme dictionary: thiếu một key là chỗ đó trống trơn lúc runtime (swap MergedDictionaries
/// không có fallback). Test parse XML thuần — không cần WPF.
/// </summary>
public class ThemeDictionaryParityTests
{
    private static string ThemesRoot =>
        FindRepoRoot(Path.Combine(AppContext.BaseDirectory));

    private static readonly XNamespace Xns = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument LoadTheme(string fileName) =>
        XDocument.Load(Path.Combine(ThemesRoot, "src", "DBI.Controller.Studio", "Themes", fileName));

    private static string[] ExtractKeys(XDocument doc) =>
        doc.Descendants()
            .Where(e => e.Attribute(Xns + "Key") is not null)
            .Select(e => e.Attribute(Xns + "Key")!.Value)
            .ToArray();

    /// <summary>Bộ key bắt buộc — mọi DynamicResource trong XAML và resource-key chuỗi trong VM phải nằm trong đây.</summary>
    public static readonly string[] RequiredKeys =
    {
        // 13 key nền tảng hiện có
        "WindowBg", "CardBg", "HeaderBg", "TextPrimary", "TextSecondary", "BorderColor",
        "AccentColor", "SuccessColor", "DangerColor", "WarningColor", "IdleColor",
        "SelectionBg", "HoverBg",
        // Phase 2 bổ sung theo chuẩn Visual Studio
        "DisabledText", "InputBorderFocus", "ScrollbarBg", "ScrollbarThumb", "ScrollbarThumbHover",
        "TooltipBg", "TooltipFg", "TooltipBorder", "MenuHoverBg", "SubMenuBg", "SubMenuBorder",
        "ForceRowBg", "OverlayMarkerBrush", "DropHighlightBrush",
        // Alias đã bị 3 VM tham chiếu nhưng từng không tồn tại → glyph vô hình
        "MutedColor"
    };

    [Fact]
    public void Light_va_Dark_Cung_Bo_Key()
    {
        var light = ExtractKeys(LoadTheme("Light.xaml"));
        var dark = ExtractKeys(LoadTheme("Dark.xaml"));

        Assert.Equal(light.OrderBy(k => k), dark.OrderBy(k => k));
    }

    [Fact]
    public void Ca_Hai_Dictionary_Du_Key_Bat_Buoc()
    {
        foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
        {
            var keys = ExtractKeys(LoadTheme(fileName)).ToHashSet();
            var missing = RequiredKeys.Where(k => !keys.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{fileName} thiếu keys: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void MutedColor_ResolveRaBrushThat_KhongTrongSuot()
    {
        foreach (string fileName in new[] { "Light.xaml", "Dark.xaml" })
        {
            var muted = LoadTheme(fileName).Descendants()
                .First(e => e.Attribute(Xns + "Key")?.Value == "MutedColor");

            string colorValue = muted.Attribute("Color")?.Value
                ?? throw new InvalidOperationException($"MutedColor của {fileName} không có Color.");

            // #RRGGBB (alpha mặc định FF) hoặc #AARRGGBB — cả hai đều hợp lệ, không trong suốt.
            Assert.Matches("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", colorValue);
            if (colorValue.Length == 9)
            {
                byte alpha = Convert.ToByte(colorValue.Substring(1, 2), 16);
                Assert.True(alpha > 0, $"MutedColor của {fileName} trong suốt (alpha=0) — glyph sẽ vô hình.");
            }
        }
    }

    private static string FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DBI.Controller.slnx")))
                return directory.FullName;
            directory = directory.Parent!;
        }

        throw new DirectoryNotFoundException("Không tìm thấy root repo từ " + start);
    }
}
