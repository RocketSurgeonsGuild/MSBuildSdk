using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rocket.Surgery.AssemblyInfo.Manifest;
using Task = Microsoft.Build.Utilities.Task;

namespace Rocket.Surgery.AssemblyInfo.Tasks;

/// <summary>
/// Chooses which of the project's references <c>AssemblyInfo.Dependencies</c> describes, and finds the
/// <c>.AssemblyInfo.json</c> manifest of each one built with Rocket.Surgery.AssemblyInfo.
/// </summary>
/// <remarks>
/// The rules, first match wins:
/// <list type="number">
/// <item><c>AssemblyInfo="false"</c> on the reference (or on the <c>PackageReference</c> that brought it in) leaves it out.</item>
/// <item>An <c>@(AssemblyInfoDependency)</c> item that matches its name, or <c>AssemblyInfo="true"</c>, keeps it.</item>
/// <item>A framework assembly is left out unless <see cref="IncludeFrameworkDependencies" /> is set.</item>
/// <item>An ignore pattern that matches its name leaves it out.</item>
/// <item>Anything else is kept.</item>
/// </list>
/// </remarks>
public sealed class ResolveAssemblyInfoDependencies : Task
{
    /// <summary>The project's <c>@(ReferencePath)</c>.</summary>
    public ITaskItem[] References { get; set; } = [];

    /// <summary>The project's <c>@(PackageReference)</c>; their <c>AssemblyInfo</c> metadata applies to the assemblies they bring in.</summary>
    public ITaskItem[] PackageReferences { get; set; } = [];

    /// <summary>Assembly-name patterns (<c>@(AssemblyInfoDependency)</c>) that are kept whatever else matches them.</summary>
    public ITaskItem[] Includes { get; set; } = [];

    /// <summary>Assembly-name patterns, each item possibly a <c>;</c>- or <c>,</c>-separated list, that are left out.</summary>
    public string[] IgnorePatterns { get; set; } = [];

    /// <summary>The <c>@(AssemblyInfoDependencyIgnore)</c> items: more patterns that are left out.</summary>
    public ITaskItem[] IgnoreItems { get; set; } = [];

    /// <summary>Keep framework assemblies (targeting packs, .NET Framework reference assemblies, NETStandard.Library).</summary>
    public bool IncludeFrameworkDependencies { get; set; }

    /// <summary>
    /// One item per chosen dependency, with the assembly name as the item spec and <c>ReferencePath</c>, <c>CompilePath</c>, <c>Manifest</c>
    /// (empty when there is none), <c>Source</c> (Package, Project or Reference) and <c>IsFramework</c> metadata.
    /// </summary>
    [Output]
    public ITaskItem[] Dependencies { get; set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        var packageOptions = PackageReferences
                            .Where(package => package.GetMetadata("AssemblyInfo").Length > 0)
                            .GroupBy(package => package.ItemSpec, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(group => group.Key, group => group.Last().GetMetadata("AssemblyInfo"), StringComparer.OrdinalIgnoreCase);
        var includes = AssemblyNamePattern.Parse(Includes.Select(GetPattern)).ToArray();
        var ignores = AssemblyNamePattern.Parse(IgnorePatterns.Concat(IgnoreItems.Select(GetPattern))).ToArray();

        var dependencies = new List<ITaskItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in References)
        {
            var assemblyName = GetAssemblyName(reference);
            if (assemblyName.Length == 0 || !seen.Add(assemblyName))
                continue;

            var packageId = reference.GetMetadata("NuGetPackageId");
            var option = reference.GetMetadata("AssemblyInfo");
            if (option.Length == 0 && packageId.Length > 0 && packageOptions.TryGetValue(packageId, out var packageOption))
                option = packageOption;

            var isFramework = IsFramework(reference);
            if (!Keep(assemblyName, option, isFramework && !IncludeFrameworkDependencies, includes, ignores, out var reason))
            {
                Log.LogMessage(MessageImportance.Low, "AssemblyInfo.Dependencies leaves out {0}: {1}.", assemblyName, reason);
                continue;
            }

            var manifest = FindManifest(reference);
            var dependency = new TaskItem(assemblyName);
            dependency.SetMetadata("ReferencePath", reference.ItemSpec);
            // What the compiler gets: the reference assembly of a referenced project, otherwise the assembly itself.
            dependency.SetMetadata("CompilePath", reference.GetMetadata("ReferenceAssembly") is { Length: > 0 } referenceAssembly ? referenceAssembly : reference.ItemSpec);
            dependency.SetMetadata("Manifest", manifest ?? "");
            dependency.SetMetadata("Source", GetSource(reference));
            dependency.SetMetadata("IsFramework", isFramework ? "true" : "false");
            dependencies.Add(dependency);
        }

        Dependencies = [.. dependencies];
        return true;
    }

