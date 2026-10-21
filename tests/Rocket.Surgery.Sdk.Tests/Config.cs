using System.Collections.Frozen;
using System.Reflection;
using System.Xml.Linq;
using DiffEngine;
using Microsoft.Build.Logging.StructuredLogger;
using Microsoft.Build.Utilities.ProjectCreation;
using Task = System.Threading.Tasks.Task;

namespace Rocket.Surgery.Sdk.Tests;

internal static class Config
{
    [Before(HookType.Assembly)]
    public static async Task Setup(AssemblyHookContext context)
    {
        MSBuildAssemblyResolver.Register();
        VerifierSettings.InitializePlugins();

        DiffRunner.Disabled = true;
        VerifierSettings.AddExtraSettings(z => z.Converters.Add(new ProjectEvaluationSerializer()));

        // global.json doesn't pin the SDK (mise does), so scaffolded projects pin whichever SDK the repo resolves.
        var (exitCode, version, error) = await ProcessRunner.RunAndCaptureAsync(new("dotnet", "--version")
        {
            WorkingDirectory = RootDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        DotnetSdkVersion = exitCode == 0 ? version.Trim() : throw new InvalidOperationException($"dotnet --version failed:\n{error}");
    }

    public static string DotnetSdkVersion { get; private set; } = "";

    public static string TUnitVersion => field ??= typeof(TestAttribute).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    // Shared by every snapshot (so equal versions get equal names across tests) and written from parallel tests.
    private static readonly OrderedDictionary<string, string> _namedVersions = [];
    private static readonly Lock _namedVersionsLock = new();

    private static string NameVersion(string version)
    {
        lock (_namedVersionsLock)
        {
            if (!_namedVersions.TryGetValue(version, out _, out var index))
            {
                _namedVersions.TryAdd(version, version, out index);
            }

            return $"Version_{index}";
        }
    }

    private class ProjectEvaluationSerializer : WriteOnlyJsonConverter<ProjectEvaluation>
    {
        // Every test runs once per target framework (see TargetFrameworks) against one shared snapshot.
        private const string TargetFrameworkScrubbed = "{TargetFramework}";

        // Implicit usings Microsoft.NET.Sdk itself adds only for newer target frameworks (System.Net.Http.Json
        // from net11.0; Microsoft.NET.Sdk.Web has it on every framework). They aren't this SDK's and would
        // split the shared snapshot.
        private static readonly FrozenSet<string> TargetFrameworkImplicitUsings = ["System.Net.Http.Json"];

        private static readonly IReadOnlyCollection<string> PropertiesToWrite =
        [
            nameof(ProjectEvaluation.Configuration),
            nameof(ProjectEvaluation.Platform),
            nameof(ProjectEvaluation.TargetFramework),
        ];

        public override void Write(VerifyJsonWriter writer, ProjectEvaluation value)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Name");
            writer.WriteValue(value.ShortenedName);
            foreach (var item in PropertiesToWrite)
            {
                writer.WritePropertyName(item);
                writer.WriteValue(item == nameof(ProjectEvaluation.TargetFramework) ? TargetFrameworkScrubbed : value.GetType().GetProperty(item)?.GetValue(value));
            }

            writer.WritePropertyName("Properties");
            writer.WriteStartObject();
            foreach (var property in value.GetProperties()
                         .Where(z => AllPropertyNames.Contains(z.Key))
                         .OrderBy(z => z.Key)
                    )
            {
                writer.WritePropertyName(property.Key);
                if (property.Key == nameof(ProjectEvaluation.TargetFramework))
                {
                    writer.WriteValue(TargetFrameworkScrubbed);
                    continue;
                }

                if (property.Key.StartsWith("RsgSdk_") && property.Key.EndsWith("_Version"))
                {
                    writer.WriteValue(NameVersion(property.Value));
                    continue;
                }
                writer.WriteValue(property.Value);
            }

            writer.WriteEndObject();

            WriteAdditionalFiles(writer, value, "AdditionalFiles");
            WriteItems(writer, value, "PackageReference");
            WriteUsings(writer, value, "Using");
            WriteImports(writer, value, "Imports");

            writer.WriteEndObject();
        }

        private static void WriteAdditionalFiles(VerifyJsonWriter writer, ProjectEvaluation project, string name)
        {
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (var item in GetItemGroup(project, name).OrderBy(z => z.Name))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("Name");
                writer.WriteValue(item.Name);
                foreach (var value in item.Children.OfType<Metadata>().OrderBy(z => z.Name))
                {
                    // MSBuild synthesizes %(Link) for out-of-cone items based on a string-prefix
                    // comparison between the item's full path and the project directory; macOS's
                    // /var,/tmp -> /private/var,/private/tmp symlink makes that comparison behave
                    // differently than on Linux, so the same evaluation yields Link on one platform
                    // and not the other. Not meaningful test signal - skip it.
                    if (value.Name == "Link")
                    {
                        continue;
                    }

                    writer.WritePropertyName(value.Name);
                    if (value.Name == "Version")
                    {
                        writer.WriteValue(NameVersion(value.Value));
                        continue;
                    }

                    writer.WriteValue(value.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteImports(VerifyJsonWriter writer, ProjectEvaluation project, string name)
        {
            var (imports, noImports) = GetImportGroup(project);
            writer.WritePropertyName("Imports");

            writer.WriteStartArray();
            foreach (var item in imports)
            {
                writer.WriteValue(item.ProjectFilePath);
            }

            writer.WriteEndArray();

            writer.WritePropertyName("NoImports");
            writer.WriteStartArray();
            foreach (var item in noImports.GroupBy(z => z.ProjectFilePath))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("ProjectFilePath");
                writer.WriteValue(item.Key);
                writer.WritePropertyName("Imports");
                writer.WriteStartArray();
                foreach (var value in item)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("Path");
                    writer.WriteValue(value.Text);
                    writer.WritePropertyName("Reason");
                    writer.WriteValue(value.Reason);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteUsings(VerifyJsonWriter writer, ProjectEvaluation project, string name)
        {
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            var isWebProject = project.GetProperties().TryGetValue("UsingMicrosoftNETSdkWeb", out var usingWeb) && usingWeb == "true";
            // DistinctBy: on net11.0 Microsoft.NET.Sdk and Microsoft.NET.Sdk.Web both add System.Net.Http.Json.
            foreach (var item in GetItemGroup(project, name).Where(z => isWebProject || !TargetFrameworkImplicitUsings.Contains(z.Name)).DistinctBy(z => z.Name).OrderBy(z => z.Name))
            {
                writer.WriteValue(item.Name);
            }
            writer.WriteEndArray();
        }

        private static void WriteItems(VerifyJsonWriter writer, ProjectEvaluation project, string name)
        {
            writer.WritePropertyName(name);
            writer.WriteStartObject();
            foreach (var item in GetItemGroup(project, name).OrderBy(z => z.Name))
            {
                writer.WritePropertyName(item.Name);
                writer.WriteStartObject();
                foreach (var value in item.Children.OfType<Metadata>().OrderBy(z => z.Name))
                {
                    // MSBuild synthesizes %(Link) for out-of-cone items based on a string-prefix
                    // comparison between the item's full path and the project directory; macOS's
                    // /var,/tmp -> /private/var,/private/tmp symlink makes that comparison behave
                    // differently than on Linux, so the same evaluation yields Link on one platform
                    // and not the other. Not meaningful test signal - skip it.
                    if (value.Name == "Link")
                    {
                        continue;
                    }

                    writer.WritePropertyName(value.Name);
                    if (value.Name == "Version")
                    {
                        writer.WriteValue(NameVersion(value.Value));
                        continue;
                    }

                    writer.WriteValue(value.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }

    private static IEnumerable<Item> GetItemGroup(ProjectEvaluation project, string name)
    {
        var items = project.Children.OfType<Folder>().Single(z => z.Name == "Items");
        return items.Children.OfType<AddItem>().SingleOrDefault(z => z.Name == name)?.Children.OfType<Item>() ?? [];
    }

    private static (IEnumerable<Import>, IEnumerable<NoImport>) GetImportGroup(ProjectEvaluation project)
    {
        var dotnetRoot = project.GetProperties()["DOTNET_ROOT"];
        var items = project.Children.OfType<TimedNode>().Single(z => z.Name == "Imports");
        var allImports = items.Children
            .Expand(z => z is TreeNode tn ? tn.Children : [])
            .ToArray();
        return (
            allImports.OfType<Import>().DistinctBy(z => z.ProjectFilePath).Where(z => !z.ProjectFilePath.StartsWith(dotnetRoot)).OrderBy(z => z.Text),
            allImports.OfType<NoImport>().DistinctBy(z => z.ProjectFilePath).Where(z => !z.ProjectFilePath.StartsWith(dotnetRoot)).OrderBy(z => z.Text)
        );
    }

    // Only the SDK's own files: Rocket.Surgery.AssemblyInfo also lives under src/, but it is an ordinary
    // opt-in package, so its properties are not part of what an SDK consumer evaluates.
    public static FrozenSet<string> AllPropertyNames => field ??= Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.props", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.targets", SearchOption.AllDirectories))
        .Where(z => !z.Contains("Rocket.Surgery.AssemblyInfo", StringComparison.Ordinal))
        .SelectMany(z => XDocument.Parse(File.ReadAllText(z)).Document!.Descendants("PropertyGroup")
            .SelectMany(z => z.Elements().Select(e => e.Name.LocalName))
            .Distinct()
        )
        .ToFrozenSet();

    public static string RootDirectory => field ??= FindRootDirectory();
    public static string NugetArtifactsDirectory => field ??= Path.Exists(Path.Combine(FindRootDirectory(), "artifacts", "nuget-local"))
    ? Path.Combine(FindRootDirectory(), "artifacts", "nuget-local")
     : Path.Combine(FindRootDirectory(), "artifacts", "nuget");

    private static string FindRootDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "artifacts")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root directory. This test must be run from within the repo.");
    }
}
