namespace Rocket.Surgery.Sdk.Tests;

public class FileBasedAppTests
{
    [Test]
    public async Task FileBasedApp_BuildsAndRuns_WithSdkDirective()
    {
        using var project = new SdkTestProject();
        await project.AddFileBasedApp("script/app.cs", """
                                  #:sdk Rocket.Surgery.Sdk

                                  Console.WriteLine($"file-based:{1 + 2}");
                                  """);
        await project.VerifyProjects();
    }

    [Test]
    public async Task FileBasedApp_VirtualProject_ResolvesSdkAndCompilesEntryPoint()
    {
        using var project = new SdkTestProject();
        await project.AddFileBasedApp("app.cs", $"""
                                           #:sdk Rocket.Surgery.Sdk.Script
                                           #:property RocketSurgeryEditorConfig=false

                                           Console.WriteLine("hello");
                                           """);

        await project.VerifyProjects();
    }
}
