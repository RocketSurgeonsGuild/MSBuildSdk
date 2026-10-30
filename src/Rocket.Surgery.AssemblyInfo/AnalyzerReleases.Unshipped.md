; Release tracking: https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RSAI003 | AssemblyInfo | Warning | RSAI003: a .resx string mixes named ({name}) and indexed ({0}) format arguments, so no AssemblyInfo.Strings member is generated for it
RSAI004 | AssemblyInfo | Warning | RSAI004: a referenced assembly publishes an AssemblyInfo marker format this version can not read, so it is left out of AssemblyInfo.Dependencies
