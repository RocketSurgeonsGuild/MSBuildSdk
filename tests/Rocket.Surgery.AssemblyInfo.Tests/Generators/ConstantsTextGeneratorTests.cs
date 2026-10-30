using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

/// <summary>How <see cref="ConstantsGenerator" /> reads the text inputs that AssemblyInfo.Constants.targets prepares.</summary>
public class ConstantsTextGeneratorTests : GeneratorTest
{
    /// <summary>
    /// AssemblyInfoGitComment becomes the RootComment of the Git.* constants (AssemblyInfo.Git.targets), which are
    /// added with Root="." so the first part of their name is the class.
    /// </summary>
    [Test]
    public async Task Should_Use_The_Root_Comment_For_The_Git_Area()
    {
        var result = await Builder
                          .AddConstant("Git.Branch", "main", root: ".", rootComment: "Where this build came from")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    /// <summary>
    /// The targets turn every newline into the `n marker, because editorconfig values are a single line;
    /// the generator turns it back, in the value and in the XML doc comment.
    /// </summary>
    [Test]
    public async Task Should_Turn_Newline_Markers_Into_Newlines()
    {
        var result = await Builder
                          .AddConstant("Multi", "first`nsecond`nthird", comment: "doc first`ndoc second")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    /// <summary>A backslash-n is two characters, not a newline: values like Windows paths must not change.</summary>
    [Test]
    public async Task Should_Keep_Backslashes()
    {
        var result = await Builder
                          .AddConstant("Path", @"C:\new\node")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    /// <summary>
    /// The backtick is the escape character: the targets double every backtick in a value (so `npm` arrives as
    /// ``npm``), and the generator turns the pairs back into single backticks, in the value and in the doc comment.
    /// </summary>
    [Test]
    public async Task Should_Unescape_Doubled_Backticks()
    {
        var result = await Builder
                          .AddConstant("Ticks", "run ``npm`` now", comment: "see ``code``")
                          .Build()
                          .GenerateAsync();

        var source = GeneratedSource(result);
        await Assert.That(source).Contains("run `npm` now");
        await Assert.That(source).Contains("/// see `code`");
        await Assert.That(source).DoesNotContain("``");

        await Verify(result);
    }

    /// <summary>
    /// A newline arrives as `n and a literal backtick followed by n arrives as ``n. Read left to right, the two are
    /// not confused, whatever order they come in.
    /// </summary>
    [Test]
    public async Task Should_Tell_A_Newline_From_A_Literal_Backtick_N()
    {
        var result = await Builder
                          .AddConstant("Both", "literal ``n then newline`nnext")
                          .AddConstant("TickThenNewline", "a```nb")
                          .AddConstant("TrailingTick", "end`")
                          .Build()
                          .GenerateAsync();

        var source = GeneratedSource(result);
        await Assert.That(source).Contains("literal `n then newline" + Environment.NewLine + "next");
        await Assert.That(source).Contains("a`" + Environment.NewLine + "b");
        await Assert.That(source).Contains("end`" + Environment.NewLine + "\"\"\"");

        await Verify(result);
    }

    private static string GeneratedSource(GeneratorTestResults result) =>
        string.Join("\n", result.Results[typeof(ConstantsGenerator)].SyntaxTrees.Select(tree => tree.ToString()));
}
