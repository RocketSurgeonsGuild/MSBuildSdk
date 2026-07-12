using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Cysharp.Diagnostics;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Logging.StructuredLogger;
using Microsoft.Build.Utilities.ProjectCreation;
using TUnit.Core.Exceptions;
using Task = System.Threading.Tasks.Task;

namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// Scaffolds a throwaway consumer project wired to the packed SDKs in the repo's
/// <c>artifacts/</c> directory. Evaluation-level assertions run in-proc via the MSBuild
/// runtime and are snapshotted with Verify; end-to-end behavior (restore/build/run/test/pack)
/// goes through <see cref="Dotnet"/>.
/// </summary>
public sealed class SdkTestProject : IDisposable
{
    private readonly List<ProjectCreator> _projects = new();
    private readonly VerifySettings _settings;

    public string NugetArtifactsDirectory { get; }
    public string Directory { get; }

    public SdkTestProject(string? nugetArtifactsDirectory = null)
    {
        NugetArtifactsDirectory = nugetArtifactsDirectory ?? Config.NugetArtifactsDirectory;

        Directory = Path.Combine(Path.GetTempPath(), "rsg-sdk-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        var repository = PackageRepository.Create(Directory, feeds: [new("https://api.nuget.org/v3/index.json"), new(NugetArtifactsDirectory)]);
        foreach (var package in System.IO.Directory.EnumerateFiles(NugetArtifactsDirectory, "*.nupkg"))
        {
            repository.Package(new(package), out _);
        }

        _settings = new VerifySettings();
        _settings.ScrubLinesWithReplace(z => z.Replace(Directory, "{project-root}"));

        var currentGlobalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(Config.RootDirectory, "global.json")));
        var version = currentGlobalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();

        var globalJson = GlobalJsonCreator.Create(new DirectoryInfo(Directory));
        foreach (var sdkName in repository.Packages.Where(z => z.Id.StartsWith("Rocket.Surgery.Sdk", StringComparison.OrdinalIgnoreCase)))
        {
            globalJson.MSBuildSdk(sdkName.Id, sdkName.Version);
            _settings.ScrubLinesWithReplace(z => z.Replace(sdkName.Version, "{sdk-version}"));
        }