    internal static bool Keep(
        string assemblyName,
        string option,
        bool excludeAsFramework,
        IReadOnlyCollection<AssemblyNamePattern> includes,
        IReadOnlyCollection<AssemblyNamePattern> ignores,
        out string reason
    )
    {
        if (string.Equals(option, "false", StringComparison.OrdinalIgnoreCase))
        {
            reason = "AssemblyInfo=\"false\"";
            return false;
        }

        if (string.Equals(option, "true", StringComparison.OrdinalIgnoreCase))
        {
            reason = "AssemblyInfo=\"true\"";
            return true;
        }

        if (includes.FirstOrDefault(pattern => pattern.IsMatch(assemblyName)) is { } include)
        {
            reason = $"included by '{include.Pattern}'";
            return true;
        }

        if (excludeAsFramework)
        {
            reason = "it is a framework assembly";
            return false;
        }

        if (ignores.FirstOrDefault(pattern => pattern.IsMatch(assemblyName)) is { } ignore)
        {
            reason = $"ignored by '{ignore.Pattern}'";
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>
    /// The pattern of an item: its <c>Pattern</c> metadata, or the item spec. MSBuild expands <c>*</c> and <c>?</c> in an
    /// <c>Include</c> as a file glob, so a wildcard pattern only survives in metadata (or escaped as <c>%2A</c>/<c>%3F</c>).
    /// </summary>
    private static string GetPattern(ITaskItem item) =>
        item.GetMetadata("Pattern") is { Length: > 0 } pattern ? pattern : item.ItemSpec;

    internal static bool IsFramework(ITaskItem reference) =>
        reference.GetMetadata("FrameworkReferenceName").Length > 0
     || string.Equals(reference.GetMetadata("FrameworkFile"), "true", StringComparison.OrdinalIgnoreCase)
     || string.Equals(reference.GetMetadata("NuGetPackageId"), "NETStandard.Library", StringComparison.OrdinalIgnoreCase);

    private static string GetAssemblyName(ITaskItem reference)
    {
        var assemblyName = reference.GetMetadata("AssemblyName");
        if (assemblyName.Length > 0)
            return assemblyName;

        var fusionName = reference.GetMetadata("FusionName");
        if (fusionName.Length > 0)
        {
            var comma = fusionName.IndexOf(',');
            return ( comma < 0 ? fusionName : fusionName[..comma] ).Trim();
        }

        return Path.GetFileNameWithoutExtension(reference.ItemSpec);
    }

    private static string GetSource(ITaskItem reference) =>
        reference.GetMetadata("ReferenceSourceTarget") == "ProjectReference" ? "Project"
        : reference.GetMetadata("NuGetPackageId").Length > 0 ? "Package"
        : "Reference";

    /// <summary>
    /// The manifest sits next to the assembly: in a package's lib/&lt;tfm&gt;/ folder, and in a referenced project's output
    /// folder (ReferencePath points at the output assembly, not the reference assembly the compiler gets).
    /// </summary>
    private static string? FindManifest(ITaskItem reference)
    {
        var fileName = Path.GetFileNameWithoutExtension(reference.ItemSpec) + AssemblyInfoManifest.FileSuffix;
        var directory = Path.GetDirectoryName(reference.ItemSpec) ?? "";
        var candidate = Path.Combine(directory, fileName);
        if (File.Exists(candidate))
            return candidate;

        // A package that ships reference assemblies: the compile asset is ref/<tfm>/, the manifest is in lib/<tfm>/.
        var pathInPackage = reference.GetMetadata("PathInPackage").Replace('\\', '/');
        if (pathInPackage.StartsWith("ref/", StringComparison.OrdinalIgnoreCase))
        {
            var packageRoot = reference.ItemSpec[..^pathInPackage.Length];
            candidate = Path.Combine(packageRoot, "lib", Path.GetFileName(Path.GetDirectoryName(pathInPackage)) ?? "", fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
