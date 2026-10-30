using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Rocket.Surgery.AssemblyInfo.Dependencies;

/// <summary>One value a marked assembly published: <paramref name="Path" /> is the dotted path under the generated <c>AssemblyInfo</c> class (<c>Git.Branch</c>).</summary>
internal sealed record DependencyEntry(string Path, string Type, string Value);

/// <summary>
/// A referenced assembly that carries the marker, as the generator reads it. Equatable, so the incremental
/// pipeline does not regenerate when a compilation changes but the references' markers do not.
/// </summary>
internal sealed class DependencyInfo(string name, string version, string format, ImmutableArray<DependencyEntry> entries) : IEquatable<DependencyInfo>
{

    /// <summary>The assembly's simple name.</summary>
    public string Name { get; } = name;

    /// <summary>The assembly's version, which only tells apart two references with the same name.</summary>
    public string Version { get; } = version;

    /// <summary>The marker's value: the format version the producer wrote.</summary>
    public string Format { get; } = format;

    /// <summary>The published values, ordered by path. Empty when <see cref="Format" /> is not one this build reads.</summary>
    public ImmutableArray<DependencyEntry> Entries { get; } = entries;

    public bool IsSupportedFormat => Format == DependencyMarker.FormatVersion;

    public bool Equals(DependencyInfo? other) =>
        other is not null
        && Name == other.Name
        && Version == other.Version
        && Format == other.Format
        && Entries.SequenceEqual(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as DependencyInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = ( Name.GetHashCode() * 397 ) ^ Version.GetHashCode();
            return ( hash * 397 ) ^ Entries.Length;
        }
    }

    /// <summary>
    /// Finds the marked assemblies among the compilation's references. Everything the compilation was given counts,
    /// which includes the transitive references MSBuild passes to the compiler. Unmarked assemblies are skipped.
    /// </summary>
    public static ImmutableArray<DependencyInfo> Read(Compilation compilation, CancellationToken cancellationToken)
    {
        var result = ImmutableArray.CreateBuilder<DependencyInfo>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryRead(assembly) is { } dependency)
                result.Add(dependency);
        }

        // Stable, so a re-ordered reference list does not change the output.
        result.Sort((left, right) =>
        {
            var byName = string.CompareOrdinal(left.Name, right.Name);
            return byName != 0 ? byName : string.CompareOrdinal(left.Version, right.Version);
        });
        return result.ToImmutable();
    }

    private static DependencyInfo? TryRead(IAssemblySymbol assembly)
    {
        string? format = null;
        var entries = new SortedDictionary<string, DependencyEntry>(StringComparer.Ordinal);
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!IsAssemblyMetadata(attribute.AttributeClass) || attribute.ConstructorArguments.Length != 2)
                continue;

            if (attribute.ConstructorArguments[0].Value is not string key)
                continue;

            var value = attribute.ConstructorArguments[1].Value as string;
            if (key == DependencyMarker.MarkerKey)
            {
                format ??= value ?? "";
            }
            else if (key.StartsWith(DependencyMarker.EntryPrefix, StringComparison.Ordinal)
                  && value is not null
                  && DependencyMarker.TryDecode(value, out var type, out var text))
            {
                var path = key[DependencyMarker.EntryPrefix.Length..];
                // The first attribute wins, like a compiler would for a duplicate.
                if (!entries.ContainsKey(path))
                    entries.Add(path, new DependencyEntry(path, type, text));
            }
        }

        if (format is null)
            return null;

        var identity = assembly.Identity;
        return new DependencyInfo(
            identity.Name,
            identity.Version.ToString(),
            format,
            // A format this build can not read is reported, not guessed at.
            format == DependencyMarker.FormatVersion ? [.. entries.Values] : []
        );
    }

    private static bool IsAssemblyMetadata(INamedTypeSymbol? type) =>
        type is
        {
            Name: "AssemblyMetadataAttribute",
            ContainingNamespace:
            {
                Name: "Reflection",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true, },
            },
        };
}

/// <summary>Compares the dependencies of two pipeline runs by value.</summary>
internal sealed class DependencyInfoListComparer : IEqualityComparer<ImmutableArray<DependencyInfo>>
{
    public static DependencyInfoListComparer Instance { get; } = new();

    public bool Equals(ImmutableArray<DependencyInfo> x, ImmutableArray<DependencyInfo> y) => x.SequenceEqual(y);

    public int GetHashCode(ImmutableArray<DependencyInfo> obj) => obj.Length;
}
