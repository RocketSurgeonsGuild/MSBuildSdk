namespace Rocket.Surgery.Sdk.Tests;

public class FeatureTests
{
    [Test]
    public async Task ZeroContentProject_GetsDefaultTargetFramework_AndBuilds()
    {
        using var project = new SdkTestProject();
        project.WriteFile("lib/lib.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
            </Project>
            """);
        project.WriteFile("lib/Class1.cs", """
            namespace Scratch;

            /// <summary>A class.</summary>
            public static class Class1
            {
                /// <summary>Echo.</summary>
                public static int Echo(int value) => value;
            }
            """);

        var evaluated = await project.Compile("lib/lib.csproj");

        await Assert.That(evaluated.GetPropertyValue("TargetFramework")).IsEqualTo("net10.0");
    }

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
    public async Task PackageMetadata_IsDiscovered_FromReadmeAndLicense()
    {
        using var project = new SdkTestProject();
        project.WriteFile("LICENSE", "license text");
        project.WriteFile("lib/lib.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Version>1.2.3</Version>
                    <Description>Scaffolded metadata test package.</Description>
                </PropertyGroup>
            </Project>
            """);
        project.WriteFile("lib/README.md", "# scaffolded package");
        project.WriteFile("lib/Class1.cs", """
            namespace Scratch;

            /// <summary>A class.</summary>
            public static class Class1;
            """);

        var evaluated = await project.Compile("lib/lib.csproj");

        await Assert.That(evaluated.GetPropertyValue("PackageReadmeFile")).IsEqualTo("README.md");
        await Assert.That(evaluated.GetPropertyValue("PackageLicenseFile")).IsEqualTo("LICENSE");

        var pack = await project.Dotnet("pack lib -v q --nologo");
        pack.ShouldHaveSucceeded();
    }

    [Test]
    public async Task RocketSurgeryProjects_GetBrandingDefaults()
    {
        using var project = new SdkTestProject();
        project.WriteFile("lib/Rocket.Surgery.Widget.csproj", """
            <Project Sdk="Rocket.Surgery.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
            </Project>
            """);

        var evaluated = await project.Compile("lib/Rocket.Surgery.Widget.csproj");

        await Assert.That(evaluated.GetPropertyValue("Company")).IsEqualTo("Rocket Surgeons Guild");
        await Assert.That(evaluated.GetPropertyValue("Authors")).Contains("Rocket Surgeons Guild");
        await Assert.That(evaluated.GetPropertyValue("PackageProjectUrl")).IsEqualTo("https://rocketsurgeonsguild.github.io/");
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

        var packages = evaluated.GetItems("PackageReference").Select(i => i.EvaluatedInclude).ToList();
        await Assert.That(packages).Contains("Microsoft.Testing.Extensions.Retry");
        await Assert.That(packages).Contains("Microsoft.Testing.Extensions.HotReload");

        // Coverage collection is CI-scoped by default.
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
                    <RocketSurgeryPackageValidationBaseline>1.0.0</RocketSurgeryPackageValidationBaseline>
                </PropertyGroup>
            </Project>
            """);

        var evaluated = await project.Compile("lib/lib.csproj");

        await Assert.That(evaluated.GetPropertyValue("EnablePackageValidation")).IsEqualTo("true");
        await Assert.That(evaluated.GetPropertyValue("PackageValidationBaselineVersion")).IsEqualTo("1.0.0");
    }
}
