using TUnit.Assertions.Enums;

namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// End-to-end checks for the packed Rocket.Surgery.AssemblyInfo package in a real consumer build:
/// how newlines survive the trip through the editorconfig file, and that the AssemblyInfoVisibility and
/// AssemblyInfoGitComment properties (and their MyAssembly* fallbacks) reach the generators.
/// </summary>
public class AssemblyInfoHygieneTests
{
    public static IEnumerable<(string TargetFramework, string VisibilityProperty, string GitCommentProperty)> PropertyNames() =>
        from targetFramework in TargetFrameworks.All()
        from names in new[]
        {
            ("AssemblyInfoVisibility", "AssemblyInfoGitComment"),
            ("MyAssemblyVisibility", "MyAssemblyGitComment"),
        }
        select (targetFramework, names.Item1, names.Item2);

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Newlines_RoundTrip_Whatever_The_Line_Ending(string targetFramework)
    {
        // &#10; and &#13; are the only way to put a lone LF or CR into an attribute value.
        using var project = new SdkTestProject();
        project.AddFile("app/app.csproj", Csproj(
                        project,
                        targetFramework,
                        items: """
                            <Constant Include="Lf" Value="first&#10;second" />
                            <Constant Include="CrLf" Value="first&#13;&#10;second" />
                            <Constant Include="Cr" Value="first&#13;second" />
                            <Constant Include="WindowsPath" Value="C:\new\node" />
                            """
                    ))
               .AddFile("app/Program.cs", """
                   static string Show(string value) => value.Replace("\r", "<CR>").Replace("\n", "<LF>");
                   System.Console.Write(string.Join("|", new[]
                   {
                       Show(AssemblyInfo.Constants.Lf),
                       Show(AssemblyInfo.Constants.CrLf),
                       Show(AssemblyInfo.Constants.Cr),
                       Show(AssemblyInfo.Constants.WindowsPath),
                   }));
                   """);

        var output = await project.DotnetRun("app");

        // Every newline becomes the build machine's own newline, and a backslash-n in a value stays two characters.
        var newline = Environment.NewLine.Replace("\r", "<CR>").Replace("\n", "<LF>");
        var multiline = $"first{newline}second";
        await Assert.That(output).IsEqualTo($"{multiline}|{multiline}|{multiline}|C:\\new\\node");
    }

    /// <summary>
    /// Each row is a Constant: the Value attribute as it is written in the project (MSBuild wants $ as %24 and % as
    /// %25 there) and the text the generated constant must hold. Whatever the characters, the value has to get from the
    /// project through the targets, the editorconfig file and the generator unchanged, newlines aside, which
    /// become the build machine's own newline.
    /// </summary>
    private static readonly (string Name, string Attribute, string Expected)[] SpecialCharacters =
    [
        ("Apostrophe", "Rocket's guild", "Rocket's guild"),
        ("DoubleQuotes", "say &quot;hi&quot; and 'bye'", "say \"hi\" and 'bye'"),
        ("Dollar", "cost %245 and %24(Foo) and %24%24", "cost $5 and $(Foo) and $$"),
        ("Percent", "100%25 and %2541", "100% and %41"),
        ("Semicolons", "A;B;C", "A;B;C"),
        ("Padded", " it's; padded ", " it's; padded "),
        ("PercentInWrapped", "50%2541; done", "50%41; done"),
        ("Tab", "a&#9;b", "a\tb"),
        // The compiler splits the editorconfig file at these as well as at CR and LF, so they count as newlines.
        ("UnicodeLineBreaks", "a&#x85;b&#x2028;c&#x2029;d", $"a{Environment.NewLine}b{Environment.NewLine}c{Environment.NewLine}d"),
        ("Backslashes", @"C:\new\node and \\server\share", @"C:\new\node and \\server\share"),
        ("Multiline", "first&#10;it's second&#13;&#10;third", $"first{Environment.NewLine}it's second{Environment.NewLine}third"),
        ("NonAscii", "héllo ✓ 😀 日本語", "héllo ✓ 😀 日本語"),
        ("Backticks", "run `npm` now", "run `npm` now"),
        ("BacktickN", "a`nb", "a`nb"),
        ("DoubledBackticks", "a``b ``n", "a``b ``n"),
        ("BacktickThenNewline", "x`&#10;y", $"x`{Environment.NewLine}y"),
        ("TrailingBacktick", "end`", "end`"),
        ("Everything", "It's &quot;all&quot;; %24(X) 100%25 C:\\new`n&#10;é ✓`", $"It's \"all\"; $(X) 100% C:\\new`n{Environment.NewLine}é ✓`"),
    ];

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Special_Characters_RoundTrip_In_Constant_Values(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddFile("app/app.csproj", Csproj(
                        project,
                        targetFramework,
                        // Description reaches the generated code twice more: as Project.Description and as Attributes.Description.
                        properties: """
                            <Description>Rocket's guild</Description>
                            <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                            <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)/generated</CompilerGeneratedFilesOutputPath>
                            """,
                        items: string.Join(
                            Environment.NewLine,
                            SpecialCharacters
                               .Select(row => $"""<Constant Include="{row.Name}" Value="{row.Attribute}" />""")
                               .Append("""<ProjectProperty Include="Description" />""")
                               .Append("""<Constant Include="Documented" Value="value" Comment="Don't break &amp; don't `n escape" />""")
                        )
                    ))
               .AddFile("app/Program.cs", $$"""
                   static string Show(string value) => string.Concat(value.Select(c => c switch
                   {
                       '\r' => "<CR>",
                       '\n' => "<LF>",
                       < ' ' or > '~' => "\\u" + ((int)c).ToString("X4"),
                       _ => c.ToString(),
                   }));
                   {{string.Join(Environment.NewLine, SpecialCharacters.Select(row => $"""System.Console.WriteLine("{row.Name}=" + Show(AssemblyInfo.Constants.{row.Name}));"""))}}
                   System.Console.WriteLine("ProjectDescription=" + Show(AssemblyInfo.Project.Description));
                   System.Console.WriteLine("AttributeDescription=" + Show(AssemblyInfo.Attributes.Description));
                   """);

        var output = await project.DotnetRun("app");

        var expected = SpecialCharacters
                      .Select(row => $"{row.Name}={Show(row.Expected)}")
                      .Append("ProjectDescription=Rocket's guild")
                      .Append("AttributeDescription=Rocket's guild");
        await Assert.That(output.Split(Environment.NewLine)).IsEquivalentTo(expected, CollectionOrdering.Matching);

        // A comment goes through the same path, and into the XML doc comment.
        var generatedDocumented = Path.Combine(
            project.Directory,
            "app",
            "generated",
            "Rocket.Surgery.AssemblyInfo",
            "Rocket.Surgery.AssemblyInfo.ConstantsGenerator",
            "Constants.Documented.g.cs"
        );
        await Assert.That(File.ReadAllText(generatedDocumented)).Contains("/// Don't break &amp; don't `n escape");
    }

