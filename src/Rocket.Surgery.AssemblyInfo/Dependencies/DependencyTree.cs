using System.Reflection;
using Rocket.Surgery.AssemblyInfo.Constants;

namespace Rocket.Surgery.AssemblyInfo.Dependencies;

/// <summary>What the Dependencies template renders: the <c>Dependencies</c> area, and how <c>AssemblyInfo</c> itself is declared.</summary>
internal record DependenciesModel(Area RootArea, string? Namespace, bool IsPublic)
{
    public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    public string Visibility => IsPublic ? "public " : "";
    public string Modifier => IsPublic ? "static" : "const";
    public string Lambda => IsPublic ? ">" : "";
}

/// <summary>
/// Builds the <c>Dependencies</c> area: one class per referenced assembly, holding the same areas the producer's own
/// <c>AssemblyInfo</c> has. Names go through the same <see cref="Area" /> rules as constants, so a value is spelled the
/// same in both places.
/// </summary>
internal static class DependencyTree
{
    /// <summary>The name of the area, which the producer's own constants could also use as a root.</summary>
    public const string RootName = "Dependencies";

    public static Area Build(IEnumerable<DependencyInfo> dependencies)
    {
        var root = new Area(RootName, "")
        {
            Comment = "Provides access to the AssemblyInfo of the referenced assemblies that publish it, one class per assembly.",
        };

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in dependencies.Where(dependency => dependency.IsSupportedFormat))
        {
            var areas = new List<Area>();
            foreach (var entry in dependency.Entries)
            {
                if (!DependencyLiteral.TryFormat(entry.Type, entry.Value, out var literal))
                    continue;

                var parts = entry.Path.Split(['.'], 2);
                // A path needs a root and a name; the producer never writes anything else.
                if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                    continue;

                var comment = $"The {Escape(entry.Path)} value of the {Escape(dependency.Name)} assembly.";
                var chain = Area.Load([new Constant(parts[1], literal, comment, entry.Type),], parts[0], $"The {Escape(parts[0])} area of the {Escape(dependency.Name)} assembly.");
                Attach(areas, chain);
            }

            // Two assemblies can share an identifier (Foo.Bar and Foo-Bar), and a class can not be named like one of its members.
            var name = Area.EscapeIdentifier(dependency.Name);
            var candidate = name;
            for (var suffix = 2; used.Contains(candidate) || candidate == RootName || areas.Any(area => area.Name == candidate); suffix++)
            {
                candidate = name + "_" + suffix;
            }

            used.Add(candidate);
            root.NestedAreas.Add(
                new Area(candidate, "")
                {
                    Comment = $"Provides access to the AssemblyInfo of the {Escape(dependency.Name)} assembly, version {Escape(dependency.Version)}.",
                    NestedAreas = areas,
                }
            );
        }

        return root;
    }

    // Constants arrive one at a time, each as a chain of areas down to its value. Merge the chains; when a value and an
    // area want the same name the first one stays, because both can not exist and a producer's own build would not compile either.
    private static void Attach(List<Area> siblings, Area chain)
    {
        var existing = siblings.FirstOrDefault(area => area.Name == chain.Name);
        if (existing is null)
        {
            siblings.Add(chain);
            return;
        }

        foreach (var value in chain.Values)
        {
            if (existing.Values.All(other => other.Name != value.Name) && existing.NestedAreas.All(area => area.Name != value.Name))
            {
                existing.Values.Add(value);
                existing.Values.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            }
        }

        foreach (var nested in chain.NestedAreas)
        {
            if (existing.Values.All(value => value.Name != nested.Name))
            {
                Attach(existing.NestedAreas, nested);
            }
        }
    }

    // The text ends up in an XML doc comment on a single line, and it comes from a reference's metadata.
    private static string Escape(string text) =>
        string.Concat(
            text.Select(c => c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ when char.IsControl(c) => " ",
                _ => c.ToString(),
            })
        );
}
