using System.Diagnostics;

namespace Rocket.Surgery.MyAssembly.Strings;

[DebuggerDisplay("Id = {Id}, NestedAreas = {NestedAreas.Count}, Values = {Values.Count}")]
internal record ResourceArea(string Id, string Prefix)
{
    public List<ResourceArea> NestedAreas { get; init; } = [];
    public List<ResourceValue> Values { get; init; } = [];
}
