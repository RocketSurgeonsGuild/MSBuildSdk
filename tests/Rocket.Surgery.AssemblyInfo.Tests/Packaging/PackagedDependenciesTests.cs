using System.Reflection;
using System.Runtime.Loader;
using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Packaging;

/// <summary>
/// The package ships the generators once per Roslyn release (analyzers/dotnet/roslyn5.0, 5.3, 5.6 and 5.9). The same
/// producer and consumer run through each of those builds and must give the very same snapshot as the one built here.
/// They run on this test's Roslyn, which is newer: what this shows is that the builds against the older APIs behave the same.
/// The SDK tests (AssemblyInfoDependenciesTests) compile with the real compilers of those releases.
/// </summary>
public class PackagedDependenciesTests : GeneratorTest
{
    private const string MarkerOption = "build_property.AssemblyInfoMarker";
    private const string DependenciesOption = "build_property.AssemblyInfoDependencies";

    public static IEnumerable<string> Builds() => ["current", "roslyn5.0", "roslyn5.3", "roslyn5.6",];

    [Test]
    [MethodDataSource(nameof(Builds))]
    public async Task Marker_Is_The_Same_On_Every_Build(string build)
    {
        var (marker, _) = Load(build);

        var result = await Produce(marker);

        await Verify(result).IgnoreParametersForVerified(build);
    }

    [Test]
    [MethodDataSource(nameof(Builds))]
    public async Task Dependencies_Are_The_Same_On_Every_Build(string build)
    {
        var (marker, dependencies) = Load(build);
        var producer = await Produce(marker);

        var result = await GeneratorTestContextBuilder
                          .Create()
                          .WithProjectName("Consumer")
                          .WithGenerator(dependencies)
                          .AddGlobalOption(DependenciesOption, "true")
                          .AddReferences(producer.Publish("Acme.Alpha", "1.2.3.0", marker))
                          .Build()
                          .GenerateAsync();

        await Verify(result).IgnoreParametersForVerified(build);
    }

    private static Task<GeneratorTestResults> Produce(Type marker) =>
        GeneratorTestContextBuilder
           .Create()
           .WithGenerator(marker)
           .AddGlobalOption(MarkerOption, "true")
           .AddConstant("Git.Branch", "main", root: ".")
           .AddConstant("Attributes.Version", "1.2.3.0", root: ".")
           .AddConstant("Metadata.Flavor", "Spicy", root: ".")
           .AddConstant("Count", "7", "int")
           .AddConstant("Multi", "first`nsecond")
           .Build()
           .GenerateAsync();

    // The older builds are loaded on their own, so their types are not this project's: everything else they use (Roslyn
    // itself) comes from the default context, as it does for a compiler that hosts them.
    private static (Type Marker, Type Dependencies) Load(string build)
    {
        if (build == "current")
        {
            return (typeof(DependenciesMarkerGenerator), typeof(DependenciesGenerator));
        }

        var path = build switch
        {
            "roslyn5.0" => MyNamespace.AssemblyInfo.Project.Roslyn50GeneratorAssembly,
            "roslyn5.3" => MyNamespace.AssemblyInfo.Project.Roslyn53GeneratorAssembly,
            "roslyn5.6" => MyNamespace.AssemblyInfo.Project.Roslyn56GeneratorAssembly,
            _ => throw new ArgumentOutOfRangeException(nameof(build), build, null),
        };

        var assembly = new PackagedBuildLoadContext(build).LoadFromAssemblyPath(path);
        return (assembly.GetType(typeof(DependenciesMarkerGenerator).FullName!, throwOnError: true)!, assembly.GetType(typeof(DependenciesGenerator).FullName!, throwOnError: true)!);
    }

    private sealed class PackagedBuildLoadContext(string name) : AssemblyLoadContext(name)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
