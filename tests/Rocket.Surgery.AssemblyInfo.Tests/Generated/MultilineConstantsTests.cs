// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using TUnit.Assertions.Enums;
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

/// <summary>
/// Newlines go through the build (AssemblyInfo.Constants.targets), the editorconfig file and the generator.
/// Foo.Raw is the csproj's multiline $(Multiline) property; Project.ProjectFile is the whole csproj.
/// </summary>
public class MultilineConstantsTests
{
    [Test]
    public async Task Round_Trips_Newlines_In_Msbuild_Properties()
    {
        // Indentation comes from the csproj, so compare trimmed lines. The leading newline is trimmed by the
        // generator; the last line is the whitespace before the closing tag.
        var lines = GeneratedInfo.Constants.Foo.Raw.Split(Environment.NewLine).Select(line => line.Trim());

        await Assert.That(lines).IsEquivalentTo(
            ["A Description", "with a newline and", "* Some \"things\" with quotes", "// Some comments too.", ""],
            CollectionOrdering.Matching
        );
    }

    [Test]
    public async Task Does_Not_Leave_Escaped_Newlines_In_Values()
    {
        await Assert.That(GeneratedInfo.Constants.Foo.Raw).DoesNotContain("\\n");
        await Assert.That(GeneratedInfo.Project.Description).DoesNotContain("\\n");
        await Assert.That(GeneratedInfo.Project.ProjectFile).DoesNotContain("\\n");
    }

    [Test]
    public async Task Round_Trips_Newlines_In_The_Project_File()
    {
        var lines = GeneratedInfo.Project.ProjectFile.Split(Environment.NewLine);

        await Assert.That(lines.Length).IsGreaterThan(10);
        await Assert.That(lines[0]).IsEqualTo("<Project Sdk=\"Microsoft.NET.Sdk\">");
    }
}
