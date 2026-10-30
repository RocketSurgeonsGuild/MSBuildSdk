using System.Diagnostics;

namespace Rocket.Surgery.AssemblyInfo.Strings;

[DebuggerDisplay("{Id} = {Value}")]
internal record ResourceValue(string Id, string Name, string? Raw)
{
#pragma warning disable RS1035
    public string? Value => Raw?.Replace(Environment.NewLine, "")?.Replace("<", "&lt;")?.Replace(">", "&gt;");
#pragma warning restore RS1035
    public string? Comment { get; init; }
    public bool HasFormat => Format.Count > 0;
    public bool HasArgFormat => Format.Any(x => x.Format != null);
    // We either have *all* named or all indexed. Can't mix. We skip generating
    // methods for mixed ones and report RSAI003 on the Resx (see StringsGenerator).
    public bool IsNamedFormat => HasFormat && Format.All(x => !int.TryParse(x.Arg, out _));
    public bool IsIndexedFormat => HasFormat && Format.All(x => int.TryParse(x.Arg, out _));
    public bool IsMixedFormat => HasFormat && !IsNamedFormat && !IsIndexedFormat;
    /// <summary>The 1-based line of the <c>data</c> element in the resx, or 0 when unknown.</summary>
    public int Line { get; init; }
    public List<ArgFormat> Format { get; } = [];
    public HashSet<string> Args => [with(Format.Select(x => x.Arg))];
}
