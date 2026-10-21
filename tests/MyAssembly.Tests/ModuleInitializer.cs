using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DiffEngine;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.MyAssembly.Tests;

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
        DerivePathInfo((sourceFile, _, type, method) =>
                       {
                           static string GetTypeName(Type type) =>
                               // ReSharper disable once NullableWarningSuppressionIsUsed
                               type.IsNested ? $"{type.ReflectedType!.Name}.{type.Name}" : type.Name;

                           var typeName = GetTypeName(type);

                           // ReSharper disable once NullableWarningSuppressionIsUsed
                           return new(Path.Combine(Path.GetDirectoryName(sourceFile)!, "snapshots"), typeName, method.Name);
                       }
        );
    }

    [GeneratedRegex(@"^(//\s+MyAssembly(?:\.\w+)?: ).+$")]
    private static partial Regex GeneratorVersionHeader();
}
