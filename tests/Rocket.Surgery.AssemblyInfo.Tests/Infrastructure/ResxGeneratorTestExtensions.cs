using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

/// <summary>Adds the .resx input that AssemblyInfo.Resources.targets hands to <see cref="StringsGenerator" />.</summary>
public static class ResxGeneratorTestExtensions
{
    /// <summary>
    /// A primary .resx file (<c>_InjectStringsResxAdditionalFiles</c> adds it with ResxCode metadata).
    /// The text puts every <c>data</c> element on its own line, starting at line 5, so diagnostics can be located.
    /// </summary>
    public static GeneratorTestContextBuilder AddResx(
        this GeneratorTestContextBuilder builder,
        string path,
        string manifestResourceName,
        params (string Name, string Value)[] strings
    )
    {
        var text = new ResxAdditionalText(path, CreateResx(strings));
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.ResxCode.AssemblyInfoStrings", "true")
              .AddOption(text, "build_metadata.ResxCode.ManifestResourceName", manifestResourceName);
    }

    private static string CreateResx((string Name, string Value)[] strings)
    {
        // \n on purpose, so the line numbers do not depend on the platform.
        var lines = new List<string>
        {
            """<?xml version="1.0" encoding="utf-8"?>""",
            "<root>",
            """  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>""",
            """  <resheader name="version"><value>2.0</value></resheader>""",
        };
        lines.AddRange(strings.Select(s => $"""  <data name="{s.Name}" xml:space="preserve"><value>{s.Value}</value></data>"""));
        lines.Add("</root>");
        return string.Join("\n", lines);
    }

    private sealed class ResxAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken cancellationToken = default) => SourceText.From(content, Encoding.UTF8);
    }
}
