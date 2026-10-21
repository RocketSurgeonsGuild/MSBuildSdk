using Microsoft.Build.Utilities.ProjectCreation;

namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// Rocket.Surgery.AssemblyInfo is opt-in: an SDK consumer adds a PackageReference to the package
/// packed from this repo and gets the generated AssemblyInfo class.
/// </summary>
public class AssemblyInfoTests
{
    [Test]
    public async Task PackageReference_GeneratesAssemblyInfo()
    {
        using var project = new SdkTestProject();
        project.AddProject("app/app.csproj", CreateApp(project))
               .AddFile("app/Program.cs", """
                   System.Console.Write($"{AssemblyInfo.Constants.Hello}|{AssemblyInfo.Attributes.Title}|{AssemblyInfo.Project.AssemblyName}");
                   """);

        var output = await project.DotnetRun("app");

        await Assert.That(output).IsEqualTo("World|app|app");
    }

    [Test]
    public async Task Legacy_MyAssembly_Properties_StillApply()
    {
        // Rocket.Surgery.AssemblyInfo was Rocket.Surgery.MyAssembly; MyAssemblyNamespace keeps working.
        using var project = new SdkTestProject();
        project.AddProject("app/app.csproj", CreateApp(project).Property("MyAssemblyNamespace", "Legacy"))
               .AddFile("app/Program.cs", """
                   System.Console.Write(Legacy.AssemblyInfo.Constants.Hello);
                   """);

        var output = await project.DotnetRun("app");

        await Assert.That(output).IsEqualTo("World");
    }

    [Test]
    public async Task FileBasedApp_PackageDirective_GeneratesAssemblyInfo()
    {
        // A file-based app's project is virtual (no .csproj on disk); reading the project file must not fail.
        using var project = new SdkTestProject();
        project.AddFile("app.cs", $"""
            #:sdk Rocket.Surgery.Sdk
            #:package Rocket.Surgery.AssemblyInfo@{project.PackageVersion("Rocket.Surgery.AssemblyInfo")}

            System.Console.Write(AssemblyInfo.Project.AssemblyName);
            """);

        var output = await project.DotnetRunFile("app.cs");

        await Assert.That(output).IsEqualTo("app");
    }

    private static ProjectCreator CreateApp(SdkTestProject project) =>
        ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: "net10.0")
                      .Property("OutputType", "Exe")
                      .Property("IsSampleProject", "true")
                      .ItemPackageReference("Rocket.Surgery.AssemblyInfo", project.PackageVersion("Rocket.Surgery.AssemblyInfo"), privateAssets: "all")
                      .ItemInclude("Constant", "Hello", metadata: new Dictionary<string, string?> { ["Value"] = "World" });
}
