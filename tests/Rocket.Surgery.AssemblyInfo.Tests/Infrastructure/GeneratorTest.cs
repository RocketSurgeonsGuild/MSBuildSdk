using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.Testing;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

/// <summary>
/// Base class for in-memory generator runs: every AssemblyInfo generator, with the references the
/// generated code needs. Inputs are added the way MSBuild hands them to the generators (as
/// additional files with build_metadata options).
/// </summary>
public abstract class GeneratorTest() : LoggerTest<TestRecord>(new())
{
    public GeneratorTestContextBuilder Builder { get; } = GeneratorTestContextBuilder
                                                         .Create()
                                                         .WithGenerator<ConstantsGenerator>()
                                                         .WithGenerator<ResourcesGenerator>()
                                                         .WithGenerator<StringsGenerator>()
                                                         .AddReferences(
                                                              typeof(ActivatorUtilities).Assembly,
                                                              typeof(IServiceProvider).Assembly,
                                                              // By name: .NET 11 moved ConcurrentDictionary into System.Private.CoreLib, and
                                                              // typeof() would make the snapshot's reference list differ per framework.
                                                              System.Reflection.Assembly.Load("System.Collections.Concurrent"),
                                                              // Generated resource accessors use System.Linq.
                                                              typeof(Enumerable).Assembly
                                                          );
}

/// <summary>Adds generator inputs shaped like AssemblyInfo.Constants.targets / AssemblyInfo.Resources.targets produce.</summary>
public static class GeneratorTestContextBuilderExtensions
{
    /// <summary>A @(Constant) item.</summary>
    public static GeneratorTestContextBuilder AddConstant(
        this GeneratorTestContextBuilder builder,
        string name,
        string value,
        string? type = null,
        string? comment = null,
        string? root = null,
        string? rootComment = null
    )
    {
        var text = new EmptyAdditionalText(name);
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.Constant.ItemType", "Constant")
              .AddOption(text, "build_metadata.Constant.Value", value)
              .AddOption(text, "build_metadata.Constant.Type", type ?? "")
              .AddOption(text, "build_metadata.Constant.Comment", comment ?? "")
              .AddOption(text, "build_metadata.Constant.Root", root ?? "")
              .AddOption(text, "build_metadata.Constant.RootComment", rootComment ?? "");
    }

    /// <summary>An @(EmbeddedResource) item; <paramref name="path" /> is its project-relative path.</summary>
    public static GeneratorTestContextBuilder AddEmbeddedResource(
        this GeneratorTestContextBuilder builder,
        string path,
        string? kind = null,
        string? comment = null
    )
    {
        var text = new EmptyAdditionalText(path);
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.EmbeddedResource.AssemblyInfoResource", "true")
              .AddOption(text, "build_metadata.EmbeddedResource.Value", path)
              .AddOption(text, "build_metadata.EmbeddedResource.Kind", kind ?? "")
              .AddOption(text, "build_metadata.EmbeddedResource.Comment", comment ?? "");
    }

    /// <summary>
    /// An @(EmbeddedResource) item some other package added, by absolute path: AssemblyInfo.Resources.targets
    /// passes it on like the rest but only its item definitions apply, so Value (and the rest) is empty.
    /// </summary>
    public static GeneratorTestContextBuilder AddForeignEmbeddedResource(this GeneratorTestContextBuilder builder, string path)
    {
        var text = new EmptyAdditionalText(path);
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.EmbeddedResource.AssemblyInfoResource", "true")
              .AddOption(text, "build_metadata.EmbeddedResource.Value", "")
              .AddOption(text, "build_metadata.EmbeddedResource.Kind", "")
              .AddOption(text, "build_metadata.EmbeddedResource.Comment", "");
    }

    private sealed class EmptyAdditionalText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken cancellationToken = default) => SourceText.From("", Encoding.UTF8);
    }
}
