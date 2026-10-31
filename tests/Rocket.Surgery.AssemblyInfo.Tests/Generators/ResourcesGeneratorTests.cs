using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

public class ResourcesGeneratorTests : GeneratorTest
{
    [Test]
    public async Task Should_Add_An_Embedded_Resource()
    {
        var result = await Builder
                          .AddEmbeddedResource("somefile.txt", "text", "comment")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    // Joinery.Dovetail adds its appsettings files to @(EmbeddedResource) by absolute path. They reach the generator without
    // a Value, and must neither fail it (no resources would be generated at all) nor get an accessor of their own.
    [Test]
    public async Task Should_Skip_An_Embedded_Resource_Added_By_Another_Package()
    {
        var result = await Builder
                          .AddForeignEmbeddedResource("/project/appsettings.json")
                          .AddEmbeddedResource("somefile.txt", "text", "comment")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Add_A_Nested_Embedded_Resource()
    {
        var result = await Builder
                          .AddEmbeddedResource("something/else/somefile.bin", comment: "comment")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }
}
