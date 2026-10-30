namespace Rocket.Surgery.AssemblyInfo.Dependencies;

/// <summary>
/// The wire format a marked assembly publishes: plain <c>System.Reflection.AssemblyMetadata</c> attributes.
/// Assembly-level attributes of a framework type survive reference assemblies whatever the AssemblyInfo visibility
/// is, so a consuming compilation can read them from compiler symbols without any reflection.
/// </summary>
/// <remarks>
/// <code>
/// [assembly: AssemblyMetadata("Rocket.Surgery.AssemblyInfo", "1")]                      // the marker: the format version
/// [assembly: AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Git.Branch", "string:main")] // one per value: path, then type:value
/// </code>
/// </remarks>
internal static class DependencyMarker
{
    /// <summary>The key of the attribute that marks an assembly. Its value is the format version.</summary>
    public const string MarkerKey = "Rocket.Surgery.AssemblyInfo";

    /// <summary>The only format version this build writes and reads.</summary>
    public const string FormatVersion = "1";

    /// <summary>Prefix of the attributes that carry one value each.</summary>
    public const string EntryPrefix = MarkerKey + ":";

    // Constants whose declared type is one of these can travel in the marker. Anything else (a type that is only
    // valid in the public mode, say) is left out: the consumer would have to trust arbitrary code from a reference.
    private static readonly Dictionary<string, string> CanonicalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = "string",
        ["bool"] = "bool",
        ["byte"] = "byte",
        ["sbyte"] = "sbyte",
        ["short"] = "short",
        ["ushort"] = "ushort",
        ["int"] = "int",
        ["uint"] = "uint",
        ["long"] = "long",
        ["ulong"] = "ulong",
        ["float"] = "float",
        ["double"] = "double",
        ["decimal"] = "decimal",
        // The .NET names; String, Byte, SByte, Double and Decimal already match the keywords case-insensitively.
        ["Boolean"] = "bool",
        ["Int16"] = "short",
        ["UInt16"] = "ushort",
        ["Int32"] = "int",
        ["UInt32"] = "uint",
        ["Int64"] = "long",
        ["UInt64"] = "ulong",
        ["Single"] = "float",
    };

    /// <summary>The C# keyword for a constant's declared type (<c>Int64</c> and <c>System.Int64</c> become <c>long</c>), if it can travel.</summary>
    public static bool TryGetCanonicalType(string? declared, [NotNullWhen(true)] out string? canonical)
    {
        var name = string.IsNullOrWhiteSpace(declared) ? "string" : declared!.Trim();
        if (name.StartsWith("System.", StringComparison.Ordinal))
            name = name["System.".Length..];

        return CanonicalTypes.TryGetValue(name, out canonical);
    }

    /// <summary>The value of a per-value attribute: <c>type:value</c>.</summary>
    public static string Encode(string canonicalType, string value) => canonicalType + ":" + value;

    /// <summary>Reverses <see cref="Encode" />. The type has to be one this build knows.</summary>
    public static bool TryDecode(string encoded, [NotNullWhen(true)] out string? canonicalType, [NotNullWhen(true)] out string? value)
    {
        canonicalType = null;
        value = null;

        var separator = encoded.IndexOf(':');
        if (separator <= 0)
            return false;

        var type = encoded[..separator];
        // The writer only ever writes the keyword form.
        if (!CanonicalTypes.TryGetValue(type, out var canonical) || canonical != type)
            return false;

        canonicalType = canonical;
        value = encoded[( separator + 1 )..];
        return true;
    }
}
