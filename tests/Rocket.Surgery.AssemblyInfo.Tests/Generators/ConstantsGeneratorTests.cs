using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

public class ConstantsGeneratorTests : GeneratorTest
{
    [Test]
    public async Task Should_Add_A_Constant()
    {
        var result = await Builder
                          .AddConstant("Test", "\"Value\"", "string", "comment", "Constant", "rootComment")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Add_A_Nested_Constant()
    {
        var result = await Builder
                          .AddConstant("Test", "\"Value\"", "string", "comment", "Some.Nested.Root", "should be some")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Add_Typed_Constants()
    {
        var result = await Builder
                          .AddConstant("Count", "123", "int")
                          .AddConstant("Enabled", "true", "bool")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Should_Escape_Keyword_Names()
    {
        var result = await Builder
                          .AddConstant("class.else", "value")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }
}
