using System.Globalization;
using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.Services;

public enum IssueSeverity { Error, Warning }

/// <summary>Một vấn đề tìm thấy khi kiểm tra project. Hiển thị ở Inspector tab Information (phase-04).</summary>
/// <param name="Severity">Error chặn biên dịch/deploy; Warning chỉ nhắc.</param>
/// <param name="Message">Thông báo cho kỹ sư, tiếng Việt, nói rõ phải làm gì.</param>
/// <param name="Target">Đối tượng liên quan — tên tag, tên block, tên thiết bị. Để UI nhảy tới.</param>
public record ValidationIssue(IssueSeverity Severity, string Message, string Target = "")
{
    public override string ToString() =>
        Severity == IssueSeverity.Error ? $"[Lỗi] {Message}" : $"[Cảnh báo] {Message}";
}

/// <summary>
/// Kiểm tra tính hợp lệ của <see cref="DbiProject"/> trước khi sinh code / biên dịch / deploy.
/// </summary>
public static class ProjectValidator
{
    /// <summary>
    /// Từ khoá C# <b>dành riêng</b> — không dùng làm identifier được (khác với contextual keyword
    /// như <c>value</c>, <c>var</c>, <c>async</c> vốn vẫn dùng làm tên biến bình thường).
    /// </summary>
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while"
    };

    public static IReadOnlyList<ValidationIssue> Validate(DbiProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var issues = new List<ValidationIssue>();

        ValidateTags(project, issues);
        ValidateBlocks(project, issues);

        return issues;
    }

    private static void ValidateTags(DbiProject project, List<ValidationIssue> issues)
    {
        // MemorySnapshot dùng OrdinalIgnoreCase, nên "Motor" và "motor" là CÙNG một tag lúc chạy.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new HashSet<string>(project.Devices.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var tag in project.AllTags())
        {
            if (!IsValidCSharpIdentifier(tag.Name))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    $"Tên tag '{tag.Name}' không hợp lệ. Tên tag phải bắt đầu bằng chữ cái hoặc '_', " +
                    "và chỉ chứa chữ, số, '_'.", tag.Name));
            }
            else if (ReservedKeywords.Contains(tag.Name))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    $"'{tag.Name}' là từ khoá C#, không dùng làm tên tag được.", tag.Name));
            }

            if (!seen.Add(tag.Name))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    $"Đã có tag tên '{tag.Name}'. Tên tag không phân biệt hoa/thường.", tag.Name));
            }

            if (tag.Direction != TagDirection.Memory && !devices.Contains(tag.Device))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    string.IsNullOrWhiteSpace(tag.Device)
                        ? $"Tag '{tag.Name}' là {tag.Direction} nên phải trỏ tới một thiết bị."
                        : $"Thiết bị '{tag.Device}' chưa được khai báo.", tag.Name));
            }
        }
    }

    private static void ValidateBlocks(DbiProject project, List<ValidationIssue> issues)
    {
        int mainCount = project.Blocks.Count(b => b.Kind == BlockKind.Main);

        if (mainCount != 1)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error,
                mainCount == 0
                    ? "Project phải có đúng một khối Main. Hiện chưa có khối nào."
                    : $"Project phải có đúng một khối Main. Hiện có {mainCount}."));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in project.Blocks)
        {
            if (!seen.Add(block.Name))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    $"Đã có khối tên '{block.Name}'.", block.Name));
            }
        }
    }

    /// <summary>
    /// Identifier C# hợp lệ: ký tự đầu là chữ cái hoặc '_', các ký tự sau là chữ/số/'_'.
    /// Dùng phân loại Unicode giống đặc tả C# để tên tag tiếng Việt có dấu vẫn dùng được.
    /// </summary>
    public static bool IsValidCSharpIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        if (!IsIdentifierStart(name[0])) return false;

        for (int i = 1; i < name.Length; i++)
        {
            if (!IsIdentifierPart(name[i])) return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c) =>
        c == '_' || char.GetUnicodeCategory(c) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;

    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c) || char.GetUnicodeCategory(c) is
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.Format;
}
