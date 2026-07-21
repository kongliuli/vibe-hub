using System.Diagnostics;
using System.Text;
using VibeHub.Core.Models;

namespace VibeHub.Core.Supervisor;

/// <summary>
/// Real Process.Start headless runner for App / Distill. Do not use from unit tests.
/// </summary>
public sealed class ProcessHeadlessRunner : IHeadlessRunner
{
    public async Task<HeadlessRunResult> RunAsync(ProcessStartSpec spec, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in spec.Arguments)
            psi.ArgumentList.Add(a);
        if (spec.Environment is not null)
        {
            foreach (var (k, v) in spec.Environment)
                psi.Environment[k] = v;
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!proc.Start())
            throw new InvalidOperationException("Failed to start: " + spec.FileName);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
            throw;
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new HeadlessRunResult
        {
            ExitCode = proc.ExitCode,
            StdOut = stdout,
            StdErr = stderr
        };
    }
}
