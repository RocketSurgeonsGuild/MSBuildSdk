using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Sanitizes paths for use as identifiers.
/// </summary>
public static class PathSanitizer
{
    private static readonly Regex invalidCharsRegex = new(@"\W");

    /// <summary>
    /// Sanitizes the specified path for use as an identifier.
    /// </summary>
    public static string Sanitize(string path, string parent)
    {
        var partStr = invalidCharsRegex.Replace(path, "_");
        if (char.IsDigit(partStr[0]))
            partStr = "_" + partStr;
        if (partStr == parent)
            partStr = "_" + partStr;
        // Folder or file names like "else" or "class" are C# keywords; escape them so the type compiles.
        if (SyntaxFacts.GetKeywordKind(partStr) != SyntaxKind.None)
            partStr = "@" + partStr;
        return partStr;
    }
}
