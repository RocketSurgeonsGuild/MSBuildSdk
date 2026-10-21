// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class MetadataTests
{
    [Test]
    public async Task Exposes_Assembly_Metadata() => await Assert.That(GeneratedInfo.Metadata.Foo).IsEqualTo("Bar");

    [Test]
    public async Task Nests_Dotted_Metadata_Names() => await Assert.That(GeneratedInfo.Metadata.Root.Foo.Bar).IsEqualTo("Baz");
}
