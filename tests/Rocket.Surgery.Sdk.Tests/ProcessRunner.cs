using System.Diagnostics;

namespace Rocket.Surgery.Sdk.Tests;

internal static class ProcessRunner
{
    /// <summary>
    /// Runs a process to completion and captures both redirected streams. .NET 11's
    /// <see cref="Process.RunAndCaptureTextAsync(ProcessStartInfo, CancellationToken)"/> drains them
    /// concurrently; reading one to the end before the other deadlocks once the child fills the second
    /// pipe's buffer.
    /// </summary>
    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAndCaptureAsync(
        ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        var result = await Process.RunAndCaptureTextAsync(startInfo, cancellationToken);
        return (result.ExitStatus.ExitCode, result.StandardOutput, result.StandardError);
    }
}
