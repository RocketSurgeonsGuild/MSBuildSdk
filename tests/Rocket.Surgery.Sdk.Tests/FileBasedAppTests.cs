namespace Rocket.Surgery.Sdk.Tests;

public class FileBasedAppTests
{
    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task FileBasedApp_BuildsAndRuns_WithSdkDirective(string targetFramework)
    {
        using var project = new SdkTestProject();
        await project.AddFileBasedApp("script/app.cs", targetFramework, """
                                  #:sdk Rocket.Surgery.Sdk

                                  Console.WriteLine($"file-based:{1 + 2}");
                                  """);
        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task FileBasedApp_VirtualProject_ResolvesSdkAndCompilesEntryPoint(string targetFramework)
    {
        using var project = new SdkTestProject();
        await project.AddFileBasedApp("app.cs", targetFramework, $"""
                                           #:sdk Rocket.Surgery.Sdk.Script
                                           #:property RocketSurgeryEditorConfig=false

                                           Console.WriteLine("hello");
                                           """);

        await project.VerifyProjects();
    }
}