        globalJson
            .TestRunner("Microsoft.Testing.Platform")
            .SdkVersion(version)
            .SdkRollForward(GlobalJsonSdkRollForward.LatestMajor)
            .Save();
    }

    public SdkTestProject AddProject(string path, ProjectCreator project)
    {
        project.Save(Path.Combine(Directory, path));
        if (project.FullPath.EndsWith(".csproj"))
        {
            _projects.Add(project);
        }
        return this;
    }

    public SdkTestProject AddFile(string path, string content)
    {
        var fullPath = Path.Combine(Directory, path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return this;
    }

    public async Task VerifyProjects()
    {
        var results = new List<ProjectEvaluation>();
        foreach (var project in _projects)
        {
            var relativePath = Path.GetDirectoryName(project.FullPath)!;

            // for some reason... this works... I really don't understand why.
            // Is it a local machine issue? or just an issual overall??

            var (process, output, error) = ProcessX.GetDualAsyncEnumerable($"dotnet build -bl", workingDirectory: relativePath);
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var errorOutput = (await error.ToListAsync()).Concat(await output.ToListAsync());
                throw new TUnitException($"dotnet build failed for {relativePath}:\n{string.Join("\n", errorOutput)}");
            }

            var binlog = Path.Combine(relativePath, "msbuild.binlog");
            var build = BinaryLog.ReadBuild(binlog);
            BuildAnalyzer.AnalyzeBuild(build);
            var projectEvaluation = build.FindChildrenRecursive<ProjectEvaluation>().First();
            results.Add(projectEvaluation);
        }

        await Verify(results, settings: _settings);
    }

    public async Task DotnetTest(string projectPath)
    {
        await ProcessX.StartAsync($"dotnet test -bl", workingDirectory: Path.Combine(Directory, projectPath)).ToTask();
    }
    //
    // private IReadOnlyList<(string Token, string Replacement)> CreateScrubbers(Project project)
    // {
    //     var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    //     var scrubbers = new List<(string, string)>
    //     {
    //         // macOS: Path.GetTempPath() paths surface both with and without the /private prefix,
    //         // and MSBuildProjectDirectoryNoRoot drops the leading path separator.
    //         ("/private" + Directory, "{project-root}"),
    //         (Directory, "{project-root}"),
    //         ("private" + Directory, "{project-root}"),
    //         (Directory.TrimStart('/'), "{project-root}"),
    //         (Path.Combine(home, ".nuget", "packages"), "{nuget}"),
    //     };
    //
    //     // Order matters: full dotnet root before the bare version number it contains.
    //     scrubbers.Add((Environment.CurrentDirectory, "{cwd}"));
    //     scrubbers.Add((home, "{home}"));
    //     scrubbers.Add((Directory, "{solution_root}"));
    //     return scrubbers;
    // }

    // public async Task<DotnetResult> Dotnet(string arguments, CancellationToken cancellationToken = default)
    // {
    //     var psi = CreateDotnetStartInfo(arguments);
    //
    //     var output = new StringBuilder();
    //     using var process = Process.Start(psi)!;
    //     var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
    //     var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
    //     await process.WaitForExitAsync(cancellationToken);
    //     output.Append(await stdout).Append(await stderr);
    //     return new DotnetResult(process.ExitCode, output.ToString());
    // }

    // /// <summary>
    // /// Evaluates a project in-proc with the MSBuild runtime (no build, no restore).
    // /// Each evaluation gets its own <see cref="ProjectCollection"/> so parallel tests
    // /// never contend on shared MSBuild state.
    // /// </summary>
    // public Project Evaluate(string projectPath, IDictionary<string, string>? properties = null)
    // {
    //     return Project.FromFile(Path.Combine(Directory, projectPath), CreateProjectOptions(properties));
    // }
    //
    // /// <summary>Output of the most recent <see cref="Compile"/> build, for asserting on build diagnostics.</summary>
    // public DotnetResult? LastBuild { get; private set; }
    //
    // /// <summary>
    // /// Evaluates the project in-proc, snapshots its normalized surface with Verify, and then
    // /// builds it out-of-proc with <c>dotnet build</c> (restore included — the in-proc
    // /// <c>Project.Build()</c> cannot restore and <c>BuildManager</c> is single-flight,
    // /// which breaks under parallel tests). Build output lands in <see cref="LastBuild"/>.
    // /// </summary>
    // public async Task<Project> Compile(string projectPath, IDictionary<string, string>? properties = null)
    // {
    //     var project = Evaluate(projectPath, properties);
    //     await VerifySnapshot(project);
    //
    //     var flags = properties is null
    //         ? ""
    //         : " " + string.Join(' ', properties.Select(p => $"-p:{p.Key}={p.Value}"));
    //     var build = await Dotnet($"build {projectPath} -v q --nologo{flags}");
    //     LastBuild = build;
    //     build.ShouldHaveSucceeded();
    //     return project;
    // }

    /// <summary>
    /// Evaluates a file-based app (<c>dotnet run app.cs</c>) in-proc with the MSBuild runtime.
    /// MSBuild has no notion of <c>#:</c> directives, so the CLI is asked (via its
    /// <c>dotnet run-api</c> IDE contract) for the virtual project it would build, and that XML
    /// is evaluated exactly like a project on disk. Contract:
    /// dotnet/sdk src/Cli/dotnet/Commands/Run/Api/RunApiCommand.cs (release/10.0.3xx).
    /// </summary>
    public async Task<SdkTestProject> AddFileBasedApp(string entryPointRelativePath, string fileContent, CancellationToken cancellationToken = default)
    {
        AddFile(entryPointRelativePath, fileContent);
        var entryPointPath = Path.Combine(Directory, entryPointRelativePath);
        var (content, projectPath) = await GetVirtualProject(entryPointPath, cancellationToken);
        using var reader = XmlReader.Create(new StringReader(content));
        var root = ProjectRootElement.Create(reader);
        root.Properties.Single(z => z.Name == "TargetFramework").Value = "net10.0";
        var ctro = typeof(ProjectCreator).GetTypeInfo().DeclaredConstructors.Single(z => z.GetParameters().Length == 2);
        var project = (ProjectCreator)ctro.Invoke([root, null]);
        // var project = (ProjectCreator)Activator.CreateInstance(typeof(ProjectCreator), BindingFlags.NonPublic | BindingFlags.CreateInstance, null, [root, null], null)!;
        project.Save(projectPath);
        _projects.Add(project);
        return this;
    }
    //
    // /// <summary>
    // /// Verifies the evaluated properties and package references. Machine-specific fragments
    // /// (scaffold directory, NuGet cache, dotnet install root and version, home directory,
    // /// runfile hash directories, the SDK package version) are replaced with stable tokens so
    // /// snapshots are deterministic across runs and machines.
    // /// </summary>
    // public async Task VerifySnapshot(Project project)
    // {
    //     var scrubbers = CreateScrubbers(project);
    //
    //     await Verify(new
    //     {
    //         Properties = project.Properties
    //             .Where(p => Config.AllPropertyNames.Contains(p.Name))
    //             .ToDictionary(p => p.Name, p => Scrub(p.EvaluatedValue, scrubbers)),
    //         PackageReferences = project
    //             .GetItems("PackageReference")
    //             .ToDictionary(
    //                 i => i.EvaluatedInclude,
    //                 i => i.Metadata.Where(z => z.Name != "Version").ToDictionary(m => m.Name, m => Scrub(m.EvaluatedValue, scrubbers))),
    //     });
    // }
    //
    /// <summary>Asks <c>dotnet run-api</c> for the virtual project XML of a file-based app.</summary>
    private async Task<(string Content, string ProjectPath)> GetVirtualProject(
        string entryPointPath, CancellationToken cancellationToken)
    {
        var psi = CreateDotnetStartInfo("run-api");
        psi.RedirectStandardInput = true;
        psi.WorkingDirectory = Directory;

        var (process, output, errors) = ProcessX.GetDualAsyncEnumerable(psi);
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["$type"] = "GetProject",
            ["EntryPointFileFullPath"] = entryPointPath,
        }));
        process.StandardInput.Close();

        var responseLine = await output.FirstAsync(cancellationToken);
        using var response = JsonDocument.Parse(responseLine);
        var type = response.RootElement.GetProperty("$type").GetString();
        if (type != "Project")
        {
            throw new TUnitException($"dotnet run-api returned '{type}' for {entryPointPath}:\n{responseLine}");
        }

        // Version is the run-api contract version; a bump means the response shape changed
        // (see RunApiOutput in dotnet/sdk) and this helper needs to be revisited.
        var version = response.RootElement.GetProperty("Version").GetInt32();
        if (version != 1)
        {
            throw new TUnitException($"dotnet run-api contract version changed ({version}); expected 1.");
        }

        var diagnostics = response.RootElement.GetProperty("Diagnostics");
        if (diagnostics.GetArrayLength() > 0)
        {
            throw new TUnitException($"Invalid #: directives in {entryPointPath}:\n{diagnostics}");
        }

        return (
            response.RootElement.GetProperty("Content").GetString()!,
            response.RootElement.GetProperty("ProjectPath").GetString()!);
    }
    //
    // private static Microsoft.Build.Definition.ProjectOptions CreateProjectOptions(IDictionary<string, string>? properties) => new()
    // {
    //     GlobalProperties = properties,
    //     ProjectCollection = new ProjectCollection(),
    //     EvaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Isolated),
    // };
    //
    //
    // private static readonly Regex RunfilePathRegex = new(@"runfile[/\\][^/\\""';\s]+", RegexOptions.Compiled);
    //
    // private static string Scrub(string value, IReadOnlyList<(string Token, string Replacement)> scrubbers)
    // {
    //     if (value.Length == 0)
    //     {
    //         return value;
    //     }
    //
    //     foreach (var (token, replacement) in scrubbers)
    //     {
    //         value = value.Replace(token, replacement);
    //     }
    //
    //     return RunfilePathRegex.Replace(value, "runfile/{app}");
    // }
    //
    private ProcessStartInfo CreateDotnetStartInfo(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Do not let the test host's MSBuild/test environment leak into the scaffolded build.
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("TESTINGPLATFORM_UI_LANGUAGE");
        // Keep scaffolded builds hermetic: CI env vars would flip ContinuousIntegrationBuild
        // (warnings-as-errors, coverage) and make results differ between local and CI runs.
        psi.Environment.Remove("GITHUB_ACTIONS");
        psi.Environment.Remove("CI");
        psi.Environment.Remove("TF_BUILD");
        psi.Environment.Remove("GITLAB_CI");
        psi.Environment.Remove("APPVEYOR");
        psi.Environment.Remove("TEAMCITY_VERSION");
        return psi;
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup; temp directory reaping will handle stragglers.
        }
    }
}

public readonly record struct DotnetResult(int ExitCode, string Output)
{
    public void ShouldHaveSucceeded()
    {
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet exited with {ExitCode}:\n{Output}");
        }
    }
}
