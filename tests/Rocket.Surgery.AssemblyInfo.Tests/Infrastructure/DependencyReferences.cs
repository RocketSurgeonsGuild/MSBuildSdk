using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

/// <summary>Turns what a producing project generated into the reference a consuming project gets: an assembly on disk, as far as the compiler can tell.</summary>
public static class DependencyReferences
{
    /// <summary>
    /// Compiles what <see cref="DependenciesMarkerGenerator" /> generated in <paramref name="producer" /> into an assembly called
    /// <paramref name="assemblyName" /> (the test project name drops everything after a dot) and references it by its image,
    /// the way csc is handed a project's output. <paramref name="markerGenerator" /> is the generator that ran, when it is not
    /// the one this project builds (see the tests of the packaged Roslyn builds).
    /// </summary>
    public static MetadataReference Publish(this GeneratorTestResults producer, string assemblyName, string version = "1.0.0.0", Type? markerGenerator = null)
    {
        var trees = producer.Results[markerGenerator ?? typeof(DependenciesMarkerGenerator)].SyntaxTrees
                            .Append(CSharpSyntaxTree.ParseText($"""[assembly: System.Reflection.AssemblyVersion("{version}")]"""));
        return Emit(assemblyName, trees, producer.FinalCompilation!.References);
    }

    /// <summary>An assembly that never used the marker.</summary>
    public static MetadataReference Plain(string assemblyName, GeneratorTestResults any) =>
        Emit(assemblyName, [CSharpSyntaxTree.ParseText("public class Plain { }")], any.FinalCompilation!.References);

    /// <summary>An assembly with attributes written by hand, as a broken or newer producer would have.</summary>
    public static MetadataReference Handwritten(string assemblyName, string source, GeneratorTestResults any) =>
        Emit(assemblyName, [CSharpSyntaxTree.ParseText(source)], any.FinalCompilation!.References);

    private static MetadataReference Emit(string assemblyName, IEnumerable<SyntaxTree> trees, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success)
            throw new InvalidOperationException($"{assemblyName} did not compile: {string.Join(Environment.NewLine, emitted.Diagnostics)}");

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream, filePath: assemblyName + ".dll");
    }
}
