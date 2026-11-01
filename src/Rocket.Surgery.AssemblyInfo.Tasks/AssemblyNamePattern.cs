using System.Text;
using System.Text.RegularExpressions;

namespace Rocket.Surgery.AssemblyInfo.Tasks;

/// <summary>
/// An assembly-name pattern from <c>AssemblyInfoDependencyIgnore</c> or <c>@(AssemblyInfoDependency)</c>.
/// </summary>
/// <remarks>
/// <c>*</c> matches any run of characters and <c>?</c> matches one; the pattern has to match the whole name, ignoring case.
/// A pattern without a wildcard is therefore an exact name: <c>corelib</c> matches nothing, <c>*corelib</c> matches
/// <c>System.Private.CoreLib</c>.
/// </remarks>
internal sealed class AssemblyNamePattern
{
    private readonly Regex _regex;

    public AssemblyNamePattern(string pattern)
    {
        Pattern = pattern;
        var expression = new StringBuilder("^");
        foreach (var c in pattern)
        {
            expression.Append(
                c switch
                {
                    '*' => ".*",
                    '?' => ".",
                    _ => Regex.Escape(c.ToString()),
                }
            );
        }

        _regex = new(expression.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Pattern { get; }

    public bool IsMatch(string assemblyName) => _regex.IsMatch(assemblyName);

    /// <summary>Splits a <c>;</c>- or <c>,</c>-separated list of patterns, dropping empty entries and surrounding whitespace.</summary>
    public static IEnumerable<AssemblyNamePattern> Parse(IEnumerable<string?> lists) =>
        lists
           .SelectMany(list => ( list ?? "" ).Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
           .Select(pattern => pattern.Trim())
           .Where(pattern => pattern.Length > 0)
           .Distinct(StringComparer.OrdinalIgnoreCase)
           .Select(pattern => new AssemblyNamePattern(pattern));
}
