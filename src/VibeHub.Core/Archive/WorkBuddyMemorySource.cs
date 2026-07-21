using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeHub.Core.Models;

namespace VibeHub.Core.Archive;

/// <summary>Read-only ~/.workbuddy/memory/*_memory.md (skip .bak).</summary>
public sealed class WorkBuddyMemorySource : IArchiveSource
{
    private readonly string _memoryDir;

    public WorkBuddyMemorySource(string? memoryDir = null)
    {
        _memoryDir = memoryDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".workbuddy", "memory");
    }

    public string SourceId => "workbuddy-memory";
    public string DisplayName => "WorkBuddy Memory";

    public bool Discover() => Directory.Exists(_memoryDir);

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        if (!Discover()) return [];

        return Directory.EnumerateFiles(_memoryDir, "*_memory.md")
            .Where(p => !p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(limit)
            .Select(path =>
            {
                var id = Path.GetFileNameWithoutExtension(path);
                DateTimeOffset? updated = null;
                try
                {
                    var head = File.ReadLines(path).Take(8).ToList();
                    foreach (var line in head)
                    {
                        var m = Regex.Match(line, @"Last updated:\s*(.+)");
                        if (m.Success
                            && DateTimeOffset.TryParse(m.Groups[1].Value.Trim(),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeLocal,
                                out var dto))
                        {
                            updated = dto;
                            break;
                        }
                    }
                }
                catch { /* tolerate encoding/IO */ }

                return new ArchiveEntry(
                    id,
                    SourceId,
                    id,
                    path,
                    updated ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path)),
                    "memory");
            })
            .ToList();
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        var path = Path.Combine(_memoryDir, entryId.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? entryId
            : entryId + ".md");
        if (!File.Exists(path))
        {
            // entryId may be bare filename without extension already handled
            var hit = Directory.EnumerateFiles(_memoryDir, "*_memory.md")
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                    .Equals(entryId, StringComparison.OrdinalIgnoreCase));
            if (hit is null) return [];
            path = hit;
        }

        string text;
        try { text = File.ReadAllText(path); }
        catch { return []; }

        var block = ExtractMemoryBlock(text) ?? StripRawJson(text);
        var updated = TryParseUpdated(text);

        return
        [
            new CanonicalMessage(
                entryId + ":memory",
                entryId,
                "memory",
                block.Trim(),
                updated)
        ];
    }

    internal static string? ExtractMemoryBlock(string text)
    {
        var idx = text.IndexOf("## Memory Block", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = text[(idx + "## Memory Block".Length)..];
        var end = rest.IndexOf("\n---", StringComparison.Ordinal);
        if (end < 0) end = rest.IndexOf("<!-- RAW_JSON", StringComparison.Ordinal);
        if (end >= 0) rest = rest[..end];
        return rest;
    }

    internal static string StripRawJson(string text)
    {
        var start = text.IndexOf("<!-- RAW_JSON_START", StringComparison.Ordinal);
        if (start < 0) return text;
        return text[..start].TrimEnd();
    }

    private static DateTimeOffset? TryParseUpdated(string text)
    {
        var m = Regex.Match(text, @"Last updated:\s*(.+)");
        if (m.Success
            && DateTimeOffset.TryParse(m.Groups[1].Value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var dto))
            return dto;

        // fallback: RAW_JSON updatedAt
        var jsonStart = text.IndexOf("RAW_JSON_START", StringComparison.Ordinal);
        if (jsonStart < 0) return null;
        var brace = text.IndexOf('{', jsonStart);
        var jsonEnd = text.IndexOf("RAW_JSON_END", StringComparison.Ordinal);
        if (brace < 0 || jsonEnd < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[brace..jsonEnd]);
            if (doc.RootElement.TryGetProperty("updatedAt", out var u)
                && DateTimeOffset.TryParse(u.GetString(), out var dto2))
                return dto2;
        }
        catch (JsonException) { }

        return null;
    }
}
