namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// End-to-end checks for AssemblyInfo.Dependencies with the packed Rocket.Surgery.AssemblyInfo package: producing
/// projects opt in with AssemblyInfoMarker, the application with AssemblyInfoDependencies, and the application reads
/// every marked assembly it references, transitive ones included, through typed members.
/// </summary>
public class AssemblyInfoDependenciesTests
{
    // "" is the compiler of the SDK under test. The others replace it with that Roslyn release's compiler
    // (Microsoft.Net.Compilers.Toolset), which makes the package hand the build its matching analyzers/dotnet/roslynX.Y
    // build of the generators: the three older builds cannot be exercised any other way than by running a compiler of their version.
    private static readonly string[] Compilers = ["", "5.0.0", "5.3.0", "5.6.0",];

    public static IEnumerable<(string TargetFramework, string Roslyn)> RoslynVersions() =>
        from targetFramework in TargetFrameworks.All()
        from roslyn in Compilers
        select (targetFramework, roslyn);

    // Acme.Alpha and Acme.Beta are referenced by the application, Acme.Gamma only by Acme.Beta (transitive),
    // Acme.Plain uses AssemblyInfo without the marker, and Acme.Bare does not use AssemblyInfo at all.
    private const string Program = """
        static string Names() => string.Join(",", typeof(AssemblyInfo.Dependencies).GetNestedTypes().Select(type => type.Name).Order());

        System.Console.WriteLine(Names());
        System.Console.WriteLine($"{AssemblyInfo.Dependencies.Acme_Alpha.Attributes.Version}|{AssemblyInfo.Dependencies.Acme_Alpha.Git.Branch}|{AssemblyInfo.Dependencies.Acme_Alpha.Git.Commit}|{AssemblyInfo.Dependencies.Acme_Alpha.Metadata.Flavor}|{AssemblyInfo.Dependencies.Acme_Alpha.Constants.Count}");
        System.Console.WriteLine($"{AssemblyInfo.Dependencies.Acme_Beta.Attributes.Version}|{AssemblyInfo.Dependencies.Acme_Beta.Git.Branch}|{AssemblyInfo.Dependencies.Acme_Beta.Metadata.Flavor}");
        System.Console.WriteLine($"{AssemblyInfo.Dependencies.Acme_Gamma.Attributes.Version}|{AssemblyInfo.Dependencies.Acme_Gamma.Git.Branch}|{AssemblyInfo.Dependencies.Acme_Gamma.Metadata.Flavor}");
        System.Console.WriteLine(AssemblyInfo.Dependencies.Acme_Alpha.Constants.Multi.Replace("\r", "<CR>").Replace("\n", "<LF>"));
        System.Console.Write($"{AssemblyInfo.Project.AssemblyName}|{AssemblyInfo.Git.Branch}");
        """;

