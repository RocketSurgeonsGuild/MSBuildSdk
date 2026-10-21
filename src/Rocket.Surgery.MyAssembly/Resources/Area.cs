using System.Diagnostics;

namespace Rocket.Surgery.MyAssembly.Resources;

[DebuggerDisplay("Name = {Name}")]
internal record Area(string Name)
{
    private Area? parent = null;

    public string? Comment
    {
        get => field ?? $"Provides access to embedded resources under {Path}";
        set;
    } = null;

    public Area? NestedArea
    {
        get;
        set
        {
            field = value;
            field?.parent = this;
        }
    } = null;

    public string Path => parent == null ? Name : $"{parent.Path}/{Name}";

    public IEnumerable<Resource>? Resources { get; private set; }

    public static Area Load(string basePath, List<Resource> resources, string rootArea = "Resources", string comment = "Provides access to embedded resources.")
    {
        var root = new Area(rootArea) { Comment = comment };

        //  Splits: ([area].)*[name]
        var area = root;
        var parts = basePath.Split(["\\", "/"], StringSplitOptions.RemoveEmptyEntries);
        var end = resources.Count == 1 ? ^1 : ^0;

        var parent = "Resources";
        foreach (var part in parts.AsSpan()[..end])
        {
            var partStr = PathSanitizer.Sanitize(part, parent);
            parent = partStr;
            area = area.NestedArea = new Area(partStr);
        }

        area.Resources = resources
           .Select(r => r with
           {
               Name = PathSanitizer.Sanitize(r.Name, parent),
           });
        return root;
    }
}
