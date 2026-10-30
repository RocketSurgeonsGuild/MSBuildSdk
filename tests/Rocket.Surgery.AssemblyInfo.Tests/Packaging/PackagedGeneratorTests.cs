using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Rocket.Surgery.AssemblyInfo.Tests.Packaging;

/// <summary>
/// The package ships extra builds of the generators for older Roslyn hosts
/// (analyzers/dotnet/roslyn5.0/cs, roslyn5.3/cs and roslyn5.6/cs); make sure those builds actually contain them.
/// </summary>
public class PackagedGeneratorTests
{
    private static readonly string[] GeneratorAssemblies =
    [
        MyNamespace.AssemblyInfo.Project.Roslyn50GeneratorAssembly,
        MyNamespace.AssemblyInfo.Project.Roslyn53GeneratorAssembly,
        MyNamespace.AssemblyInfo.Project.Roslyn56GeneratorAssembly,
    ];

    private static readonly string[] GeneratorTypeNames =
    [
        "Rocket.Surgery.AssemblyInfo.ConstantsGenerator",
        "Rocket.Surgery.AssemblyInfo.ResourcesGenerator",
        "Rocket.Surgery.AssemblyInfo.StringsGenerator",
        "Rocket.Surgery.AssemblyInfo.DependenciesMarkerGenerator",
        "Rocket.Surgery.AssemblyInfo.DependenciesGenerator",
    ];

    public static IEnumerable<(string GeneratorAssembly, string GeneratorTypeName)> GeneratorCases() =>
        from assembly in GeneratorAssemblies
        from typeName in GeneratorTypeNames
        select (assembly, typeName);

    /// <summary>The supports assembly at <paramref name="generatorAssembly" /> defines <paramref name="generatorTypeName" />.</summary>
    [Test]
    [MethodDataSource(nameof(GeneratorCases))]
    public async Task SupportsBuild_ContainsGenerator(string generatorAssembly, string generatorTypeName)
    {
        using var stream = File.OpenRead(generatorAssembly);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var typeNames = metadata.TypeDefinitions
                                .Select(metadata.GetTypeDefinition)
                                .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")
                                .ToHashSet(StringComparer.Ordinal);

        await Assert.That(typeNames).Contains(generatorTypeName);
    }
}
