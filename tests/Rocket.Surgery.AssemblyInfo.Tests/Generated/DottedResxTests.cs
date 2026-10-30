// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

/// <summary>
/// Dotted.Names.resx has a dot in its file name but is not a culture variant;
/// Dotted.Names.es.resx next to it is (the SDK treats the "es" as a culture).
/// </summary>
public class DottedResxTests
{
    [Test]
    public async Task Generates_Strings_For_A_File_Name_With_A_Dot() =>
        await Assert.That(GeneratedInfo.Strings.DottedFileName).IsEqualTo("Strings from a file with a dot in its name");

    // Both files define DottedFileName. If the culture variant were generated as well, the class would have the member
    // twice and this project would not compile, so building at all proves it is skipped.
    [Test]
    public async Task Skips_The_Culture_Variant_Of_A_Dotted_File() =>
        await Assert.That(typeof(GeneratedInfo.Strings).GetProperties().Count(property => property.Name == "DottedFileName")).IsEqualTo(1);
}
