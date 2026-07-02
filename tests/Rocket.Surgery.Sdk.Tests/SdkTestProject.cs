using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Build.Locator;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using TUnit.Core.Exceptions;

namespace Rocket.Surgery.Sdk.Tests;

class Init
{
    [Before(HookType.Assembly)]
    public static void Setup()
    {
        MSBuildLocator.RegisterDefaults();
    }
}

/// <summary>
/// Scaffolds a throwaway consumer project wired to the packed SDKs in the repo's
/// <c>artifacts/</c> directory. Evaluation-level assertions run in-proc via the MSBuild
/// runtime and are snapshotted with Verify; end-to-end behavior (restore/build/run/test/pack)
/// goes through <see cref="Dotnet"/>.
/// </summary>
public sealed class SdkTestProject : IDisposable
{
    public static readonly string ArtifactsDirectory =
        Environment.GetEnvironmentVariable("RSG_SDK_ARTIFACTS") ?? FindArtifactsDirectory();

    public static readonly string PackageVersion =
        Environment.GetEnvironmentVariable("RSG_SDK_VERSION") ?? "0.0.1-local";

    private static readonly string[] SdkNames =
    [
        "Rocket.Surgery.Sdk",
        "Rocket.Surgery.Sdk.Web",
        "Rocket.Surgery.Sdk.Razor",
        "Rocket.Surgery.Sdk.BlazorWebAssembly",
        "Rocket.Surgery.Sdk.Script",
        "Rocket.Surgery.Sdk.Test",
        "Rocket.Surgery.Sdk.WindowsDesktop",
    ];

    public string Directory { get; }

