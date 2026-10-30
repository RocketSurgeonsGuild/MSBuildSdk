using Microsoft.CodeAnalysis;
using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

/// <summary>
/// <see cref="DependenciesMarkerGenerator" /> publishes a marked assembly's constants as assembly metadata and
/// <see cref="DependenciesGenerator" /> reads them back from the references: the first tests snapshot the producing side,
/// the rest run what a producer generated through a consumer, the way two projects do.
/// </summary>
public class DependenciesGeneratorTests : GeneratorTest
{
    private const string MarkerOption = "build_property.AssemblyInfoMarker";
    private const string DependenciesOption = "build_property.AssemblyInfoDependencies";

    [Test]
    public async Task Marker_Publishes_The_Constants()
    {
        var result = await Producer()
                          .AddConstant("Git.Branch", "main", root: ".")
                          .AddConstant("Git.Root", "/home/runner/work/alpha", root: ".")
                          .AddConstant("Attributes.Version", "1.2.3.0", root: ".")
                          .AddConstant("Metadata.Flavor", "Spicy", root: ".")
                          .AddConstant("Project.AssemblyName", "Acme.Alpha", root: ".")
                          .AddConstant("Greeting", "Hello", comment: "dropped, only values travel")
                          .AddConstant("Count", "123", "int")
                          .AddConstant("Wide", "123", "Int64")
                          .AddConstant("Enabled", "true", "bool")
                          .AddConstant("Ratio", "1.5", "double")
                          .AddConstant("Some.Nested.Value", "deep", root: "Custom.Root")
                          .AddConstant("WithSemiColon", "|A|B|C|")
                          .AddConstant("Multi", "first`nsecond`nthird")
                          .AddConstant("Quoted", "say \"hi\" \\ done")
                          .AddConstant("Unsupported", "TimeSpan.Zero", "TimeSpan")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Marker_Is_Off_By_Default()
    {
        var result = await GeneratorTestContextBuilder
                          .Create()
                          .WithGenerator<DependenciesMarkerGenerator>()
                          .AddConstant("Git.Branch", "main", root: ".")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Copy_Marked_References_Only()
    {
        var alpha = await Producer()
                         .AddConstant("Git.Branch", "main", root: ".")
                         .AddConstant("Attributes.Version", "1.2.3.0", root: ".")
                         .AddConstant("Metadata.Flavor", "Spicy", root: ".")
                         .AddConstant("Greeting", "Hello")
                         .Build()
                         .GenerateAsync();
        var beta = await Producer()
                        .AddConstant("Git.Branch", "release", root: ".")
                        .AddConstant("Attributes.Version", "2.0.0.0", root: ".")
                        .AddConstant("Count", "42", "int")
                        .Build()
                        .GenerateAsync();

        // Acme.Unmarked is an ordinary assembly: no marker, no copy.
        var result = await Consumer(
                                alpha.Publish("Acme.Alpha", "1.2.3.0"),
                                DependencyReferences.Plain("Acme.Unmarked", alpha),
                                beta.Publish("Acme.Beta", "2.0.0.0")
                            )
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Are_Off_By_Default()
    {
        var alpha = await Producer().AddConstant("Git.Branch", "main", root: ".").Build().GenerateAsync();

        var result = await Consumer(alpha.Publish("Acme.Alpha"))
                          .AddGlobalOption(DependenciesOption, "false")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Need_A_Marker()
    {
        // The marker option is off, so the producer publishes nothing: referencing it adds nothing either.
        var notPublished = await GeneratorTestContextBuilder
                                .Create()
                                .WithGenerator<DependenciesMarkerGenerator>()
                                .AddConstant("Git.Branch", "main", root: ".")
                                .Build()
                                .GenerateAsync();

        var result = await Consumer(notPublished.Publish("Acme.NotPublished"))
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Sanitize_Names_And_Handle_Collisions()
    {
        // Foo.Bar, Foo-Bar and Foo_Bar all become Foo_Bar; 1st.Place starts with a digit; class is a keyword; a Dependencies class can not sit in Dependencies, nor a Git class in the Git area.
        var producer = await Producer().AddConstant("Git.Branch", "main", root: ".").Build().GenerateAsync();
        MetadataReference[] references =
        [
            producer.Publish("Foo.Bar"),
            producer.Publish("Foo-Bar"),
            producer.Publish("Foo_Bar"),
            producer.Publish("1st.Place"),
            producer.Publish("class"),
            producer.Publish("Dependencies"),
            producer.Publish("Git"),
        ];

        var result = await Consumer(references)
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Follow_The_Namespace_And_Visibility()
    {
        var alpha = await Producer()
                         .AddConstant("Git.Branch", "main", root: ".")
                         .AddConstant("Count", "123", "int")
                         .Build()
                         .GenerateAsync();

        var result = await Consumer(alpha.Publish("Acme.Alpha"))
                          .AddGlobalOption("build_property.AssemblyInfoNamespace", "My.Namespace")
                          .AddGlobalOption("build_property.AssemblyInfoVisibility", "public")
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Report_A_Format_They_Cannot_Read()
    {
        var alpha = await Producer().AddConstant("Git.Branch", "main", root: ".").Build().GenerateAsync();
        var future = DependencyReferences.Handwritten(
            "Acme.Future",
            """
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo", "2")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Git.Branch", "main")]
            """,
            alpha
        );

        var result = await Consumer(future, alpha.Publish("Acme.Alpha"))
                          .Build()
                          .GenerateAsync();

        // Acme.Alpha is still copied; Acme.Future is named, not guessed at.
        var warnings = result.FinalDiagnostics.Where(d => d.Id == "RSAI004").ToArray();
        await Assert.That(warnings).HasSingleItem();
        await Assert.That(warnings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(warnings[0].GetMessage()).Contains("'Acme.Future'").And.Contains("format '2'");

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Ignore_Values_They_Cannot_Trust()
    {
        // Written by hand, as a hostile or broken reference might: none of these may reach the generated code as written.
        var seed = await Producer().Build().GenerateAsync();
        var odd = DependencyReferences.Handwritten(
            "Acme.Odd",
            """
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo", "1")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Fine", "string:fine \"quoted\"")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Injected", "int:1; static class Evil { }")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Expression", "int:int.MaxValue")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Overflow", "byte:300")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Fraction", "int:1.5")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Hex", "int:0x10")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Negative", "long:-5")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Single", "float:1.25")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Money", "decimal:19.99")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Unknown", "Guid:abc")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.NoType", "no type here")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants", "string:no root")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Value", "string:a")]
            [assembly: System.Reflection.AssemblyMetadata("Rocket.Surgery.AssemblyInfo:Constants.Value.Under", "string:value wins, area dropped")]
            """,
            seed
        );

        var result = await Consumer(odd)
                          .Build()
                          .GenerateAsync();

        await Verify(result);
    }

    [Test]
    public async Task Dependencies_Round_Trip_Values_And_The_Generated_Code_Compiles()
    {
        var alpha = await Producer()
                         .AddConstant("Git.Branch", "release/1.x", root: ".")
                         .AddConstant("Multi", "first`nsecond`nthird")
                         .AddConstant("Count", "123", "int")
                         .AddConstant("Wide", "5000000000", "long")
                         .AddConstant("Enabled", "true", "bool")
                         .AddConstant("Ratio", "1.5", "double")
                         .AddConstant("Money", "19.99", "decimal")
                         .AddConstant("Quoted", "say \"hi\" \\ done")
                         .AddConstant("WithSemiColon", "|A|B|C|")
                         .Build()
                         .GenerateAsync();

        var result = await Consumer(alpha.Publish("Acme.Alpha"))
                          .Build()
                          .GenerateAsync();

        result.AssertCompilationWasSuccessful();
        var constants = result.Assembly!.GetType("AssemblyInfo+Dependencies+Acme_Alpha+Constants", throwOnError: true)!;
        object? Value(string name) => constants.GetField(name)!.GetValue(null);

        await Assert.That(Value("Multi")).IsEqualTo("first\nsecond\nthird");
        await Assert.That(Value("Count")).IsEqualTo(123);
        await Assert.That(Value("Wide")).IsEqualTo(5_000_000_000L);
        await Assert.That((bool)Value("Enabled")!).IsTrue();
        await Assert.That(Value("Ratio")).IsEqualTo(1.5d);
        await Assert.That(Value("Money")).IsEqualTo(19.99m);
        await Assert.That(Value("Quoted")).IsEqualTo("say \"hi\" \\ done");
        await Assert.That(Value("WithSemiColon")).IsEqualTo("A;B;C");

        var git = result.Assembly!.GetType("AssemblyInfo+Dependencies+Acme_Alpha+Git", throwOnError: true)!;
        await Assert.That(git.GetField("Branch")!.GetValue(null)).IsEqualTo("release/1.x");
    }

    // A producing project: the marker generator, switched on, over the constants the targets would hand it.
    private static GeneratorTestContextBuilder Producer() =>
        GeneratorTestContextBuilder
           .Create()
           .WithGenerator<DependenciesMarkerGenerator>()
           .AddGlobalOption(MarkerOption, "true");

    // A consuming project: the dependencies generator, switched on, referencing what the producers built.
    private static GeneratorTestContextBuilder Consumer(params MetadataReference[] references) =>
        GeneratorTestContextBuilder
           .Create()
           .WithProjectName("Consumer")
           .WithGenerator<DependenciesGenerator>()
           .AddGlobalOption(DependenciesOption, "true")
           .AddReferences(references);
}
