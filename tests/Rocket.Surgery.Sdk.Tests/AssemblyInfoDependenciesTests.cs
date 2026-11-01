using System.IO.Compression;

namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// End-to-end checks for <c>AssemblyInfo.Dependencies</c> with the packed Rocket.Surgery.AssemblyInfo package. Three
/// libraries: <c>Packaged</c> (built with AssemblyInfo, consumed as a package), <c>ProjectLib</c> (built with AssemblyInfo,
/// consumed by ProjectReference) and <c>Plain</c> (a package without AssemblyInfo, so only its attributes are known).
/// </summary>
public class AssemblyInfoDependenciesTests
{
    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Dependencies_Expose_Manifests_And_Attributes(string targetFramework)
    {
        using var project = await CreateLibraries(targetFramework);
        project.AddFile("app/app.csproj", App(project, targetFramework))
               .AddFile("app/Program.cs", """
                   using System.Linq;
                   System.Console.WriteLine(string.Join(",", typeof(AssemblyInfo.Dependencies).GetNestedTypes().Select(t => t.Name).OrderBy(n => n, System.StringComparer.Ordinal)));
                   System.Console.WriteLine(AssemblyInfo.Dependencies.Packaged.Constants.Answer);
                   System.Console.WriteLine(AssemblyInfo.Dependencies.Packaged.Metadata.Foo);
                   System.Console.WriteLine(AssemblyInfo.Dependencies.Packaged.Attributes.Version);
                   System.Console.WriteLine(AssemblyInfo.Dependencies.Packaged.Resources.Hello.Text);
                   System.Console.WriteLine(typeof(AssemblyInfo.Dependencies.Packaged.Constants).GetField("Secret") is null ? "no secret" : "secret leaked");
                   System.Console.WriteLine(AssemblyInfo.Dependencies.ProjectLib.Constants.Answer);
                   System.Console.WriteLine(AssemblyInfo.Dependencies.Plain.Attributes.Company);
                   System.Console.WriteLine(AssemblyInfo.Dependencies.Plain.Attributes.Version);
                   """);

        var output = await project.DotnetRun("app");

        await Assert.That(output.Split(Environment.NewLine)).IsEquivalentTo(
            [
                "Packaged,Plain,ProjectLib,Rocket_Surgery_AssemblyInfo",
                "42",
                "Bar",
                "1.0.0.0",
                "hello from a package",
                "no secret",
                "7",
                "Plain Co",
                "3.0.0.0",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );

        // Packed next to the assembly, once per target framework, and never copied into consumers' output.
        using (var package = ZipFile.OpenRead(Path.Combine(project.Directory, "feed", "Packaged.1.0.0.nupkg")))
        {
            await Assert.That(package.Entries.Select(e => e.FullName)).Contains($"lib/{targetFramework}/Packaged.AssemblyInfo.json");
        }

        var appOutput = Path.Combine(project.Directory, "app", "bin", "Debug", targetFramework);
        await Assert.That(Directory.EnumerateFiles(appOutput, "*.AssemblyInfo.json").Select(file => Path.GetFileName(file))).IsEquivalentTo(["app.AssemblyInfo.json"]);
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Dependencies_Can_Be_Chosen(string targetFramework)
    {
        using var project = await CreateLibraries(targetFramework);
        project.AddFile(
                    "app/app.csproj",
                    App(
                        project,
                        targetFramework,
                        // Replaces the Microsoft.*;System.* default, so System.Runtime is back with the framework assemblies.
                        properties: """
                            <AssemblyInfoDependencyIgnore>Plain*,Rocket.*</AssemblyInfoDependencyIgnore>
                            <AssemblyInfoIncludeFrameworkDependencies>true</AssemblyInfoIncludeFrameworkDependencies>
                            """,
                        items: """
                            <AssemblyInfoDependencyIgnore Include="project libraries" Pattern="Project*" />
                            <AssemblyInfoDependency Include="Plain" />
                            """,
                        packagedMetadata: """AssemblyInfo="false" """
                    )
                )
               .AddFile("app/Program.cs", """
                   using System.Linq;
                   System.Console.WriteLine(string.Join(",", typeof(AssemblyInfo.Dependencies).GetNestedTypes().Select(t => t.Name)));
                   """);

        var dependencies = ( await project.DotnetRun("app") ).Trim().Split(',');

        await Assert.That(dependencies).Contains("System_Runtime");
        // Ignored by the property, kept by the include.
        await Assert.That(dependencies).Contains("Plain");
        await Assert.That(dependencies).DoesNotContain("ProjectLib");
        await Assert.That(dependencies).DoesNotContain("Packaged");
        await Assert.That(dependencies).DoesNotContain("Rocket_Surgery_AssemblyInfo");
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Builds_Incrementally(string targetFramework)
    {
        using var project = await CreateLibraries(targetFramework);
        project.AddFile("app/app.csproj", App(project, targetFramework))
               .AddFile("app/Program.cs", "System.Console.WriteLine(AssemblyInfo.Dependencies.ProjectLib.Constants.Answer);");

        // The test's package folder gets the repo's packages with the zip entries' times read as local time, which can be
        // hours ahead; a package file from the future is newer than every output, so nothing would ever be up to date.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(project.Directory, ".nuget"), "*", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) > DateTime.UtcNow)
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));
        }

        await project.DotnetBuild("app");
        var manifest = Path.Combine(project.Directory, "project", "obj", "Debug", targetFramework, "ProjectLib.AssemblyInfo.json");
        var copy = Path.Combine(project.Directory, "project", "bin", "Debug", targetFramework, "ProjectLib.AssemblyInfo.json");
        var written = File.GetLastWriteTimeUtc(manifest);
        var copied = File.GetLastWriteTimeUtc(copy);

        // Nothing changed: neither project recompiles, and the manifest keeps its timestamp.
        var output = await project.DotnetBuild("app", "-v:n");

        await Assert.That(output.Split('\n').Count(line => line.Contains("Skipping target \"CoreCompile\"", StringComparison.Ordinal))).IsEqualTo(2);
        await Assert.That(File.GetLastWriteTimeUtc(manifest)).IsEqualTo(written);
        await Assert.That(File.GetLastWriteTimeUtc(copy)).IsEqualTo(copied);

        // A changed constant reaches the library and, through its manifest, the app.
        var projectFile = Path.Combine(project.Directory, "project", "project.csproj");
        File.WriteAllText(projectFile, File.ReadAllText(projectFile).Replace("Value=\"7\"", "Value=\"8\""));

        await Assert.That(( await project.DotnetRun("app") ).Trim()).IsEqualTo("8");
        await Assert.That(File.GetLastWriteTimeUtc(manifest)).IsNotEqualTo(written);
    }

