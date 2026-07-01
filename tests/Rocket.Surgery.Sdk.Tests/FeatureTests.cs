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

        var properties = await project.GetProperties("lib", "TargetFramework");
        await Assert.That(properties["TargetFramework"]).Matches("^net[0-9.]+$");

        var build = await project.Dotnet("build lib -v q --nologo");
        build.ShouldHaveSucceeded();
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
        // coexist with CPM without NU1008/NU1010 errors.
        var build = await project.Dotnet("build lib -v q --nologo");
        build.ShouldHaveSucceeded();
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

        var properties = await project.GetProperties("lib", "PackageReadmeFile", "PackageLicenseFile");
        await Assert.That(properties["PackageReadmeFile"]).IsEqualTo("README.md");
        await Assert.That(properties["PackageLicenseFile"]).IsEqualTo("LICENSE");

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

        var properties = await project.GetProperties("lib", "Company", "Authors", "PackageProjectUrl");
        await Assert.That(properties["Company"]).IsEqualTo("Rocket Surgeons Guild");
        await Assert.That(properties["Authors"]).Contains("Rocket Surgeons Guild");
        await Assert.That(properties["PackageProjectUrl"]).IsEqualTo("https://rocketsurgeonsguild.github.io/");
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

        var properties = await project.GetProperties("tests", "TestingPlatformCommandLineArguments");
        var arguments = properties["TestingPlatformCommandLineArguments"];
        await Assert.That(arguments).Contains("--crashdump");
        await Assert.That(arguments).Contains("--hangdump-timeout 10m");
        await Assert.That(arguments).Contains("--minimum-expected-tests 1");
        await Assert.That(arguments).Contains("--report-trx");
        // Coverage collection is CI-scoped by default.
        await Assert.That(arguments).DoesNotContain("--coverage");

        var ciBuild = await project.Dotnet(
            $"msbuild {Path.Combine(project.Directory, "tests")} -p:ContinuousIntegrationBuild=true -getProperty:TestingPlatformCommandLineArguments");
        ciBuild.ShouldHaveSucceeded();
        await Assert.That(ciBuild.Output).Contains("--coverage");

        var packages = await project.GetPackageReferences("tests");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.Retry");
        await Assert.That(packages.Keys).Contains("Microsoft.Testing.Extensions.HotReload");
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

        var properties = await project.GetProperties("lib", "EnablePackageValidation", "PackageValidationBaselineVersion");
        await Assert.That(properties["EnablePackageValidation"]).IsEqualTo("true");
        await Assert.That(properties["PackageValidationBaselineVersion"]).IsEqualTo("1.0.0");
    }
}
