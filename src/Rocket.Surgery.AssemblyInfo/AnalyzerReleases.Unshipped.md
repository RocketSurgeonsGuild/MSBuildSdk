; Release tracking: https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RSAI003 | AssemblyInfo | Warning | RSAI003: a .resx string mixes named ({name}) and indexed ({0}) format arguments, so no AssemblyInfo.Strings member is generated for it
RSAI004 | AssemblyInfo | Warning | RSAI004: a dependency's .AssemblyInfo.json manifest has a newer schema than this generator reads, so AssemblyInfo.Dependencies only has what the dependency's attributes say
RSAI005 | AssemblyInfo | Warning | RSAI005: a dependency's .AssemblyInfo.json manifest could not be read, so AssemblyInfo.Dependencies only has what the dependency's attributes say
RSAI006 | AssemblyInfo | Info | RSAI006: two dependencies flatten to the same AssemblyInfo.Dependencies name, so the second one gets a numeric suffix
RSAI007 | AssemblyInfo | Error | RSAI007: a constant is under the Dependencies root, which AssemblyInfo.Dependencies uses
