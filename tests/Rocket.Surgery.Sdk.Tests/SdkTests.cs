namespace Rocket.Surgery.Sdk.Tests;

public class SdkTests
{
    [Test]
    public async Task BaseSdk_AppliesSharedDefaults_AndInjectsAnalyzers()
    {
        using var project = new SdkTestProject();
        project.WriteFile("lib/lib.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
            </Project>
            """);
        project.WriteFile("lib/Class1.cs", """
            namespace Scratch;

            /// <summary>A class.</summary>
            public class Class1
            {
                /// <summary>Echo.</summary>
                public int Echo(int value) => value;
            }
            """);

        var properties = await project.GetProperties(
            "lib", "LangVersion", "Features", "Nullable", "ImplicitUsings", "AnalysisMode",
            "AnalysisLevel", "ProduceReferenceAssembly", "NuGetAudit", "NuGetAuditMode",
            "NuGetAuditLevel", "GenerateDocumentationFile", "WarningsAsErrors");

        await Assert.That(properties["LangVersion"]).IsEqualTo("preview");
        await Assert.That(properties["Features"]).IsEqualTo("strict");
        await Assert.That(properties["Nullable"]).IsEqualTo("enable");
        await Assert.That(properties["ImplicitUsings"]).IsEqualTo("enable");
        await Assert.That(properties["AnalysisMode"]).IsEqualTo("AllEnabledByDefault");
        await Assert.That(properties["AnalysisLevel"]).IsEqualTo("latest");
        await Assert.That(properties["ProduceReferenceAssembly"]).IsEqualTo("true");
        await Assert.That(properties["NuGetAudit"]).IsEqualTo("true");
        await Assert.That(properties["NuGetAuditMode"]).IsEqualTo("all");
        await Assert.That(properties["NuGetAuditLevel"]).IsEqualTo("moderate");
        await Assert.That(properties["GenerateDocumentationFile"]).IsEqualTo("true");
        await Assert.That(properties["WarningsAsErrors"]).Contains("RS0017");

        var packages = await project.GetPackageReferences("lib");
        await Assert.That(packages.Keys).Contains("Roslynator.Analyzers");
        await Assert.That(packages.Keys).Contains("Roslynator.Formatting.Analyzers");
        await Assert.That(packages.Keys).Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers");
        await Assert.That(packages.Keys).Contains("Microsoft.CodeAnalysis.Analyzers");

        var build = await project.Dotnet("build lib -v q --nologo");
        build.ShouldHaveSucceeded();
        // The shipped global analyzer configs must not clash with the .NET SDK's own configs.
        await Assert.That(build.Output).DoesNotContain("MultipleGlobalAnalyzerKeys");
    }

    [Test]
    public async Task BaseSdk_ConsumerValues_Win()
    {
        using var project = new SdkTestProject();
        project.WriteFile("lib/lib.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <LangVersion>12</LangVersion>
                    <Nullable>disable</Nullable>
                    <RocketSurgeryAnalyzers>false</RocketSurgeryAnalyzers>
                </PropertyGroup>
            </Project>
            """);

        var properties = await project.GetProperties("lib", "LangVersion", "Nullable");
        await Assert.That(properties["LangVersion"]).IsEqualTo("12");
        await Assert.That(properties["Nullable"]).IsEqualTo("disable");

        var packages = await project.GetPackageReferences("lib");
        await Assert.That(packages.Keys).DoesNotContain("Roslynator.Analyzers");
    }

