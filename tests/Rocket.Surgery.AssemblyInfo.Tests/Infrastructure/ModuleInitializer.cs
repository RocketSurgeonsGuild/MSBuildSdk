using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DiffEngine;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

public static partial class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyGeneratorTextContext.Initialize(DiagnosticSeverity.Error, Customizers.Default, Customizers.ExcludeParseOptions);

        DiffRunner.Disabled = true;
        // Generated headers carry the generator's own assembly version, which follows the repo's
        // calendar version (and differs between local and CI builds).
        VerifierSettings.ScrubLinesWithReplace(line => GeneratorVersionHeader().Replace(line, "$1{version}"));
        // Snapshots live next to the test file, one folder per test class: <dir>/snapshots/<Class>/<Method>.
        DerivePathInfo((sourceFile, _, type, method) => new(
                           Path.Combine(Path.GetDirectoryName(sourceFile)!, "snapshots", type.IsNested ? $"{type.ReflectedType!.Name}.{type.Name}" : type.Name),
                           methodName: method.Name
                       ));
    }

    [GeneratedRegex(@"^(//\s+AssemblyInfo(?:\.\w+)?: ).+$")]
    private static partial Regex GeneratorVersionHeader();
}
