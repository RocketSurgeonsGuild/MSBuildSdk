using System.Reflection;
using Microsoft.Build.Framework;
using Rocket.Surgery.AssemblyInfo.Manifest;
using Task = Microsoft.Build.Utilities.Task;

namespace Rocket.Surgery.AssemblyInfo.Tasks;

/// <summary>
/// Writes <c>$(AssemblyName).AssemblyInfo.json</c> from the project's <c>@(Constant)</c> and <c>@(EmbeddedResource)</c> items.
/// </summary>
/// <remarks>
/// Content is opt-in: a constant goes in only when its <c>Manifest</c> metadata is <c>true</c> (the targets set it on Git,
/// Metadata, Attributes and the default project properties), and a resource goes in unless its <c>Manifest</c> metadata is
/// <c>false</c>. The file is only rewritten when its content changes, so its timestamp tells consumers when to rebuild.
/// </remarks>
public sealed class WriteAssemblyInfoManifest : Task
{
    /// <summary>The project's assembly name.</summary>
    [Required]
    public string AssemblyName { get; set; } = "";

    /// <summary>The target framework being built.</summary>
    public string? TargetFramework { get; set; }

    /// <summary>The name and version of what wrote the manifest.</summary>
    public string? Generator { get; set; }

    /// <summary>The <c>@(Constant)</c> items.</summary>
    public ITaskItem[] Constants { get; set; } = [];

    /// <summary>The <c>@(EmbeddedResource)</c> items, with the <c>Value</c> metadata the AssemblyInfo targets give them.</summary>
    public ITaskItem[] Resources { get; set; } = [];

    /// <summary>The <c>|</c>-separated extensions of resources that are text (<c>$(EmbeddedResourceStringExtensions)</c>).</summary>
    public string? TextExtensions { get; set; }

    /// <summary>Where to write the manifest.</summary>
    [Required]
    public string ManifestFile { get; set; } = "";

    /// <inheritdoc />
    public override bool Execute()
    {
        ChangedFile.Write(ManifestFile, ManifestJson.Write(CreateManifest()), Log);
        return true;
    }

    internal AssemblyInfoManifest CreateManifest()
    {
        var manifest = new AssemblyInfoManifest
        {
            AssemblyName = AssemblyName,
            TargetFramework = string.IsNullOrEmpty(TargetFramework) ? null : TargetFramework,
            Generator = string.IsNullOrEmpty(Generator) ? DefaultGenerator : Generator,
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constant in Constants)
        {
            if (!IsTrue(constant.GetMetadata("Manifest")))
                continue;

            var path = GetPath(constant.ItemSpec, constant.GetMetadata("Root"));
            if (!seen.Add(path))
                continue;

            var type = constant.GetMetadata("Type");
            var comment = constant.GetMetadata("Comment");
            manifest.Constants.Add(
                new(
                    path,
                    constant.GetMetadata("Value"),
                    string.IsNullOrWhiteSpace(type) ? "string" : type,
                    string.IsNullOrWhiteSpace(comment) ? null : comment
                )
            );
        }

        var textExtensions = new HashSet<string>(
            ( TextExtensions ?? "" ).Split(['|', ';'], StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase
        );
        seen.Clear();
        foreach (var resource in Resources)
        {
            if (IsFalse(resource.GetMetadata("Manifest")) || IsTrue(resource.GetMetadata("WithCulture")))
                continue;

            // Value is the project-relative path the AssemblyInfo targets give every resource they can name; one another
            // package added by absolute path has none, and the generator has no accessor for it either.
            var path = resource.GetMetadata("Value").Replace('\\', '/');
            var manifestResourceName = GetManifestResourceName(resource);
            if (path.Length == 0 || manifestResourceName.Length == 0 || !seen.Add(manifestResourceName))
                continue;

            var kind = resource.GetMetadata("Kind");
            var comment = resource.GetMetadata("Comment");
            var isText = kind.Equals(ManifestResource.Text, StringComparison.OrdinalIgnoreCase)
             || textExtensions.Contains(Path.GetExtension(path));
            manifest.Resources.Add(
                new(
                    path,
                    manifestResourceName,
                    isText ? ManifestResource.Text : ManifestResource.Binary,
                    string.IsNullOrWhiteSpace(comment) ? null : comment
                )
            );
        }

        manifest.Constants.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        manifest.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return manifest;
    }

    private static string DefaultGenerator
    {
        get
        {
            var assembly = typeof(WriteAssemblyInfoManifest).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
             ?? assembly.GetName().Version?.ToString()
             ?? "";
            // The informational version ends in +<commit> when SourceLink is on; the package version is enough here.
            var plus = version.IndexOf('+');
            return "Rocket.Surgery.AssemblyInfo/" + ( plus < 0 ? version : version[..plus] );
        }
    }

    /// <summary>The dotted path ConstantsGenerator puts a constant under.</summary>
    internal static string GetPath(string name, string? root) =>
        root switch
        {
            null or "" => "Constants." + name,
            // A root of "." takes the first part of the name as the root, or Constants when the name has no dot.
            "." => name.IndexOf('.') > 0 ? name : "Constants." + name,
            _ => root + "." + name,
        };

    private static string GetManifestResourceName(ITaskItem resource)
    {
        var logicalName = resource.GetMetadata("LogicalName");
        if (logicalName.Length > 0)
            return logicalName;

        var manifestResourceName = resource.GetMetadata("ManifestResourceName");
        if (manifestResourceName.Length == 0)
            return "";

        // A .resx is compiled into a .resources resource; ManifestResourceName leaves the extension off.
        return string.Equals(resource.GetMetadata("Extension"), ".resx", StringComparison.OrdinalIgnoreCase)
            ? manifestResourceName + ".resources"
            : manifestResourceName;
    }

    private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    private static bool IsFalse(string value) => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
}
