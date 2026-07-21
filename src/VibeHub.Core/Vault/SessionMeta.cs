using System.Text.Json.Serialization;

namespace VibeHub.Core.Vault;

public sealed class SessionMeta
{
    public required string SessionId { get; set; }
    public required string ProjectId { get; set; }
    public required string Provider { get; set; }
    public string? ResumeToken { get; set; }
    public string? SourcePath { get; set; }
    public SessionLifecycle Lifecycle { get; set; } = SessionLifecycle.Draft;
    public string? RawHash { get; set; }
    public string? CanonicalHash { get; set; }
    public int MessageCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsHarvested => Lifecycle is SessionLifecycle.Harvested
        or SessionLifecycle.Distilled or SessionLifecycle.Archived;
}
