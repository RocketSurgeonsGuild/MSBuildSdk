using System.Text.Json;
using Microsoft.Build.Utilities.ProjectCreation;

namespace Rocket.Surgery.Sdk.Tests;

public class FeatureTests
{
    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task CentralPackageManagement_Consumer_RestoresAndBuilds(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject(
            "Directory.Packages.props",
            ProjectCreator.Templates.DirectoryPackagesProps()
            .ItemPackageVersion("Polyfill", "10.11.2")
        ).AddProject(
            "lib/lib.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework)
            .ItemPackageReference("Polyfill")
        ).AddFile("lib/Class1.cs", """
            namespace Scratch;

            /// <summary>A class.</summary>
            public static class Class1;
            """);

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task TestProjects_GetDumpAndMinimumExpectedTestArguments(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("tests/tests.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Test", targetFramework: targetFramework)
                .Property("ContinuousIntegrationBuild", "true")
                .ItemPackageReference("TUnit", Config.TUnitVersion));

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task ContinuousIntegrationBuild_GithubActions(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("tests/tests.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Test", targetFramework: targetFramework)
                .Property("GITHUB_ACTIONS", "true")
                .ItemPackageReference("TUnit", Config.TUnitVersion));

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task ContinuousIntegrationBuild_AzureDevOps(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("tests/tests.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk.Test", targetFramework: targetFramework)
                .Property("TF_BUILD", "true")
                .ItemPackageReference("TUnit", Config.TUnitVersion));

        await project.VerifyProjects();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task GitVersion_EnvironmentVariables_SetVersionProperties(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.WithEnvironment("GitVersion_Major", "1")
               .WithEnvironment("GitVersion_SemVer", "1.2.3-beta.4")
               .WithEnvironment("GitVersion_AssemblySemVer", "1.2.3.0")
               .WithEnvironment("GitVersion_InformationalVersion", "1.2.3-beta.4+Branch.main.Sha.abcdef")
               .AddProject("lib/lib.csproj", ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework));

        using var evaluation = await project.Evaluate(
            "lib/lib.csproj",
            ["Version", "PackageVersion", "AssemblyVersion", "FileVersion", "InformationalVersion", "IncludeSourceRevisionInInformationalVersion"],
            ["AssemblyMetadata"]
        );

        var properties = evaluation.RootElement.GetProperty("Properties");
        await Assert.That(properties.GetProperty("Version").GetString()).IsEqualTo("1.2.3-beta.4");
        await Assert.That(properties.GetProperty("PackageVersion").GetString()).IsEqualTo("1.2.3-beta.4");
        await Assert.That(properties.GetProperty("AssemblyVersion").GetString()).IsEqualTo("1.0.0.0");
        await Assert.That(properties.GetProperty("FileVersion").GetString()).IsEqualTo("1.2.3.0");
        await Assert.That(properties.GetProperty("InformationalVersion").GetString()).IsEqualTo("1.2.3-beta.4+Branch.main.Sha.abcdef");
        // The sha is already in GitVersion's InformationalVersion; the .NET SDK must not append it again.
        await Assert.That(properties.GetProperty("IncludeSourceRevisionInInformationalVersion").GetString()).IsEqualTo("false");

        var metadata = evaluation.RootElement.GetProperty("Items").GetProperty("AssemblyMetadata")
                                 .EnumerateArray()
                                 .ToDictionary(z => z.GetProperty("Identity").GetString()!, z => z.GetProperty("Value").GetString());
        await Assert.That(metadata["GitVersion_SemVer"]).IsEqualTo("1.2.3-beta.4");
        await Assert.That(metadata["GitVersion_Major"]).IsEqualTo("1");
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task GitVersion_WithoutVariables_LeavesSdkDefaults(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("lib/lib.csproj", ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework));

        using var evaluation = await project.Evaluate("lib/lib.csproj", ["Version", "IncludeSourceRevisionInInformationalVersion"], ["AssemblyMetadata"]);

        var properties = evaluation.RootElement.GetProperty("Properties");
        await Assert.That(properties.GetProperty("Version").GetString()).IsEqualTo("1.0.0");
        await Assert.That(properties.GetProperty("IncludeSourceRevisionInInformationalVersion").GetString()).IsEqualTo("true");
        var metadata = evaluation.RootElement.TryGetProperty("Items", out var items) && items.TryGetProperty("AssemblyMetadata", out var values)
            ? values.EnumerateArray().Select(z => z.GetProperty("Identity").GetString()).ToArray()
            : [];
        await Assert.That(metadata.Any(z => z!.StartsWith("GitVersion_", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Pack_ProducesSnupkg(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("lib/lib.csproj", ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework))
               .AddFile("lib/Class1.cs", """
                   namespace Scratch;

                   /// <summary>A class.</summary>
                   public static class Class1;
                   """)
               .AddFile("lib/PublicAPI.Shipped.txt", "")
               .AddFile("lib/PublicAPI.Unshipped.txt", """
                   #nullable enable
                   Scratch.Class1
                   """);

        await project.DotnetPack("lib");

        var output = Path.Combine(project.Directory, "lib", "bin", "Release");
        await Assert.That(Directory.GetFiles(output, "*.snupkg", SearchOption.AllDirectories)).IsNotEmpty();
        await Assert.That(Directory.GetFiles(output, "*.symbols.nupkg", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    public async Task NetStandard20_UsesDependentUponConventionForResources()
    {
        using var project = new SdkTestProject();
        project.AddProject("lib/lib.csproj", ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: "netstandard2.0"));

        using var evaluation = await project.Evaluate("lib/lib.csproj", ["EmbeddedResourceUseDependentUponConvention"]);

        await Assert.That(evaluation.RootElement.GetProperty("Properties").GetProperty("EmbeddedResourceUseDependentUponConvention").GetString()).IsEqualTo("true");
    }

    [Test]
    [Arguments("Rocket.Surgery.Sdk", "net10.0", false)]
    [Arguments("Rocket.Surgery.Sdk", "net11.0", false)]
    [Arguments("Rocket.Surgery.Sdk.WindowsDesktop", "net10.0-windows", true)]
    public async Task EmbedUntrackedSources_ComesFromDotnetSdk(string sdk, string targetFramework, bool useWpf)
    {
        // Rocket.Surgery.Sdk no longer sets EmbedUntrackedSources itself; the .NET SDK (8+) defaults it.
        using var project = new SdkTestProject();
        project.AddProject("app/app.csproj",
            ProjectCreator.Templates.SdkCsproj(sdk: sdk, targetFramework: targetFramework)
                .Property("UseWPF", useWpf ? "true" : "false")
                .Property("EnableWindowsTargeting", "true"));

        using var evaluation = await project.Evaluate("app/app.csproj", ["EmbedUntrackedSources"]);

        await Assert.That(evaluation.RootElement.GetProperty("Properties").GetProperty("EmbedUntrackedSources").GetString()).IsEqualTo("true");
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task JetBrainsAnnotations_AreInjected_AndCanBeDisabled(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddProject("on/on.csproj", ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework))
               .AddProject("off/off.csproj",
                   ProjectCreator.Templates.SdkCsproj(sdk: "Rocket.Surgery.Sdk", targetFramework: targetFramework)
                       .Property("RocketSurgeryJetBrainsAnnotations", "false"));

        using var on = await project.Evaluate("on/on.csproj", [], ["PackageReference", "Using"]);
        using var off = await project.Evaluate("off/off.csproj", [], ["PackageReference", "Using"]);

        await Assert.That(ItemIdentities(on, "PackageReference")).Contains("JetBrains.Annotations.Sources");
        await Assert.That(ItemIdentities(on, "Using")).Contains("JetBrains.Annotations");
        await Assert.That(ItemIdentities(off, "PackageReference")).DoesNotContain("JetBrains.Annotations.Sources");
        await Assert.That(ItemIdentities(off, "Using")).DoesNotContain("JetBrains.Annotations");
    }

    [Test]
    [Arguments("Rocket.Surgery.Sdk", "net11.0", null, false)]
    [Arguments("Rocket.Surgery.Sdk", "net11.0", "true", true)]
    [Arguments("Rocket.Surgery.Sdk", "net10.0", "true", false)]
    [Arguments("Rocket.Surgery.Sdk", "net11.0-browser", "true", false)]
    [Arguments("Rocket.Surgery.Sdk.BlazorWebAssembly", "net11.0", "true", false)]
    public async Task RuntimeAsync_IsOptIn_AndOnlyForNet11NonBrowser(string sdk, string targetFramework, string? runtimeAsync, bool expected)
    {
        using var project = new SdkTestProject();
        var csproj = ProjectCreator.Templates.SdkCsproj(sdk: sdk, targetFramework: targetFramework);
        if (runtimeAsync is not null)
        {
            csproj.Property("RocketSurgeryRuntimeAsync", runtimeAsync);
        }

        project.AddProject("lib/lib.csproj", csproj);

        using var evaluation = await project.Evaluate("lib/lib.csproj", ["Features"]);

        var features = evaluation.RootElement.GetProperty("Properties").GetProperty("Features").GetString()!.Split(';');
        await Assert.That(features.Contains("runtime-async=on")).IsEqualTo(expected);
    }

    [Test]
    public async Task RuntimeAsync_OptIn_BuildsAndRuns_AcrossTargetFrameworks()
    {
        // A net10.0 build with the flag fails (SYSLIB5007/CA2252), so this proves the gate keeps it off there.
        using var project = new SdkTestProject();
        project.AddProject("app/app.csproj",
                   ProjectCreator.Templates.SdkCsproj(targetFrameworks: TargetFrameworks.All(), sdk: "Rocket.Surgery.Sdk")
                                 .Property("OutputType", "Exe")
                                 .Property("IsSampleProject", "true")
                                 .Property("RocketSurgeryRuntimeAsync", "true"))
               .AddFile("app/Program.cs", """
                   await System.Threading.Tasks.Task.Yield();
                   System.Console.Write("ok");
                   """);

        foreach (var targetFramework in TargetFrameworks.All())
        {
            await Assert.That(await project.DotnetRun("app", targetFramework)).IsEqualTo("ok");
        }
    }

    private static string[] ItemIdentities(JsonDocument evaluation, string itemType) =>
        evaluation.RootElement.TryGetProperty("Items", out var items) && items.TryGetProperty(itemType, out var values)
            ? [.. values.EnumerateArray().Select(z => z.GetProperty("Identity").GetString()!)]
            : [];
}
