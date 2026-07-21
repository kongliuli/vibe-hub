using VibeHub.Core.Models;

namespace VibeHub.Core.Adapters;

/// <summary>
/// Cursor <c>agent</c> CLI adapter. Discover() is false until installed:
/// <c>irm 'https://cursor.com/install?win32=true' | iex</c>
/// </summary>
public sealed class CursorAgentAdapter : IProviderAdapter
{
    private string? _cliPath;

    /// <summary>Test override; production uses PATH.</summary>
    public string? CliPathOverride { get; set; }

    /// <summary>Optional override for transcript roots (tests).</summary>
    public string? ProjectsRootOverride { get; set; }

    public string ProviderId => "cursor-agent";

    public string InstallHint =>
        "本机未检测到 agent CLI。安装（PowerShell）：irm 'https://cursor.com/install?win32=true' | iex";

    public string ProjectsRoot => ProjectsRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cursor", "projects");

    public bool Discover()
    {
        // Explicit override (incl. missing path) isolates tests from a real local install.
        if (CliPathOverride is not null)
        {
            _cliPath = File.Exists(CliPathOverride) ? CliPathOverride : null;
            return _cliPath is not null;
        }

        _cliPath = CliResolver.FindCursorAgent();
        return _cliPath is not null;
    }

    public ProcessStartSpec BuildStart(string cwd)
    {
        EnsureCli();
        return new ProcessStartSpec(_cliPath!, [], cwd);
    }

    public ProcessStartSpec BuildResume(string cwd, string sessionId)
    {
        EnsureCli();
        // design/02: agent --resume="<chatId>"
        return new ProcessStartSpec(_cliPath!, [$"--resume={sessionId}"], cwd);
    }

    /// <summary>Headless stream-json (primary transcript channel).</summary>
    public ProcessStartSpec BuildHeadless(string cwd, string prompt)
    {
        EnsureCli();
        return new ProcessStartSpec(
            _cliPath!,
            ["-p", prompt, "--output-format", "stream-json"],
            cwd);
    }

    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string? cwd, CancellationToken ct = default)
    {
        // Primary archive path is stream-json capture (P6+). IDE transcripts are a read-only supplement.
        if (!Directory.Exists(ProjectsRoot))
            return Task.FromResult<IReadOnlyList<SessionInfo>>([]);

        var list = new List<SessionInfo>();
        foreach (var jsonl in Directory.EnumerateFiles(ProjectsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (jsonl.IndexOf("agent-transcripts", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var id = Path.GetFileNameWithoutExtension(jsonl);
            list.Add(new SessionInfo(
                id,
                ProviderId,
                id,
                Title: id,
                Cwd: cwd,
                StartedAt: new DateTimeOffset(File.GetLastWriteTimeUtc(jsonl))));
        }

        return Task.FromResult<IReadOnlyList<SessionInfo>>(
            list.OrderByDescending(s => s.StartedAt).Take(200).ToList());
    }

    private void EnsureCli()
    {
        if (_cliPath is null && !Discover())
            throw new InvalidOperationException(InstallHint);
    }
}
