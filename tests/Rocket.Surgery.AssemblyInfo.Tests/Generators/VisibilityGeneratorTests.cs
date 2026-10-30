using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

/// <summary>
/// <c>AssemblyInfoVisibility=public</c> reaches the generators as build_property.AssemblyInfoVisibility; the generated
/// <c>AssemblyInfo</c> class is internal unless it says public. The other tests snapshot the internal default.
/// </summary>
public class VisibilityGeneratorTests : GeneratorTest
{
    private const string VisibilityOption = "build_property.AssemblyInfoVisibility";

    [Test]
    public async Task Should_Make_Constants_Public()
    {
        var result = await Builder
                          .AddGlobalOption(VisibilityOption, "public")
                          .AddConstant("Test", "Value", "string", "comment", "Constant", "rootComment")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Make_Resources_Public()
    {
        var result = await Builder
                          .AddGlobalOption(VisibilityOption, "public")
                          .AddEmbeddedResource("somefile.txt", "text", "comment")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Make_Strings_Public()
    {
        var result = await Builder
                          .AddGlobalOption(VisibilityOption, "public")
                          .AddResx("Strings/Messages.resx", "Test.Strings.Messages", ("Greeting", "Hello {name}"))
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    [Arguments("public")]
    [Arguments("Public")]
    [Arguments("PUBLIC")]
    public async Task Should_Ignore_The_Case_Of_Public(string visibility)
    {
        var result = await Builder
                          .AddGlobalOption(VisibilityOption, visibility)
                          .AddConstant("Test", "Value")
                          .Build()
                          .GenerateAsync();

        await Assert.That(GeneratedSource<ConstantsGenerator>(result)).Contains("public partial class AssemblyInfo");
    }

    [Test]
    [Arguments("internal")]
    [Arguments("private")]
    [Arguments("")]
    public async Task Should_Stay_Internal_For_Anything_Else(string visibility)
    {
        var result = await Builder
                          .AddGlobalOption(VisibilityOption, visibility)
                          .AddConstant("Test", "Value")
                          .Build()
                          .GenerateAsync();

        var source = GeneratedSource<ConstantsGenerator>(result);
        await Assert.That(source).Contains("partial class AssemblyInfo");
        await Assert.That(source).DoesNotContain("public partial class AssemblyInfo");
    }

    private static string GeneratedSource<TGenerator>(GeneratorTestResults result) =>
        string.Join("\n", result.Results[typeof(TGenerator)].SyntaxTrees.Select(tree => tree.ToString()));
}
