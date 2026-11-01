using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Rocket.Surgery.AssemblyInfo.Manifest;

namespace Rocket.Surgery.AssemblyInfo.Dependencies;

/// <summary>
/// A class under <c>AssemblyInfo.Dependencies.&lt;name&gt;.Resources</c>: a folder, a resource, or both when a resource and a
/// folder have the same name.
/// </summary>
/// <remarks>
/// The names follow ResourcesGenerator, so a dependency's resources have the shape that dependency generated for itself:
/// folders nest, a file is named without its extension, and files that only differ by extension (swagger.css, swagger.js)
/// share a class named after the file with one class per extension inside.
/// </remarks>
[DebuggerDisplay("{Name}")]
internal sealed class ResourceNode(string name, string comment)
{
    private readonly SortedDictionary<string, ResourceNode> _children = [with(StringComparer.Ordinal)];

    public string Name { get; } = name;

    /// <summary>The summary, already escaped for XML.</summary>
    public string Comment { get; private set; } = comment;

    public ManifestResource? Resource { get; private set; }

    /// <summary>The escaped manifest resource name, for a verbatim string.</summary>
    public string? ManifestResourceName => Resource?.ManifestResourceName.Replace("\"", "\"\"");

    public bool IsText => Resource?.IsText == true;

    public IEnumerable<ResourceNode> Children => _children.Values;

    public static ResourceNode Build(IEnumerable<ManifestResource> resources)
    {
        var root = new ResourceNode("Resources", "");
        var groups = resources
                    .Where(resource => resource.Path.Trim('/').Length > 0)
                    .GroupBy(resource => WithoutExtension(resource.Path.Trim('/')), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var parts = group.Key.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            var parent = root;
            foreach (var folder in parts.Take(parts.Length - 1))
            {
                parent = parent.GetOrAddFolder(folder);
            }

            var fileName = parts[^1];
            if (group.Count() == 1)
            {
                parent.AddResource(fileName, group.First());
                continue;
            }

            var area = parent.GetOrAddFolder(fileName);
            foreach (var resource in group)
            {
                var extension = Path.GetExtension(resource.Path);
                area.AddResource(extension.Length > 1 ? extension[1..] : "_", resource);
            }
        }

        return root;
    }

    private ResourceNode GetOrAddFolder(string folder)
    {
        var name = PathSanitizer.Sanitize(folder, Name);
        if (!_children.TryGetValue(name, out var child))
        {
            child = new(name, new XText($"Provides access to the embedded resources under {folder}").ToString());
            _children[name] = child;
        }

        return child;
    }

    private void AddResource(string fileName, ManifestResource resource)
    {
        var name = PathSanitizer.Sanitize(fileName, Name);
        var comment = new XText(resource.Comment ?? $"=> {resource.Path}").ToString().Replace("\r", "").Replace("\n", " ");
        // Two resources whose names sanitize alike (a-b.txt, a_b.txt) cannot share a class.
        var unique = name;
        for (var suffix = 2; _children.TryGetValue(unique, out var existing) && existing.Resource is not null; suffix++)
        {
            unique = $"{name}_{suffix}";
        }

        if (!_children.TryGetValue(unique, out var child))
        {
            child = new(unique, comment);
            _children[unique] = child;
        }

        child.Resource = resource;
        child.Comment = comment;
    }

    private static string WithoutExtension(string path)
    {
        var slash = path.LastIndexOf('/');
        var dot = path.LastIndexOf('.');
        return dot > slash + 1 ? path[..dot] : path;
    }
}

/// <summary>The model of <c>DependencyResourceAccessors.sbntxt</c>.</summary>
internal sealed record ResourcesModel(ResourceNode Root, string DependencyName, string AssemblyName, bool Accessors, string? Namespace, bool IsPublic)
{
    public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    public string Visibility => IsPublic ? "public " : "";

    /// <summary>The escaped assembly name, for a verbatim string.</summary>
    public string AssemblyNameLiteral => AssemblyName.Replace("\"", "\"\"");
}
