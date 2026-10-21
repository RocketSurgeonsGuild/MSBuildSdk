using System.Diagnostics;
using System.Xml.Linq;

namespace Rocket.Surgery.AssemblyInfo.Constants;

[DebuggerDisplay("{Name} = {Value}")]
internal record Constant(string Name, string? Value, string? Comment, string Type = "string")
{
    public string? EscapedValue => Value == null ? null : new XText(Value).ToString();
    public bool IsText => Type.Equals("string", StringComparison.OrdinalIgnoreCase);
}
