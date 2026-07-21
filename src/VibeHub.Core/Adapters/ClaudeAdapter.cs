using System.Text.Json;
using VibeHub.Core.Models;
using VibeHub.Core.Transcript;

namespace VibeHub.Core.Adapters;

public sealed class ClaudeAdapter : IProviderAdapter
{
    private string? _cliPath;

    public string? CliPathOverride { get; set; }
    public string? ProjectsRootOverride { get; set; }

    public string ProviderId => "claude";

    public string InstallHint =>
        "未检测到 Claude Code CLI。PowerShell 安装：irm https://claude.ai/install.ps1 | iex";

    public string ProjectsRoot => ProjectsRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    public bool Discover()
    {
        if (CliPathOverride is not null)
        {
            _cliPath = File.Exists(CliPathOverride) ? CliPathOverride : null;
            return _cliPath is not null;
        }

        _cliPath = CliResolver.PreferNonPs1("claude");
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
        return new ProcessStartSpec(_cliPath!, ["--resume", sessionId], cwd);
    }

    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(
        string? cwd,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(ProjectsRoot))
            return Task.FromResult<IReadOnlyList<SessionInfo>>([]);

        var sessions = new List<SessionInfo>();
        foreach (var path in Directory.EnumerateFiles(ProjectsRoot, "*.jsonl", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(300))
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(path);
            var (sessionCwd, title, startedAt) = ReadHeader(path, id);
            if (cwd is not null && sessionCwd is not null
                && !string.Equals(Normalize(cwd), Normalize(sessionCwd), StringComparison.OrdinalIgnoreCase))
                continue;

            sessions.Add(new SessionInfo(
                id,
                ProviderId,
                id,
                title ?? id,
                sessionCwd,
                startedAt ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path))));
        }

        return Task.FromResult<IReadOnlyList<SessionInfo>>(sessions);
    }

    public string? FindTranscript(string sessionId)
    {
        if (!Directory.Exists(ProjectsRoot))
            return null;
        return Directory.EnumerateFiles(ProjectsRoot, sessionId + ".jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    public IReadOnlyList<CanonicalMessage> ReadMessages(string sessionId, int limit = 500)
    {
        var path = FindTranscript(sessionId);
        return path is null
            ? []
            : StreamJsonParser.ParseFile(path, sessionId).Messages.Take(limit).ToList();
    }

    private static (string? Cwd, string? Title, DateTimeOffset? StartedAt) ReadHeader(
        string path,
        string sessionId)
    {
        string? cwd = null;
        string? title = null;
        DateTimeOffset? startedAt = null;
        foreach (var line in File.ReadLines(path).Take(80))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (cwd is null && root.TryGetProperty("cwd", out var cwdElement))
                    cwd = cwdElement.GetString();
                if (startedAt is null && root.TryGetProperty("timestamp", out var timestamp)
                    && DateTimeOffset.TryParse(timestamp.GetString(), out var parsed))
                    startedAt = parsed;

                if (title is null
                    && root.TryGetProperty("type", out var type)
                    && type.GetString() == "user")
                {
                    var one = StreamJsonParser.Parse(line, sessionId).Messages.FirstOrDefault();
                    title = Truncate(one?.Content, 80);
                }
            }
            catch (JsonException)
            {
                // Skip incomplete or corrupt records.
            }

            if (cwd is not null && title is not null && startedAt is not null)
                break;
        }

        return (cwd, title, startedAt);
    }

    private void EnsureCli()
    {
        if (_cliPath is null && !Discover())
            throw new InvalidOperationException(InstallHint);
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd('\\', '/');

    private static string? Truncate(string? value, int length)
        => value is null ? null : value.Length <= length ? value : value[..length] + "…";
}
