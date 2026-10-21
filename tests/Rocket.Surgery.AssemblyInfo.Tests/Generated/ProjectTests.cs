// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class ProjectTests
{
    [Test]
    public async Task Exposes_Project_Property() => await Assert.That(GeneratedInfo.Project.Foo).IsEqualTo("Bar");

    [Test]
    public async Task Exposes_Default_Project_Properties() => await Assert.That(GeneratedInfo.Project.RuntimeIdentifiers).IsNotEmpty();

    [Test]
    public async Task Exposes_Repository_Url() => await Assert.That(GeneratedInfo.Project.RepositoryUrl).IsNotEmpty();

    [Test]
    public async Task Exposes_Full_Project_File_Unescaped()
    {
        await Assert.That(GeneratedInfo.Project.ProjectFile).Contains("<Project");
        await Assert.That(GeneratedInfo.Project.ProjectFile).DoesNotStartWith("|");
    }
}
