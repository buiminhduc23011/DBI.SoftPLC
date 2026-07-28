using System.Text;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Studio.Core.Services;

/// <summary>Pure mapping operations used by WPF drag/drop handlers and keyboard commands.</summary>
public sealed class TagMappingService
{
    public TagMappingResult MapToTag(TagRowViewModel target, DeviceTagCandidate source)
    {
        if (target.DataType != source.DataType)
            return TagMappingResult.Rejected($"Không khớp kiểu: {source.DataType} ≠ {target.DataType}");

        target.Device = source.DeviceName;
        target.Address = source.Address;
        return TagMappingResult.Accepted;
    }

    public TagMappingResult AddToTable(TagTable table, DeviceTagCandidate source)
    {
        var name = SuggestName(source, table.Tags.Select(t => t.Name));
        table.Tags.Add(new Tag
        {
            Name = name,
            DataType = source.DataType,
            Direction = source.Direction,
            Device = source.DeviceName,
            Address = source.Address
        });
        return new TagMappingResult(true, null, name);
    }

    public bool AddToWatch(WatchTable table, string tagName)
    {
        if (table.TagNames.Any(n => n.Equals(tagName, StringComparison.OrdinalIgnoreCase))) return false;
        table.TagNames.Add(tagName);
        return true;
    }

    public static string InsertIoReference(string source, int caretOffset, string tagName)
    {
        if (caretOffset < 0 || caretOffset > source.Length) throw new ArgumentOutOfRangeException(nameof(caretOffset));
        return source.Insert(caretOffset, $"IO.{tagName}");
    }

    private static string SuggestName(DeviceTagCandidate source, IEnumerable<string> existing)
    {
        var baseName = new string(source.Address.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Tag";
        if (char.IsDigit(baseName[0])) baseName = "Tag_" + baseName;
        var used = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{source.DeviceName}_{baseName}";
        for (var i = 2; used.Contains(candidate); i++) candidate = $"{source.DeviceName}_{baseName}_{i}";
        return candidate;
    }
}

public sealed record DeviceTagCandidate(
    string DeviceName,
    string Address,
    TagDataType DataType,
    TagDirection Direction = TagDirection.Input);

public sealed record TagMappingResult(bool Success, string? Error, string? CreatedTagName = null)
{
    public static TagMappingResult Accepted { get; } = new(true, null);
    public static TagMappingResult Rejected(string error) => new(false, error);
}
