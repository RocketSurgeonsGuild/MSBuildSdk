using Microsoft.CodeAnalysis;
using Rocket.Surgery.AssemblyInfo.Tests.Infrastructure;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generators;

public class StringsGeneratorTests : GeneratorTest
{
    // CreateResx starts the data elements on line 5, so "Mixed", the second one, is on line 6.
    private const int MixedLine = 6;

    [Test]
    public async Task Should_Skip_And_Warn_About_Mixed_Named_And_Indexed_Arguments()
    {
        var result = await Builder
                          .AddResx(
                               "Strings/Messages.resx",
                               "Test.Strings.Messages",
                               ("Greeting", "Hello {name}"),
                               ("Mixed", "{0} and {name}"),
                               ("Count", "{0} items")
                           )
                          .Build()
                          .GenerateAsync();

        var warnings = result.FinalDiagnostics.Where(d => d.Id == "RSAI003").ToArray();
        await Assert.That(warnings).HasSingleItem();
        await Assert.That(warnings[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(warnings[0].GetMessage()).Contains("'Mixed'");
        await Assert.That(warnings[0].Location.GetLineSpan().Path).IsEqualTo("Strings/Messages.resx");
        await Assert.That(warnings[0].Location.GetLineSpan().StartLinePosition.Line + 1).IsEqualTo(MixedLine);

        await Verify(result);
    }

    [Test]
    public async Task Should_Not_Warn_When_Arguments_Are_All_Named_Or_All_Indexed()
    {
        var result = await Builder
                          .AddResx(
                               "Strings/Messages.resx",
                               "Test.Strings.Messages",
                               ("Greeting", "Hello {name}"),
                               ("Count", "{0} items"),
                               ("Plain", "Nothing to format")
                           )
                          .Build()
                          .GenerateAsync();

        await Assert.That(result.FinalDiagnostics.Where(d => d.Id == "RSAI003")).IsEmpty();
    }
}
