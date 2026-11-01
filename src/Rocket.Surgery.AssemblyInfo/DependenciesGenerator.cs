using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rocket.Surgery.AssemblyInfo.Constants;
using Rocket.Surgery.AssemblyInfo.Dependencies;
using Rocket.Surgery.AssemblyInfo.Manifest;
using Scriban;
using Constant = Rocket.Surgery.AssemblyInfo.Constants.Constant;
using ManifestResource = Rocket.Surgery.AssemblyInfo.Manifest.ManifestResource;

#pragma warning disable RS1035
namespace Rocket.Surgery.AssemblyInfo;

/// <summary>
/// Generates <c>AssemblyInfo.Dependencies</c>: for each reference AssemblyInfo.Dependencies.targets chose, what it exposes.
/// </summary>
/// <remarks>
/// A reference built with Rocket.Surgery.AssemblyInfo has a <c>.AssemblyInfo.json</c> manifest, which the targets pass as an
/// additional file; it gets the same tree that reference generated for itself. Any other reference, and one whose manifest
/// cannot be read, gets what its own attributes and manifest resources say.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public class DependenciesGenerator : IIncrementalGenerator
{
    /// <summary>The class under <c>AssemblyInfo</c> this generator owns.</summary>
    internal const string RootName = "Dependencies";

    private static readonly DiagnosticDescriptor ManifestTooNew = new(
        "RSAI004",
        "Dependency manifest is from a newer Rocket.Surgery.AssemblyInfo",
        "'{0}' has schema version {1}, but this Rocket.Surgery.AssemblyInfo reads up to {2}; AssemblyInfo.Dependencies for {3} only has what its attributes say. Update Rocket.Surgery.AssemblyInfo to get the rest.",
        "AssemblyInfo",
        DiagnosticSeverity.Warning,
        true
    );

    private static readonly DiagnosticDescriptor ManifestUnreadable = new(
        "RSAI005",
        "Dependency manifest could not be read",
        "'{0}' could not be read ({1}); AssemblyInfo.Dependencies for {2} only has what its attributes say",
        "AssemblyInfo",
        DiagnosticSeverity.Warning,
        true
    );

    private static readonly DiagnosticDescriptor NameCollision = new(
        "RSAI006",
        "Two dependencies have the same AssemblyInfo.Dependencies name",
        "'{0}' and '{1}' both become AssemblyInfo.Dependencies.{2}, so '{1}' is AssemblyInfo.Dependencies.{3}",
        "AssemblyInfo",
        DiagnosticSeverity.Info,
        true
    );

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settings = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) =>
            {
                string Get(string name) => provider.GlobalOptions.TryGetValue("build_property." + name, out var value) ? value ?? "" : "";
                return new Settings(
                    Get("AssemblyInfoNamespace") is { Length: > 0 } ns ? ns : null,
                    Get("AssemblyInfoVisibility") is { Length: > 0 } visibility ? visibility : null,
                    Get("AssemblyInfoDependencyReferences"),
                    Get("AssemblyInfoFrameworkDependencyReferences"),
                    Get("EmbeddedResourceStringExtensions")
                );
            }
        );

        var manifests = context
                       .AdditionalTextsProvider.Combine(context.AnalyzerConfigOptionsProvider)
                       .Select(
                            (x, ct) =>
                            {
                                var options = x.Right.GetOptions(x.Left);
                                return !options.TryGetValue("build_metadata.AssemblyInfoDependencyManifest.AssemblyName", out var assemblyName)
                                 || string.IsNullOrEmpty(assemblyName)
                                    ? null
                                    : new ManifestInput(x.Left.Path, assemblyName!, x.Left.GetText(ct)?.ToString() ?? "");
                            }
                        )
                       .Where(x => x is not null)
                       .Collect();

        // Settings is a record of strings, so this only changes when the chosen references or the text extensions do: editing
        // code reuses every reference's result.
        var selection = settings.Select((s, _) => (s.References, s.FrameworkReferences, s.TextExtensions));
        var attributes = context
                        .MetadataReferencesProvider.Combine(selection)
                        .Select((x, ct) => ReadReference(x.Left, x.Right.References, x.Right.FrameworkReferences, x.Right.TextExtensions))
                        .Where(x => x is not null)
                        .Collect();

        context.RegisterSourceOutput(manifests.Combine(attributes).Combine(settings).Combine(context.ParseOptionsProvider), Generate);
    }

    private static void Generate(
        SourceProductionContext spc,
        (((ImmutableArray<ManifestInput?> manifests, ImmutableArray<AttributesInput?> attributes), Settings settings), ParseOptions parse) args
    )
    {
        var (((manifests, attributes), settings), parse) = args;
        var cs = (CSharpParseOptions)parse;
        // ConstantsGenerator reports RSAI002 for this; the namespace would not compile either way.
        if (settings.Namespace is not null && cs.LanguageVersion < LanguageVersion.CSharp10)
            return;

        var dependencies = new SortedDictionary<string, (AssemblyInfoManifest Manifest, bool IsFramework)>(StringComparer.Ordinal);
        foreach (var input in attributes)
        {
            dependencies[input!.AssemblyName] = (ManifestJson.Read(input.Json), input.IsFramework);
        }

        foreach (var input in manifests)
        {
            var isFramework = dependencies.TryGetValue(input!.AssemblyName, out var fallback) && fallback.IsFramework;
            try
            {
                var manifest = ManifestJson.Read(input.Text);
                if (manifest.SchemaVersion > AssemblyInfoManifest.CurrentSchemaVersion)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(ManifestTooNew, Location.None, input.Path, manifest.SchemaVersion, AssemblyInfoManifest.CurrentSchemaVersion, input.AssemblyName)
                    );
                    continue;
                }

                manifest.AssemblyName = input.AssemblyName;
                dependencies[input.AssemblyName] = (manifest, isFramework);
            }
            catch (FormatException e)
            {
                spc.ReportDiagnostic(Diagnostic.Create(ManifestUnreadable, Location.None, input.Path, e.Message, input.AssemblyName));
            }
        }

        var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            // Flattened, so that Foo and Foo.Git cannot collide with each other's areas.
            var flatName = dependency.Key.Replace('.', '_');
            var identifier = Area.EscapeIdentifier(flatName);
            var name = flatName;
            for (var suffix = 2; identifiers.ContainsKey(Area.EscapeIdentifier(name)); suffix++)
            {
                name = $"{flatName}_{suffix}";
            }

            if (Area.EscapeIdentifier(name) != identifier)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(NameCollision, Location.None, identifiers[identifier], dependency.Key, identifier, Area.EscapeIdentifier(name))
                );
            }

            identifiers[Area.EscapeIdentifier(name)] = dependency.Key;
            var hintName = HintName(name);
            try
            {
                spc.AddSource($"{hintName}.g.cs", SourceText.From(RenderConstants(dependency.Value.Manifest, name, settings, cs), Encoding.UTF8));
            }
            catch (ArgumentException e)
            {
                // Area.Load: a value and an area with the same name, which a manifest edited by hand can have.
                spc.ReportDiagnostic(Diagnostic.Create(ManifestUnreadable, Location.None, dependency.Key + AssemblyInfoManifest.FileSuffix, e.Message, dependency.Key));
                continue;
            }

            if (dependency.Value.Manifest.Resources.Count > 0)
            {
                spc.AddSource(
                    $"{hintName}.Resources.g.cs",
                    SourceText.From(RenderResources(dependency.Value.Manifest, name, !dependency.Value.IsFramework, settings, cs), Encoding.UTF8)
                );
            }
        }
    }

    private static string RenderConstants(AssemblyInfoManifest manifest, string name, Settings settings, CSharpParseOptions cs)
    {
        var constants = new List<Constant> { new("AssemblyName", manifest.AssemblyName, ConstantText.DocComment("The name of the assembly.", trimWhitespace: true)) };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constant in manifest.Constants)
        {
            if (!seen.Add(constant.Path))
                continue;

            var value = ConstantText.NormalizeNewLines(constant.Value).Trim('\r', '\n');
            constants.Add(new(constant.Path, value, ConstantText.DocComment(constant.Comment ?? value, trimWhitespace: constant.Comment is not null), constant.Type));
        }

        var root = Area.Load(constants, $"{RootName}.{name}", $"Provides access to what {manifest.AssemblyName} exposes.");
        root.Comment = "Provides access to what the assembly's dependencies expose.";
        return ConstantsGenerator.Render(root, settings.Namespace, settings.Visibility, cs);
    }

    private static string RenderResources(AssemblyInfoManifest manifest, string name, bool accessors, Settings settings, CSharpParseOptions cs)
    {
        const string file = "DependencyResourceAccessors.sbntxt";
        var template = Template.Parse(EmbeddedResource.GetContent(file), file);
        var model = new ResourcesModel(
            ResourceNode.Build(manifest.Resources),
            Area.EscapeIdentifier(name),
            manifest.AssemblyName,
            accessors,
            settings.Namespace,
            "public".Equals(settings.Visibility, StringComparison.OrdinalIgnoreCase)
        );

        var output = template.Render(model, member => member.Name);
        return SyntaxFactory.ParseCompilationUnit(output, options: cs).NormalizeWhitespace(eol: Environment.NewLine).GetText().ToString();
    }

    /// <summary>
    /// What the assembly's own metadata says, for a reference in <paramref name="references" />, as a manifest so that both
    /// kinds of dependency render the same way.
    /// </summary>
    private static AttributesInput? ReadReference(MetadataReference reference, string references, string frameworkReferences, string textExtensions)
    {
        if (reference is not PortableExecutableReference { FilePath: { Length: > 0 } path } executable || !Contains(references, path))
            return null;

        try
        {
            if (executable.GetMetadata() is not AssemblyMetadata metadata)
                return null;

            var reader = metadata.GetModules()[0].GetMetadataReader();
            if (!reader.IsAssembly)
                return null;

            var definition = reader.GetAssemblyDefinition();
            var manifest = new AssemblyInfoManifest { AssemblyName = reader.GetString(definition.Name) };
            manifest.Constants.Add(new("Attributes.Version", definition.Version.ToString()));
            string? informationalVersion = null;
            string? repositoryUrl = null;
            var metadataSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handle in definition.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (GetAttributeTypeName(reader, attribute) is not { Namespace: "System.Reflection" } type)
                    continue;

                var typeName = type.Name;

                CustomAttributeValue<string> value;
                try
                {
                    value = attribute.DecodeValue(AttributeTypeProvider.Instance);
                }
                catch (Exception e) when (e is BadImageFormatException or InvalidOperationException or NotSupportedException)
                {
                    continue;
                }

                if (typeName == "AssemblyMetadataAttribute" && value.FixedArguments is [{ Value: string key }, var entry])
                {
                    // Flattened: a key like Foo.Bar would nest, and then collide with a key Foo.
                    if (metadataSeen.Add(key))
                        manifest.Constants.Add(new("Metadata." + key.Replace('.', '_'), entry.Value as string ?? ""));
                    if (key == "RepositoryUrl")
                        repositoryUrl = entry.Value as string;
                }
                else if (typeName.StartsWith("Assembly", StringComparison.Ordinal)
                      && typeName.EndsWith("Attribute", StringComparison.Ordinal)
                      && typeName.Length > "AssemblyAttribute".Length
                      && value.FixedArguments is [{ Value: string text }])
                {
                    var name = typeName.Substring("Assembly".Length, typeName.Length - "AssemblyAttribute".Length);
                    if (name != "Version")
                        manifest.Constants.Add(new("Attributes." + name, text));
                    if (name == "InformationalVersion")
                        informationalVersion = text;
                }
            }

            // What git information the SDK puts in an assembly: the commit after the + of the informational version
            // (SourceLink), and the repository URL.
            var plus = informationalVersion?.LastIndexOf('+') ?? -1;
            var commit = plus < 0 ? null : informationalVersion![( plus + 1 )..];
            if (commit is { Length: >= 7 } && commit.All(Uri.IsHexDigit))
            {
                manifest.Constants.Add(new("Git.Commit", commit));
                manifest.Constants.Add(new("Git.Sha", commit[..Math.Min(9, commit.Length)]));
            }

            if (!string.IsNullOrEmpty(repositoryUrl))
                manifest.Constants.Add(new("Git.Url", repositoryUrl!));

            var extensions = new HashSet<string>(textExtensions.Split(['|'], StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            foreach (var handle in reader.ManifestResources)
            {
                var resourceName = reader.GetString(reader.GetManifestResource(handle).Name);
                // Without the source path, the name less the assembly name is the closest thing to one.
                var resourcePath = resourceName.StartsWith(manifest.AssemblyName + ".", StringComparison.Ordinal)
                    ? resourceName[( manifest.AssemblyName.Length + 1 )..]
                    : resourceName;
                var isText = extensions.Contains(Path.GetExtension(resourceName));
                manifest.Resources.Add(new(resourcePath, resourceName, isText ? ManifestResource.Text : ManifestResource.Binary));
            }

            manifest.Constants.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            manifest.Resources.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return new(manifest.AssemblyName, ManifestJson.Write(manifest), Contains(frameworkReferences, path));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static (string Namespace, string Name)? GetAttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        // The constructor of an attribute from another assembly is a member reference, of one from this assembly a method.
        if (attribute.Constructor.Kind == HandleKind.MemberReference)
        {
            var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference)
                return null;

            var reference = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            return (reader.GetString(reference.Namespace), reader.GetString(reference.Name));
        }

        if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
            var definition = reader.GetTypeDefinition(method.GetDeclaringType());
            return (reader.GetString(definition.Namespace), reader.GetString(definition.Name));
        }

        return null;
    }

    private static bool Contains(string list, string path) =>
        list.Length > 0 && ( "|" + list + "|" ).IndexOf("|" + path + "|", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string HintName(string name) =>
        string.Concat($"{RootName}.{name}".Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_'));

    private sealed record Settings(string? Namespace, string? Visibility, string References, string FrameworkReferences, string TextExtensions);

    private sealed record ManifestInput(string Path, string AssemblyName, string Text);

    private sealed record AttributesInput(string AssemblyName, string Json, bool IsFramework);

    /// <summary>Decodes attribute arguments with type names as strings; only string arguments are used.</summary>
    private sealed class AttributeTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public static AttributeTypeProvider Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => "";
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => "";
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