    public SdkTestProject()
    {
        Directory = Path.Combine(Path.GetTempPath(), "rsg-sdk-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        File.WriteAllText(Path.Combine(Directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
                <packageSources>
                    <clear />
                    <add key="local" value="{ArtifactsDirectory}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                </packageSources>
            </configuration>
            """);

        var sdkEntries = string.Join(",\n        ", SdkNames.Select(name => $"\"{name}\": \"{PackageVersion}\""));
        File.WriteAllText(Path.Combine(Directory, "global.json"), $$"""
            {
                "test": { "runner": "Microsoft.Testing.Platform" },
                "msbuild-sdks": {
                    {{sdkEntries}}
                }
            }
            """);
    }

    public void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(Directory, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    public async Task<DotnetResult> Dotnet(string arguments, CancellationToken cancellationToken = default)
    {
        var psi = CreateDotnetStartInfo(arguments);

        var output = new StringBuilder();
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        output.Append(await stdout).Append(await stderr);
        return new DotnetResult(process.ExitCode, output.ToString());
    }

    /// <summary>
    /// Evaluates a project in-proc with the MSBuild runtime (no build, no restore).
    /// Each evaluation gets its own <see cref="ProjectCollection"/> so parallel tests
    /// never contend on shared MSBuild state.
    /// </summary>
    public Project Evaluate(string projectPath, IDictionary<string, string>? properties = null)
    {
        return Project.FromFile(Path.Combine(Directory, projectPath), CreateProjectOptions(properties));
    }

    /// <summary>Output of the most recent <see cref="Compile"/> build, for asserting on build diagnostics.</summary>
    public DotnetResult? LastBuild { get; private set; }

    /// <summary>
    /// Evaluates the project in-proc, snapshots its normalized surface with Verify, and then
    /// builds it out-of-proc with <c>dotnet build</c> (restore included — the in-proc
    /// <c>Project.Build()</c> cannot restore and <c>BuildManager</c> is single-flight,
    /// which breaks under parallel tests). Build output lands in <see cref="LastBuild"/>.
    /// </summary>
    public async Task<Project> Compile(string projectPath, IDictionary<string, string>? properties = null)
    {
        var project = Evaluate(projectPath, properties);
        await VerifySnapshot(project);

        var flags = properties is null
            ? ""
            : " " + string.Join(' ', properties.Select(p => $"-p:{p.Key}={p.Value}"));
        var build = await Dotnet($"build {projectPath} -v q --nologo{flags}");
        LastBuild = build;
        build.ShouldHaveSucceeded();
        return project;
    }

    /// <summary>
    /// Evaluates a file-based app (<c>dotnet run app.cs</c>) in-proc with the MSBuild runtime.
    /// MSBuild has no notion of <c>#:</c> directives, so the CLI is asked (via its
    /// <c>dotnet run-api</c> IDE contract) for the virtual project it would build, and that XML
    /// is evaluated exactly like a project on disk. Contract:
    /// dotnet/sdk src/Cli/dotnet/Commands/Run/Api/RunApiCommand.cs (release/10.0.3xx).
    /// </summary>
    public async Task<Project> EvaluateFileBasedApp(
        string entryPointRelativePath,
        IDictionary<string, string>? properties = null,
        CancellationToken cancellationToken = default)
    {
        var entryPointPath = Path.Combine(Directory, entryPointRelativePath);
        var (content, projectPath) = await GetVirtualProject(entryPointPath, cancellationToken);

        using var reader = XmlReader.Create(new StringReader(content));
        var root = ProjectRootElement.Create(reader);
        // FullPath anchors SDK resolution (global.json msbuild-sdks), nuget.config, and
        // Directory.Build.props lookup to the scaffolded directory — same as the CLI's
        // in-memory evaluation, which uses <entry-point>.csproj as the virtual path.
        root.FullPath = projectPath;

        return Project.FromProjectRootElement(root, CreateProjectOptions(properties));
    }

    /// <summary>
    /// Verifies the evaluated properties and package references. Machine-specific fragments
    /// (scaffold directory, NuGet cache, dotnet install root and version, home directory,
    /// runfile hash directories, the SDK package version) are replaced with stable tokens so
    /// snapshots are deterministic across runs and machines.
    /// </summary>
    public async Task VerifySnapshot(Project project)
    {
        var scrubbers = CreateScrubbers(project);

        await Verify(new
        {
            Properties = project.Properties
                .Where(p => !p.IsEnvironmentProperty)
                .ToDictionary(p => p.Name, p => Scrub(p.EvaluatedValue, scrubbers)),
            PackageReferences = project.GetItems("PackageReference")
                .ToDictionary(
                    i => i.EvaluatedInclude,
                    i => i.Metadata.ToDictionary(m => m.Name, m => Scrub(m.EvaluatedValue, scrubbers))),
        });
    }

    /// <summary>Asks <c>dotnet run-api</c> for the virtual project XML of a file-based app.</summary>
    private async Task<(string Content, string ProjectPath)> GetVirtualProject(
        string entryPointPath, CancellationToken cancellationToken)
    {
        var psi = CreateDotnetStartInfo("run-api");
        psi.RedirectStandardInput = true;

        using var process = Process.Start(psi)!;
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["$type"] = "GetProject",
            ["EntryPointFileFullPath"] = entryPointPath,
        }));
        process.StandardInput.Close();

        var responseLine = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (responseLine is null)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new TUnitException($"dotnet run-api produced no response for {entryPointPath}:\n{stderr}");
        }

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

    private static Microsoft.Build.Definition.ProjectOptions CreateProjectOptions(IDictionary<string, string>? properties) => new()
    {
        GlobalProperties = properties,
        ProjectCollection = new ProjectCollection(),
        EvaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Isolated),
    };

    private IReadOnlyList<(string Token, string Replacement)> CreateScrubbers(Project project)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var scrubbers = new List<(string, string)>
        {
            // macOS: Path.GetTempPath() paths surface both with and without the /private prefix,
            // and MSBuildProjectDirectoryNoRoot drops the leading path separator.
            ("/private" + Directory, "{project-root}"),
            (Directory, "{project-root}"),
            ("private" + Directory, "{project-root}"),
            (Directory.TrimStart('/'), "{project-root}"),
            (ArtifactsDirectory, "{artifacts}"),
            (Path.Combine(home, ".nuget", "packages"), "{nuget}"),
        };

        // Order matters: full dotnet root before the bare version number it contains.
        AddPropertyToken("NetCoreRoot", "{dotnet-root}");
        AddPropertyToken("NETCoreSdkVersion", "{dotnet-sdk-version}");
        AddPropertyToken("NETCoreSdkRuntimeIdentifier", "{rid}");
        AddPropertyToken("NETCoreSdkPortableRuntimeIdentifier", "{rid}");
        scrubbers.Add((PackageVersion, "{sdk-version}"));
        scrubbers.Add((Environment.CurrentDirectory, "{cwd}"));
        scrubbers.Add((home, "{home}"));
        return scrubbers;

        void AddPropertyToken(string name, string token)
        {
            var value = project.GetPropertyValue(name).TrimEnd('/', '\\');
            if (value.Length > 3)
            {
                scrubbers.Add((value, token));
            }
        }
    }

    private static readonly Regex RunfilePathRegex = new(@"runfile[/\\][^/\\""';\s]+", RegexOptions.Compiled);

    private static string Scrub(string value, IReadOnlyList<(string Token, string Replacement)> scrubbers)
    {
        if (value.Length == 0)
        {
            return value;
        }

        foreach (var (token, replacement) in scrubbers)
        {
            value = value.Replace(token, replacement);
        }

        return RunfilePathRegex.Replace(value, "runfile/{app}");
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

    private static string FindArtifactsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "artifacts");
            if (System.IO.Directory.Exists(candidate)
                && System.IO.Directory.EnumerateFiles(candidate, "Rocket.Surgery.Sdk.*.nupkg").Any())
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the artifacts directory containing the packed SDKs. "
            + "Run ./build.sh (or dotnet pack src/*.csproj -o artifacts) first, or set RSG_SDK_ARTIFACTS.");
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
