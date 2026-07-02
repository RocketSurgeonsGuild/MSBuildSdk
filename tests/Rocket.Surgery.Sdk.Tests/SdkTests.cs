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

        var properties = await project.Compile("lib/lib.csproj");
        var packages = properties.GetItems("PackageReference");
        await Assert.That(packages).Contains(z => z.EvaluatedInclude == "Roslynator.Analyzers");
        await Assert.That(packages).Contains(z => z.EvaluatedInclude == "Roslynator.Formatting.Analyzers");
        await Assert.That(packages).Contains(z => z.EvaluatedInclude == "Microsoft.CodeAnalysis.BannedApiAnalyzers");
        await Assert.That(packages).Contains(z => z.EvaluatedInclude == "Microsoft.CodeAnalysis.Analyzers");

        // The shipped global analyzer configs must not clash with the .NET SDK's own configs.
        await Assert.That(project.LastBuild!.Value.Output).DoesNotContain("MultipleGlobalAnalyzerKeys");
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

        var properties = await project.Compile("lib/lib.csproj");
        var packages = properties.GetItems("PackageReference");
        await Assert.That(packages).DoesNotContain(z => z.EvaluatedInclude == "Roslynator.Analyzers");
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

        var result = await project.Compile("tests/tests.csproj");
        var packages = result.GetItems("PackageReference");
        await Assert.That(result.GetProperty("IsTestProject").EvaluatedValue).IsEqualTo("true");
        await Assert.That(result.GetProperty("IsPackable").EvaluatedValue).IsEqualTo("false");
        await Assert.That(result.GetProperty("GenerateDocumentationFile").EvaluatedValue).IsEqualTo("false");
        await Assert.That(result.GetProperty("TestingPlatformDotnetTestSupport").EvaluatedValue).IsEqualTo("true");
        await Assert.That(result.GetProperty("RunSettingsFilePath").EvaluatedValue).EndsWith("coverage.runsettings");

        await Assert.That(packages).Contains(c => c.EvaluatedInclude == "Rocket.Surgery.Extensions.Testing.TUnit");
        await Assert.That(packages).Contains(c => c.EvaluatedInclude == "Microsoft.Testing.Extensions.CrashDump");
        await Assert.That(packages).Contains(c => c.EvaluatedInclude == "Microsoft.Testing.Extensions.HangDump");
        await Assert.That(packages).Contains(c => c.EvaluatedInclude == "Microsoft.Testing.Extensions.TrxReport");
        // Coverage collection (and its package) is CI-scoped by default.
        await Assert.That(packages).DoesNotContain(c => c.EvaluatedInclude == "Microsoft.Testing.Extensions.CodeCoverage");

        var ci = project.Evaluate("tests/tests.csproj", new Dictionary<string, string>
        {
            ["ContinuousIntegrationBuild"] = "true",
        });
        await Assert.That(ci.GetItems("PackageReference"))
            .Contains(c => c.EvaluatedInclude == "Microsoft.Testing.Extensions.CodeCoverage");
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
    public async Task TestSdk_WithXunitV3_InjectsXUnit3Adapter()
    {
        // Adapter injection lives in Testing.targets, which only imports for test projects —
        // the Test SDK marks IsTestProject during the props phase.
        using var project = new SdkTestProject();
        project.WriteFile("tests/tests.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk.Test">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="xunit.v3" Version="$(XunitV3Version)" />
                </ItemGroup>
            </Project>
            """);

        var result = await project.Compile("tests/tests.csproj");
        var packages = result.GetItems("PackageReference");
        await Assert.That(packages).Contains(c => c.EvaluatedInclude == "Rocket.Surgery.Extensions.Testing.XUnit3");
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

        // Evaluation-only: actually restoring 2.2.2 would be a legitimate NU1605 downgrade
        // against the version TUnit references; this test only proves the override flows through.
        var result = project.Evaluate("tests/tests.csproj");
        var packages = result.GetItems("PackageReference");
        await Assert.That(packages).Contains(c => c.EvaluatedInclude == "Microsoft.Testing.Extensions.CrashDump" && c.Metadata.Any(m => m.Name == "Version" && m.EvaluatedValue == "2.2.2"));
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


        var result = await project.Compile("web/web.csproj");
        var properties = result.Properties.ToDictionary(p => p.Name, p => p.EvaluatedValue);
        await Assert.That(properties["UsingMicrosoftNETSdkWeb"]).IsEqualTo("true");
        await Assert.That(properties["RocketSurgerySdkName"]).IsEqualTo("Rocket.Surgery.Sdk.Web");
        await Assert.That(properties["LangVersion"]).IsEqualTo("preview");
    }

    [Test]
    public async Task SampleProfile_RelaxesPublicApiTracking()
    {
        using var project = new SdkTestProject();
        project.WriteFile("sample/sample.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsSampleProject>true</IsSampleProject>
                </PropertyGroup>
            </Project>
            """);

        var result = await project.Compile("sample/sample.csproj");
        var properties = result.Properties.ToDictionary(p => p.Name, p => p.EvaluatedValue);
        await Assert.That(properties["IsPackable"]).IsEqualTo("false");
        await Assert.That(properties["GenerateDocumentationFile"]).IsNotEqualTo("true");
        await Assert.That(properties["WarningsAsErrors"]).DoesNotContain("RS0017");
    }
}