    [Test]
    [MethodDataSource(nameof(RoslynVersions))]
    public async Task Application_Reads_Marked_References_Including_Transitive_Ones(string targetFramework, string roslyn)
    {
        using var project = Scaffold(targetFramework, dependencies: "true", roslyn: roslyn);

        var output = await project.DotnetRun("app");

        // Every project built with the generator build that belongs to the compiler it ran on.
        foreach (var name in new[] { "app", "alpha", "beta", "gamma" })
        {
            var report = await File.ReadAllTextAsync(Path.Combine(project.Directory, name, "analyzer.txt"));
            // The SDK under test carries Roslyn 5.9. That the package gives a toolset's build of the generators to its compiler
            // is also what shows the toolset's compiler, not the SDK's, is the one that compiled.
            var folder = roslyn == "" ? "roslyn5.9" : $"roslyn{Version.Parse(roslyn).Major}.{Version.Parse(roslyn).Minor}";
            await Assert.That(report).Contains($"analyzers/dotnet/{folder}/cs/Rocket.Surgery.AssemblyInfo.dll");
        }

        await Assert.That(output.ReplaceLineEndings("\n")).IsEqualTo(
            """
            Acme_Alpha,Acme_Beta,Acme_Gamma
            1.2.3.0|release/1.x|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|Spicy|7
            2.0.0.0|release/2.x|Hot
            3.0.0.0|main|Mild
            first<LF>second
            app|app-branch
            """.ReplaceLineEndings("\n")
        );
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Public_Producers_Work_Too(string targetFramework)
    {
        // The public mode emits properties, not constants, and the marker does not care.
        using var project = Scaffold(targetFramework, dependencies: "true", producerVisibility: "public");

        var output = await project.DotnetRun("app");

        await Assert.That(output.ReplaceLineEndings("\n")).StartsWith("Acme_Alpha,Acme_Beta,Acme_Gamma\n1.2.3.0|release/1.x|");
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Without_The_Property_Nothing_Is_Generated(string targetFramework)
    {
        using var project = Scaffold(targetFramework, dependencies: "false");
        // Marked references are all there, but the application did not ask for them.
        project.AddFile("app/Program.cs", """
            System.Console.Write(typeof(AssemblyInfo).GetNestedType("Dependencies") is null);
            """);

        var output = await project.DotnetRun("app");

        await Assert.That(output).IsEqualTo("True");
    }

    // ---- scaffolding -------------------------------------------------------------------------------------------

    private static SdkTestProject Scaffold(string targetFramework, string dependencies, string roslyn = "", string producerVisibility = "")
    {
        var project = AlignMSBuildWithDotnet(new SdkTestProject());
        // Written next to every project's output, to tell which build of the generators the compiler was given.
        project.AddFile(
            "Directory.Build.targets",
            """
            <Project>
              <Target Name="ReportAssemblyInfoAnalyzer" AfterTargets="CoreCompile">
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/analyzer.txt"
                  Lines="@(Analyzer->WithMetadataValue('NuGetPackageId', 'Rocket.Surgery.AssemblyInfo')->'%(FullPath)')"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """
        );
        if (roslyn != "")
        {
            project.AddFile(
                "Directory.Build.props",
                $"""
                 <Project>
                   <ItemGroup>
                     <PackageReference Include="Microsoft.Net.Compilers.Toolset" Version="{roslyn}" PrivateAssets="all" />
                   </ItemGroup>
                 </Project>
                 """
            );
        }

        project.AddFile(
                    "alpha/Acme.Alpha.csproj",
                    Library(
                        project,
                        targetFramework,
                        visibility: producerVisibility, marker: true,
                        version: "1.2.3",
                        branch: "release/1.x",
                        flavor: "Spicy",
                        commit: new string('a', 40),
                        items: """
                            <Constant Include="Count" Value="7" Type="int" />
                            <Constant Include="Multi" Value="first&#10;second" />
                            """
                    )
                )
               .AddFile("alpha/Class.cs", "public class Alpha { }")
               .AddFile(
                    "beta/Acme.Beta.csproj",
                    Library(
                        project,
                        targetFramework,
                        visibility: producerVisibility, marker: true,
                        version: "2.0.0",
                        branch: "release/2.x",
                        flavor: "Hot",
                        commit: new string('b', 40),
                        references: ["../gamma/Acme.Gamma.csproj"]
                    )
                )
               .AddFile("beta/Class.cs", "public class Beta { }")
               .AddFile(
                    "gamma/Acme.Gamma.csproj",
                    Library(project, targetFramework, visibility: producerVisibility, marker: true, version: "3.0.0", branch: "main", flavor: "Mild", commit: new string('c', 40))
                )
               .AddFile("gamma/Class.cs", "public class Gamma { }")
               .AddFile(
                    "plain/Acme.Plain.csproj",
                    Library(project, targetFramework, visibility: producerVisibility, marker: false, version: "4.0.0", branch: "main", flavor: "Never", commit: new string('d', 40))
                )
               .AddFile("plain/Class.cs", "public class Plain { }")
               .AddFile(
                    "bare/Acme.Bare.csproj",
                    $"""
                     <Project Sdk="Rocket.Surgery.Sdk">
                       <PropertyGroup>
                         <TargetFramework>{targetFramework}</TargetFramework>
                         <IsSampleProject>true</IsSampleProject>
                       </PropertyGroup>
                     </Project>
                     """
                )
               .AddFile("bare/Class.cs", "public class Bare { }")
               .AddFile(
                    "app/app.csproj",
                    $$"""
                      <Project Sdk="Rocket.Surgery.Sdk">
                        <PropertyGroup>
                          <TargetFramework>{{targetFramework}}</TargetFramework>
                          <OutputType>Exe</OutputType>
                          <IsSampleProject>true</IsSampleProject>
                          <AssemblyInfoDependencies>{{dependencies}}</AssemblyInfoDependencies>
                          <EnableSourceControlManagerQueries>true</EnableSourceControlManagerQueries>
                          <RepositoryBranch>app-branch</RepositoryBranch>
                        </PropertyGroup>
                        <ItemGroup>
                          <PackageReference Include="Rocket.Surgery.AssemblyInfo" Version="{{project.PackageVersion("Rocket.Surgery.AssemblyInfo")}}" PrivateAssets="all" />
                          <ProjectReference Include="../alpha/Acme.Alpha.csproj" />
                          <ProjectReference Include="../beta/Acme.Beta.csproj" />
                          <ProjectReference Include="../plain/Acme.Plain.csproj" />
                          <ProjectReference Include="../bare/Acme.Bare.csproj" />
                        </ItemGroup>
                      </Project>
                      """
                )
               .AddFile("app/Program.cs", Program);
        return project;
    }

    // These builds have several projects, so MSBuild starts worker nodes, and a node launched from a different SDK than the
    // parent's fails without saying why. The test host points MSBuild at the SDK that global.json names (MSBuildLocator),
    // while the scaffold's own global.json lets `dotnet` pick a newer one when several are installed. Point both at the
    // SDK `dotnet` resolves; where only one is installed this changes nothing.
    private static SdkTestProject AlignMSBuildWithDotnet(SdkTestProject project)
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var sdk = dotnetRoot is null ? null : Path.Combine(dotnetRoot, "sdk", Config.DotnetSdkVersion);
        if (sdk is null || !Directory.Exists(sdk))
        {
            return project;
        }

        return project
              .WithEnvironment("MSBUILD_EXE_PATH", Path.Combine(sdk, "MSBuild.dll"))
              .WithEnvironment("MSBuildExtensionsPath", sdk)
              .WithEnvironment("MSBuildSDKsPath", Path.Combine(sdk, "Sdks"));
    }

    // A class library that uses AssemblyInfo; with marker it publishes what the application reads.
    // Git values are set by hand because the scaffolded directory is not a repository.
    private static string Library(
        SdkTestProject project,
        string targetFramework,
        string visibility,
        bool marker,
        string version,
        string branch,
        string flavor,
        string commit,
        string items = "",
        string[]? references = null
    )
    {
        var markerProperty = marker ? "<AssemblyInfoMarker>true</AssemblyInfoMarker>" : "";
        var visibilityProperty = visibility == "" ? "" : "<AssemblyInfoVisibility>" + visibility + "</AssemblyInfoVisibility>";
        var projectReferences = string.Join(Environment.NewLine, ( references ?? [] ).Select(reference => "<ProjectReference Include=\"" + reference + "\" />"));
        var sha = commit[..9];
        return $$"""
          <Project Sdk="Rocket.Surgery.Sdk">
            <PropertyGroup>
              <TargetFramework>{{targetFramework}}</TargetFramework>
              <IsSampleProject>true</IsSampleProject>
              <Version>{{version}}</Version>
              <AssemblyVersion>{{version}}.0</AssemblyVersion>
              <FileVersion>{{version}}.0</FileVersion>
              <InformationalVersion>{{version}}</InformationalVersion>
              <EnableSourceControlManagerQueries>true</EnableSourceControlManagerQueries>
              <RepositoryBranch>{{branch}}</RepositoryBranch>
              <RepositoryCommit>{{commit}}</RepositoryCommit>
              <RepositorySha>{{sha}}</RepositorySha>
              {{markerProperty}}
              {{visibilityProperty}}
            </PropertyGroup>
            <ItemGroup>
              <PackageReference Include="Rocket.Surgery.AssemblyInfo" Version="{{project.PackageVersion("Rocket.Surgery.AssemblyInfo")}}" PrivateAssets="all" />
              <AssemblyMetadata Include="Flavor" Value="{{flavor}}" />
              {{items}}
              {{projectReferences}}
            </ItemGroup>
          </Project>
          """;
    }
}
