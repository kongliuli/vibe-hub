using VibeHub.Core.Adapters;
using VibeHub.Core.Models;
using VibeHub.Core.Transcript;

namespace VibeHub.Core.Archive;

/// <summary>
/// Cursor transcripts: Hub stream-json captures first, IDE agent-transcripts as fallback (often REDACTED).
/// </summary>
public sealed class CursorAgentArchiveSource : IArchiveSource
{
    private readonly CursorAgentAdapter _adapter;
    private readonly StreamJsonCaptureStore _captures;

    public CursorAgentArchiveSource(
        CursorAgentAdapter? adapter = null,
        StreamJsonCaptureStore? captures = null)
    {
        _adapter = adapter ?? new CursorAgentAdapter();
        _captures = captures ?? new StreamJsonCaptureStore();
    }

    public string SourceId => "cursor-agent";
    public string DisplayName => "Cursor agent (stream-json + IDE)";

    public bool Discover()
        => Directory.Exists(_adapter.ProjectsRoot)
           || Directory.Exists(_captures.Root)
           || _adapter.Discover();

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        var list = new List<ArchiveEntry>();

        foreach (var c in _captures.List(SourceId, limit))
        {
            list.Add(new ArchiveEntry(
                c.Id,
                SourceId,
                $"[capture] {c.Id}",
                c.Path,
                c.UpdatedAt,
                "capture"));
        }

        var sessions = _adapter.ListSessionsAsync(null).GetAwaiter().GetResult();
        foreach (var s in sessions)
        {
            if (list.Count >= limit) break;
            if (list.Any(e => e.Id.Equals(s.ProviderSessionId, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(new ArchiveEntry(
                s.ProviderSessionId,
                SourceId,
                s.Title ?? s.ProviderSessionId,
                FindIdePath(s.ProviderSessionId),
                s.StartedAt,
                "session"));
        }

        return list
            .OrderByDescending(e => e.UpdatedAt ?? DateTimeOffset.MinValue)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        var capture = _captures.Find(SourceId, entryId);
        if (capture is not null)
            return _captures.GetMessages(capture.Path, limit);

        var path = FindIdePath(entryId);
        if (path is null || !File.Exists(path))
        {
            return
            [
                new CanonicalMessage(
                    entryId + ":note",
                    entryId,
                    "meta",
                    "未找到 stream-json 捕获或 IDE transcript（后者常 REDACTED）。\n"
                    + _adapter.InstallHint,
                    DateTimeOffset.UtcNow)
            ];
        }

        // IDE jsonl: try stream-json shape first, else line dump
        try
        {
            var parsed = StreamJsonParser.ParseFile(path, entryId);
            if (parsed.Messages.Count > 0)
                return parsed.Messages.Take(limit).ToList();
        }
        catch
        {
            /* fall through */
        }

        var msgs = new List<CanonicalMessage>();
        var i = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (msgs.Count >= limit) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            msgs.Add(new CanonicalMessage(
                $"{entryId}:{i++}",
                entryId,
                "assistant",
                line.Length > 2000 ? line[..2000] + "…" : line,
                null));
        }

        return msgs;
    }

    private string? FindIdePath(string entryId)
    {
        if (!Directory.Exists(_adapter.ProjectsRoot)) return null;
        return Directory.EnumerateFiles(_adapter.ProjectsRoot, "*.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                f.IndexOf("agent-transcripts", StringComparison.OrdinalIgnoreCase) >= 0
                && Path.GetFileNameWithoutExtension(f)
                    .Equals(entryId, StringComparison.OrdinalIgnoreCase));
    }
}
