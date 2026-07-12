# Rocket.Surgery.Sdk

MSBuild project SDKs that distill the Rocket Surgeons Guild build conventions into a single
versioned package family — compiler defaults, analyzers, standardized editorconfigs, NuGet audit,
source link, and Microsoft.Testing.Platform test wiring.

| Package | Chains to | Use for |
| --- | --- | --- |
| `Rocket.Surgery.Sdk` | Microsoft.NET.Sdk | Libraries, console apps, file-based apps |
| `Rocket.Surgery.Sdk.Web` | Microsoft.NET.Sdk.Web | ASP.NET Core |
| `Rocket.Surgery.Sdk.Razor` | Microsoft.NET.Sdk.Razor | Razor class libraries |
| `Rocket.Surgery.Sdk.BlazorWebAssembly` | Microsoft.NET.Sdk.BlazorWebAssembly | Blazor WASM |
| `Rocket.Surgery.Sdk.Test` | Microsoft.NET.Sdk | Test projects (Microsoft.Testing.Platform) |
| `Rocket.Surgery.Sdk.WindowsDesktop` | Microsoft.NET.Sdk.WindowsDesktop | WPF / WinForms |
| `Rocket.Surgery.Sdk.Script` | Microsoft.NET.Sdk | File-based C# apps (`#:sdk` shorthand) |

## Usage

Pin the version once in `global.json`:

```json
{
    "msbuild-sdks": {
        "Rocket.Surgery.Sdk": "2026.7.0",
        "Rocket.Surgery.Sdk.Test": "2026.7.0"
    }
}
```

```xml
<Project Sdk="Rocket.Surgery.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>
</Project>
```

Or inline: `<Project Sdk="Rocket.Surgery.Sdk/2026.7.0">`.

### File-based C# apps (.NET 10+)

```csharp
#:sdk Rocket.Surgery.Sdk@2026.7.0

Console.WriteLine("Hello");
```

File-based apps are detected automatically and get a relaxed analyzer profile.

## What you get

