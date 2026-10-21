using System.Diagnostics;
using Assembly = System.Reflection.Assembly;

namespace Rocket.Surgery.MyAssembly.Strings;

[DebuggerDisplay("ResourceName = {ResourceName}, Values = {RootArea.Values.Count}")]
internal record Model(ResourceArea RootArea, string ResourceName, string? Namespace, bool IsPublic)
{
    public string? Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
    public string Visibility => IsPublic ? "public " : "";
}
