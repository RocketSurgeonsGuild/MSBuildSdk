using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// Scaffolds a throwaway consumer project wired to the packed SDKs in the repo's
/// <c>artifacts/</c> directory, and runs <c>dotnet</c> commands against it.
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

        var output = new StringBuilder();
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        output.Append(await stdout).Append(await stderr);
        return new DotnetResult(process.ExitCode, output.ToString());
    }

    /// <summary>Evaluates MSBuild properties in <paramref name="projectDirectory"/> via <c>-getProperty</c>.</summary>
    public async Task<Dictionary<string, string>> GetProperties(string projectDirectory, params string[] properties)
    {
        var result = await Dotnet($"msbuild {Path.Combine(Directory, projectDirectory)} -getProperty:{string.Join(',', properties)}");
        result.ShouldHaveSucceeded();
        using var json = JsonDocument.Parse(result.Output);
        if (properties.Length == 1)
        {
            return new Dictionary<string, string> { [properties[0]] = json.RootElement.GetString() ?? "" };
        }

        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    /// <summary>Returns identity/version pairs of evaluated <c>PackageReference</c> items.</summary>
    public async Task<Dictionary<string, string>> GetPackageReferences(string projectDirectory)
    {
        var result = await Dotnet($"msbuild {Path.Combine(Directory, projectDirectory)} -getItem:PackageReference");
        result.ShouldHaveSucceeded();
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.GetProperty("Items").GetProperty("PackageReference").EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("Identity").GetString()!,
                item => item.TryGetProperty("Version", out var version) ? version.GetString() ?? "" : "");
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
