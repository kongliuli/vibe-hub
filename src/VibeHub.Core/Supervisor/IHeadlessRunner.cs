using VibeHub.Core.Models;

namespace VibeHub.Core.Supervisor;

public sealed class HeadlessRunResult
{
    public required int ExitCode { get; init; }
    public required string StdOut { get; init; }
    public string StdErr { get; init; } = "";
}

/// <summary>
/// Run a one-shot CLI with redirected stdout (for Distill / stream-json capture).
/// Unit tests must mock this — never start real processes in default tests.
/// </summary>
public interface IHeadlessRunner
{
    Task<HeadlessRunResult> RunAsync(ProcessStartSpec spec, CancellationToken ct = default);
}
