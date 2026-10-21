using System.Text.RegularExpressions;

namespace Rocket.Surgery.MyAssembly;

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
        return partStr;
    }
}
