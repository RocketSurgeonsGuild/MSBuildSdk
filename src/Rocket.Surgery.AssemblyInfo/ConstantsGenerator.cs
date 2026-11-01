using System.Runtime.InteropServices;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Rocket.Surgery.AssemblyInfo.Constants;
using Rocket.Surgery.AssemblyInfo.Manifest;

using Scriban;

#pragma warning disable RS1035
namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Generates <c>AssemblyInfo.Constants</c> from <c>@(Constant)</c> and <c>@(FileConstant)</c> items.
/// </summary>
[Generator]
public class ConstantsGenerator : IIncrementalGenerator
{
    // A static readonly field so analyzer release tracking (AnalyzerReleases.*.md) sees it.
    private static readonly DiagnosticDescriptor NamespaceRequiresCSharp10 = new(
        "RSAI002",
        "AssemblyInfoNamespace requires C# 10.0 or higher",
        "AssemblyInfoNamespace requires C# 10.0 or higher",
        "AssemblyInfo",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor DependenciesRootIsReserved = new(
        "RSAI007",
        "The Dependencies root is reserved for AssemblyInfo.Dependencies",
        "Constant '{0}' is under the Dependencies root, which AssemblyInfo.Dependencies uses; give it another Root or name",
        "AssemblyInfo",
        DiagnosticSeverity.Error,
        true
    );

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // AssemblyInfo.Constants.targets writes every @(Constant) to one file, $(AssemblyName).AssemblyInfo.constants.json.
        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(x =>
                x.Right.GetOptions(x.Left).TryGetValue("build_metadata.AssemblyInfoConstantsInput.AssemblyInfoConstants", out var isConstants)
                && bool.TryParse(isConstants, out var value) && value)
            .Select((x, ct) => x.Left.GetText(ct)?.ToString() ?? "")
            .SelectMany((text, _) => ReadConstants(text))
            .Select((constant, ct) =>
            {
                var (value, type, comment, root, rootComment) = (constant.Value, constant.Type, constant.Comment, constant.Root, constant.RootComment);
                if (string.IsNullOrEmpty(rootComment))
                    rootComment = "Provides access project-defined constants.";

                var name = constant.Name;
                if (string.IsNullOrEmpty(root))
                {
                    root = "Constants";
                }
                else if (root == ".")
                {
                    var parts = name.Split(['.'], 2);
                    if (parts.Length == 2)
                    {
                        // root should be the first part up to the first dot of name
                        // and name should be the rest
                        // note we only do this if there's an actual dot, otherwise, we
                        // just leave the root's default of Constants
                        root = parts[0];
                        name = parts[1];
                    }
                }

                return (name, value: value ?? "", type: string.IsNullOrWhiteSpace(type) ? null : type, comment: string.IsNullOrWhiteSpace(comment) ? null : comment, root!, rootComment!);
            });

        // Read the AssemblyInfoNamespace property or default to null
        var right = context.AnalyzerConfigOptionsProvider
            .Select((c, t) => (
                c.GlobalOptions.TryGetValue("build_property.AssemblyInfoNamespace", out var ns) && !string.IsNullOrEmpty(ns) ? ns : null,
                c.GlobalOptions.TryGetValue("build_property.AssemblyInfoVisibility", out var visibility) && !string.IsNullOrEmpty(visibility) ? visibility : null
              ))
            .Combine(context.ParseOptionsProvider);

        var inputs = files.Combine(right);

        context.RegisterSourceOutput(inputs, GenerateConstant);
    }

    private void GenerateConstant(SourceProductionContext spc,
        ((string name, string value, string? type, string? comment, string root, string rootComment), ((string? ns, string? visibility), ParseOptions parse)) args)
    {
        var ((name, value, type, comment, root, rootComment), ((ns, visibility), parse)) = args;
        var cs = (CSharpParseOptions)parse;

        if (!string.IsNullOrWhiteSpace(ns) &&
            cs.LanguageVersion < LanguageVersion.CSharp10)
        {
            spc.ReportDiagnostic(Diagnostic.Create(NamespaceRequiresCSharp10, Location.None));
            return;
        }

        // AssemblyInfo.Dependencies (DependenciesGenerator) owns this root.
        if (root == DependenciesGenerator.RootName || root.StartsWith(DependenciesGenerator.RootName + ".", StringComparison.Ordinal))
        {
            spc.ReportDiagnostic(Diagnostic.Create(DependenciesRootIsReserved, Location.None, $"{root}.{name}"));
            return;
        }

        comment = comment is not null ? ConstantText.DocComment(comment, trimWhitespace: true) : ConstantText.DocComment(value, trimWhitespace: false);

        var rootArea = Area.Load([new(name, ConstantText.NormalizeNewLines(value).Trim(['\r', '\n']), comment, type ?? "string"),], root, rootComment);
        var output = Render(rootArea, ns, visibility, cs);

        var hintName = string.Concat($"{root}.{name}.g.cs".Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_'));
        spc.AddSource(hintName, SourceText.From(output, Encoding.UTF8));
    }

    /// <summary>The constants in one constants file; a file that cannot be read has none (the targets always write a valid one).</summary>
    private static IEnumerable<ProjectConstant> ReadConstants(string text)
    {
        try
        {
            return ProjectConstantsJson.Read(text);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    /// <summary>Renders <c>Constants.sbntxt</c> for an area tree: the constants under <c>AssemblyInfo</c>.</summary>
    internal static string Render(Area rootArea, string? ns, string? visibility, CSharpParseOptions cs)
    {
        // For now, we only support C# though
        const string file = "Constants.sbntxt";
        var template = Template.Parse(EmbeddedResource.GetContent(file), file);
        var model = new Model(rootArea, ns, "public".Equals(visibility, StringComparison.OrdinalIgnoreCase));
        if ((int)cs.LanguageVersion >= 1100)
            model.RawStrings = true;

        var output = template.Render(model, member => member.Name);

        // Apply formatting since indenting isn't that nice in Scriban when rendering nested
        // structures via functions.
        // We alos rewrite to prepend a newline leading trivia before the raw string literals if any
        var node = new RawStringLiteralRewriter().Visit(
            SyntaxFactory.ParseCompilationUnit(output, options: cs).NormalizeWhitespace(eol: Environment.NewLine));

        return node.GetText().ToString();
    }

    private class RawStringLiteralRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            // See https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntaxkind?view=roslyn-dotnet-4.13.0
            // MultiLineRawStringLiteralToken = 8519
            // Utf8MultiLineRawStringLiteralToken = 8522
            return token.RawKind is 8519 or 8522
                ? token.WithLeadingTrivia(
                    token.LeadingTrivia.Add(
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                        SyntaxFactory.CarriageReturnLineFeed :
                        SyntaxFactory.LineFeed))
                : base.VisitToken(token);
        }
    }
}
