using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.Services;

public record TagFieldErrors(
    IReadOnlyList<string> NameErrors,
    IReadOnlyList<string> DeviceErrors,
    IReadOnlyList<string> AddressErrors);

public static class TagValidationService
{
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

    public static TagFieldErrors ValidateFieldErrors(DbiProject project, Tag tag)
    {
        var nameErrors = new List<string>();
        var deviceErrors = new List<string>();
        var addressErrors = new List<string>();

        if (!ProjectValidator.IsValidCSharpIdentifier(tag.Name))
            nameErrors.Add($"Tên tag '{tag.Name}' không hợp lệ. Tên phải là identifier C# hợp lệ.");
        else if (IsReservedKeyword(tag.Name))
            nameErrors.Add($"'{tag.Name}' là từ khoá C#, không dùng làm tên tag được.");

        if (project.AllTags().Count(t => t.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)) > 1)
            nameErrors.Add($"Đã có tag tên '{tag.Name}'.");

        if (tag.Direction != TagDirection.Memory)
        {
            bool deviceExists = project.Devices.Any(d => d.Name.Equals(tag.Device, StringComparison.OrdinalIgnoreCase));
            if (!deviceExists)
            {
                deviceErrors.Add(string.IsNullOrWhiteSpace(tag.Device)
                    ? "Tag Input/Output phải trỏ tới một thiết bị."
                    : $"Thiết bị '{tag.Device}' chưa được khai báo.");
            }

            if (string.IsNullOrWhiteSpace(tag.Address))
                addressErrors.Add("Tag Input/Output phải có địa chỉ phần cứng.");
        }

        return new TagFieldErrors(nameErrors, deviceErrors, addressErrors);
    }

    public static IReadOnlyList<ValidationIssue> ValidateProject(DbiProject project)
    {
        var issues = new List<ValidationIssue>(ProjectValidator.Validate(project));
        var routeCollisions = project.AllTags()
            .Where(t => t.Direction != TagDirection.Memory &&
                        !string.IsNullOrWhiteSpace(t.Device) &&
                        !string.IsNullOrWhiteSpace(t.Address))
            .GroupBy(t => $"{t.Device}\0{t.Address}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var collision in routeCollisions)
        {
            var sample = collision.First();
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                $"Có nhiều tag cùng trỏ tới ({sample.Device}, {sample.Address}). Hợp lệ nhưng đáng ngờ.",
                sample.Name));
        }

        foreach (var tag in project.AllTags())
        {
            foreach (string message in ValidateFieldErrors(project, tag).NameErrors)
                issues.Add(new ValidationIssue(IssueSeverity.Error, message, tag.Name));
            foreach (string message in ValidateFieldErrors(project, tag).DeviceErrors)
                issues.Add(new ValidationIssue(IssueSeverity.Error, message, tag.Name));
            foreach (string message in ValidateFieldErrors(project, tag).AddressErrors)
                issues.Add(new ValidationIssue(IssueSeverity.Error, message, tag.Name));
        }

        return issues
            .DistinctBy(i => (i.Severity, i.Message, i.Target))
            .ToList();
    }

    private static bool IsReservedKeyword(string text)
        => ReservedKeywords.Contains(text);
}
