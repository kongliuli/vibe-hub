using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeHub.Core.Archive;
using VibeHub.Core.Inject;
using VibeHub.Core.Models;

namespace VibeHub.Core.Vault;

public sealed class HarvestRequest
{
    public required string ProjectId { get; init; }
    public required string SessionId { get; init; }
    public required string Provider { get; init; }
    public string? SourcePath { get; init; }
    public string? ResumeToken { get; init; }
    public IReadOnlyList<CanonicalMessage> Messages { get; init; } = [];
}

public sealed class HarvestResult
{
    public required SessionMeta Meta { get; init; }
    public required string SessionDir { get; init; }
}

/// <summary>
/// Copy raw (if file) + write canonical.jsonl + meta.json with hashes. Vault is independent of tool data.
/// </summary>
public sealed class Harvester
{
    private readonly VaultPaths _vault;
    private readonly VaultIndex? _index;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public Harvester(VaultPaths? vault = null, VaultIndex? index = null)
    {
        _vault = vault ?? new VaultPaths();
        _index = index;
    }

    public VaultPaths Paths => _vault;

    public HarvestResult Ingest(HarvestRequest req)
    {
        _vault.EnsureLayout(req.ProjectId);
        var sessionDir = _vault.SessionDir(req.ProjectId, req.SessionId);
        var rawDir = _vault.RawDir(req.ProjectId, req.SessionId);
        Directory.CreateDirectory(rawDir);

        var meta = new SessionMeta
        {
            SessionId = req.SessionId,
            ProjectId = req.ProjectId,
            Provider = req.Provider,
            ResumeToken = req.ResumeToken ?? req.SessionId,
            SourcePath = req.SourcePath,
            Lifecycle = SessionLifecycle.Leased,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            string? rawHash = null;
            if (!string.IsNullOrEmpty(req.SourcePath) && File.Exists(req.SourcePath))
            {
                var destName = Path.GetFileName(req.SourcePath);
                if (string.IsNullOrEmpty(destName)) destName = "raw.bin";
                var dest = Path.Combine(rawDir, destName);
                File.Copy(req.SourcePath, dest, overwrite: true);
                rawHash = Sha256File(dest);
            }
            else if (req.Messages.Count > 0)
            {
                // no file source — persist messages snapshot as raw sidecar
                var snap = Path.Combine(rawDir, "messages-snapshot.json");
                File.WriteAllText(snap, JsonSerializer.Serialize(req.Messages, JsonOpts));
                rawHash = Sha256File(snap);
            }

            var canonicalPath = _vault.CanonicalPath(req.ProjectId, req.SessionId);
            using (var w = new StreamWriter(canonicalPath, append: false, Encoding.UTF8))
            {
                foreach (var m in req.Messages)
                    w.WriteLine(JsonSerializer.Serialize(m));
            }

            var canonicalHash = Sha256File(canonicalPath);
            if (req.Messages.Count == 0)
            {
                meta.Lifecycle = SessionLifecycle.IngestError;
                meta.Error = rawHash is null ? "no raw source and no messages" : "no canonical messages";
            }
            else
            {
                meta.Lifecycle = SessionLifecycle.Harvested;
                meta.Error = null;
            }

            meta.RawHash = rawHash;
            meta.CanonicalHash = canonicalHash;
            meta.MessageCount = req.Messages.Count;
            meta.UpdatedAt = DateTimeOffset.UtcNow;

            File.WriteAllText(_vault.MetaPath(req.ProjectId, req.SessionId),
                JsonSerializer.Serialize(meta, JsonOpts));

            if (meta.Lifecycle == SessionLifecycle.Harvested && _index is not null)
                _index.IndexSession(req.ProjectId, req.SessionId, req.Messages);

            return new HarvestResult { Meta = meta, SessionDir = sessionDir };
        }
        catch (Exception ex)
        {
            meta.Lifecycle = SessionLifecycle.IngestError;
            meta.Error = ex.Message;
            meta.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                File.WriteAllText(_vault.MetaPath(req.ProjectId, req.SessionId),
                    JsonSerializer.Serialize(meta, JsonOpts));
            }
            catch { /* ignore */ }

            return new HarvestResult { Meta = meta, SessionDir = sessionDir };
        }
    }

    /// <summary>Harvest from an archive source entry (copy Path if file + GetMessages).</summary>
    public HarvestResult IngestFromArchive(
        string projectId,
        IArchiveSource source,
        ArchiveEntry entry,
        IReadOnlyList<CanonicalMessage>? messages = null)
    {
        var msgs = messages ?? source.GetMessages(entry.Id);
        var tempDir = Path.Combine(Path.GetTempPath(), "vibe-hub", Guid.NewGuid().ToString("n"));
        var exported = Path.Combine(tempDir, "session.jsonl");
        var sourcePath = entry.Path is not null && File.Exists(entry.Path) ? entry.Path : null;
        try
        {
            Directory.CreateDirectory(tempDir);
            if (source.ExportRawSession(entry.Id, exported))
                sourcePath = exported;
            return Ingest(new HarvestRequest
            {
                ProjectId = projectId,
                SessionId = entry.Id.Length > 80 ? ManagedBlock.Sha256Hex(entry.Id)[..16] : entry.Id,
                Provider = entry.SourceId,
                SourcePath = sourcePath,
                ResumeToken = entry.Id,
                Messages = msgs
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public SessionMeta? ReadMeta(string projectId, string sessionId)
    {
        var path = _vault.MetaPath(projectId, sessionId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<SessionMeta>(File.ReadAllText(path));
    }

    private static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}