    [Test]
    [MethodDataSource(nameof(PropertyNames))]
    public async Task Visibility_And_GitComment_Properties_Apply(string targetFramework, string visibilityProperty, string gitCommentProperty)
    {
        using var project = new SdkTestProject();
        project.AddFile("app/app.csproj", Csproj(
                        project,
                        targetFramework,
                        properties: $"""
                            <{visibilityProperty}>public</{visibilityProperty}>
                            <{gitCommentProperty}>Where this build came from</{gitCommentProperty}>
                            <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                            <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)/generated</CompilerGeneratedFilesOutputPath>
                            """
                    ))
               .AddFile("app/Program.cs", """
                   // The type is only public if the property reached the generators.
                   System.Console.Write(typeof(AssemblyInfo).IsPublic);
                   """);

        var output = await project.DotnetRun("app");

        await Assert.That(output).IsEqualTo("True");

        // The Git area's summary is the only place the comment shows up.
        var generatedGit = Path.Combine(
            project.Directory,
            "app",
            "generated",
            "Rocket.Surgery.AssemblyInfo",
            "Rocket.Surgery.AssemblyInfo.ConstantsGenerator",
            "Git.Branch.g.cs"
        );
        await Assert.That(File.ReadAllText(generatedGit)).Contains("/// Where this build came from");
    }

    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Without_The_Properties_The_Class_Is_Internal_And_The_Git_Comment_Is_The_Default(string targetFramework)
    {
        using var project = new SdkTestProject();
        project.AddFile("app/app.csproj", Csproj(
                        project,
                        targetFramework,
                        properties: """
                            <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                            <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)/generated</CompilerGeneratedFilesOutputPath>
                            """
                    ))
               .AddFile("app/Program.cs", """
                   System.Console.Write(typeof(AssemblyInfo).IsPublic);
                   """);

        var output = await project.DotnetRun("app");

        await Assert.That(output).IsEqualTo("False");
        var generatedGit = Path.Combine(
            project.Directory,
            "app",
            "generated",
            "Rocket.Surgery.AssemblyInfo",
            "Rocket.Surgery.AssemblyInfo.ConstantsGenerator",
            "Git.Branch.g.cs"
        );
        await Assert.That(File.ReadAllText(generatedGit)).Contains("/// Provides access project-defined constants.");
    }

