// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class ConstantsTests
{
    [Test]
    public async Task Exposes_Nested_Constant() => await Assert.That(GeneratedInfo.Constants.Foo.Bar).IsEqualTo("Baz");

    [Test]
    public async Task Keeps_Semicolons() => await Assert.That(GeneratedInfo.Constants.WithSemiColon).IsEqualTo("A;B;C");

    [Test]
    public async Task Keeps_Multiline_Values() => await Assert.That(GeneratedInfo.Constants.Foo.Raw).Contains("with a newline and");

    // The generated members are compile-time constants on purpose; asserting on them checks the generator
    // emitted the right type and value, which TUnitAssertions0005 can't see.
#pragma warning disable TUnitAssertions0005
    [Test]
    public async Task Supports_Typed_Constants()
    {
        await Assert.That(GeneratedInfo.Constants.TypedInt).IsEqualTo(123);
        await Assert.That(GeneratedInfo.Constants.TypedInt64).IsEqualTo(123L);
        await Assert.That(GeneratedInfo.Constants.TypedLong).IsEqualTo(123L);
        await Assert.That(GeneratedInfo.Constants.TypedDouble).IsEqualTo(1.23);
        await Assert.That(GeneratedInfo.Constants.TypedBoolean).IsTrue();
    }
#pragma warning restore TUnitAssertions0005

    [Test]
    public async Task Exposes_File_Constants() =>
        await Assert.That(GeneratedInfo.Constants.Content.Docs.License).IsEqualTo(Path.Combine("Content", "Docs", "License.md"));

    [Test]
    public async Task Escapes_Invalid_File_Identifiers() =>
        await Assert.That(GeneratedInfo.Constants.Content.Docs._12._Readme_copy_).IsEqualTo(Path.Combine("Content", "Docs", "12. Readme (copy).txt"));

    [Test]
    public async Task Uses_Link_For_Linked_File_Constants() =>
        await Assert.That(GeneratedInfo.Constants.Included.Readme).IsEqualTo(Path.Combine("Included", "Readme.txt"));
}
