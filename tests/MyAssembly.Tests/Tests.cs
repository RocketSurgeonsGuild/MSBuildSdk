using Rocket.Surgery.MyAssembly.Strings;

using TUnit.Core.Logging;

namespace Rocket.Surgery.MyAssembly.Tests;

#pragma warning disable TUnitAssertions0005/// <summary />
public class SomeTests
{
    /// <summary />
    [Test]
    public async Task CanReadResourceFile() =>
        Assert.NotNull(ResourceFile.Load("Resources.resx", "Strings"));

    /// <summary>
    /// Verifies that the MyAssembly.Info.Title property returns the expected title value.
    /// </summary>
    [Test]
    public async Task CanUseInfo() =>
        await Assert.That(MyNamespace.MyAssembly.Info.Title).IsEqualTo("MyAssembly.Tests");

    /// <summary />
    [Test]
    public async Task CanUseProjectFullFileContents()
    {
        await Assert.That(MyNamespace.MyAssembly.Project.ProjectFile).IsNotEmpty();
        await Assert.That(MyNamespace.MyAssembly.Project.ProjectFile).DoesNotStartWith("|");
    }

    /// <summary />
    [Test]
    public async Task CanUseProjectRepositoryUrl() => await Assert.That(MyNamespace.MyAssembly.Project.RepositoryUrl).IsNotEmpty();

    /// <summary />
    [Test]
    public async Task CanUseConstants() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.Foo.Bar).IsEqualTo("Baz");

    /// <summary />
    [Test]
    public async Task CanUseTypedIntConstant() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.TypedInt).IsEqualTo(123);

    /// <summary />
    [Test]
    public async Task CanUseTypedInt64Constant() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.TypedInt64).IsEqualTo(123);

    /// <summary />
    [Test]
    public async Task CanUseTypedLongConstant() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.TypedLong).IsEqualTo(123);

    /// <summary />
    [Test]
    public async Task CanUseTypedBoolConstant() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.TypedBoolean).IsTrue();

    /// <summary />
    [Test]
    public async Task CanUseTypedDoubleConstant() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.TypedDouble).IsEqualTo(1.23);

    [Test]
    public async Task CanUseFileConstants() =>
        await Assert.That(Path.Combine("Content", "Docs", "License.md")).IsEqualTo(MyNamespace.MyAssembly.Constants.Content.Docs.License);

    /// <summary />
    [Test]
    public async Task CanUseFileConstantInvalidIdentifier() =>
        await Assert.That(Path.Combine("Content", "Docs", "12. Readme (copy).txt")).IsEqualTo(MyNamespace.MyAssembly.Constants.Content.Docs._12._Readme_copy_);

    /// <summary />
    [Test]
    public async Task CanUseMetadata() =>
        await Assert.That(MyNamespace.MyAssembly.Metadata.Foo).IsEqualTo("Bar");

    /// <summary />
    [Test]
    public async Task CanUseHierarchicalMetadata() =>
        await Assert.That(MyNamespace.MyAssembly.Metadata.Root.Foo.Bar).IsEqualTo("Baz");

    /// <summary />
    [Test]
    public async Task CanUseProjectProperty() =>
        await Assert.That(MyNamespace.MyAssembly.Project.Foo).IsEqualTo("Bar");

    /// <summary />
    [Test]
    public async Task CanUseProjectProperty2() =>
        await Assert.That(MyNamespace.MyAssembly.Project.RuntimeIdentifiers).IsNotEmpty();

    /// <summary />
    [Test]
    public async Task CanUseStringsNamedArguments() =>
        Assert.NotNull(MyNamespace.MyAssembly.Strings.Named("hello", "world"));

    /// <summary />
    [Test]
    public async Task CanUseStringsIndexedArguments() =>
        Assert.NotNull(MyNamespace.MyAssembly.Strings.Indexed("hello", "world"));

    /// <summary />
    [Test]
    public async Task CanUseStringsNamedFormattedArguments() =>
        await Assert.That(MyNamespace.MyAssembly.Strings.WithNamedFormat(new DateTime(2020, 3, 20))).IsEqualTo("Year 2020, Month 03");

    /// <summary />
    [Test]
    public async Task CanUseStringsIndexedFormattedArguments() =>
        await Assert.That(MyNamespace.MyAssembly.Strings.WithIndexedFormat(new DateTime(2020, 3, 20))).IsEqualTo("Year 2020, Month 03");

    /// <summary />
    [Test]
    public async Task CanUseStringResource() =>
        await Assert.That(MyNamespace.MyAssembly.Strings.Foo.Bar.Baz).IsEqualTo("Value");

    /// <summary />
    [Test]
    public async Task CanUseTextResource() =>
        Assert.NotNull(MyNamespace.MyAssembly.Resources.Content.Styles.Custom.Text);

    /// <summary />
    [Test]
    public async Task CanUseByteResource() =>
        Assert.NotNull(MyNamespace.MyAssembly.Resources.Content.Styles.Main.GetBytes());

    /// <summary />
    [Test]
    public async Task CanUseSameNameDifferentExtensions() =>
        Assert.NotNull(MyNamespace.MyAssembly.Resources.Content.Swagger.swagger_ui.css.GetBytes());

    /// <summary />
    [Test]
    public async Task CanUseFileInvalidCharacters() =>
        Assert.NotNull(MyNamespace.MyAssembly.Resources.webhook_data.Text);

    /// <summary />
    [Test]
    public async Task CanUseGitConstants() =>
        await Assert.That(MyNamespace.MyAssembly.Git.Commit).IsNotEmpty();

    /// <summary />
    [Test]
    public async Task CanUseGitBranchConstants()
    {
        await Assert.That(MyNamespace.MyAssembly.Git.Branch).IsNotEmpty();
        await TestContext.Current!.GetDefaultLogger().LogInformationAsync(MyNamespace.MyAssembly.Git.Branch);
    }

    /// <summary />
    [Test]
    public async Task CanUseSemicolonsInConstant() =>
        await Assert.That(MyNamespace.MyAssembly.Constants.WithSemiColon).IsEqualTo("A;B;C");
}
