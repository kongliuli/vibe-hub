using VibeHub.Core.Models;

namespace VibeHub.Core.Transcript;

public sealed record CaptureEntry(
    string Id,
    string Provider,
    string Path,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Hub-owned stream-json captures (primary Cursor transcript channel; IDE jsonl is often REDACTED).
/// </summary>
public sealed class StreamJsonCaptureStore
{
    private readonly string _root;

    public StreamJsonCaptureStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibe-hub", "captures");
    }

    public string Root => _root;

    public string Begin(string provider, string? sessionHint = null)
    {
        var id = string.IsNullOrWhiteSpace(sessionHint)
            ? Guid.NewGuid().ToString("n")[..12]
            : Sanitize(sessionHint);
        var dir = Path.Combine(_root, Sanitize(provider));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".jsonl");
        if (!File.Exists(path))
            File.WriteAllText(path, "");
        return path;
    }

    public void WriteAll(string path, string ndjson)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ndjson ?? "");
    }

    public IReadOnlyList<CaptureEntry> List(string? provider = null, int limit = 100)
    {
        if (!Directory.Exists(_root)) return [];
        IEnumerable<string> files = provider is null
            ? Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories)
            : Directory.Exists(Path.Combine(_root, Sanitize(provider)))
                ? Directory.EnumerateFiles(Path.Combine(_root, Sanitize(provider)), "*.jsonl")
                : [];

        return files
            .Select(f =>
            {
                var prov = Path.GetFileName(Path.GetDirectoryName(f)!) ?? "unknown";
                var id = Path.GetFileNameWithoutExtension(f);
                return new CaptureEntry(
                    id,
                    prov,
                    f,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero));
            })
            .OrderByDescending(e => e.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    public CaptureEntry? Find(string provider, string id)
        => List(provider, 500).FirstOrDefault(e =>
            e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CanonicalMessage> GetMessages(string path, int limit = 500)
    {
        if (!File.Exists(path)) return [];
        var parsed = StreamJsonParser.ParseFile(path, Path.GetFileNameWithoutExtension(path));
        return parsed.Messages.Take(limit).ToList();
    }

    private static string Sanitize(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '_');
        return string.IsNullOrWhiteSpace(id) ? "unknown" : id;
    }
}
