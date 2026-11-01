using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rocket.Surgery.AssemblyInfo.Manifest;
using Rocket.Surgery.AssemblyInfo.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// Rocket.Surgery.AssemblyInfo's MSBuild tasks on their own: which references AssemblyInfo.Dependencies keeps, and what
/// goes into the <c>.AssemblyInfo.json</c> manifest. <see cref="AssemblyInfoDependenciesTests" /> runs them in real builds.
/// </summary>
public class AssemblyInfoTasksTests
{
    [Test]
    [Arguments("Microsoft.*", "Microsoft.Extensions.Logging", true)]
    [Arguments("Microsoft.*", "Microsoft", false)]
    [Arguments("microsoft.*", "Microsoft.Extensions.Logging", true)]
    [Arguments("System.?", "System.X", true)]
    [Arguments("System.?", "System.XY", false)]
    [Arguments("corelib", "System.Private.CoreLib", false)]
    [Arguments("*corelib", "System.Private.CoreLib", true)]
    [Arguments("*corelib", "mscorlib", false)]
    [Arguments("*cor*lib", "mscorlib", true)]
    [Arguments("Exact.Name", "Exact.Name", true)]
    [Arguments("Exact.Name", "Exact.NameAndMore", false)]
    [Arguments("Exact.Name", "ExactXName", false)]
    public async Task Pattern_Matches_The_Whole_Name(string pattern, string assemblyName, bool expected)
    {
        await Assert.That(new AssemblyNamePattern(pattern).IsMatch(assemblyName)).IsEqualTo(expected);
    }

    [Test]
    public async Task Patterns_Are_Split_On_Semicolons_And_Commas()
    {
        var patterns = AssemblyNamePattern.Parse(["Microsoft.*,System.*; corelib ;;", null, "System.*"]).Select(p => p.Pattern);

        await Assert.That(patterns).IsEquivalentTo(["Microsoft.*", "System.*", "corelib"]);
    }

    [Test]
    public async Task Resolves_Which_Dependencies_To_Keep()
    {
        var task = new ResolveAssemblyInfoDependencies
        {
            BuildEngine = new FakeBuildEngine(),
            References =
            [
                Reference("/nuget/plain/1.0.0/lib/net10.0/Plain.dll", ("NuGetPackageId", "Plain")),
                Reference("/ref/System.Runtime.dll", ("FrameworkReferenceName", "Microsoft.NETCore.App"), ("AssemblyName", "System.Runtime")),
                Reference("/ref/mscorlib.dll", ("FrameworkFile", "true")),
                Reference("/nuget/netstandard.library/2.0.3/build/netstandard2.0/ref/netstandard.dll", ("NuGetPackageId", "NETStandard.Library")),
                Reference("/nuget/microsoft.extensions.logging/lib/net10.0/Microsoft.Extensions.Logging.dll", ("NuGetPackageId", "Microsoft.Extensions.Logging")),
                Reference("/nuget/microsoft.extensions.options/lib/net10.0/Microsoft.Extensions.Options.dll", ("NuGetPackageId", "Microsoft.Extensions.Options")),
                Reference("/nuget/vendor.one/lib/net10.0/Vendor.One.dll", ("NuGetPackageId", "Vendor.One")),
                Reference("/nuget/vendor.two/lib/net10.0/Vendor.Two.dll", ("NuGetPackageId", "Vendor.Two")),
                Reference("/nuget/opted.out/lib/net10.0/Opted.Out.dll", ("NuGetPackageId", "Opted.Out")),
                Reference("/nuget/opted.out/lib/net10.0/Opted.Out.Too.dll", ("NuGetPackageId", "Opted.Out")),
                Reference("/src/proj/bin/Proj.dll", ("ReferenceSourceTarget", "ProjectReference"), ("ReferenceAssembly", "/src/proj/obj/ref/Proj.dll")),
                Reference("/src/proj/bin/Skipped.dll", ("ReferenceSourceTarget", "ProjectReference"), ("AssemblyInfo", "false")),
                // The same assembly twice only counts once.
                Reference("/elsewhere/Plain.dll"),
            ],
            PackageReferences = [Item("Opted.Out", ("AssemblyInfo", "false")), Item("Vendor.Two", ("AssemblyInfo", "true"))],
            Includes = [Item("Microsoft.Extensions.Options"), Item("anything", ("Pattern", "*Runtime"))],
            IgnorePatterns = ["Microsoft.*;System.*", "Vendor.*"],
            IgnoreItems = [Item("ignored", ("Pattern", "Nothing.*"))],
        };

        await Assert.That(task.Execute()).IsTrue();

        // An include (*Runtime) keeps a framework assembly; the other framework assemblies stay out.
        await Assert.That(task.Dependencies.Select(d => d.ItemSpec)).IsEquivalentTo(["Plain", "System.Runtime", "Microsoft.Extensions.Options", "Vendor.Two", "Proj"]);
        var proj = task.Dependencies.Single(d => d.ItemSpec == "Proj");
        await Assert.That(proj.GetMetadata("Source")).IsEqualTo("Project");
        await Assert.That(proj.GetMetadata("CompilePath")).IsEqualTo("/src/proj/obj/ref/Proj.dll");
        await Assert.That(proj.GetMetadata("ReferencePath")).IsEqualTo("/src/proj/bin/Proj.dll");
        await Assert.That(task.Dependencies.Single(d => d.ItemSpec == "Plain").GetMetadata("Source")).IsEqualTo("Package");
        await Assert.That(task.Dependencies.Single(d => d.ItemSpec == "System.Runtime").GetMetadata("IsFramework")).IsEqualTo("true");
    }

