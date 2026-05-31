using System.Diagnostics;
using System.Text;

namespace ReflowOven.Infrastructure.Platform;

/// <summary>Result of running an external process: exit code + captured stdout/stderr.</summary>
public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs OS commands (nmcli, systemctl, df, …) for the Linux controller. Never throws on a
/// non-zero exit — the caller inspects <see cref="ProcessResult"/> and decides.</summary>
public static class ProcessRunner
{
    /// <summary>Runs with a single pre-tokenized argument string. Use only for <b>static</b> commands with
    /// no caller-supplied values — .NET re-parses this string into argv, so embedded quotes/spaces in a
    /// dynamic value would be mangled or injected. For anything with user/system-derived values, use the
    /// <see cref="RunAsync(string, IEnumerable{string}, CancellationToken)"/> overload.</summary>
    public static Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct = default) =>
        RunCoreAsync(fileName, psi => psi.Arguments = arguments, ct);

    /// <summary>Runs with discrete argument tokens via <see cref="ProcessStartInfo.ArgumentList"/>; each token
    /// is passed to the process verbatim (no shell, no re-tokenization), so quotes/spaces/special characters
    /// in a value can neither be mangled nor injected as extra arguments.</summary>
    public static Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken ct = default) =>
        RunCoreAsync(fileName, psi => { foreach (var a in args) psi.ArgumentList.Add(a); }, ct);

    private static async Task<ProcessResult> RunCoreAsync(string fileName, Action<ProcessStartInfo> configureArgs, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        configureArgs(startInfo);

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
