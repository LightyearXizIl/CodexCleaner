using System.Diagnostics;
using System.Text;
using CodexCleaner.Core;

namespace CodexCleaner.Services;

public sealed class ExternalCommandRunner : IExternalCommandRunner
{
    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var resolved = Resolve(executable);
        if (resolved is null) return new CommandResult(false, -1, string.Empty, $"找不到 {executable}", false);
        using var process = new Process { StartInfo = new ProcessStartInfo(resolved) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { TryKill(process); return new CommandResult(true, -1, await SafeRead(output), await SafeRead(error), true); }
        return new CommandResult(true, process.ExitCode, await SafeRead(output), await SafeRead(error), false);
    }

    private static string? Resolve(string executable)
    {
        if (Path.IsPathFullyQualified(executable) && File.Exists(executable)) return executable;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? executable : executable + ".exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<string> SafeRead(Task<string> task) { try { return (await task).Length > 64_000 ? (await task)[..64_000] : await task; } catch { return string.Empty; } }
    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch { } }
}
