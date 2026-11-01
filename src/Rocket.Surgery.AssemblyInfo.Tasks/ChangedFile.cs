using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rocket.Surgery.AssemblyInfo.Tasks;

internal static class ChangedFile
{
    /// <summary>
    /// Writes <paramref name="content" /> unless the file already holds it, so the timestamp only moves when the content
    /// does: the files these tasks write are compiler inputs, and a newer one recompiles every project that reads it.
    /// </summary>
    public static void Write(string path, string content, TaskLoggingHelper log)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            log.LogMessage(MessageImportance.Low, "{0} is up to date.", path);
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, content, new UTF8Encoding(false));
        log.LogMessage(MessageImportance.Low, "Wrote {0}.", path);
    }
}
