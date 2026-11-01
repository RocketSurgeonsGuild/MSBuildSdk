using System.Diagnostics;

namespace Rocket.Surgery.AssemblyInfo.Manifest;

/// <summary>
/// The <c>$(AssemblyName).AssemblyInfo.json</c> file a project that references Rocket.Surgery.AssemblyInfo writes next to its
/// assembly and packs into its package, so a consumer can generate <c>AssemblyInfo.Dependencies</c> from it.
/// </summary>
/// <remarks>
/// These sources are compiled into both the MSBuild tasks (which write the file) and the generators (which read it), so the
/// two sides always agree on the format.
/// </remarks>
[DebuggerDisplay("{AssemblyName} ({TargetFramework})")]
internal sealed class AssemblyInfoManifest
{
    /// <summary>The schema this code writes, and the newest one it can read.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The file name suffix, after the assembly name.</summary>
    public const string FileSuffix = ".AssemblyInfo.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string? Generator { get; set; }
    public string AssemblyName { get; set; } = "";
    public string? TargetFramework { get; set; }

    /// <summary>The constants, by the dotted path the producer generated them under (e.g. <c>Git.Branch</c>).</summary>
    public List<ManifestConstant> Constants { get; } = [];

    public List<ManifestResource> Resources { get; } = [];
}

/// <summary>A constant in the manifest.</summary>
/// <param name="Path">The dotted path under <c>AssemblyInfo</c>, e.g. <c>Git.Branch</c> or <c>Constants.Foo.Bar</c>.</param>
/// <param name="Value">The value, as written.</param>
/// <param name="Type">The C# type of the constant (<c>string</c>, <c>int</c>, ...).</param>
/// <param name="Comment">The comment, if the item had one.</param>
[DebuggerDisplay("{Path} = {Value}")]
internal sealed record ManifestConstant(string Path, string Value, string Type = "string", string? Comment = null);

/// <summary>An embedded resource in the manifest.</summary>
/// <param name="Path">The project-relative path, with <c>/</c> separators, e.g. <c>Content/Styles/Main.css</c>.</param>
/// <param name="ManifestResourceName">The name the resource has in the assembly.</param>
/// <param name="Kind"><c>text</c> or <c>binary</c>.</param>
/// <param name="Comment">The comment, if the item had one.</param>
[DebuggerDisplay("{Path}")]
internal sealed record ManifestResource(string Path, string ManifestResourceName, string Kind = ManifestResource.Binary, string? Comment = null)
{
    public const string Text = "text";
    public const string Binary = "binary";

    public bool IsText => string.Equals(Kind, Text, StringComparison.OrdinalIgnoreCase);
}
