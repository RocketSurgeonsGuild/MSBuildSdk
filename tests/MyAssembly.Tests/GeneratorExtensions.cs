using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.MyAssembly.Tests;

public static class GeneratorExtensions
{
    public static GeneratorTestContextBuilder AddConstant(this GeneratorTestContextBuilder builder, string path, string value, string type, string comment, string root, string rootComment)
    {
        var text = new MyText(path);
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.Constant.ItemType", "Constant")
              .AddOption(text, "build_metadata.Constant.Value", value)
              .AddOption(text, "build_metadata.Constant.Type", type)
              .AddOption(text, "build_metadata.Constant.Comment", comment)
              .AddOption(text, "build_metadata.Constant.Root", root)
              .AddOption(text, "build_metadata.Constant.RootComment", rootComment);
    }
    public static GeneratorTestContextBuilder AddEmbeddedResource(this GeneratorTestContextBuilder builder, string path, string resourceName, string area, string comment)
    {
        var text = new MyText(path);
        return builder
              .AddAdditionalTexts(text)
              .AddOption(text, "build_metadata.EmbeddedResource.Value", resourceName)
              .AddOption(text, "build_metadata.EmbeddedResource.Area", area)
              .AddOption(text, "build_metadata.EmbeddedResource.Comment", comment);
    }

    private class MyText(string name) : AdditionalText
    {
        public override SourceText? GetText(CancellationToken cancellationToken = new CancellationToken()) => SourceText.From("", Encoding.UTF8);

        public override string Path { get; } = name;
    }
}