    private static async Task<SdkTestProject> CreateLibraries(string targetFramework)
    {
        var project = new SdkTestProject();
        var assemblyInfo = project.PackageVersion("Rocket.Surgery.AssemblyInfo");
        project.AddFile("packaged/packaged.csproj", $$"""
                   <Project Sdk="Microsoft.NET.Sdk">
                     <PropertyGroup>
                       <TargetFramework>{{targetFramework}}</TargetFramework>
                       <AssemblyName>Packaged</AssemblyName>
                       <Version>1.0.0</Version>
                       <PackageOutputPath>$(MSBuildThisFileDirectory)../feed</PackageOutputPath>
                     </PropertyGroup>
                     <ItemGroup>
                       <PackageReference Include="Rocket.Surgery.AssemblyInfo" Version="{{assemblyInfo}}" PrivateAssets="all" />
                       <Constant Include="Answer" Value="42" Type="int" Manifest="true" />
                       <Constant Include="Secret" Value="hunter2" />
                       <AssemblyMetadata Include="Foo" Value="Bar" />
                       <EmbeddedResource Include="Hello.txt" />
                     </ItemGroup>
                   </Project>
                   """)
               .AddFile("packaged/Hello.txt", "hello from a package")
               .AddFile("packaged/Class1.cs", "namespace Packaged; public class Class1;")
               .AddFile("plain/plain.csproj", $$"""
                   <Project Sdk="Microsoft.NET.Sdk">
                     <PropertyGroup>
                       <TargetFramework>{{targetFramework}}</TargetFramework>
                       <AssemblyName>Plain</AssemblyName>
                       <Version>3.0.0</Version>
                       <Company>Plain Co</Company>
                       <PackageOutputPath>$(MSBuildThisFileDirectory)../feed</PackageOutputPath>
                     </PropertyGroup>
                   </Project>
                   """)
               .AddFile("plain/Class1.cs", "namespace Plain; public class Class1;")
               .AddFile("project/project.csproj", $$"""
                   <Project Sdk="Microsoft.NET.Sdk">
                     <PropertyGroup>
                       <TargetFramework>{{targetFramework}}</TargetFramework>
                       <AssemblyName>ProjectLib</AssemblyName>
                     </PropertyGroup>
                     <ItemGroup>
                       <PackageReference Include="Rocket.Surgery.AssemblyInfo" Version="{{assemblyInfo}}" PrivateAssets="all" />
                       <Constant Include="Answer" Value="7" Type="int" Manifest="true" />
                     </ItemGroup>
                   </Project>
                   """)
               .AddFile("project/Class1.cs", "namespace ProjectLib; public class Class1;");

        await project.DotnetPack("packaged");
        await project.DotnetPack("plain");
        return project;
    }

    private static string App(SdkTestProject project, string targetFramework, string properties = "", string items = "", string packagedMetadata = "") =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <OutputType>Exe</OutputType>
            <RestoreAdditionalProjectSources>$(MSBuildThisFileDirectory)../feed</RestoreAdditionalProjectSources>
            {{properties}}
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Rocket.Surgery.AssemblyInfo" Version="{{project.PackageVersion("Rocket.Surgery.AssemblyInfo")}}" PrivateAssets="all" />
            <PackageReference Include="Packaged" Version="1.0.0" {{packagedMetadata}}/>
            <PackageReference Include="Plain" Version="3.0.0" />
            <ProjectReference Include="../project/project.csproj" />
            {{items}}
          </ItemGroup>
        </Project>
        """;
}
