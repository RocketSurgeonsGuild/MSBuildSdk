using Microsoft.CodeAnalysis;

#pragma warning disable RS1035
namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Reads the <c>@(Constant)</c> items that AssemblyInfo.Constants.targets hands to the compiler as additional files.
/// Shared by the generators that need the constants, so they all resolve a constant's root, name and value the same way.
/// </summary>
internal static class ConstantItems
{
    /// <summary>
    /// The <c>@(Constant)</c> items, with the root resolved (<c>Root="."</c> takes the first part of the name) and the
    /// value unwrapped. The value is still as AssemblyInfo.Constants.targets escaped it (backticks doubled, newlines as
    /// <c>`n</c>); see <see cref="RestoreValue" />.
    /// </summary>
    public static IncrementalValuesProvider<(string name, string value, string? type, string? comment, string root, string rootComment)> Read(
        IncrementalGeneratorInitializationContext context
    ) => context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Where(x =>
                    x.Right.GetOptions(x.Left).TryGetValue("build_metadata.Constant.ItemType", out var itemType)
                    && itemType == "Constant")
                .Select((x, ct) =>
                {
                    var options = x.Right.GetOptions(x.Left);
                    options.TryGetValue("build_metadata.Constant.Value", out var value);
                    options.TryGetValue("build_metadata.Constant.Type", out var type);
                    options.TryGetValue("build_metadata.Constant.Comment", out var comment);
                    options.TryGetValue("build_metadata.Constant.Root", out var root);
                    options.TryGetValue("build_metadata.Constant.RootComment", out var rootComment);

                    if (string.IsNullOrEmpty(rootComment))
                        rootComment = "Provides access project-defined constants.";

                    // Revert auto-escaping due to https://github.com/dotnet/roslyn/issues/51692
                    if (value?.StartsWith("|") == true && value.EndsWith("|"))
                        value = value[1..^1].Replace('|', ';');

                    var name = Path.GetFileName(x.Left.Path);
                    if (string.IsNullOrEmpty(root))
                    {
                        root = "Constants";
                    }
                    else if (root == ".")
                    {
                        var parts = name.Split(['.'], 2);
                        if (parts.Length == 2)
                        {
                            // root should be the first part up to the first dot of name
                            // and name should be the rest
                            // note we only do this if there's an actual dot, otherwise, we
                            // just leave the root's default of Constants
                            root = parts[0];
                            name = parts[1];
                        }
                    }

                    return (name, value: value ?? "", type: string.IsNullOrWhiteSpace(type) ? null : type, comment: string.IsNullOrWhiteSpace(comment) ? null : comment, root!, rootComment!);
                });

    /// <summary>
    /// Revert the escaping performed in MSBuild to work around the limitation in editorconfig (see
    /// <see cref="ConstantText.Unescape" />), without the newlines around the value.
    /// </summary>
    public static string RestoreValue(string value) => ConstantText.Unescape(value).Trim(['\r', '\n']);
}