    [Test]
    [Arguments("", "false", true, true, false)]
    [Arguments("", "false", false, false, true)]
    [Arguments("", "true", true, false, true)]
    [Arguments("false", "true", false, false, false)]
    [Arguments("true", "false", true, true, true)]
    public async Task Keep_Applies_The_Rules_In_Order(string option, string included, bool framework, bool ignored, bool expected)
    {
        AssemblyNamePattern[] includes = included == "true" ? [new("Lib")] : [];
        AssemblyNamePattern[] ignores = ignored ? [new("Lib")] : [];

        await Assert.That(ResolveAssemblyInfoDependencies.Keep("Lib", option, framework, includes, ignores, out _)).IsEqualTo(expected);
    }

    [Test]
    public async Task Framework_Assemblies_Are_Kept_When_Asked_For()
    {
        var task = new ResolveAssemblyInfoDependencies
        {
            BuildEngine = new FakeBuildEngine(),
            References = [Reference("/ref/System.Runtime.dll", ("FrameworkReferenceName", "Microsoft.NETCore.App"))],
            IncludeFrameworkDependencies = true,
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(task.Dependencies.Select(d => d.GetMetadata("IsFramework"))).IsEquivalentTo(["true"]);
    }

    [Test]
    public async Task Finds_Manifests_Next_To_The_Assembly_Or_In_Lib_For_Reference_Assemblies()
    {
        var root = Directory.CreateTempSubdirectory("assemblyinfo-tasks").FullName;
        try
        {
            File.WriteAllText(Directory.CreateDirectory(Path.Combine(root, "next", "lib", "net10.0")).FullName + "/Next.AssemblyInfo.json", "{}");
            File.WriteAllText(Directory.CreateDirectory(Path.Combine(root, "refs", "lib", "net10.0")).FullName + "/Refs.AssemblyInfo.json", "{}");
            var task = new ResolveAssemblyInfoDependencies
            {
                BuildEngine = new FakeBuildEngine(),
                References =
                [
                    Reference(Path.Combine(root, "next", "lib", "net10.0", "Next.dll"), ("PathInPackage", "lib/net10.0/Next.dll")),
                    Reference(Path.Combine(root, "refs", "ref", "net10.0", "Refs.dll"), ("PathInPackage", "ref/net10.0/Refs.dll")),
                    Reference(Path.Combine(root, "none", "lib", "net10.0", "None.dll"), ("PathInPackage", "lib/net10.0/None.dll")),
                ],
            };

            await Assert.That(task.Execute()).IsTrue();
            var manifests = task.Dependencies.ToDictionary(d => d.ItemSpec, d => d.GetMetadata("Manifest"));
            await Assert.That(manifests["Next"]).IsEqualTo(Path.Combine(root, "next", "lib", "net10.0", "Next.AssemblyInfo.json"));
            await Assert.That(manifests["Refs"]).IsEqualTo(Path.Combine(root, "refs", "lib", "net10.0", "Refs.AssemblyInfo.json"));
            await Assert.That(manifests["None"]).IsEqualTo("");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Manifest_Content_Is_Opt_In()
    {
        var task = new WriteAssemblyInfoManifest
        {
            BuildEngine = new FakeBuildEngine(),
            AssemblyName = "Lib",
            TargetFramework = "net10.0",
            Generator = "test",
            TextExtensions = ".txt|.json",
            Constants =
            [
                Item("Git.Branch", ("Value", "main"), ("Root", "."), ("Manifest", "true")),
                Item("Git.Root", ("Value", "/agent/work"), ("Root", "."), ("Manifest", "false")),
                Item("Answer", ("Value", "42"), ("Type", "int"), ("Comment", "The answer"), ("Manifest", "true")),
                Item("Secret", ("Value", "hunter2")),
                Item("Deep", ("Value", "x"), ("Root", "Some.Root"), ("Manifest", "TRUE")),
                Item("NoDot", ("Value", "y"), ("Root", "."), ("Manifest", "true")),
                // The first of two items with one path wins.
                Item("Git.Branch", ("Value", "other"), ("Root", "."), ("Manifest", "true")),
            ],
            Resources =
            [
                Item("Content/Hello.txt", ("Value", "Content\\Hello.txt"), ("ManifestResourceName", "Lib.Content.Hello.txt")),
                Item("data.bin", ("Value", "data.bin"), ("LogicalName", "custom.name"), ("Kind", "text"), ("Comment", "Data")),
                Item("Strings.resx", ("Value", "Strings.resx"), ("ManifestResourceName", "Lib.Strings")),
                Item("Strings.fr.resx", ("Value", "Strings.fr.resx"), ("ManifestResourceName", "Lib.Strings.fr"), ("WithCulture", "true")),
                Item("hidden.txt", ("Value", "hidden.txt"), ("ManifestResourceName", "Lib.hidden.txt"), ("Manifest", "false")),
                // Added by another package by absolute path: the AssemblyInfo targets give it no Value.
                Item("/other/appsettings.json", ("ManifestResourceName", "Lib.appsettings.json")),
            ],
            ManifestFile = "unused",
        };

        var manifest = task.CreateManifest();

        await Assert.That(manifest.Constants).IsEquivalentTo(
            [
                new ManifestConstant("Constants.Answer", "42", "int", "The answer"),
                new ManifestConstant("Constants.NoDot", "y"),
                new ManifestConstant("Git.Branch", "main"),
                new ManifestConstant("Some.Root.Deep", "x"),
            ]
        );
        await Assert.That(manifest.Resources).IsEquivalentTo(
            [
                new ManifestResource("Content/Hello.txt", "Lib.Content.Hello.txt", ManifestResource.Text),
                new ManifestResource("Strings.resx", "Lib.Strings.resources", ManifestResource.Binary),
                new ManifestResource("data.bin", "custom.name", ManifestResource.Text, "Data"),
            ]
        );
    }

    [Test]
    public async Task Manifest_Is_Only_Rewritten_When_It_Changes()
    {
        var directory = Directory.CreateTempSubdirectory("assemblyinfo-tasks").FullName;
        try
        {
            var file = Path.Combine(directory, "obj", "Lib.AssemblyInfo.json");
            WriteAssemblyInfoManifest Task(string value) => new()
            {
                BuildEngine = new FakeBuildEngine(),
                AssemblyName = "Lib",
                Generator = "test",
                Constants = [Item("Git.Sha", ("Value", value), ("Root", "."), ("Manifest", "true"))],
                ManifestFile = file,
            };

            await Assert.That(Task("abc").Execute()).IsTrue();
            var written = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, written);

            await Assert.That(Task("abc").Execute()).IsTrue();
            await Assert.That(File.GetLastWriteTimeUtc(file)).IsEqualTo(written);

            await Assert.That(Task("def").Execute()).IsTrue();
            await Assert.That(File.GetLastWriteTimeUtc(file)).IsNotEqualTo(written);
            await Assert.That(ManifestJson.Read(File.ReadAllText(file)).Constants.Single().Value).IsEqualTo("def");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task Manifest_Names_The_Package_Version_When_No_Generator_Is_Given()
    {
        var manifest = new WriteAssemblyInfoManifest { BuildEngine = new FakeBuildEngine(), AssemblyName = "Lib", ManifestFile = "unused" }.CreateManifest();

        await Assert.That(manifest.Generator).StartsWith("Rocket.Surgery.AssemblyInfo/");
        await Assert.That(manifest.Generator).DoesNotContain("+");
    }

    [Test]
    public async Task Constants_Are_Written_As_Is_And_Only_Once()
    {
        var task = new WriteAssemblyInfoConstants
        {
            BuildEngine = new FakeBuildEngine(),
            ConstantsFile = "unused",
            Constants =
            [
                Item("Git.Branch", ("Value", "main"), ("Root", "."), ("RootComment", "Git")),
                Item("Tricky", ("Value", "a;b `n c\nnew line 'quoted'"), ("Type", "string"), ("Comment", " ")),
                Item("Answer", ("Value", "42"), ("Type", "int"), ("Comment", "The answer")),
                // Same name and root: the first wins. Same name, other root: kept.
                Item("Answer", ("Value", "43"), ("Type", "int")),
                Item("Answer", ("Value", "44"), ("Root", "Other")),
            ],
        };

        await Assert.That(task.CreateConstants()).IsEquivalentTo(
            [
                new ProjectConstant("Git.Branch", "main", Root: ".", RootComment: "Git"),
                new ProjectConstant("Tricky", "a;b `n c\nnew line 'quoted'", "string"),
                new ProjectConstant("Answer", "42", "int", "The answer"),
                new ProjectConstant("Answer", "44", Root: "Other"),
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching
        );
        await Assert.That(ProjectConstantsJson.Read(ProjectConstantsJson.Write(task.CreateConstants()))).IsEquivalentTo(task.CreateConstants().ToList());
    }

    private static TaskItem Reference(string path, params (string Name, string Value)[] metadata) => Item(path, metadata);

    private static TaskItem Item(string itemSpec, params (string Name, string Value)[] metadata)
    {
        var item = new TaskItem(itemSpec);
        foreach (var (name, value) in metadata)
        {
            item.SetMetadata(name, value);
        }

        return item;
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.proj";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => false;

        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => throw new InvalidOperationException(e.Message);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
