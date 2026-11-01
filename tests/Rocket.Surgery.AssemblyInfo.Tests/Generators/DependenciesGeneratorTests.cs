using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.AssemblyInfo.Manifest;
using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

/// <summary>
/// <c>AssemblyInfo.Dependencies</c>, with its inputs shaped like AssemblyInfo.Dependencies.targets produces them: manifests
/// as additional files with <c>build_metadata.AssemblyInfoDependencyManifest.*</c>, and the chosen references' paths in
/// <c>build_property.AssemblyInfoDependencyReferences</c>.
/// </summary>
public partial class DependenciesGeneratorTests : GeneratorTest
{
    private const string ReferencesOption = "build_property.AssemblyInfoDependencyReferences";
    private const string FrameworkReferencesOption = "build_property.AssemblyInfoFrameworkDependencyReferences";

    private GeneratorTestContextBuilder DependenciesBuilder =>
        Builder
           .WithGenerator<DependenciesGenerator>()
           .AddGlobalOption("build_property.EmbeddedResourceStringExtensions", ".txt|.json");

    [Test]
    public async Task Should_Generate_A_Dependency_From_Its_Manifest()
    {
        var manifest = new AssemblyInfoManifest { AssemblyName = "Rocket.Surgery.Lib", TargetFramework = "net10.0", Generator = "test" };
        manifest.Constants.Add(new("Attributes.Version", "1.2.3.0"));
        manifest.Constants.Add(new("Git.Sha", "abcdef123"));
        manifest.Constants.Add(new("Metadata.Foo", "Bar"));
        manifest.Constants.Add(new("Project.TargetFrameworkVersion", "v10.0"));
        manifest.Constants.Add(new("Constants.Answer", "42", "int", "The answer"));
        manifest.Constants.Add(new("Constants.Nested.Multiline", "first\nsecond"));
        manifest.Resources.Add(new("Content/Styles/Main.css", "Rocket.Surgery.Lib.Content.Styles.Main.css", ManifestResource.Text, "Primary CSS"));
        manifest.Resources.Add(new("Content/Swagger/swagger.css", "Rocket.Surgery.Lib.Content.Swagger.swagger.css", ManifestResource.Text));
        manifest.Resources.Add(new("Content/Swagger/swagger.js", "Rocket.Surgery.Lib.Content.Swagger.swagger.js", ManifestResource.Binary));
        manifest.Resources.Add(new("data.bin", "Rocket.Surgery.Lib.data.bin"));

        var result = await DependenciesBuilder
                          .AddManifest("Rocket.Surgery.Lib", manifest)
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Public_Members_When_AssemblyInfo_Is_Public()
    {
        var manifest = new AssemblyInfoManifest { AssemblyName = "Lib" };
        manifest.Constants.Add(new("Git.Sha", "abcdef123"));
        manifest.Resources.Add(new("hello.txt", "Lib.hello.txt", ManifestResource.Text));

        var result = await DependenciesBuilder
                          .AddGlobalOption("build_property.AssemblyInfoVisibility", "public")
                          .AddGlobalOption("build_property.AssemblyInfoNamespace", "MyNamespace")
                          .AddManifest("Lib", manifest)
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_A_Dependency_From_Its_Attributes()
    {
        using var dependency = DependencyAssembly.Create(
            "Plain.Library",
            """
            [assembly: System.Reflection.AssemblyCompany("Plain Co")]
            [assembly: System.Reflection.AssemblyInformationalVersion("2.0.0-beta.1+0123456789abcdef0123456789abcdef01234567")]
            [assembly: System.Reflection.AssemblyMetadata("RepositoryUrl", "https://example.com/plain.git")]
            [assembly: System.Reflection.AssemblyMetadata("Dotted.Key", "flattened")]
            [assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
            """,
            ("Plain.Library.Templates.page.txt", "hello"),
            ("Plain.Library.logo.png", "png")
        );

        var result = await DependenciesBuilder
                          .AddReferences(dependency.Reference)
                          .AddGlobalOption(ReferencesOption, dependency.Path)
                          .Build()
                          .GenerateAsync();

        await Verify(result).ScrubLinesWithReplace(ScrubDependencyDirectory);
    }

    [Test]
    public async Task Should_Only_Name_The_Resources_Of_A_Framework_Dependency()
    {
        using var dependency = DependencyAssembly.Create("Framework.Thing", "", ("Framework.Thing.strings.txt", "hello"));

        var result = await DependenciesBuilder
                          .AddReferences(dependency.Reference)
                          .AddGlobalOption(ReferencesOption, dependency.Path)
                          .AddGlobalOption(FrameworkReferencesOption, dependency.Path)
                          .Build()
                          .GenerateAsync();

        await Verify(result).ScrubLinesWithReplace(ScrubDependencyDirectory);
    }

    [Test]
    public async Task Should_Leave_Out_References_That_Were_Not_Chosen()
    {
        using var dependency = DependencyAssembly.Create("Not.Chosen", """[assembly: System.Reflection.AssemblyCompany("Nope")]""");

        var result = await DependenciesBuilder
                          .AddReferences(dependency.Reference)
                          .Build()
                          .GenerateAsync();

        await Assert.That(GeneratedFiles(result)).IsEmpty();
    }

    [Test]
    public async Task Should_Suffix_Names_That_Flatten_Alike()
    {
        var result = await DependenciesBuilder
                          .AddManifest("Foo.Bar", new AssemblyInfoManifest { AssemblyName = "Foo.Bar" })
                          .AddManifest("Foo_Bar", new AssemblyInfoManifest { AssemblyName = "Foo_Bar" })
                          .Build()
                          .GenerateAsync();

        await Assert.That(GeneratedFiles(result)).IsEquivalentTo(["Dependencies.Foo_Bar.g.cs", "Dependencies.Foo_Bar_2.g.cs"]);
        await Assert.That(Diagnostics(result).Select(d => d.Id)).IsEquivalentTo(["RSAI006"]);
        await Assert.That(Diagnostics(result).Single().GetMessage()).Contains("'Foo.Bar' and 'Foo_Bar' both become AssemblyInfo.Dependencies.Foo_Bar");
    }

    [Test]
    public async Task Should_Fall_Back_To_Attributes_When_The_Manifest_Is_Too_New()
    {
        using var dependency = DependencyAssembly.Create("Lib", """[assembly: System.Reflection.AssemblyCompany("From attributes")]""");

        var result = await DependenciesBuilder
                          .AddReferences(dependency.Reference)
                          .AddGlobalOption(ReferencesOption, dependency.Path)
                          .AddManifest("Lib", """{ "schemaVersion": 99, "assemblyName": "Lib", "constants": [{ "path": "Constants.New", "value": "x" }] }""")
                          .Build()
                          .GenerateAsync();

        await Assert.That(Diagnostics(result).Select(d => d.Id)).IsEquivalentTo(["RSAI004"]);
        await Assert.That(GeneratedText(result, "Dependencies.Lib.g.cs")).Contains("From attributes");
        await Assert.That(GeneratedText(result, "Dependencies.Lib.g.cs")).DoesNotContain("New");
    }

    [Test]
    [Arguments("not json")]
    [Arguments("""{ "schemaVersion": 1 }""")]
    public async Task Should_Warn_When_The_Manifest_Cannot_Be_Read(string text)
    {
        var result = await DependenciesBuilder
                          .AddManifest("Lib", text)
                          .Build()
                          .GenerateAsync();

        await Assert.That(Diagnostics(result).Select(d => d.Id)).IsEquivalentTo(["RSAI005"]);
        await Assert.That(GeneratedFiles(result)).IsEmpty();
    }

    [Test]
    public async Task Should_Reserve_The_Dependencies_Root()
    {
        var result = await Builder
                          .AddConstant("Dependencies.Mine", "value", root: ".")
                          .AddConstant("Other", "value", root: "Dependencies.Nested")
                          .Build()
                          .GenerateAsync();

        var diagnostics = result.Results.Values.SelectMany(r => r.Diagnostics).Where(d => d.Id == "RSAI007").ToArray();
        await Assert.That(diagnostics).Count().IsEqualTo(2);
        await Assert.That(diagnostics.Select(d => d.Severity).Distinct()).IsEquivalentTo([DiagnosticSeverity.Error]);
    }

    // DependencyAssembly writes each assembly to a folder of its own.
    private static string ScrubDependencyDirectory(string line) => DependencyDirectory().Replace(line, "assemblyinfo-dependencies/{id}/");

    [GeneratedRegex(@"assemblyinfo-dependencies[\\/][0-9a-f]{32}[\\/]")]
    private static partial Regex DependencyDirectory();

    private static Diagnostic[] Diagnostics(GeneratorTestResults result) =>
        [.. result.Results.Values.SelectMany(r => r.Diagnostics).Where(d => d.Id.StartsWith("RSAI", StringComparison.Ordinal))];

    private static string[] GeneratedFiles(GeneratorTestResults result) =>
        [.. result
           .Results.Values.SelectMany(r => r.SyntaxTrees)
           .Select(tree => Path.GetFileName(tree.FilePath))
           .Where(name => name.StartsWith("Dependencies.", StringComparison.Ordinal))
           .Order(StringComparer.Ordinal)];

    private static string GeneratedText(GeneratorTestResults result, string fileName) =>
        result.Results.Values.SelectMany(r => r.SyntaxTrees).Single(tree => Path.GetFileName(tree.FilePath) == fileName).ToString();

    /// <summary>A small assembly compiled to disk: the generator only reads references with a file path.</summary>
    private sealed class DependencyAssembly : IDisposable
    {
        private readonly string _directory;

        private DependencyAssembly(string directory, string path)
        {
            _directory = directory;
            Path = path;
            Reference = MetadataReference.CreateFromFile(path);
        }

        public string Path { get; }
        public MetadataReference Reference { get; }

        public static DependencyAssembly Create(string assemblyName, string source, params (string Name, string Content)[] resources)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "assemblyinfo-dependencies", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, assemblyName + ".dll");
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new(OutputKind.DynamicallyLinkedLibrary)
            );
            using (var stream = File.Create(path))
            {
                var emit = compilation.Emit(
                    stream,
                    manifestResources: resources.Select(resource => new ResourceDescription(resource.Name, () => new MemoryStream(Encoding.UTF8.GetBytes(resource.Content)), true))
                );
                if (!emit.Success)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
            }

            return new(directory, path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // The compiler may still hold the file on some platforms; the temp folder is cleaned up eventually.
            }
            catch (UnauthorizedAccessException) { }
        }
    }
}

internal static class DependenciesGeneratorTestExtensions
{
    /// <summary>A dependency's manifest, the way AssemblyInfo.Dependencies.targets passes it.</summary>
    public static GeneratorTestContextBuilder AddManifest(this GeneratorTestContextBuilder builder, string assemblyName, AssemblyInfoManifest manifest) =>
        builder.AddManifest(assemblyName, ManifestJson.Write(manifest));

    public static GeneratorTestContextBuilder AddManifest(this GeneratorTestContextBuilder builder, string assemblyName, string json)
    {
        var text = new ManifestText($"/packages/{assemblyName.ToLowerInvariant()}/lib/net10.0/{assemblyName}{AssemblyInfoManifest.FileSuffix}", json);
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.AssemblyInfoDependencyManifest.AssemblyName", assemblyName);
    }

    private sealed class ManifestText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
