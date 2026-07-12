using System.Collections.Frozen;
using System.Xml.Linq;
using DiffEngine;
using Microsoft.Build.Logging.StructuredLogger;
using Microsoft.Build.Utilities.ProjectCreation;

namespace Rocket.Surgery.Sdk.Tests;

static class Config
{
    [Before(Assembly)]
    public static void Setup(AssemblyHookContext context)
    {
        MSBuildAssemblyResolver.Register();

        DiffRunner.Disabled = true;
        VerifierSettings.AddExtraSettings(z => z.Converters.Add(new ProjectEvaluationSerializer()));
    }

    class ProjectEvaluationSerializer : WriteOnlyJsonConverter<ProjectEvaluation>
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
                writer.WriteValue(property.Value);
            }

            writer.WriteEndObject();

            WriteAdditionalFiles(writer, value, "AdditionalFiles");
            WriteItems(writer, value, "PackageReference");
            WriteUsings(writer, value, "Using");
            WriteImports(writer, value, "Imports");

            writer.WriteEndObject();
        }

        static void WriteAdditionalFiles(VerifyJsonWriter writer, ProjectEvaluation project, string name)
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
                    writer.WriteValue(value.Name == "Version" ? "{version}" : value.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        static void WriteImports(VerifyJsonWriter writer, ProjectEvaluation project, string name)
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

        static void WriteUsings(VerifyJsonWriter writer, ProjectEvaluation project, string name)
        {
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (var item in GetItemGroup(project, name).OrderBy(z => z.Name))
            {
                writer.WriteValue(item.Name);
            }
            writer.WriteEndArray();
        }

        static void WriteItems(VerifyJsonWriter writer, ProjectEvaluation project, string name)
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
                    writer.WriteValue(value.Name == "Version" ? "{version}" : value.Value);
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
    public static string NugetArtifactsDirectory => field ??= Path.Combine(FindRootDirectory(), "artifacts");

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
