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
