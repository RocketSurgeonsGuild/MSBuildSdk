using System.Reflection;

namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Allows easy access to embedded resources.
/// </summary>
public static class EmbeddedResource
{
#if DEBUG
    private static readonly string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
#endif

    /// <summary>
    /// Gets the content of the embedded resource at the specified relative path.
    /// </summary>
    public static string GetContent(string relativePath)
    {
        using var stream = GetStream(relativePath);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets the bytes of the embedded resource at the specified relative path.
    /// </summary>
    public static byte[] GetBytes(string relativePath)
    {
        // Stream.Read may return fewer bytes than requested; copy the whole stream instead.
        using var stream = GetStream(relativePath);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Gets the stream of the embedded resource at the specified relative path.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static Stream GetStream(string relativePath)
    {
        var baseName = Assembly.GetExecutingAssembly().GetName().Name;
        var resourceName = relativePath
                          .TrimStart('.')
                          .Replace('/', '.')
                          .Replace('\\', '.');

        var manifestResourceName = Assembly.GetExecutingAssembly()
                                           .GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(resourceName, StringComparison.Ordinal));

        return string.IsNullOrEmpty(manifestResourceName)
            ? throw new InvalidOperationException($"Did not find required resource ending in '{resourceName}' in assembly '{baseName}'.")
            : Assembly.GetExecutingAssembly().GetManifestResourceStream(manifestResourceName) ??
            throw new InvalidOperationException($"Did not find required resource '{manifestResourceName}' in assembly '{baseName}'.");
    }
}
