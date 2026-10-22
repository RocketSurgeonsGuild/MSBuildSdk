; Release tracking: https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2026.10.3

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RSAI002 | AssemblyInfo | Error | RSAI002: AssemblyInfoNamespace is set but the project compiles with C# older than 10.0, which file-scoped namespaces require
