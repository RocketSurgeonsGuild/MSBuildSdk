using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.AssemblyInfo.Dependencies;
using Scriban;

namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Generates <c>AssemblyInfo.Dependencies</c>: a typed copy of the <c>AssemblyInfo</c> of every referenced assembly that
/// carries the marker (see <see cref="DependenciesMarkerGenerator" />). Opt in with <c>AssemblyInfoDependencies=true</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class DependenciesGenerator : IIncrementalGenerator
{
    // A static readonly field so analyzer release tracking (AnalyzerReleases.*.md) sees it.
    private static readonly DiagnosticDescriptor UnsupportedMarkerFormat = new(
        "RSAI004",
        "Referenced assembly publishes an AssemblyInfo format this version can not read",
        "Assembly '{0}' publishes AssemblyInfo format '{1}', which this version of Rocket.Surgery.AssemblyInfo can not read, so it is left out of AssemblyInfo.Dependencies; update the package",
        "AssemblyInfo",
        DiagnosticSeverity.Warning,
        true
    );

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settings = context.AnalyzerConfigOptionsProvider
                              .Select((c, _) => (
                                   enabled: c.GlobalOptions.TryGetValue("build_property.AssemblyInfoDependencies", out var value)
                                       && bool.TryParse(value, out var isEnabled)
                                       && isEnabled,
                                   ns: c.GlobalOptions.TryGetValue("build_property.AssemblyInfoNamespace", out var ns) && !string.IsNullOrEmpty(ns) ? ns : null,
                                   visibility: c.GlobalOptions.TryGetValue("build_property.AssemblyInfoVisibility", out var visibility) && !string.IsNullOrEmpty(visibility) ? visibility : null
                               ));

        // Reading the references' attributes is only worth doing when the feature is on. The result compares by value,
        // so typing in the project does not regenerate anything unless a reference changed.
        var dependencies = context.CompilationProvider
                                  .Combine(settings.Select((x, _) => x.enabled))
                                  .Select((x, ct) => x.Right ? DependencyInfo.Read(x.Left, ct) : ImmutableArray<DependencyInfo>.Empty)
                                  .WithComparer(DependencyInfoListComparer.Instance);

        context.RegisterSourceOutput(dependencies.Combine(settings).Combine(context.ParseOptionsProvider), Generate);
    }

    private static void Generate(
        SourceProductionContext spc,
        ((ImmutableArray<DependencyInfo> dependencies, (bool enabled, string? ns, string? visibility) settings), ParseOptions parse) args
    )
    {
        var ((dependencies, (enabled, ns, visibility)), parse) = args;
        if (!enabled || dependencies.IsDefaultOrEmpty)
            return;

        foreach (var dependency in dependencies.Where(dependency => !dependency.IsSupportedFormat))
        {
            spc.ReportDiagnostic(Diagnostic.Create(UnsupportedMarkerFormat, Location.None, dependency.Name, dependency.Format));
        }

        var cs = (CSharpParseOptions)parse;
        // ConstantsGenerator reports RSAI002 for this; the generated file-scoped namespace would only add errors.
        if (!string.IsNullOrWhiteSpace(ns) && cs.LanguageVersion < LanguageVersion.CSharp10)
            return;

        var root = DependencyTree.Build(dependencies);
        if (root.NestedAreas.Count == 0)
            return;

        var file = "Dependencies.sbntxt";
        var template = Template.Parse(EmbeddedResource.GetContent(file), file);
        var model = new DependenciesModel(root, ns, "public".Equals(visibility, StringComparison.OrdinalIgnoreCase));
        var output = template.Render(model, member => member.Name);

        // Indenting isn't that nice in Scriban when rendering nested structures via functions.
        output = SyntaxFactory
                .ParseCompilationUnit(output, options: cs)
                .NormalizeWhitespace(eol: "\n")
                .GetText()
                .ToString();

        spc.AddSource("AssemblyInfo.Dependencies.g.cs", SourceText.From(output, Encoding.UTF8));
    }
}