    [Test]
    public async Task TestSdk_WithTUnit_InjectsAdapterAndTestingPlatformExtensions()
    {
        using var project = new SdkTestProject();
        project.WriteFile("tests/tests.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk.Test">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="TUnit" Version="$(TUnitVersion)" />
                </ItemGroup>
            </Project>
            """);

        var properties = await project.GetProperties(
            "tests", "IsTestProject", "IsPackable", "GenerateDocumentationFile",
            "TestingPlatformDotnetTestSupport", "RunSettingsFilePath");
        await Assert.That(properties["IsTestProject"]).IsEqualTo("true");
        await Assert.That(properties["IsPackable"]).IsEqualTo("false");
        await Assert.That(properties["GenerateDocumentationFile"]).IsEqualTo("false");
        await Assert.That(properties["TestingPlatformDotnetTestSupport"]).IsEqualTo("true");
        await Assert.That(properties["RunSettingsFilePath"]).EndsWith("coverage.runsettings");

        var packages = await project.GetPackageReferences("tests");
        await Assert.That(packages.Keys).Contains("Rocket.Surgery.Extensions.Testing.TUnit");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.CrashDump");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.HangDump");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.TrxReport");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.CodeCoverage");
    }

    [Test]
    public async Task TestSdk_WithTUnit_RunsTests_AndProducesCoverageAndTrx()
    {
        using var project = new SdkTestProject();
        project.WriteFile("tests/tests.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk.Test">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <!-- Coverage collection is CI-only by default; force it for this assertion. -->
                    <RocketSurgeryCodeCoverage>true</RocketSurgeryCodeCoverage>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="TUnit" Version="$(TUnitVersion)" />
                </ItemGroup>
            </Project>
            """);
        project.WriteFile("tests/MathTests.cs", """
            namespace Scaffolded.Tests;

            public class MathTests
            {
                [Test]
                public async Task Adds() => await Assert.That(1 + 2).IsEqualTo(3);
            }
            """);

        var test = await project.Dotnet("test tests");
        test.ShouldHaveSucceeded();

        var resultsDirectory = Path.Combine(project.Directory, "tests", "bin", "Debug", "net10.0", "TestResults");
        var coverage = Directory.GetFiles(resultsDirectory, "*.cobertura.xml", SearchOption.AllDirectories);
        var trx = Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories);
        await Assert.That(coverage).IsNotEmpty();
        await Assert.That(trx).IsNotEmpty();
    }

    [Test]
    public async Task BaseSdk_WithXunitV3_InjectsXUnit3Adapter()
    {
        using var project = new SdkTestProject();
        project.WriteFile("tests/tests.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="xunit.v3" Version="$(XunitV3Version)" />
                </ItemGroup>
            </Project>
            """);

        var packages = await project.GetPackageReferences("tests");
        await Assert.That(packages.Keys).Contains("Rocket.Surgery.Extensions.Testing.XUnit3");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.CodeCoverage");
    }

    [Test]
    public async Task VersionProperties_CanBeOverriddenByConsumer()
    {
        using var project = new SdkTestProject();
        project.WriteFile("tests/tests.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk.Test">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <MicrosoftTestingExtensionsVersion>2.2.2</MicrosoftTestingExtensionsVersion>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="TUnit" Version="$(TUnitVersion)" />
                </ItemGroup>
            </Project>
            """);

        var packages = await project.GetPackageReferences("tests");
        await Assert.That(packages["Microsoft.Testing.Extensions.CrashDump"]).IsEqualTo("2.2.2");
    }

    [Test]
    public async Task WebSdk_ChainsToMicrosoftNetSdkWeb()
    {
        using var project = new SdkTestProject();
        project.WriteFile("web/web.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk.Web">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
            </Project>
            """);
        project.WriteFile("web/Program.cs", """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => "ok");
            app.Run();
            """);

        var properties = await project.GetProperties("web", "UsingMicrosoftNETSdkWeb", "RocketSurgerySdkName", "LangVersion");
        await Assert.That(properties["UsingMicrosoftNETSdkWeb"]).IsEqualTo("true");
        await Assert.That(properties["RocketSurgerySdkName"]).IsEqualTo("Rocket.Surgery.Sdk.Web");
        await Assert.That(properties["LangVersion"]).IsEqualTo("preview");

        var build = await project.Dotnet("build web -v q --nologo");
        build.ShouldHaveSucceeded();
    }

    [Test]
    public async Task SampleProfile_RelaxesPublicApiTracking()
    {
        using var project = new SdkTestProject();
        project.WriteFile("sample/sample.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RocketSurgerySampleProject>true</RocketSurgerySampleProject>
                </PropertyGroup>
            </Project>
            """);

        var properties = await project.GetProperties("sample", "IsPackable", "GenerateDocumentationFile", "WarningsAsErrors");
        await Assert.That(properties["IsPackable"]).IsEqualTo("false");
        await Assert.That(properties["GenerateDocumentationFile"]).IsNotEqualTo("true");
        await Assert.That(properties["WarningsAsErrors"]).DoesNotContain("RS0017");
    }
}
