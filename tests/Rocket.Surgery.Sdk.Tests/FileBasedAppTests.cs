namespace Rocket.Surgery.Sdk.Tests;

public class FileBasedAppTests
{
    [Test]
    public async Task FileBasedApp_BuildsAndRuns_WithSdkDirective()
    {
        using var project = new SdkTestProject();
        project.WriteFile("app.cs", $$"""
            #:sdk Rocket.Surgery.Sdk@{{SdkTestProject.PackageVersion}}

            Console.WriteLine($"file-based:{1 + 2}");
            """);

        var run = await project.Dotnet("run app.cs");
        run.ShouldHaveSucceeded();
        await Assert.That(run.Output).Contains("file-based:3");
    }

    [Test]
    public async Task FileBasedApp_IsDetected_AndGetsRelaxedProfile()
    {
        using var project = new SdkTestProject();
        project.WriteFile("app.cs", $"""
            #:sdk Rocket.Surgery.Sdk@{SdkTestProject.PackageVersion}

            Console.WriteLine("hello");
            """);

        var build = await project.Dotnet(
            "build app.cs -getProperty:RocketSurgerySingleFileApp,GenerateDocumentationFile,LangVersion");
        build.ShouldHaveSucceeded();
        await Assert.That(build.Output).Contains("\"RocketSurgerySingleFileApp\": \"true\"");
        await Assert.That(build.Output).Contains("\"GenerateDocumentationFile\": \"false\"");
        await Assert.That(build.Output).Contains("\"LangVersion\": \"preview\"");
    }
}
