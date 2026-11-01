using Microsoft.Build.Framework;
using Rocket.Surgery.AssemblyInfo.Manifest;
using Task = Microsoft.Build.Utilities.Task;

namespace Rocket.Surgery.AssemblyInfo.Tasks;

/// <summary>
/// Writes <c>$(AssemblyName).AssemblyInfo.constants.json</c> from the project's <c>@(Constant)</c> items, for
/// ConstantsGenerator. The file is only rewritten when its content changes.
/// </summary>
public sealed class WriteAssemblyInfoConstants : Task
{
    /// <summary>The <c>@(Constant)</c> items.</summary>
    public ITaskItem[] Constants { get; set; } = [];

    /// <summary>Where to write the file.</summary>
    [Required]
    public string ConstantsFile { get; set; } = "";

    /// <inheritdoc />
    public override bool Execute()
    {
        ChangedFile.Write(ConstantsFile, ProjectConstantsJson.Write(CreateConstants()), Log);
        return true;
    }

    internal IEnumerable<ProjectConstant> CreateConstants()
    {
        // Two items with one name and root would be one member twice; the first wins.
        var seen = new HashSet<(string, string)>();
        foreach (var constant in Constants)
        {
            var root = constant.GetMetadata("Root");
            if (!seen.Add((constant.ItemSpec, root)))
                continue;

            yield return new(
                constant.ItemSpec,
                constant.GetMetadata("Value"),
                Optional(constant.GetMetadata("Type")),
                Optional(constant.GetMetadata("Comment")),
                Optional(root),
                Optional(constant.GetMetadata("RootComment"))
            );
        }
    }

    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
