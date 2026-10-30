using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.AssemblyInfo.Dependencies;
using Scriban;

namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Marks the assembly so projects that reference it can read its <c>AssemblyInfo</c> (<c>AssemblyInfoDependencies</c>).
/// Opt in with <c>AssemblyInfoMarker=true</c>: the constants are published as assembly metadata attributes.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class DependenciesMarkerGenerator : IIncrementalGenerator
{
    // Local paths of the build machine: they say nothing about the assembly, and publishing them undoes deterministic builds.
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.Ordinal) { "Git.Root", };

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider
                             .Select((options, _) => options.GlobalOptions.TryGetValue("build_property.AssemblyInfoMarker", out var value)
                                 && bool.TryParse(value, out var isEnabled)
                                 && isEnabled);

        context.RegisterSourceOutput(ConstantItems.Read(context).Collect().Combine(enabled), Generate);
    }

    private static void Generate(
        SourceProductionContext spc,
        (System.Collections.Immutable.ImmutableArray<(string name, string value, string? type, string? comment, string root, string rootComment)> constants, bool enabled) args
    )
    {
        var (constants, enabled) = args;
        if (!enabled)
            return;

        // The same constant twice (an item listed twice) is published once. The first one wins, as it does for the generated member.
        var entries = new SortedDictionary<string, MarkerEntry>(StringComparer.Ordinal);
        foreach (var (name, value, type, _, root, _) in constants)
        {
            // Root="." without a dot in the name has no place to go.
            if (root == "." || name.Length == 0 || root.Length == 0 || !DependencyMarker.TryGetCanonicalType(type, out var canonicalType))
                continue;

            var path = root + "." + name;
            if (ExcludedPaths.Contains(path) || entries.ContainsKey(path))
                continue;

            entries.Add(
                path,
                new MarkerEntry(
                    SymbolDisplay.FormatLiteral(DependencyMarker.EntryPrefix + path, quote: true),
                    // LF whatever the build machine uses: the attribute is part of the assembly, and the same source should give the same assembly.
                    SymbolDisplay.FormatLiteral(DependencyMarker.Encode(canonicalType, ConstantItems.RestoreNewlines(value).Replace("\r\n", "\n")), quote: true)
                )
            );
        }

        var file = "DependenciesMarker.sbntxt";
        var template = Template.Parse(EmbeddedResource.GetContent(file), file);
        var output = template.Render(
            new MarkerModel(
                SymbolDisplay.FormatLiteral(DependencyMarker.MarkerKey, quote: true),
                SymbolDisplay.FormatLiteral(DependencyMarker.FormatVersion, quote: true),
                [..entries.Values]
            ),
            member => member.Name
        );

        spc.AddSource("AssemblyInfo.Marker.g.cs", SourceText.From(output, Encoding.UTF8));
    }

    private sealed record MarkerEntry(string Key, string Value);

    private sealed record MarkerModel(string MarkerKey, string FormatVersion, List<MarkerEntry> Entries)
    {
        public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    }
}
