using VibeHub.Core.Adapters;
using VibeHub.Core.Inject;
using VibeHub.Core.Models;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Migrate;

public sealed class MigrationPlan
{
    public required string ProjectId { get; init; }
    public required string SessionId { get; init; }
    public required string SourceProvider { get; init; }
    public required string TargetProvider { get; init; }
    public required string Summary { get; init; }
    public required string Handoff { get; init; }
}

/// <summary>
/// Cross-tool semantic migration: summary+handoff → inject sink → project → new Job spec.
/// Raw transcript portability is intentionally not promised.
/// </summary>
public sealed class MigrationService
{
    private readonly VaultPaths _vault;
    private readonly InjectSink _sink;
    private readonly InjectProjector _projector;

    public MigrationService(VaultPaths? vault = null, InjectSink? sink = null)
    {
        _vault = vault ?? new VaultPaths();
        _sink = sink ?? new InjectSink();
        _projector = new InjectProjector(_sink);
    }

    public MigrationPlan Prepare(
        string projectId,
        string sessionId,
        string sourceProvider,
        string targetProvider,
        string? summaryOverride = null)
    {
        var summaryPath = Path.Combine(_vault.SessionDir(projectId, sessionId), "summary.md");
        var summary = summaryOverride
                      ?? (File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : null)
                      ?? $"# Migration from {sourceProvider}/{sessionId}\n\n(无 summary.md — 请先 Distill)";

        var handoff =
            $"# Handoff ({DateTimeOffset.UtcNow:u})\n\n"
            + $"From `{sourceProvider}` session `{sessionId}` → `{targetProvider}`.\n\n"
            + "## Summary\n\n" + summary.Trim() + "\n";

        return new MigrationPlan
        {
            ProjectId = projectId,
            SessionId = sessionId,
            SourceProvider = sourceProvider,
            TargetProvider = targetProvider,
            Summary = summary,
            Handoff = handoff
        };
    }

    public string ApplyToSink(MigrationPlan plan)
    {
        _sink.Write(plan.ProjectId, InjectKind.Handoff, plan.Handoff);
        var memoryPath = _sink.PathFor(plan.ProjectId, InjectKind.Memory);
        if (!File.Exists(memoryPath) || string.IsNullOrWhiteSpace(File.ReadAllText(memoryPath)))
            _sink.Write(plan.ProjectId, InjectKind.Memory, "## Migrated context\n\nSee handoff.md\n");
        return _sink.ProjectDir(plan.ProjectId);
    }

    public string ProjectForTarget(MigrationPlan plan, string workspaceCwd)
    {
        ApplyToSink(plan);
        var target = ResolveInjectTarget(plan.TargetProvider, workspaceCwd);
        _projector.Project(plan.ProjectId, [target]);
        return target;
    }

    public ProcessStartSpec BuildNewJobSpec(IProviderAdapter adapter, string cwd)
        => adapter.BuildStart(cwd);

    public static string ResolveInjectTarget(string providerId, string workspaceCwd)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return providerId.ToLowerInvariant() switch
        {
            "opencode" => Path.Combine(home, ".config", "opencode", "AGENTS.md"),
            "codex" => Path.Combine(home, ".codex", "AGENTS.md"),
            "cursor-agent" => Path.Combine(workspaceCwd, "AGENTS.md"),
            "claude" => Path.Combine(workspaceCwd, "CLAUDE.md"),
            _ => Path.Combine(workspaceCwd, "AGENTS.md")
        };
    }
}
