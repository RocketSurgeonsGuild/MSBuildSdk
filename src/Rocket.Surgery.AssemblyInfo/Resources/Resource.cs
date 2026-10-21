using System.Diagnostics;

namespace Rocket.Surgery.AssemblyInfo.Resources;

[DebuggerDisplay("{Name}")]
internal record Resource(string Name, string? Comment, bool IsText, string Path);
