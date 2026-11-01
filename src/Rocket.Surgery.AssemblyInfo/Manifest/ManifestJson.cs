using System.Globalization;
using System.Text;

namespace Rocket.Surgery.AssemblyInfo.Manifest;

/// <summary>
/// Reads and writes <see cref="AssemblyInfoManifest" /> as JSON.
/// </summary>
/// <remarks>
/// Hand-written on purpose: Visual Studio's .NET Framework MSBuild and the older Roslyn hosts cannot be relied on to load a
/// System.Text.Json that matches the one these assemblies would be built against. The format is small, so a small reader
/// and writer cover it. The reader accepts any valid JSON and ignores members it does not know, so a newer producer can add
/// members without breaking an older consumer.
/// </remarks>
internal static class ManifestJson
{
    /// <summary>Writes the manifest with two-space indents and <c>\n</c> line endings, so the output only depends on the manifest.</summary>
    public static string Write(AssemblyInfoManifest manifest)
    {
        var json = new StringBuilder();
        json.Append("{\n");
        json.Append("  \"schemaVersion\": ").Append(manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        if (manifest.Generator is { } generator)
            AppendMember(json, "  ", "generator", generator).Append(",\n");
        AppendMember(json, "  ", "assemblyName", manifest.AssemblyName).Append(",\n");
        if (manifest.TargetFramework is { } targetFramework)
            AppendMember(json, "  ", "targetFramework", targetFramework).Append(",\n");

        json.Append("  \"constants\": [");
        for (var i = 0; i < manifest.Constants.Count; i++)
        {
            var constant = manifest.Constants[i];
            json.Append(i == 0 ? "\n" : ",\n").Append("    {\n");
            AppendMember(json, "      ", "path", constant.Path).Append(",\n");
            AppendMember(json, "      ", "value", constant.Value).Append(",\n");
            AppendMember(json, "      ", "type", constant.Type);
            if (constant.Comment is { } comment)
                AppendMember(json.Append(",\n"), "      ", "comment", comment);
            json.Append("\n    }");
        }

        json.Append(manifest.Constants.Count == 0 ? "],\n" : "\n  ],\n");

        json.Append("  \"resources\": [");
        for (var i = 0; i < manifest.Resources.Count; i++)
        {
            var resource = manifest.Resources[i];
            json.Append(i == 0 ? "\n" : ",\n").Append("    {\n");
            AppendMember(json, "      ", "path", resource.Path).Append(",\n");
            AppendMember(json, "      ", "manifestResourceName", resource.ManifestResourceName).Append(",\n");
            AppendMember(json, "      ", "kind", resource.Kind);
            if (resource.Comment is { } comment)
                AppendMember(json.Append(",\n"), "      ", "comment", comment);
            json.Append("\n    }");
        }

        json.Append(manifest.Resources.Count == 0 ? "]\n" : "\n  ]\n");
        json.Append("}\n");
        return json.ToString();
    }

    /// <summary>Reads a manifest.</summary>
    /// <exception cref="FormatException">The text is not JSON, or not shaped like a manifest.</exception>
    public static AssemblyInfoManifest Read(string text)
    {
        var root = Parse(text) as Dictionary<string, object?> ?? throw new FormatException("The manifest is not a JSON object.");

        var manifest = new AssemblyInfoManifest
        {
            SchemaVersion = root.TryGetValue("schemaVersion", out var version) && version is double number
                ? (int)number
                : throw new FormatException("The manifest has no numeric schemaVersion."),
            Generator = GetString(root, "generator"),
            AssemblyName = GetString(root, "assemblyName") ?? throw new FormatException("The manifest has no assemblyName."),
            TargetFramework = GetString(root, "targetFramework"),
        };

        foreach (var item in GetArray(root, "constants"))
        {
            var constant = item as Dictionary<string, object?> ?? throw new FormatException("A constant is not a JSON object.");
            manifest.Constants.Add(
                new(
                    GetString(constant, "path") ?? throw new FormatException("A constant has no path."),
                    GetString(constant, "value") ?? "",
                    GetString(constant, "type") ?? "string",
                    GetString(constant, "comment")
                )
            );
        }

        foreach (var item in GetArray(root, "resources"))
        {
            var resource = item as Dictionary<string, object?> ?? throw new FormatException("A resource is not a JSON object.");
            manifest.Resources.Add(
                new(
                    GetString(resource, "path") ?? throw new FormatException("A resource has no path."),
                    GetString(resource, "manifestResourceName") ?? throw new FormatException("A resource has no manifestResourceName."),
                    GetString(resource, "kind") ?? ManifestResource.Binary,
                    GetString(resource, "comment")
                )
            );
        }

        return manifest;
    }

    internal static string? GetString(Dictionary<string, object?> json, string name) =>
        json.TryGetValue(name, out var value) switch
        {
            false => null,
            true when value is null => null,
            true when value is string text => text,
            _ => throw new FormatException($"'{name}' is not a string."),
        };

    internal static List<object?> GetArray(Dictionary<string, object?> json, string name) =>
        !json.TryGetValue(name, out var value) || value is null
            ? []
            : value as List<object?> ?? throw new FormatException($"'{name}' is not an array.");

    internal static StringBuilder AppendMember(StringBuilder json, string indent, string name, string value)
    {
        json.Append(indent);
        AppendString(json, name);
        json.Append(": ");
        AppendString(json, value);
        return json;
    }

    /// <summary>Parses JSON into dictionaries, lists, strings, doubles, booleans and nulls.</summary>
    /// <exception cref="FormatException">The text is not JSON.</exception>
    internal static object? Parse(string text) => new Reader(text).ReadDocument();

    private static void AppendString(StringBuilder json, string value)
    {
        json.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    json.Append("\\\"");
                    break;
                case '\\':
                    json.Append("\\\\");
                    break;
                case '\n':
                    json.Append("\\n");
                    break;
                case '\r':
                    json.Append("\\r");
                    break;
                case '\t':
                    json.Append("\\t");
                    break;
                // Line and paragraph separators are valid in JSON strings, but not in some readers' idea of a line.
                case < ' ' or (char)0x2028 or (char)0x2029:
                    json.Append("\\u").Append(( (int)c ).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    json.Append(c);
                    break;
            }
        }

        json.Append('"');
    }

