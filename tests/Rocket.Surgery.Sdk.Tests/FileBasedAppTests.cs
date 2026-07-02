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

        var evaluated = await project.EvaluateFileBasedApp("app.cs");
        await project.VerifySnapshot(evaluated);

        await Assert.That(evaluated.GetPropertyValue("RocketSurgerySingleFileApp")).IsEqualTo("true");
        await Assert.That(evaluated.GetPropertyValue("GenerateDocumentationFile")).IsEqualTo("false");
        await Assert.That(evaluated.GetPropertyValue("LangVersion")).IsEqualTo("preview");
    }

    [Test]
    public async Task FileBasedApp_VirtualProject_ResolvesSdkAndCompilesEntryPoint()
    {
        using var project = new SdkTestProject();
        project.WriteFile("app.cs", $"""
            #:sdk Rocket.Surgery.Sdk@{SdkTestProject.PackageVersion}
            #:property Foo=Bar

            Console.WriteLine("hello");
            """);

        var evaluated = await project.EvaluateFileBasedApp("app.cs");

        // The CLI-generated virtual project marks file-based apps and carries #:property values.
        await Assert.That(evaluated.GetPropertyValue("FileBasedProgram")).IsEqualTo("true");
        await Assert.That(evaluated.GetPropertyValue("Foo")).IsEqualTo("Bar");
        // Our SDK's identity property proves Sdk.props/Sdk.targets were imported.
        await Assert.That(evaluated.GetPropertyValue("RocketSurgerySdkName")).IsEqualTo("Rocket.Surgery.Sdk");

        var compileItems = evaluated.GetItems("Compile").Select(i => i.EvaluatedInclude).ToList();
        await Assert.That(compileItems).Contains(Path.Combine(project.Directory, "app.cs"));
    }
}
