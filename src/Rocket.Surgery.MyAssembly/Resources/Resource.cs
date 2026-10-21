using System.Diagnostics;

namespace Rocket.Surgery.MyAssembly.Resources;

[DebuggerDisplay("{Name}")]
internal record Resource(string Name, string? Comment, bool IsText, string Path);