    /// <summary>
    /// A JSON reader that turns the text into dictionaries, lists, strings, doubles, booleans and nulls.
    /// </summary>
    private sealed class Reader(string text)
    {
        private int _position;

        public object? ReadDocument()
        {
            // A UTF-8 byte order mark read as text.
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                _position = 1;

            var value = ReadValue();
            SkipWhitespace();
            return _position == text.Length ? value : throw Error("Unexpected text after the JSON value");
        }

        private object? ReadValue()
        {
            SkipWhitespace();

            return _position >= text.Length
                ? throw Error("Unexpected end of the JSON text")
                : text[_position] switch
                {
                    '{' => ReadObject(),
                    '[' => ReadArray(),
                    '"' => ReadString(),
                    't' => ReadLiteral("true", true),
                    'f' => ReadLiteral("false", false),
                    'n' => ReadLiteral("null", null),
                    _ => ReadNumber(),
                };
        }

        private Dictionary<string, object?> ReadObject()
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            _position++;
            SkipWhitespace();
            if (TryConsume('}'))
                return result;

            while (true)
            {
                SkipWhitespace();
                if (_position >= text.Length || text[_position] != '"')
                    throw Error("Expected a member name");

                var name = ReadString();
                SkipWhitespace();
                Expect(':');
                // The last of duplicate members wins, like most readers.
                result[name] = ReadValue();
                SkipWhitespace();
                if (TryConsume('}'))
                    return result;

                Expect(',');
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            _position++;
            SkipWhitespace();
            if (TryConsume(']'))
                return result;

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();
                if (TryConsume(']'))
                    return result;

                Expect(',');
            }
        }

        private string ReadString()
        {
            var result = new StringBuilder();
            _position++;
            while (true)
            {
                if (_position >= text.Length)
                    throw Error("Unterminated string");

                var c = text[_position++];
                if (c == '"')
                    return result.ToString();

                if (c < ' ')
                    throw Error("Control character in a string");

                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                if (_position >= text.Length)
                    throw Error("Unterminated escape");

                var escape = text[_position++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        result.Append(escape);
                        break;
                    case 'b':
                        result.Append('\b');
                        break;
                    case 'f':
                        result.Append('\f');
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    case 'u':
                        result.Append(ReadHexCharacter());
                        break;
                    default:
                        throw Error($"Invalid escape '\\{escape}'");
                }
            }
        }

        private char ReadHexCharacter()
        {
            if (_position + 4 > text.Length)
                throw Error("Invalid \\u escape");

            var code = 0;
            for (var end = _position + 4; _position < end; _position++)
            {
                var c = text[_position];
                code = code * 16
                     + c switch
                     {
                         >= '0' and <= '9' => c - '0',
                         >= 'a' and <= 'f' => c - 'a' + 10,
                         >= 'A' and <= 'F' => c - 'A' + 10,
                         _ => throw Error("Invalid \\u escape"),
                     };
            }

            return (char)code;
        }

        private double ReadNumber()
        {
            var start = _position;
            while (_position < text.Length && text[_position] is '+' or '-' or '.' or 'e' or 'E' or >= '0' and <= '9')
                _position++;

            // netstandard2.0 (the tasks and the generators) has no span overload of double.TryParse.
#pragma warning disable CA1846
            return double.TryParse(text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
#pragma warning restore CA1846
                ? number
                : throw Error("Invalid value");
        }

        private object? ReadLiteral(string literal, object? value)
        {
            if (string.CompareOrdinal(text, _position, literal, 0, literal.Length) != 0)
                throw Error("Invalid value");

            _position += literal.Length;
            return value;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && text[_position] is ' ' or '\t' or '\n' or '\r')
                _position++;
        }

        private bool TryConsume(char c)
        {
            if (_position >= text.Length || text[_position] != c)
                return false;

            _position++;
            return true;
        }

        private void Expect(char c)
        {
            if (!TryConsume(c))
                throw Error($"Expected '{c}'");
        }

        private FormatException Error(string message) => new($"{message} at position {_position}.");
    }
}