    /// <summary>
    /// Other packages add EmbeddedResource items of their own. Joinery.Dovetail, for one, adds its appsettings files by
    /// absolute path, copied from another item type, with metadata of its own (LogicalName, DovetailConfiguration, ...).
    /// A rooted path takes no part in the Area and FileExtension step of the resource targets, so such an item only has
    /// what the targets' item definitions give it, and the build must not trip over that (MSB4096).
    /// </summary>
    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Embedded_Resources_From_Other_Packages_Do_Not_Break_The_Build(string targetFramework)
    {
        using var project = new SdkTestProject();
        AddResourceApp(
            project,
            targetFramework,
            items: """
                <ForeignSettings Include="$(MSBuildProjectDirectory)/appsettings.json" />
                <EmbeddedResource Include="@(ForeignSettings)" LogicalName="app.json" DovetailConfiguration="true" Visible="false" />
                <EmbeddedResource Include="$(MSBuildProjectDirectory)/appsettings.yaml" LogicalName="app.yaml" DovetailConfiguration="true" Visible="false" />
                <FileConstant Include="$(MSBuildProjectDirectory)/appsettings.yaml" />
                """
        );

        var output = await project.DotnetRun("app");

        await Assert.That(output).IsEqualTo(ResourceAppOutput);
    }

    /// <summary>
    /// The same, for items another package adds once the SDK has already named the resources (which is when it sets
    /// WithCulture and ManifestResourceName on the ones it knows). Nothing defaults those two, so the strings step has
    /// to cope with an EmbeddedResource that has neither.
    /// </summary>
    [Test]
    [MethodDataSource(typeof(TargetFrameworks), nameof(TargetFrameworks.All))]
    public async Task Embedded_Resources_Added_After_The_Sdk_Named_Them_Do_Not_Break_The_Build(string targetFramework)
    {
        using var project = new SdkTestProject();
        AddResourceApp(
            project,
            targetFramework,
            targets: """
                <Target Name="AddForeignResources" AfterTargets="CreateManifestResourceNames">
                  <ItemGroup>
                    <EmbeddedResource Include="$(MSBuildProjectDirectory)/appsettings.json" LogicalName="app.json" DovetailConfiguration="true" Visible="false" />
                    <EmbeddedResource Include="Content/Late.txt" />
                    <FileConstant Include="$(MSBuildProjectDirectory)/appsettings.yaml" />
                  </ItemGroup>
                </Target>
                """
        );
        project.AddFile("app/Content/Late.txt", "late");

        var output = await project.DotnetRun("app");

        // The resources that carry the metadata come out as they always do; the late ones are not part of the output.
        await Assert.That(output).IsEqualTo(ResourceAppOutput);
    }

    // What Program.cs of the app AddResourceApp writes: a text resource and a string from a .resx, both ordinary.
    private const string ResourceAppOutput = "hello|Hi from resx";

    private static void AddResourceApp(SdkTestProject project, string targetFramework, string items = "", string targets = "") =>
        project.AddFile("app/app.csproj", Csproj(
                        project,
                        targetFramework,
                        items: $"""
                            <EmbeddedResource Include="Content/Hello.txt" Kind="Text" Comment="Greeting" />
                            {items}
                            """,
                        targets: targets
                    ))
               .AddFile("app/Program.cs", """
                   System.Console.Write(AssemblyInfo.Resources.Content.Hello.Text + "|" + AssemblyInfo.Strings.Greeting);
                   """)
               .AddFile("app/Content/Hello.txt", "hello")
               .AddFile("app/Strings.resx", Resx("Greeting", "Hi from resx"))
               .AddFile("app/appsettings.json", "{}")
               .AddFile("app/appsettings.yaml", "a: 1");

    private static string Resx(string name, string value) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
          <resheader name="version"><value>2.0</value></resheader>
          <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
          <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
          <data name="{name}" xml:space="preserve"><value>{value}</value></data>
        </root>
        """;

    // The same escaping the Program.cs above uses, so the expected text can be compared line by line.
    private static string Show(string value) =>
        string.Concat(value.Select(c => c switch
        {
            '\r' => "<CR>",
            '\n' => "<LF>",
            < ' ' or > '~' => "\\u" + ( (int)c ).ToString("X4"),
            _ => c.ToString(),
        }));

    // Written by hand rather than with ProjectCreator: the newlines in Constant values have to be exactly the
    // character references above, and ProjectCreator would normalize them when it saves.
    private static string Csproj(SdkTestProject project, string targetFramework, string properties = "", string items = "", string targets = "") =>
        $$"""
        <Project Sdk="Rocket.Surgery.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <OutputType>Exe</OutputType>
            <IsSampleProject>true</IsSampleProject>
            {{properties}}
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Rocket.Surgery.AssemblyInfo" Version="{{project.PackageVersion("Rocket.Surgery.AssemblyInfo")}}" PrivateAssets="all" />
            {{items}}
          </ItemGroup>
          {{targets}}
        </Project>
        """;
}
