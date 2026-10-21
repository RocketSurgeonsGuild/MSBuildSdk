using System.Diagnostics;
using System.Reflection;

namespace Rocket.Surgery.MyAssembly.Constants;

[DebuggerDisplay("Values = {RootArea.Values.Count}")]
internal record Model(Area RootArea, string? Namespace, bool IsPublic)
{
    public bool RawStrings { get; set; } = false;
    public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    public string Visibility => IsPublic ? "public " : "";
    public string Modifier => IsPublic ? "static" : "const";
    public string Lambda => IsPublic ? ">" : "";
}
