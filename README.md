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

- `LangVersion=preview`, `Features=strict`, `Nullable`, `ImplicitUsings`, `ProduceReferenceAssembly`
- Default `TargetFramework` (the SDK's newest, e.g. `net10.0`) — a csproj can be a single line
- `AnalysisMode=AllEnabledByDefault`, `AnalysisLevel=latest`, .NET analyzers on
- Roslynator, BannedApiAnalyzers and Microsoft.CodeAnalysis.Analyzers injected (versions pinned by the SDK)
- Shared editorconfig profiles (common, tests, samples, file-based apps) applied as global analyzer configs — your own `.editorconfig` always wins
- `NuGetAudit` (mode `all`, level `moderate`)
- Source link / deterministic CI builds auto-detected (`ContinuousIntegrationBuild`)
- `TreatWarningsAsErrors` in CI and Release builds only
- A `BannedSymbols.txt` found next to (or above) your project is wired up automatically
- Package metadata auto-discovery: `README.md` next to the project, `LICENSE`/`packageicon.png` up the tree; `Rocket.Surgery.*` projects get RSG branding defaults
- SBOM generation on pack in CI (`Microsoft.Sbom.Targets`, opt out with `GenerateSBOM=false`)
- Package validation: set `RocketSurgeryPackageValidationBaseline` to your last released version
- `RollForward=LatestMajor` for executables; assemblies carry a `RocketSurgery.Sdk.Name` metadata attribute
- Applied editorconfigs/BannedSymbols are embedded into `-bl` binary logs for diagnosability

### Test projects

Use `Rocket.Surgery.Sdk.Test`, then just reference your framework. Detection is automatic:

- `TUnit` → `Rocket.Surgery.Extensions.Testing.TUnit`
- `xunit.v3` → `Rocket.Surgery.Extensions.Testing.XUnit3`

Every test project also gets `Microsoft.Testing.Extensions.CrashDump`, `HangDump`, `TrxReport`,
`Retry`, `HotReload` and `CodeCoverage` — with the matching command-line options wired through
`dotnet test` automatically: crash/hang dumps (mini, 10-minute hang timeout), TRX reports, and
`--minimum-expected-tests 1` so a run that discovers zero tests fails. Coverage (cobertura, via
the SDK's default `coverage.runsettings`) is collected in CI by default; set
`RocketSurgeryCodeCoverage=true` to also collect locally. Analyzers are skipped when compiling
for `dotnet test` (`RocketSurgeryOptimizeTestBuild=false` to disable). Set
`RocketSurgeryTestRetryCount` to enable flaky-test retries.

## Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `RocketSurgeryAnalyzers` | `true` | All injected analyzers |
| `RocketSurgeryRoslynator` / `RocketSurgeryBannedApiAnalyzers` / `RocketSurgeryCodeAnalysisAnalyzers` | `true` | Individual analyzers |
| `RocketSurgeryEditorConfig` | `true` | Shipped editorconfig profiles |
| `RocketSurgeryWarningsAsErrors` | `true` | CI/Release `TreatWarningsAsErrors` |
| `RocketSurgeryDefaultWarningsAsErrors` | `true` | `RS0017` in `WarningsAsErrors` |
| `RocketSurgerySourceLink` | `true` | Source link/symbol defaults |
| `IsSampleProject` | `false` | Sample profile (no packing, relaxed API tracking) |
| `RocketSurgeryTestingExtensions` | `true` | All test wiring |
| `RocketSurgeryTestingTUnit` / `RocketSurgeryTestingXUnit3` | `true` | Framework adapters |
| `RocketSurgeryTestingCrashDump` / `HangDump` / `TrxReport` / `Retry` / `HotReload` | `true` | MTP extensions |
| `RocketSurgeryCodeCoverage` | CI-only | Coverage collection (`true` = always, `false` = never) |
| `RocketSurgeryCoverageRunSettings` | — | Path to your own runsettings |
| `RocketSurgeryHangDumpTimeout` | `10m` | Hang dump timeout |
| `RocketSurgeryMinimumExpectedTests` | `1` | Fail on empty test runs (`0` disables) |
| `RocketSurgeryTestRetryCount` | — | Retries for failed tests |
| `RocketSurgeryOptimizeTestBuild` | `true` | Skip analyzers when building for `dotnet test` |
| `RocketSurgeryPackageMetadata` | `true` | README/LICENSE/icon auto-discovery |
| `RocketSurgeryPackageValidationBaseline` | — | Enables package validation against a released version |
| `RocketSurgeryPublicApiAnalyzers` | `false` | Inject Microsoft.CodeAnalysis.PublicApiAnalyzers |
| `GenerateSBOM` | CI-only | SBOM generation on pack |

Injected package versions are plain properties (`RoslynatorVersion`, `TUnitVersion`,
`RocketSurgeryExtensionsTestingVersion`, `MicrosoftTestingExtensionsVersion`,
`MicrosoftTestingExtensionsCodeCoverageVersion`, …) — set them anywhere to override.

> Migrating a repo that used `GlobalPackageReference` for these analyzers? Remove those entries —
> the SDK injects them.

## Versioning & releases

Versions follow `<year>.<month>.<build>` (e.g. `2026.7.1`) and a release is published weekly by
GitHub Actions using [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
(OIDC — no API keys). One-time setup: on nuget.org, add a trusted publishing policy for
`RocketSurgeonsGuild/rsg-sdk` with workflow `release.yml`.
