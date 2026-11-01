using Rocket.Surgery.AssemblyInfo.Manifest;

namespace Rocket.Surgery.AssemblyInfo.Tests.Manifest;

public class ManifestJsonTests
{
    // The values that broke the editorconfig hand-off (#98, #101) plus what JSON has to escape.
    public static IEnumerable<string> TrickyValues() =>
    [
        "first\nsecond",
        "first\r\nsecond",
        "first\rsecond",
        "a `n backtick and a `` pair",
        "A;B;C",
        "it's \"quoted\"",
        @"C:\new\node",
        "emoji 😍 and ünïcödé",
        "tab\there",
        "separators \u2028 and \u2029",
        "control \u0001 char",
        "  leading and trailing  ",
        "",
    ];

    [Test]
    [MethodDataSource(nameof(TrickyValues))]
    public async Task Values_RoundTrip(string value)
    {
        var manifest = new AssemblyInfoManifest { AssemblyName = "Lib", TargetFramework = "net10.0", Generator = "test" };
        manifest.Constants.Add(new("Constants.Value", value, "string", value));
        manifest.Resources.Add(new("Content/" + value.Replace("\n", "").Replace("\r", "") + ".txt", "Lib." + value, ManifestResource.Text, value));

        var read = ManifestJson.Read(ManifestJson.Write(manifest));

        await Assert.That(read.Constants.Single()).IsEqualTo(manifest.Constants.Single());
        await Assert.That(read.Resources.Single()).IsEqualTo(manifest.Resources.Single());
    }

    [Test]
    public async Task Writes_A_Stable_Layout()
    {
        var manifest = new AssemblyInfoManifest { AssemblyName = "Lib", TargetFramework = "net10.0", Generator = "Rocket.Surgery.AssemblyInfo/1.2.3" };
        manifest.Constants.Add(new("Git.Branch", "main"));
        manifest.Constants.Add(new("Constants.Answer", "42", "int", "The answer"));
        manifest.Resources.Add(new("Content/Hello.txt", "Lib.Content.Hello.txt", ManifestResource.Text));

        await Verify(ManifestJson.Write(manifest), "json");
    }

    [Test]
    public async Task Writes_Empty_Lists()
    {
        var json = ManifestJson.Write(new() { AssemblyName = "Lib" });

        await Assert.That(json).IsEqualTo("{\n  \"schemaVersion\": 1,\n  \"assemblyName\": \"Lib\",\n  \"constants\": [],\n  \"resources\": []\n}\n");
        await Assert.That(ManifestJson.Read(json).Constants).IsEmpty();
    }

    [Test]
    public async Task Ignores_Unknown_Members_And_Reads_Defaults()
    {
        var manifest = ManifestJson.Read(
            """
            {
              "schemaVersion": 1,
              "assemblyName": "Lib",
              "somethingNew": { "nested": [1, 2.5e3, true, false, null] },
              "constants": [ { "path": "Constants.A", "extra": "x" } ],
              "resources": [ { "path": "a.bin", "manifestResourceName": "Lib.a.bin" } ]
            }
            """
        );

        await Assert.That(manifest.TargetFramework).IsNull();
        await Assert.That(manifest.Constants.Single()).IsEqualTo(new ManifestConstant("Constants.A", "", "string"));
        await Assert.That(manifest.Resources.Single()).IsEqualTo(new ManifestResource("a.bin", "Lib.a.bin"));
    }

    [Test]
    public async Task Reads_A_Byte_Order_Mark_And_Escapes()
    {
        var manifest = ManifestJson.Read("\uFEFF{\"schemaVersion\":2,\"assemblyName\":\"L\\u0069b\\/x\",\"constants\":[],\"resources\":[]}");

        await Assert.That(manifest.SchemaVersion).IsEqualTo(2);
        await Assert.That(manifest.AssemblyName).IsEqualTo("Lib/x");
    }

    [Test]
    [Arguments("")]
    [Arguments("[]")]
    [Arguments("{")]
    [Arguments("{\"assemblyName\":\"Lib\"}")]
    [Arguments("{\"schemaVersion\":1}")]
    [Arguments("{\"schemaVersion\":1,\"assemblyName\":\"Lib\",\"constants\":{}}")]
    [Arguments("{\"schemaVersion\":1,\"assemblyName\":\"Lib\",\"constants\":[{\"value\":\"x\"}]}")]
    [Arguments("{\"schemaVersion\":1,\"assemblyName\":\"Lib\"} trailing")]
    [Arguments("{\"schemaVersion\":1,\"assemblyName\":\"unterminated}")]
    [Arguments("{\"schemaVersion\":1,\"assemblyName\":\"bad \\q escape\"}")]
    public async Task Rejects_What_Is_Not_A_Manifest(string json) => await Assert.That(() => ManifestJson.Read(json)).Throws<FormatException>();
}
