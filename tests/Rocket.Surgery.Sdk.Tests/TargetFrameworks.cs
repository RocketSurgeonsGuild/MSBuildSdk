namespace Rocket.Surgery.Sdk.Tests;

/// <summary>
/// The target frameworks every scaffolded consumer project is tested against. Snapshots are shared
/// across them: <see cref="SdkTestProject.VerifyProjects"/> ignores the parameter in verified file
/// names and the serializer scrubs <c>TargetFramework</c>.
/// </summary>
public static class TargetFrameworks
{
    public static IEnumerable<string> All() => ["net10.0", "net11.0"];
}
