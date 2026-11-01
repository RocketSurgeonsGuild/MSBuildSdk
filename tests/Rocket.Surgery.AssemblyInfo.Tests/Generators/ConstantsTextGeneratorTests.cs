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
    /// Every kind of line break in a value or comment becomes the build machine's newline, in the value and in the XML
    /// doc comment.
    /// </summary>
    [Test]
    public async Task Should_Turn_Every_Line_Break_Into_A_Newline()
    {
        var result = await Builder
                          .AddConstant("Multi", "first\nsecond\r\nthird\rfourth\u2028fifth", comment: "doc first\ndoc second")
                          .Build()
                          .GenerateAsync();

        var source = GeneratedSource(result);
        await Assert.That(source).Contains(string.Join(Environment.NewLine, "first", "second", "third", "fourth", "fifth"));
        await Assert.That(source).Contains("/// doc first" + Environment.NewLine);
        await Assert.That(source).Contains("/// doc second" + Environment.NewLine);

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
    /// Values reach the generator as written (they used to be escaped for editorconfig, with the backtick as the escape
    /// character and ; and edge whitespace wrapped in |...|), so backticks, `n, ; and | are ordinary characters.
    /// </summary>
    [Test]
    public async Task Should_Keep_Text_As_Written()
    {
        var result = await Builder
                          .AddConstant("Ticks", "run `npm` now, a literal `n and ``doubled``", comment: "see `code`")
                          .AddConstant("Separators", "A;B|C")
                          .AddConstant("Wrapped", "|looks wrapped|")
                          .AddConstant("Padded", "  padded  ")
                          .Build()
                          .GenerateAsync();

        var source = GeneratedSource(result);
        await Assert.That(source).Contains("run `npm` now, a literal `n and ``doubled``");
        await Assert.That(source).Contains("/// see `code`");
        await Assert.That(source).Contains("A;B|C");
        await Assert.That(source).Contains("|looks wrapped|");
        await Assert.That(source).Contains("  padded  ");
    }

    private static string GeneratedSource(GeneratorTestResults result) =>
        string.Join("\n", result.Results[typeof(ConstantsGenerator)].SyntaxTrees.Select(tree => tree.ToString()));
}
