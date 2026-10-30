using System.Text;

#pragma warning disable RS1035
namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// The text escaping between AssemblyInfo.Constants.targets and <see cref="ConstantsGenerator" />. Editorconfig values
/// are a single line, so the targets escape the Value and Comment of every <c>@(Constant)</c> with the backtick as the
/// escape character, and the generator reverses it here.
/// </summary>
internal static class ConstantText
{
    /// <summary>
    /// Reverses the escaping done by the targets, reading left to right: two backticks are one backtick, and a backtick
    /// followed by <c>n</c> is a newline (<see cref="Environment.NewLine" />). Any other backtick is kept as it is.
    /// </summary>
    /// <remarks>
    /// Reading left to right is what keeps a newline apart from a literal backtick followed by n: the text
    /// <c>a`n</c> in a value arrives as <c>a``n</c>, which is a backtick and an n, while a real newline arrives as <c>`n</c>.
    /// </remarks>
    public static string Unescape(string value)
    {
        if (value.IndexOf('`') < 0)
            return value;

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '`' && i + 1 < value.Length)
            {
                switch (value[i + 1])
                {
                    case '`':
                        result.Append('`');
                        i++;
                        continue;
                    case 'n':
                        result.Append(Environment.NewLine);
                        i++;
                        continue;
                    default:
                        break;
                }
            }

            result.Append(c);
        }

        return result.ToString();
    }
}
