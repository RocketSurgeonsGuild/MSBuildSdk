using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Rocket.Surgery.AssemblyInfo.Tests.Packaging;

/// <summary>
/// The package ships a second build of the generators for Roslyn 4.6 hosts
/// (analyzers/dotnet/roslyn4.6/cs); make sure that build actually contains them.
/// </summary>
public class PackagedGeneratorTests
{
    /// <summary>The roslyn4.6 assembly defines <paramref name="generatorTypeName" />.</summary>
    [Test]
    [Arguments("Rocket.Surgery.AssemblyInfo.ConstantsGenerator")]
    [Arguments("Rocket.Surgery.AssemblyInfo.ResourcesGenerator")]
    [Arguments("Rocket.Surgery.AssemblyInfo.StringsGenerator")]
    public async Task Roslyn46Build_ContainsGenerator(string generatorTypeName)
    {
        using var stream = File.OpenRead(MyNamespace.AssemblyInfo.Project.Roslyn46GeneratorAssembly);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var typeNames = metadata.TypeDefinitions
                                .Select(metadata.GetTypeDefinition)
                                .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")
                                .ToHashSet(StringComparer.Ordinal);

        await Assert.That(typeNames).Contains(generatorTypeName);
    }
}
