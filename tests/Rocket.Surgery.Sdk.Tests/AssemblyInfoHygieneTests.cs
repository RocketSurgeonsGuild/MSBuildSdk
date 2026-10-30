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

    // Written by hand rather than with ProjectCreator: the newlines in Constant values have to be exactly the
    // character references above, and ProjectCreator would normalize them when it saves.
    private static string Csproj(SdkTestProject project, string targetFramework, string properties = "", string items = "") =>
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
        </Project>
        """;
}
