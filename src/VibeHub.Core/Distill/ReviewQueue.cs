using System.Text.Json;

namespace VibeHub.Core.Distill;

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum DistillArtifactKind
{
    Summary,
    MemoryDiff,
    SkillDraft
}

public sealed class DistillArtifact
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string SessionId { get; init; }
    public required DistillArtifactKind Kind { get; init; }
    public required string Content { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>File-backed review queue under LocalAppData/vibe-hub/review-queue.json</summary>
public sealed class ReviewQueue
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ReviewQueue(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibe-hub", "review-queue.json");
    }

    public IReadOnlyList<DistillArtifact> List(ReviewStatus? status = null)
    {
        var all = Load();
        return status is null ? all : all.Where(a => a.Status == status).ToList();
    }

    public DistillArtifact Enqueue(DistillArtifact artifact)
    {
        var all = Load().ToList();
        all.Add(artifact);
        Save(all);
        return artifact;
    }

    public DistillArtifact? UpdateContent(string id, string content)
    {
        var all = Load().ToList();
        var hit = all.FirstOrDefault(a => a.Id == id);
        if (hit is null) return null;
        hit.Content = content;
        Save(all);
        return hit;
    }

    public DistillArtifact? Decide(string id, bool approve)
    {
        var all = Load().ToList();
        var hit = all.FirstOrDefault(a => a.Id == id);
        if (hit is null) return null;
        hit.Status = approve ? ReviewStatus.Approved : ReviewStatus.Rejected;
        hit.ReviewedAt = DateTimeOffset.UtcNow;
        Save(all);
        return hit;
    }

    private List<DistillArtifact> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DistillArtifact>>(File.ReadAllText(_path), JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DistillArtifact> all)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(all, JsonOpts));
    }
}
