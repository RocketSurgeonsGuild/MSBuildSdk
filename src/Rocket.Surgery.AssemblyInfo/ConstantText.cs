using System.Text;
using System.Xml.Linq;

#pragma warning disable RS1035
namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// How the generators turn constant text into C#: the same newline and doc-comment handling for a project's own
/// constants (ConstantsGenerator) and for its dependencies' (DependenciesGenerator).
/// </summary>
internal static class ConstantText
{
    /// <summary>
    /// Every kind of line break (CRLF, LF, CR, and the Unicode line breaks U+0085, U+2028, U+2029) becomes
    /// <see cref="Environment.NewLine" />, so the generated code has the build machine's newlines whatever the value had.
    /// </summary>
    public static string NormalizeNewLines(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                // CRLF is one newline.
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                result.Append(Environment.NewLine);
            }
            else if (IsLineBreak(c))
            {
                result.Append(Environment.NewLine);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// The <c>///</c> lines of a doc comment for <paramref name="text" />. The newlines at the very start and end are
    /// dropped; with <paramref name="trimWhitespace" />, so is other whitespace at either end (an explicit comment is
    /// trimmed, a value used as the comment is not).
    /// </summary>
    public static string DocComment(string text, bool trimWhitespace)
    {
        var escaped = new XText(NormalizeNewLines(text)).ToString();
        if (trimWhitespace)
            escaped = TrimWhitespaceExceptNewLines(escaped);

        var lines = escaped.Trim('\r', '\n').Split([Environment.NewLine], StringSplitOptions.None);
        return "/// " + string.Join(Environment.NewLine + "/// ", lines);
    }

    private static bool IsLineBreak(char c) => c is '\n' or '\r' or (char)0x85 or (char)0x2028 or (char)0x2029;

    private static string TrimWhitespaceExceptNewLines(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && char.IsWhiteSpace(text[start]) && !IsLineBreak(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]) && !IsLineBreak(text[end - 1]))
            end--;

        return text[start..end];
    }
}
