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
                var errorOutput = ( await error.ToListAsync() ).Concat(await output.ToListAsync());
                throw new TUnitException($"dotnet build failed for {relativePath}:\n{string.Join("\n", errorOutput)}");
            }

            var binlog = Path.Combine(relativePath, "msbuild.binlog");
            var build = BinaryLog.ReadBuild(binlog);
            BuildAnalyzer.AnalyzeBuild(build);
            var projectEvaluation = build.FindChildrenRecursive<ProjectEvaluation>()[0];
            results.Add(projectEvaluation);
        }

        await Verify(results, settings: _settings);
    }

    public async Task DotnetTest(string projectPath) => await ProcessX.StartAsync($"dotnet test -bl", workingDirectory: Path.Combine(Directory, projectPath)).ToTask();

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
        return  diagnostics.GetArrayLength() > 0 
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
