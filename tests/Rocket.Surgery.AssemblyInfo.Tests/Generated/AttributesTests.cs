// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class AttributesTests
{
    [Test]
    public async Task Exposes_Assembly_Title() =>
        await Assert.That(GeneratedInfo.Attributes.Title).IsEqualTo("Rocket.Surgery.AssemblyInfo.Tests");
}
