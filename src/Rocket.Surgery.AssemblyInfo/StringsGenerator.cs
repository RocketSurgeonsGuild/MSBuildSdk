using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.AssemblyInfo.Strings;
using Scriban;

namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Generates <c>AssemblyInfo.Strings</c> from the project's <c>.resx</c> files.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class StringsGenerator : IIncrementalGenerator
{
    // A static readonly field so analyzer release tracking (AnalyzerReleases.*.md) sees it.
    // Braces are doubled because the message is a format string.
    private static readonly DiagnosticDescriptor MixedFormatArguments = new(
        "RSAI003",
        "Resource string mixes named and indexed format arguments",
        "Resource string '{0}' mixes named ({{name}}) and indexed ({{0}}) format arguments, so no AssemblyInfo.Strings member is generated for it; use one style",
        "AssemblyInfo",
        DiagnosticSeverity.Warning,
        true
    );

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Read the AssemblyInfoNamespace property or default to null
        var right = context.AnalyzerConfigOptionsProvider
            .Select((c, t) => (
                c.GlobalOptions.TryGetValue("build_property.AssemblyInfoNamespace", out var ns) && !string.IsNullOrEmpty(ns) ? ns : null,
                c.GlobalOptions.TryGetValue("build_property.AssemblyInfoVisibility", out var visibility) && !string.IsNullOrEmpty(visibility) ? visibility : null
              ))
            .Combine(context.CompilationProvider.Select((s, _) => s.Language));

        context.RegisterSourceOutput(
            right,
            (spc, args) =>
            {
                var ((ns, _), _) = args;

                var strings = EmbeddedResource.GetContent($"ResourceManager.sbntxt");
                var template = Template.Parse(strings);
                var output = template.Render(new { Namespace = ns }, member => member.Name);
                spc.AddSource("AssemblyInfo.Strings.g.cs", SourceText.From(output, Encoding.UTF8));
            });

        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(x =>
                x.Right.GetOptions(x.Left).TryGetValue("build_metadata.ResxCode.AssemblyInfoStrings", out var resourceString)
                && bool.TryParse(resourceString, out var isResourceString) && isResourceString)
            .Where(x => x.Right.GetOptions(x.Left).TryGetValue("build_metadata.ResxCode.ManifestResourceName", out var value) && value != null)
            .Select((x, ct) =>
            {
                x.Right.GetOptions(x.Left).TryGetValue("build_metadata.ResxCode.ManifestResourceName", out var resourceName);
                return (filePath: x.Left.Path, text: x.Left.GetText(ct), resourceName!);
            })
            .Where(x => x.text != null);


        context.RegisterSourceOutput(
            files.Combine(right).Combine(context.ParseOptionsProvider),
            GenerateSource);
    }

    private static void GenerateSource(SourceProductionContext spc,
        (((string filePath, SourceText? text, string resourceName), ((string? ns, string? visibility), string language)), ParseOptions parse) arg)
    {
        var (((filePath, resourceText, resourceName), ((ns, visibility), language)), parse) = arg;

        var file = "Strings.sbntxt";
        var template = Template.Parse(EmbeddedResource.GetContent(file), file);

        var rootArea = ResourceFile.LoadText(resourceText!.ToString(), "Strings");

        // Strings.sbntxt has no output for mixed named/indexed arguments; say so instead of dropping the member silently.
        foreach (var value in GetValues(rootArea).Where(v => v.IsMixedFormat))
        {
            spc.ReportDiagnostic(Diagnostic.Create(MixedFormatArguments, GetLocation(filePath, resourceText!, value.Line), value.Name));
        }

        var model = new Model(rootArea, resourceName, ns, "public".Equals(visibility, StringComparison.OrdinalIgnoreCase));

        var output = template.Render(model, member => member.Name);

        output = SyntaxFactory
            .ParseCompilationUnit(output, options: parse as CSharpParseOptions)
            .NormalizeWhitespace()
            .GetText()
            .ToString();

        spc.AddSource(resourceName, SourceText.From(output, Encoding.UTF8));
    }

    private static IEnumerable<ResourceValue> GetValues(ResourceArea area) =>
        area.Values.Concat(area.NestedAreas.SelectMany(GetValues));

    private static Location GetLocation(string filePath, SourceText text, int line)
    {
        // line is 1-based; 0 means the resx did not report one, which leaves the diagnostic on the file.
        if (line < 1 || line > text.Lines.Count)
            return Location.Create(filePath, default, default);

        var span = text.Lines[line - 1].Span;
        return Location.Create(filePath, span, text.Lines.GetLinePositionSpan(span));
    }
}
