using Microsoft.Build.Utilities.ProjectCreation;

namespace Rocket.Surgery.Sdk.Tests;

public class FeatureTests
{
    [Test]
    public async Task CentralPackageManagement_Consumer_RestoresAndBuilds()
    {
        using var project = new SdkTestProject();
        project.AddProject(
            "Directory.Packages.props",
            ProjectCreator.Templates.DirectoryPackagesProps()
            .ItemPackageVersion("Polyfill", "10.11.2")
        ).AddProject(
            "lib/lib.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: "net10.0")
            .ItemPackageReference("Polyfill")
        ).AddFile("lib/Class1.cs", """
            namespace Scratch;

            /// <summary>A class.</summary>
            public static class Class1;
            """);

        await project.VerifyProjects();
    }

    [Test]
    public async Task TestProjects_GetDumpAndMinimumExpectedTestArguments()
    {
        using var project = new SdkTestProject();
        project.AddProject("tests/tests.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Test", targetFramework: "net10.0")
                .Property("ContinuousIntegrationBuild", "true")
                .ItemPackageReference("TUnit", "$(TUnitVersion)"));

        await project.VerifyProjects();
    }
}
