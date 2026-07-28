using System.Text.RegularExpressions;
using DBI.Controller.Protocol;

namespace DBI.Controller.Studio.Core.Services;

public sealed record CodeTagReference(int Line, string TagName);

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
}
