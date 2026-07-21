using VibeHub.Core.Adapters;
using VibeHub.Core.Models;
using VibeHub.Core.Supervisor;
using VibeHub.Core.Transcript;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Distill;

/// <summary>
/// Distill pipeline: headless CLI → review queue → (on approve) vault summary.md.
/// </summary>
public sealed class Distiller
{
    private readonly ReviewQueue _queue;
    private readonly VaultPaths _vault;
    private readonly StreamJsonCaptureStore _captures;

    public Distiller(
        ReviewQueue? queue = null,
        VaultPaths? vault = null,
        StreamJsonCaptureStore? captures = null)
    {
        _queue = queue ?? new ReviewQueue();
        _vault = vault ?? new VaultPaths();
        _captures = captures ?? new StreamJsonCaptureStore();
    }

    public ReviewQueue Queue => _queue;
    public StreamJsonCaptureStore Captures => _captures;

    public ProcessStartSpec BuildHeadlessSpec(string providerId, string cwd, string prompt)
    {
        return providerId.ToLowerInvariant() switch
        {
            "opencode" => new ProcessStartSpec("opencode", ["run", prompt, "--format", "json"], cwd),
            "codex" => new ProcessStartSpec("codex", ["exec", prompt, "--json"], cwd),
            "claude" => new ProcessStartSpec("claude", ["-p", prompt, "--output-format", "stream-json"], cwd),
            "cursor-agent" => ResolveCursorHeadless(cwd, prompt),
            _ => throw new ArgumentException("Unknown provider: " + providerId)
        };
    }

    private static ProcessStartSpec ResolveCursorHeadless(string cwd, string prompt)
    {
        var cli = CliResolver.FindCursorAgent() ?? "agent";
        return new ProcessStartSpec(cli, ["-p", prompt, "--output-format", "stream-json"], cwd);
    }

    public static string BuildDistillPrompt(IReadOnlyList<CanonicalMessage> messages, int maxMessages = 40)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Summarize the following agent session for handoff. Output markdown only:");
        sb.AppendLine("- Goal / what was done");
        sb.AppendLine("- Key decisions and file paths");
        sb.AppendLine("- Open questions / next steps");
        sb.AppendLine();
        sb.AppendLine("```");
        foreach (var m in messages.TakeLast(maxMessages))
        {
            var line = m.Content.Replace('\n', ' ');
            if (line.Length > 240) line = line[..240] + "…";
            sb.AppendLine($"{m.Role}: {line}");
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    /// <summary>Local draft (no model). Parks Pending in review queue — never auto-approves.</summary>
    public DistillArtifact ProposeSummary(
        string projectId,
        string sessionId,
        IReadOnlyList<CanonicalMessage> messages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Summary draft for `{sessionId}`");
        sb.AppendLine();
        sb.AppendLine($"Messages: {messages.Count}");
        foreach (var m in messages.Take(12))
        {
            var line = m.Content.Replace('\n', ' ');
            if (line.Length > 160) line = line[..160] + "…";
            sb.AppendLine($"- **{m.Role}**: {line}");
        }

        return EnqueueSummary(projectId, sessionId, sb.ToString());
    }

    /// <summary>True headless Distill via CLI; stdout captured as stream-json primary transcript.</summary>
    public async Task<DistillArtifact> DistillViaCliAsync(
        string providerId,
        string projectId,
        string sessionId,
        string cwd,
        IReadOnlyList<CanonicalMessage> messages,
        IHeadlessRunner runner,
        CancellationToken ct = default)
    {
        var prompt = BuildDistillPrompt(messages);
        var spec = BuildHeadlessSpec(providerId, cwd, prompt);
        var capturePath = _captures.Begin(providerId, sessionId + "-distill");
        var result = await runner.RunAsync(spec, ct).ConfigureAwait(false);
        _captures.WriteAll(capturePath, result.StdOut);

        var content = StreamJsonParser.ExtractResultText(result.StdOut);
        if (string.IsNullOrWhiteSpace(content))
            content = string.IsNullOrWhiteSpace(result.StdOut)
                ? $"# Distill failed (exit {result.ExitCode})\n\n{result.StdErr}"
                : result.StdOut.Trim();

        if (result.ExitCode != 0 && content.Length < 40)
            content = $"# Distill CLI exit {result.ExitCode}\n\n```\n{result.StdErr}\n```\n\n{content}";

        return EnqueueSummary(projectId, sessionId, content);
    }

    private DistillArtifact EnqueueSummary(string projectId, string sessionId, string content)
    {
        var artifact = new DistillArtifact
        {
            Id = Guid.NewGuid().ToString("n"),
            ProjectId = projectId,
            SessionId = sessionId,
            Kind = DistillArtifactKind.Summary,
            Content = content
        };
        return _queue.Enqueue(artifact);
    }

    public bool ApplyApproved(string artifactId, Harvester harvester)
    {
        var art = _queue.List().FirstOrDefault(a => a.Id == artifactId);
        if (art is null || art.Status != ReviewStatus.Approved) return false;
        if (art.Kind != DistillArtifactKind.Summary) return false;

        _vault.EnsureLayout(art.ProjectId);
        var sessionDir = _vault.SessionDir(art.ProjectId, art.SessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "summary.md"), art.Content);

        var meta = harvester.ReadMeta(art.ProjectId, art.SessionId);
        if (meta is not null)
        {
            meta.Lifecycle = SessionLifecycle.Distilled;
            meta.UpdatedAt = DateTimeOffset.UtcNow;
            File.WriteAllText(
                _vault.MetaPath(art.ProjectId, art.SessionId),
                System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        return true;
    }
}
