namespace Rocket.Surgery.MyAssembly.Tests;

public class ConstantsGeneratorTests : GeneratorTest
{
    [Test]
    public async Task Should_Add_A_Constant()
    {
        var result = await Builder
                          .AddConstant(
                               "Test",
                               "\"Value\"",
                               "string",
                               "comment",
                               "Constant",
                               "rootComment"
                           )
                          .Build()
                          .GenerateAsync();
        await Verify(result);
    }

    // [Test]
    // public async Task Should_Add_A_Nested_Constant()
    // {
    //     var result = await Builder
    //                       .AddConstant(
    //                            "Test",
    //                            "\"Value\"",
    //                            "string",
    //                            "comment",
    //                            "Some.Nested.Root",
    //                            "should be some"
    //                        )
    //                       .Build()
    //                       .GenerateAsync();
    //     await Verify(result);
    // }

    [Test]
    public async Task Should_Add_A_Embedded_Resource()
    {
        var result = await Builder
                          .AddEmbeddedResource(
                               "somefile.cs",
                               "somefile.cs",
                               "kind",
                               "comment"
                           )
                          .Build()
                          .GenerateAsync();
        await Verify(result);
    }

    [Test]
    public async Task Should_Add_A_Nested_Embedded_Resource()
    {
        var result = await Builder
                          .AddEmbeddedResource(
                               "something/else/somefile.cs",
                               "something.somefile.cs",
                               "something/else",
                               "comment"
                           )
                          .Build()
                          .GenerateAsync();
        await Verify(result);
    }
}
