
using System.Diagnostics;
using System.Reflection;

namespace Rocket.Surgery.MyAssembly.Resources;

[DebuggerDisplay("Values = {RootArea.Values.Count}")]
internal record Model(Area RootArea, string? Namespace, bool IsPublic)
{
    public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    public string Visibility => IsPublic ? "public " : "";
}
