using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using Cysharp.Diagnostics;
using Microsoft.Build.Construction;
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
    private readonly List<ProjectCreator> _projects = [];
    private readonly VerifySettings _settings;

    public string NugetArtifactsDirectory { get; }
    public string Directory { get; }

    public SdkTestProject(string? nugetArtifactsDirectory = null)
    {
        NugetArtifactsDirectory = nugetArtifactsDirectory ?? Config.NugetArtifactsDirectory;

        var id = Guid.NewGuid().ToString("N");
        Directory = Path.Combine(Path.GetTempPath(), "sdk-tests", id);
        System.IO.Directory.CreateDirectory(Directory);

        var repository = PackageRepository.Create(Directory, feeds: [new("https://api.nuget.org/v3/index.json"), new(NugetArtifactsDirectory)]);
        foreach (var package in System.IO.Directory.EnumerateFiles(NugetArtifactsDirectory, "*.nupkg"))
        {
            repository.Package(new(package), out _);
        }
        _settings = new VerifySettings();
        // MSBuild resolves the entry project's own full path via the process's current directory,
        // which macOS reports through the /var,/tmp -> /private/var,/private/tmp symlink. Verify's
        // built-in TempPath scrubber only matches the unresolved Path.GetTempPath() form, so every
        // other (import-chain-derived) path gets normalized to {TempPath} but this one doesn't.
        // Scrub both forms - and the id - in a single pass, since separate ScrubLinesWithReplace
        // registrations each run against the original line rather than chaining.
        var tempPath = Path.GetTempPath();
        _settings.ScrubLinesWithReplace(z => z.Replace("/private" + tempPath, "{TempPath}").Replace(id, "{id}"));

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

            var psi = CreateHermeticStartInfo("build -bl");
            psi.WorkingDirectory = relativePath;
            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                throw new TUnitException($"dotnet build failed for {relativePath}:\n{stdout}\n{stderr}");
            }

            var binlog = Path.Combine(relativePath, "msbuild.binlog");
            var build = BinaryLog.ReadBuild(binlog);
            BuildAnalyzer.AnalyzeBuild(build);
            var projectEvaluation = build.FindChildrenRecursive<ProjectEvaluation>()[0];
            results.Add(projectEvaluation);
        }

        await Verify(results, settings: _settings);
    }

    public async Task DotnetTest(string projectPath)
    {
        var psi = CreateHermeticStartInfo("test -bl");
        psi.WorkingDirectory = Path.Combine(Directory, projectPath);
        await ProcessX.StartAsync(psi).ToTask();
    }

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
        return diagnostics.GetArrayLength() > 0
            ? throw new TUnitException($"Invalid #: directives in {entryPointPath}:\n{diagnostics}")
            : ((string Content, string ProjectPath))(
            response.RootElement.GetProperty("Content").GetString()!,
            response.RootElement.GetProperty("ProjectPath").GetString()!);
    }

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
        return psi;
    }

    /// <summary>
    /// Like <see cref="CreateDotnetStartInfo"/>, but only strips CI-detection env vars so the
    /// scaffolded build's evaluated properties (e.g. <c>ContinuousIntegrationBuild</c>) don't
    /// differ between a local run and a CI runner. Deliberately leaves MSBuildSDKsPath/
    /// TESTINGPLATFORM_UI_LANGUAGE untouched: stripping those breaks apphost/SDK resolution for
    /// real (non-evaluation-only) builds on at least one dev machine.
    /// </summary>
    private ProcessStartInfo CreateHermeticStartInfo(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
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
