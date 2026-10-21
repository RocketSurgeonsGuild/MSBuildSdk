// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using TUnit.Core.Logging;
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class GitTests
{
    [Test]
    public async Task Exposes_Commit()
    {
        await Assert.That(GeneratedInfo.Git.Commit).IsNotEmpty();
        await Assert.That(GeneratedInfo.Git.Sha).IsEqualTo(GeneratedInfo.Git.Commit[..9]);
    }

    [Test]
    public async Task Exposes_Branch()
    {
        await Assert.That(GeneratedInfo.Git.Branch).IsNotEmpty();
        await TestContext.Current!.GetDefaultLogger().LogInformationAsync(GeneratedInfo.Git.Branch);
    }

    [Test]
    public async Task Exposes_Repository_Root() => await Assert.That(Directory.Exists(GeneratedInfo.Git.Root)).IsTrue();
}
