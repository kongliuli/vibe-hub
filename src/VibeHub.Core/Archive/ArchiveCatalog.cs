using VibeHub.Core.Adapters;
using VibeHub.Core.Models;
using VibeHub.Core.Transcript;

namespace VibeHub.Core.Archive;

/// <summary>Registers built-in archive sources for Structured pane switching.</summary>
public sealed class ArchiveCatalog
{
    private readonly Dictionary<string, IArchiveSource> _sources;

    public ArchiveCatalog(IEnumerable<IArchiveSource>? extras = null)
    {
        var list = new List<IArchiveSource>
        {
            new OpenCodeArchiveSource(),
            new CodexArchiveSource(),
            new ClaudeArchiveSource(),
            new CursorAgentArchiveSource(),
            new WorkBuddyMemorySource(),
            new KimiMemoryVaultSource(),
            new TraeSkillsSource(),
            new TraeEncryptedDbProbe(),
        };
        if (extras is not null) list.AddRange(extras);
        _sources = list.ToDictionary(s => s.SourceId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IArchiveSource> All => _sources.Values.ToList();

    public IReadOnlyList<IArchiveSource> Discovered()
        => _sources.Values.Where(s => s.Discover()).ToList();

    public IArchiveSource? Get(string sourceId)
        => _sources.TryGetValue(sourceId, out var s) ? s : null;
}

public sealed class ClaudeArchiveSource : IArchiveSource
{
    private readonly ClaudeAdapter _adapter = new();

    public string SourceId => "claude";
    public string DisplayName => "Claude Code";

    public bool Discover() => Directory.Exists(_adapter.ProjectsRoot);

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
        => _adapter.ListSessionsAsync(null).GetAwaiter().GetResult()
            .Take(limit)
            .Select(session => new ArchiveEntry(
                session.Id,
                SourceId,
                session.Title ?? session.Id,
                _adapter.FindTranscript(session.Id),
                session.StartedAt,
                "session"))
            .ToList();

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
        => _adapter.ReadMessages(entryId, limit);
}

/// <summary>Bridge existing OpenCodeArchiveReader into IArchiveSource.</summary>
public sealed class OpenCodeArchiveSource : IArchiveSource
{
    private readonly OpenCodeAdapter _adapter = new();
    private OpenCodeArchiveReader? _reader;

    public string SourceId => "opencode";
    public string DisplayName => "OpenCode";

    public bool Discover() => _adapter.Discover() && _adapter.HasArchive;

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        EnsureReader();
        return _reader!.ListSessions(limit)
            .Select(s => new ArchiveEntry(
                s.ProviderSessionId,
                SourceId,
                string.IsNullOrWhiteSpace(s.Title) ? s.ProviderSessionId : s.Title!,
                s.Cwd,
                s.StartedAt,
                "session"))
            .ToList();
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        EnsureReader();
        return _reader!.GetMessages(entryId, limit);
    }

    private void EnsureReader()
    {
        _reader ??= new OpenCodeArchiveReader(_adapter.DbPath);
    }
}

public sealed class CodexArchiveSource : IArchiveSource
{
    private readonly CodexAdapter _adapter = new();

    public string SourceId => "codex";
    public string DisplayName => "Codex";

    public bool Discover()
    {
        _adapter.Discover();
        return Directory.Exists(_adapter.SessionsRoot);
    }

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        var sessions = _adapter.ListSessionsAsync(null).GetAwaiter().GetResult();
        return sessions.Take(limit)
            .Select(s => new ArchiveEntry(
                s.ProviderSessionId,
                SourceId,
                string.IsNullOrWhiteSpace(s.Title) ? s.ProviderSessionId : s.Title!,
                s.Cwd,
                s.StartedAt,
                "session"))
            .ToList();
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        if (!Directory.Exists(_adapter.SessionsRoot)) return [];
        var path = Directory.EnumerateFiles(_adapter.SessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault(f => f.Contains(entryId, StringComparison.OrdinalIgnoreCase));
        if (path is null) return [];
        return CodexRolloutParser.ParseFile(path, entryId).Take(limit).ToList();
    }
}
