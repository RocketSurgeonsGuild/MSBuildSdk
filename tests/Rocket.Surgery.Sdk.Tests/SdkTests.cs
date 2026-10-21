using Microsoft.Build.Utilities.ProjectCreation;
using Task = System.Threading.Tasks.Task;

namespace Rocket.Surgery.Sdk.Tests;

public class SdkTests
{
    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task BaseSdk_AppliesSharedDefaults_AndInjectsAnalyzers(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("lib/lib.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework)
        );

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task BaseSdk_ConsumerValues_Win(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("lib/lib.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework)
                .Property("LangVersion", "12")
                .Property("Nullable", "disable")
                .Property("RocketSurgeryAnalyzers", "false")
            );

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task TestSdk_WithTUnit_InjectsAdapterAndTestingPlatformExtensions(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("tests/tests.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Test", targetFramework: targetFramework)
                .ItemPackageReference("TUnit", Config.TUnitVersion));

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task TestSdk_WithTUnit_RunsTests_AndProducesCoverageAndTrx(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("tests/tests.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Test", targetFramework: targetFramework)
                   .Property("RsgSdk_Testing_CodeCoverage", "true")
                   .ItemPackageReference("TUnit", Config.TUnitVersion)
            )
            .AddFile("tests/MathTests.cs", """
             namespace Scaffolded.Tests;

             public class MathTests
             {
                 [Test]
                 public async Task Adds() => await Assert.That(1 + 2).IsEqualTo(3);
             }
             """);

        await project.DotnetTest("tests");

        var resultsDirectory = Path.Combine(project.Directory, "tests", "TestResults");
        var coverage = Directory.GetFiles(resultsDirectory, "*.cobertura.xml", SearchOption.AllDirectories);
        var trx = Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories);
        await Assert.That(coverage).IsNotEmpty();
        await Assert.That(trx).IsNotEmpty();

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task WebSdk_ChainsToMicrosoftNetSdkWeb(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("web/web.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Web", targetFramework: targetFramework));
        project.AddFile("web/Program.cs", """
                                             var builder = WebApplication.CreateBuilder(args);
                                             var app = builder.Build();
                                             app.MapGet("/", () => "ok");
                                             app.Run();
                                             """);

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task SampleProfile_RelaxesPublicApiTracking(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("sample/sample.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework)
               .Property("IsSampleProject", "true"));

        await project.VerifyProjects();
    }
}
