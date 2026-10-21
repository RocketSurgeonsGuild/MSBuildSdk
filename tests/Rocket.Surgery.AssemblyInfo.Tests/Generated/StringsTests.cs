// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using Rocket.Surgery.AssemblyInfo.Strings;
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class StringsTests
{
    [Test]
    public async Task Parses_Resx_File() => await Assert.That(ResourceFile.Load("Resources.resx", "Strings")).IsNotNull();

    [Test]
    public async Task Supports_Named_Arguments() => await Assert.That(GeneratedInfo.Strings.Named("hello", "world")).IsNotEmpty();

    [Test]
    public async Task Supports_Indexed_Arguments() => await Assert.That(GeneratedInfo.Strings.Indexed("hello", "world")).IsNotEmpty();

    [Test]
    public async Task Supports_Named_Format_Arguments() =>
        await Assert.That(GeneratedInfo.Strings.WithNamedFormat(new DateTime(2020, 3, 20))).IsEqualTo("Year 2020, Month 03");

    [Test]
    public async Task Supports_Indexed_Format_Arguments() =>
        await Assert.That(GeneratedInfo.Strings.WithIndexedFormat(new DateTime(2020, 3, 20))).IsEqualTo("Year 2020, Month 03");

    [Test]
    public async Task Nests_Dotted_Resource_Names() => await Assert.That(GeneratedInfo.Strings.Foo.Bar.Baz).IsEqualTo("Value");
}
