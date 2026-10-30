using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

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
}
