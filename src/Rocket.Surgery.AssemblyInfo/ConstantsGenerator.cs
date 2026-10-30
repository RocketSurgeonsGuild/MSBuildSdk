using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Rocket.Surgery.AssemblyInfo.Constants;

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

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = ConstantItems.Read(context);

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

        comment = comment is not null
            ? "/// " + string.Join(Environment.NewLine + "/// ", new XText(comment).ToString().Trim().Replace("`n", Environment.NewLine).Trim(['\r', '\n']).Split([Environment.NewLine], StringSplitOptions.None))
            : "/// " + string.Join(Environment.NewLine + "/// ", new XText(value).ToString().Replace("`n", Environment.NewLine).Trim(['\r', '\n']).Split([Environment.NewLine], StringSplitOptions.None));

        // Revert normalization of newlines performed in MSBuild to workaround the limitation in editorconfig.
        var rootArea = Area.Load([new(name, ConstantItems.RestoreNewlines(value), comment, type ?? "string"),], root, rootComment);
        // For now, we only support C# though
        const string file = "Constants.sbntxt";
        var template = Template.Parse(EmbeddedResource.GetContent(file), file);
        var model = new Model(rootArea, ns, "public".Equals(visibility, StringComparison.OrdinalIgnoreCase));
        if ((int)cs.LanguageVersion >= 1100)
            model.RawStrings = true;

        var output = template.Render(model, member => member.Name);

        if (parse.Language == LanguageNames.CSharp)
        {
            // Apply formatting since indenting isn't that nice in Scriban when rendering nested
            // structures via functions.
            // We alos rewrite to prepend a newline leading trivia before the raw string literals if any
            var node = new RawStringLiteralRewriter().Visit(
                SyntaxFactory.ParseCompilationUnit(output, options: cs).NormalizeWhitespace(eol: Environment.NewLine));

            output = node.GetText().ToString();
        }

        var hintName = string.Concat($"{root}.{name}.g.cs".Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_'));
        spc.AddSource(hintName, SourceText.From(output, Encoding.UTF8));
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
