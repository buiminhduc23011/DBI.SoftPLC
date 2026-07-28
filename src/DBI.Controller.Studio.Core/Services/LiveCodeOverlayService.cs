using System.Text.RegularExpressions;
using DBI.Controller.Protocol;

namespace DBI.Controller.Studio.Core.Services;

public sealed record CodeTagReference(int Line, string TagName);
public sealed record CodeOverlayMarker(int Line, string TagName, string DisplayValue);

public sealed class LiveCodeOverlayService
{
    private static readonly Regex IoMember = new(@"\bIO\.(?<tag>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private readonly Dictionary<string, TagValueUpdate> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CodeTagReference> Analyze(string source) =>
        source.Split('\n').SelectMany((line, index) => IoMember.Matches(line)
            .Select(match => new CodeTagReference(index + 1, match.Groups["tag"].Value))).ToList();

    public void Apply(TagValueUpdate update) => _values[update.TagName] = update;
    public bool TryGetValue(string tagName, out TagValueUpdate update) => _values.TryGetValue(tagName, out update!);
    public void Clear() => _values.Clear();

    /// <summary>Builds the small inline markers for the currently visible editor range.</summary>
    public IReadOnlyList<CodeOverlayMarker> BuildVisibleMarkers(
        IReadOnlyList<CodeTagReference> references, int firstVisibleLine, int lastVisibleLine)
    {
        if (lastVisibleLine < firstVisibleLine) return Array.Empty<CodeOverlayMarker>();
        return references
            .Where(reference => reference.Line >= firstVisibleLine && reference.Line <= lastVisibleLine)
            .Where(reference => _values.ContainsKey(reference.TagName))
            .Select(reference => new CodeOverlayMarker(
                reference.Line,
                reference.TagName,
                Format(_values[reference.TagName].ValueJson)))
            .ToList();
    }

    private static string Format(string json)
    {
        if (json.Equals("true", StringComparison.OrdinalIgnoreCase)) return "TRUE";
        if (json.Equals("false", StringComparison.OrdinalIgnoreCase)) return "FALSE";
        return json;
    }
}