- `LangVersion=preview`, `Features=strict`, `Nullable=enable`, `ImplicitUsings=enable`, `ProduceReferenceAssembly=true` (unless it's a single-file app)
- Default `TargetFramework` (the SDK's newest, e.g. `net10.0`) — a csproj can be a single line
- `AnalysisMode=AllEnabledByDefault`, `AnalysisLevel=latest`, `EnableNETAnalyzers=true`
- `GenerateDocumentationFile=true` for regular projects (off for test/sample/single-file-app projects)
- Roslynator, BannedApiAnalyzers, PublicApiAnalyzers and Microsoft.CodeAnalysis.Analyzers injected (versions pinned by the SDK, individually overridable)
- Shared editorconfig profiles (common, coding style, JetBrains, Roslynator, tests, samples, file-based apps, per-SDK-variant) applied as global analyzer configs — your own `.editorconfig` always wins
- `NuGetAudit=true` (mode `all`, level `moderate`)
- Source link / deterministic CI builds auto-detected via `ContinuousIntegrationBuild` (GitHub Actions, Azure DevOps, GitLab CI env vars); build-provenance `AssemblyMetadata` (commit SHA, run/job/pipeline IDs, etc.) is embedded automatically on those providers
- `TreatWarningsAsErrors=true` in CI and Release builds only; `WarningsAsErrors` always includes `RS0017` (broken public API tracking)
- A `BannedSymbols.txt` (and, if not disabled, `BannedSymbols.NewtonsoftJson.txt`) is wired up automatically
- SBOM generation on pack, but only for packable projects in CI (`Microsoft.Sbom.Targets`, opt out with `GenerateSBOM=false`)
- Package validation: set `PackageValidationBaselineVersion` to your last released version to turn on `EnablePackageValidation`
- `RollForward=LatestMajor` for non-test executables; assemblies carry a `RocketSurgery.Sdk.Name` metadata attribute (when `GenerateAssemblyInfo=true`)
- Source-linked, portable-PDB, symbol-package defaults (`EmbedUntrackedSources`, `IncludeSymbols`, `DebugType=portable`)
- Publish/copy defaults: `PublishRepositoryUrl`, `PublishDocumentationFile`, `PublishReferencesDocumentationFiles`, `PublishReferencesSymbols`, `CopyDebugSymbolFilesFromPackages`, `CopyDocumentationFilesFromPackages` all default `true`
- Applied editorconfigs/BannedSymbols are embedded into `-bl` binary logs for diagnosability

### Test projects

Use `Rocket.Surgery.Sdk` (or `Rocket.Surgery.Sdk.Test`, which also sets `IsTestProject=true` and
`UseMicrosoftTestingPlatformRunner=true` up front), then just reference your framework. Detection
is automatic, based on your `PackageReference`s:

- `TUnit` → `Rocket.Surgery.Extensions.Testing.TUnit`

Every test project also gets `Microsoft.Testing.Extensions.CrashDump`, `HangDump`, `TrxReport` and
`HotReload` on by default, plus `CodeCoverage` in CI — with the matching command-line options
wired through `dotnet test` automatically: crash/hang dumps (mini, 10-minute hang timeout), TRX
reports, and (once `RocketSurgeryTestingExtensions` is on) `--minimum-expected-tests 1` so a run
that discovers zero tests fails — set `RocketSurgeryMinimumExpectedTests=0` to disable. Coverage
(cobertura, via the SDK's default `coverage.runsettings`, overridable with `RunSettingsFilePath`)
is collected in CI by default; set `RocketSurgeryCodeCoverage=true` to also collect locally, or
`=false` to disable entirely. Analyzers are skipped when compiling for `dotnet test`
(`RocketSurgeryOptimizeTestBuild=true` to enable — it's opt-in). `Microsoft.Testing.Extensions.Retry`
is opt-in too: set `RocketSurgeryTestingRetry=true` and `RocketSurgeryTestRetryCount` to the
number of retries.

## Properties

All defaults are guarded (`Condition="'$(Property)' == ''"`), so anything set in your project,
`Directory.Build.props`, or the command line always wins.

### Analyzers (`src/common/Analyzers.props`)

| Property | Default | Purpose |
| --- | --- | --- |
| `RocketSurgeryAnalyzers` | `true` | Master switch for every analyzer package below |
| `RocketSurgeryPublicApiAnalyzers` | `true` | Inject `Microsoft.CodeAnalysis.PublicApiAnalyzers` |
| `RocketSurgeryBannedApiAnalyzers` | `true` | Inject `Microsoft.CodeAnalysis.BannedApiAnalyzers` + wire up `BannedSymbols.txt` |
| `RocketSurgeryBannedNewtonsoftJson` | `true` | Also wire up `BannedSymbols.NewtonsoftJson.txt` (requires `RocketSurgeryBannedApiAnalyzers`) |
| `RocketSurgeryCodeAnalysisAnalyzers` | `true` | Inject `Microsoft.CodeAnalysis.Analyzers` |
| `RocketSurgeryRoslynatorAnalyzers` | `true` | Inject `Roslynator.Analyzers` + `Roslynator.Formatting.Analyzers` |
| `RocketSurgeryEditorConfig` | `true` | Master switch for the shipped global editorconfig profiles |
| `RocketSurgeryEditorConfigJetBrains` | `true` | Include `JetBrains.editorconfig` |
| `RocketSurgeryEditorConfigRoslynator` | `true` | Include `Roslynator.editorconfig` |

Editorconfig profiles are also applied automatically per project shape — `Tests.editorconfig` when
`IsTestProject=true`, `Samples.editorconfig` when `IsSampleProject=true`,
`SingleFileApp.editorconfig` when `RocketSurgerySingleFileApp=true`, and (if one exists)
`editorconfig/$(RocketSurgerySdkName).editorconfig` per SDK variant — none of these have their own
opt-out property beyond `RocketSurgeryEditorConfig`.

### Compiler & analysis defaults (`src/common/Common.props`)

| Property | Default | Condition |
| --- | --- | --- |
| `LangVersion` | `preview` | — |
| `Features` | `strict` | — |
| `Nullable` | `enable` | — |
| `ImplicitUsings` | `enable` | — |
| `ProduceReferenceAssembly` | `true` | not a single-file app |
| `AnalysisMode` | `AllEnabledByDefault` | — |
| `AnalysisLevel` | `latest` | — |
| `EnableNETAnalyzers` | `true` | — |
| `GenerateDocumentationFile` | `true` | not a test, sample, or single-file-app project (flipped back to `false` for test/sample projects if it was this SDK that defaulted it) |
| `NuGetAudit` | `true` | — |
| `NuGetAuditMode` | `all` | — |
| `NuGetAuditLevel` | `moderate` | — |
| `RocketSurgeryDefaultWarningsAsErrors` | `true` | appends `RS0017` to `WarningsAsErrors` |
| `RocketSurgeryWarningsAsErrors` | `true` | gates whether `TreatWarningsAsErrors` gets defaulted at all |
| `TreatWarningsAsErrors` | `true` | only when `RocketSurgeryWarningsAsErrors=true` **and** (`ContinuousIntegrationBuild=true` **or** `Configuration=Release`) |
| `RocketSurgeryEditorConfig` | `true` | see Analyzers above (also guarded here) |
| `RocketSurgerySourceLink` | `true` | gates the Source Link section below |
| `RocketSurgerySingleFileApp` | `true` | only when `FileBasedProgram=true` (file-based `dotnet run app.cs` apps) |
| `IsTestProject` / `UseMicrosoftTestingPlatformRunner` | `true` | only for `Rocket.Surgery.Sdk.Test` |
| `PublishRepositoryUrl` | `true` | — |
| `PublishDocumentationFile` | `true` | — |
| `PublishReferencesDocumentationFiles` | `true` | — |
| `PublishReferencesSymbols` | `true` | — |
| `CopyDebugSymbolFilesFromPackages` | `true` | — |
| `CopyDocumentationFilesFromPackages` | `true` | — |

`IsSampleProject` (default `false`, never set by the SDK — you opt in) relaxes packing,
documentation, and `RS0017` public-API tracking; see `src/common/Common.targets`.

### Build/publish behavior (`src/common/Common.targets`)

| Property | Default | Condition |
| --- | --- | --- |
| `RollForward` | `LatestMajor` | `OutputType=Exe` and not a test project |
| `GenerateSBOM` | `true` | CI build **and** `IsPackable=true` (opt out with `GenerateSBOM=false`) |
| `EnablePackageValidation` | `true` | only once `PackageValidationBaselineVersion` is set to your last released version |
| `IsPackable` | `false` | test projects and sample projects |

Assemblies get a `RocketSurgery.Sdk.Name` metadata attribute whenever `RocketSurgerySdkName` is set
(one fixed value per SDK package, e.g. `Rocket.Surgery.Sdk.Web`) and `GenerateAssemblyInfo=true`.

### Source Link (`src/common/SourceLink.props`, only when `RocketSurgerySourceLink=true`)

| Property | Default |
| --- | --- |
| `EmbedUntrackedSources` | `true` |
| `IncludeSymbols` | `true` |
| `DebugType` | `portable` |

### Testing (`src/common/Testing.props` / `Testing.targets`, only imported for test projects)

| Property | Default | Condition |
| --- | --- | --- |
| `RocketSurgeryTestingExtensions` | `true` | master switch for everything below |
| `RocketSurgeryTestingCodeCoverage` | `true` | - |
| `RocketSurgeryTestingTrxReport` | `true` | — |
| `RocketSurgeryTestingCrashDump` | `true` | — |
| `RocketSurgeryTestingHangDump` | `true` | — |
| `RocketSurgeryTestingHotReload` | `true` | — |
| `RocketSurgeryTestingRetry` | `false` | opt-in — enables `Microsoft.Testing.Extensions.Retry` |
| `RocketSurgeryMinimumExpectedTests` | `0`, raised to `1` | raised to `1` only when `RocketSurgeryTestingExtensions=true`; set to `0` to disable the "zero tests discovered" failure |
| `RocketSurgeryTestRetryCount` | `0` | only used when `RocketSurgeryTestingRetry=true` |
| `RocketSurgeryHangDumpTimeout` | `10m` | only when `RocketSurgeryTestingExtensions=true` |
| `RocketSurgeryOptimizeTestBuild` | *(unset/off)* | opt-in — set to `true` to skip analyzers when compiling for `dotnet test` |
| `TestingPlatformDotnetTestSupport` | `true` | — |
| `MergeCoverage` | `true` | — |
| `IncludeTestAssembly` | `true` | — |
| `RunSettingsFilePath` | `$(MSBuildThisFileDirectory)coverage.runsettings` | the SDK's own default runsettings; override to point at your own |

Framework adapters (`Rocket.Surgery.Extensions.Testing.TUnit`) are added automatically
by inspecting your `PackageReference`s (`TUnit`) — there is no property to force them
on or off independently of `RocketSurgeryTestingExtensions`.

### Injected package versions (`src/common/Packages.props`)

All overridable by setting the property anywhere before the SDK evaluates it:

| Property | Current default |
| --- | --- |
| `RoslynatorVersion` | `4.15.0` |
| `MicrosoftCodeAnalysisAnalyzersVersion` | `5.3.0` |
| `MicrosoftCodeAnalysisBannedApiAnalyzersVersion` | `4.14.0` |
| `MicrosoftCodeAnalysisPublicApiAnalyzersVersion` | `4.14.0` |
| `MicrosoftSbomTargetsVersion` | `4.1.5` |
| `TUnitVersion` | `1.58.0` |
| `XunitV3Version` | `3.2.2` |
| `RocketSurgeryExtensionsTestingVersion` | `10.0.5` |
| `MicrosoftTestingExtensionsVersion` | `2.2.3` |
| `MicrosoftTestingExtensionsCodeCoverageVersion` | `18.8.0` |

### Continuous integration detection (`src/common/ContinuousIntegration.props`)

`ContinuousIntegrationBuild` is forced `false` during design-time builds; otherwise it's left for
the normal SDK/CI-provider detection to set. When `TF_BUILD`, `GITHUB_ACTIONS`, or `GITLAB_CI` is
present, the matching `ci/*.targets` file also adds provenance `AssemblyMetadata` items (commit
SHA, run/job/pipeline/build IDs, repository info, etc.) pulled from that provider's environment
variables.

> Migrating a repo that used `GlobalPackageReference` for these analyzers? Remove those entries —
> the SDK injects them.

## Versioning & releases

Versions follow `<year>.<month>.<build>` (e.g. `2026.7.1`) and a release is published weekly by
GitHub Actions using [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
(OIDC — no API keys). One-time setup: on nuget.org, add a trusted publishing policy for
`RocketSurgeonsGuild/rsg-sdk` with workflow `release.yml`.
