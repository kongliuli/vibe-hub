namespace VibeHub.Core.Models;

public sealed record Project(string Id, string RootPath, string DisplayName);

public sealed record TaskItem(string Id, string ProjectId, string Title, string Status, string? Notes);

public sealed record SessionInfo(
    string Id,
    string Provider,
    string ProviderSessionId,
    string? Title,
    string? Cwd,
    DateTimeOffset? StartedAt);

public sealed record CanonicalMessage(
    string Id,
    string SessionId,
    string Role,
    string Content,
    DateTimeOffset? Timestamp,
    string? Model = null);

public sealed record ToolCall(
    string Id,
    string MessageId,
    string Name,
    string? InputJson,
    string? OutputText,
    string Status);

public enum JobState
{
    Idle,
    Spawning,
    Running,
    Exited,
    Failed
}

public sealed class Job
{
    public required string Id { get; init; }
    // Null only for jobs written before project ownership was introduced.
    public string? ProjectId { get; init; }
    public required string Provider { get; init; }
    public required string Cwd { get; init; }
    public string? SessionId { get; set; }
    public int? Pid { get; set; }
    public JobState State { get; set; } = JobState.Idle;
    public int? ExitCode { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ProcessStartSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null);
