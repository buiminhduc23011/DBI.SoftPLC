using System.Text.RegularExpressions;

namespace DBI.Controller.Tests;

/// <summary>
/// Cổng chống màu cứng: mọi màu phải đi qua token trong Themes/Light.xaml + Dark.xaml.
/// Mã hex/Brushes.* quay lại là test đỏ — kể cả một chỗ (plan phase-2 AC-2).
/// </summary>
public class HardCodedColorGateTests
{
    private static string RepoRoot => FindRepoRoot(AppContext.BaseDirectory);

    /// <summary>XAML ngoài Themes/ — quét hex, Color.FromRgb, Brushes.*, và brush-literal tên.</summary>
    private static readonly string[] ScannedXamlPaths =
    {
        "src/DBI.Controller.Studio/MainWindow.xaml"
    };

    /// <summary>Code-behind/behavior/converter — nơi từng chứa DodgerBlue/#005A9E/Firebrick/Gray.</summary>
    private static readonly string[] ScannedCsPaths =
    {
        "src/DBI.Controller.Studio/Behaviors/RoslynCodeEditorBehaviors.cs",
        "src/DBI.Controller.Studio/Behaviors/TagDragDropBehaviors.cs",
        "src/DBI.Controller.Studio/Services/UserPrompt.cs",
        "src/DBI.Controller.Studio/Converters/MappedToBrushConverter.cs"
    };

    // Whitelist tối thiểu theo plan: chữ trắng trên nền accent là chủ ý thiết kế;
    // Transparent là placeholder drop-border; Brushes.Transparent fallback an toàn design-time.
    private static readonly string[] AllowedXamlLiterals = { "Transparent" };
    private static readonly string[] AllowedWhiteOnAccent =
    {
        "AccentButton", "Deploy", "ForceBanner"
    };

    [Fact]
    public void Xaml_NgoaiThemes_KhongConMauCung()
    {
        var violations = new List<string>();

        foreach (string relative in ScannedXamlPaths)
        {
            string content = File.ReadAllText(Path.Combine(RepoRoot, relative));
            string[] lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("<!--")) continue; // comment được phép

                if (Regex.IsMatch(line, "#[0-9A-Fa-f]{6}"))
                    violations.Add($"{relative}:{i + 1} hex: {line.Trim()}");
                if (line.Contains("Color.FromRgb") || line.Contains("Colors."))
                    violations.Add($"{relative}:{i + 1} Color literal: {line.Trim()}");
                if (Regex.IsMatch(line, @"Brushes\."))
                    violations.Add($"{relative}:{i + 1} Brushes.: {line.Trim()}");

                foreach (Match m in Regex.Matches(line, "(?:Foreground|Background|Fill|BorderBrush)=\"([A-Za-z]+)\""))
                {
                    string literal = m.Groups[1].Value;
                    if (AllowedXamlLiterals.Contains(literal)) continue;
                    if (literal == "White" && AllowedWhiteOnAccent.Any(line.Contains)) continue;
                    violations.Add($"{relative}:{i + 1} brush literal '{literal}': {line.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Màu cứng quay lại trong XAML:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void CodeBehind_KhongConMauCung()
    {
        var violations = new List<string>();

        foreach (string relative in ScannedCsPaths)
        {
            string content = File.ReadAllText(Path.Combine(RepoRoot, relative));
            string[] lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                if (line.Contains("//") && line.IndexOf("#", StringComparison.Ordinal) > line.IndexOf("//", StringComparison.Ordinal)) continue;

                if (Regex.IsMatch(line, "#[0-9A-Fa-f]{6}") || line.Contains("Color.FromRgb"))
                    violations.Add($"{relative}:{i + 1}: {line.Trim()}");
                if (Regex.IsMatch(line, @"Brushes\.(\w+)") &&
                    !line.Contains("Brushes.Transparent")) // fallback an toàn design-time duy nhất
                    violations.Add($"{relative}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Màu cứng quay lại trong code-behind:\n" + string.Join("\n", violations));
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
