using System.Collections.Frozen;
using System.Reflection;
using System.Xml.Linq;
using DiffEngine;
using Microsoft.Build.Logging.StructuredLogger;
using Microsoft.Build.Utilities.ProjectCreation;

namespace Rocket.Surgery.Sdk.Tests;

internal static class Config
{
    [Before(HookType.Assembly)]
    public static void Setup(AssemblyHookContext context)
    {
        MSBuildAssemblyResolver.Register();

        DiffRunner.Disabled = true;
        VerifierSettings.AddExtraSettings(z => z.Converters.Add(new ProjectEvaluationSerializer()));
    }

    public static string TUnitVersion => field ??= typeof(TestAttribute).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    private static readonly OrderedDictionary<string, string> _namedVersions = [];

    private class ProjectEvaluationSerializer : WriteOnlyJsonConverter<ProjectEvaluation>
    {
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
                writer.WriteValue(value.GetType().GetProperty(item)?.GetValue(value));
            }

            writer.WritePropertyName("Properties");
            writer.WriteStartObject();
            foreach (var property in value.GetProperties()
                         .Where(z => AllPropertyNames.Contains(z.Key))
                         .OrderBy(z => z.Key)
                    )
            {
                writer.WritePropertyName(property.Key);
                if (property.Key.StartsWith("RsgSdk_") && property.Key.EndsWith("_Version"))
                {
                    if (_namedVersions.TryGetValue(property.Value, out _, out var index))
                    {
                        writer.WriteValue($"Version_{index}");
                    }
                    else
                    {
                        _namedVersions.TryAdd(property.Value, property.Value, out index);
                        writer.WriteValue($"Version_{index}");
                    }
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
                    writer.WritePropertyName(value.Name);
                    if (value.Name == "Version")
                    {
                        if (_namedVersions.TryGetValue(value.Value, out _, out var index))
                        {
                            writer.WriteValue($"Version_{index}");
                        }
                        else
                        {
                            _namedVersions.TryAdd(value.Value, $"Version_{_namedVersions.Count + 1}", out index);
                            writer.WriteValue($"Version_{index}");
                        }
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
            foreach (var item in GetItemGroup(project, name).OrderBy(z => z.Name))
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
                    writer.WritePropertyName(value.Name);
                    if (value.Name == "Version")
                    {
                        if (_namedVersions.TryGetValue(value.Value, out _, out var index))
                        {
                            writer.WriteValue($"Version_{index}");
                        }
                        else
                        {
                            _namedVersions.TryAdd(value.Value, $"Version_{_namedVersions.Count + 1}", out index);
                            writer.WriteValue($"Version_{index}");
                        }
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

    public static FrozenSet<string> AllPropertyNames => field ??= Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.props", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.targets", SearchOption.AllDirectories))
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
