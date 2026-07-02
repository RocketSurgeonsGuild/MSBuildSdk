namespace Rocket.Surgery.Sdk.Tests;

public class FeatureTests
{
    [Test]
    public async Task CentralPackageManagement_Consumer_RestoresAndBuilds()
    {
        using var project = new SdkTestProject();
        project.WriteFile("Directory.Packages.props", """
            <Project>
                <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                </PropertyGroup>
                <ItemGroup>
                    <PackageVersion Include="Polyfill" Version="10.11.2" />
                </ItemGroup>
            </Project>
            """);
        project.WriteFile("lib/lib.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="Polyfill" />
                </ItemGroup>
            </Project>
            """);
        project.WriteFile("lib/Class1.cs", """
            namespace Scratch;

            /// <summary>A class.</summary>
            public static class Class1;
            """);

        // SDK-injected analyzers carry their own versions (IsImplicitlyDefined), which must
        // coexist with CPM without NU1008/NU1010 errors — Compile's dotnet build catches those.
        await project.Compile("lib/lib.csproj");
    }

    [Test]
    public async Task TestProjects_GetDumpAndMinimumExpectedTestArguments()
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

        var evaluated = await project.Compile("tests/tests.csproj");

        var arguments = evaluated.GetPropertyValue("TestingPlatformCommandLineArguments");
        await Assert.That(arguments).Contains("--crashdump");
        await Assert.That(arguments).Contains("--hangdump-timeout 10m");
        await Assert.That(arguments).Contains("--report-trx");
        // Zero-test failure is opt-in: RocketSurgeryMinimumExpectedTests defaults to 0.
        await Assert.That(arguments).DoesNotContain("--minimum-expected-tests");
        // Coverage collection is CI-scoped by default.
        await Assert.That(arguments).DoesNotContain("--coverage");

        var packages = evaluated.GetItems("PackageReference").Select(i => i.EvaluatedInclude).ToList();
        await Assert.That(packages).Contains("Microsoft.Testing.Extensions.HotReload");
        // Flaky-test retry is opt-in via RocketSurgeryTestingRetry + RocketSurgeryTestRetryCount.
        await Assert.That(packages).DoesNotContain("Microsoft.Testing.Extensions.Retry");

        var ci = project.Evaluate("tests/tests.csproj", new Dictionary<string, string>
        {
            ["ContinuousIntegrationBuild"] = "true",
        });
        await Assert.That(ci.GetPropertyValue("TestingPlatformCommandLineArguments")).Contains("--coverage");
    }

    [Test]
    public async Task PackageValidation_IsEnabled_WhenBaselineIsSet()
    {
        using var project = new SdkTestProject();
        project.WriteFile("lib/lib.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>
                </PropertyGroup>
            </Project>
            """);

        // Evaluation-only: enabling validation makes restore fetch the (nonexistent) baseline package.
        var evaluated = project.Evaluate("lib/lib.csproj");
        await project.VerifySnapshot(evaluated);

        await Assert.That(evaluated.GetPropertyValue("EnablePackageValidation")).IsEqualTo("true");
        await Assert.That(evaluated.GetPropertyValue("PackageValidationBaselineVersion")).IsEqualTo("1.0.0");
    }
}
