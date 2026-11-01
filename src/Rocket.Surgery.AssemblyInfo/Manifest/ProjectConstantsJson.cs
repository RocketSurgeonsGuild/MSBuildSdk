using System.Diagnostics;
using System.Text;

namespace Rocket.Surgery.AssemblyInfo.Manifest;

/// <summary>
/// A <c>@(Constant)</c> item as ConstantsGenerator reads it: the item spec, the metadata, and nothing escaped.
/// </summary>
[DebuggerDisplay("{Root}/{Name} = {Value}")]
internal sealed record ProjectConstant(string Name, string Value, string? Type = null, string? Comment = null, string? Root = null, string? RootComment = null);

/// <summary>
/// Reads and writes <c>$(AssemblyName).AssemblyInfo.constants.json</c>: every <c>@(Constant)</c> of the project, for
/// ConstantsGenerator.
/// </summary>
/// <remarks>
/// One real file, rather than one additional file per constant, so that the compiler's inputs all exist and an unchanged
/// project is not recompiled; and JSON rather than editorconfig metadata, so that values need no escaping.
/// </remarks>
internal static class ProjectConstantsJson
{
    public const string FileSuffix = ".AssemblyInfo.constants.json";

    /// <summary>Writes the constants in the order given, with two-space indents and <c>\n</c> line endings.</summary>
    public static string Write(IEnumerable<ProjectConstant> constants)
    {
        var json = new StringBuilder("{\n  \"constants\": [");
        var first = true;
        foreach (var constant in constants)
        {
            json.Append(first ? "\n" : ",\n").Append("    {\n");
            ManifestJson.AppendMember(json, "      ", "name", constant.Name).Append(",\n");
            ManifestJson.AppendMember(json, "      ", "value", constant.Value);
            AppendOptional(json, "type", constant.Type);
            AppendOptional(json, "comment", constant.Comment);
            AppendOptional(json, "root", constant.Root);
            AppendOptional(json, "rootComment", constant.RootComment);
            json.Append("\n    }");
            first = false;
        }

        return json.Append(first ? "]\n}\n" : "\n  ]\n}\n").ToString();
    }

    /// <summary>Reads a constants file.</summary>
    /// <exception cref="FormatException">The text is not JSON, or not shaped like a constants file.</exception>
    public static List<ProjectConstant> Read(string text)
    {
        var root = ManifestJson.Parse(text) as Dictionary<string, object?> ?? throw new FormatException("The constants file is not a JSON object.");
        return [.. ManifestJson
              .GetArray(root, "constants")
              .Select(
                   item => item as Dictionary<string, object?> ?? throw new FormatException("A constant is not a JSON object.")
               )
              .Select(
                   constant => new ProjectConstant(
                       ManifestJson.GetString(constant, "name") ?? throw new FormatException("A constant has no name."),
                       ManifestJson.GetString(constant, "value") ?? "",
                       ManifestJson.GetString(constant, "type"),
                       ManifestJson.GetString(constant, "comment"),
                       ManifestJson.GetString(constant, "root"),
                       ManifestJson.GetString(constant, "rootComment")
                   )
               )];
    }

    private static void AppendOptional(StringBuilder json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ManifestJson.AppendMember(json.Append(",\n"), "      ", name, value!);
    }
}
