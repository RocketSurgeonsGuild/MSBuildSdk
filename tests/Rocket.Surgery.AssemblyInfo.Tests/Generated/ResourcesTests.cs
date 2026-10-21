// This project runs the AssemblyInfo generators on itself (see the csproj); these tests read what they generated.
// The alias is needed because inside Rocket.Surgery.AssemblyInfo.* the simple name AssemblyInfo is the namespace.
using GeneratedInfo = MyNamespace.AssemblyInfo;

namespace Rocket.Surgery.AssemblyInfo.Tests.Generated;

public class ResourcesTests
{
    [Test]
    public async Task Reads_Text_Resource() => await Assert.That(GeneratedInfo.Resources.Content.Styles.Custom.Text).Contains(".custom");

    [Test]
    public async Task Reads_Binary_Resource() =>
        await Assert.That(System.Text.Encoding.UTF8.GetString(GeneratedInfo.Resources.Content.Styles.Main.GetBytes())).Contains("body");

    [Test]
    public async Task Separates_Same_Name_With_Different_Extensions()
    {
        await Assert.That(GeneratedInfo.Resources.Content.Swagger.swagger_ui.css.GetBytes()).IsNotEmpty();
        await Assert.That(GeneratedInfo.Resources.Content.Swagger.swagger_ui.js.GetBytes()).IsNotEmpty();
    }

    [Test]
    public async Task Escapes_Invalid_File_Characters() => await Assert.That(GeneratedInfo.Resources.webhook_data.Text).IsNotEmpty();
}
