using VibeHub.Core.Archive;
using VibeHub.Core.Models;
using VibeHub.Core.Transcript;

namespace VibeHub.Core.Vault;

/// <summary>On Job exit: ingest stream-json capture or archive session into vault + FTS.</summary>
public sealed class JobAutoHarvester
{
    private readonly Harvester _harvester;
    private readonly StreamJsonCaptureStore _captures;
    private readonly ArchiveCatalog _archives;

    public JobAutoHarvester(
        Harvester harvester,
        StreamJsonCaptureStore? captures = null,
        ArchiveCatalog? archives = null)
    {
        _harvester = harvester;
        _captures = captures ?? new StreamJsonCaptureStore();
        _archives = archives ?? new ArchiveCatalog();
    }

    public event Action<HarvestResult>? Harvested;

    public HarvestResult? TryHarvest(Job job, string? capturePath = null)
    {
        if (string.IsNullOrWhiteSpace(job.ProjectId))
            return null;
        var projectId = job.ProjectId;

        if (!string.IsNullOrEmpty(capturePath) && File.Exists(capturePath))
            return HarvestCapture(projectId, job, capturePath);

        if (!string.IsNullOrEmpty(job.SessionId))
        {
            var cap = _captures.Find(job.Provider, job.SessionId);
            if (cap is not null)
                return HarvestCapture(projectId, job, cap.Path);

            var src = _archives.Get(job.Provider);
            if (src is not null)
            {
                var entry = new ArchiveEntry(
                    job.SessionId, job.Provider, job.SessionId, job.Cwd, DateTimeOffset.UtcNow, "session");
                var result = _harvester.IngestFromArchive(projectId, src, entry);
                Harvested?.Invoke(result);
                return result;
            }
        }

        return null;
    }

    private HarvestResult HarvestCapture(string projectId, Job job, string path)
    {
        var parsed = StreamJsonParser.ParseFile(path, job.SessionId);
        var sessionId = parsed.SessionId
                        ?? job.SessionId
                        ?? Path.GetFileNameWithoutExtension(path);
        var result = _harvester.Ingest(new HarvestRequest
        {
            ProjectId = projectId,
            SessionId = sessionId,
            Provider = job.Provider,
            SourcePath = path,
            ResumeToken = sessionId,
            Messages = parsed.Messages
        });
        Harvested?.Invoke(result);
        return result;
    }
}
