using VibeHub.Core.Models;

namespace VibeHub.Core.Archive;

public sealed record ArchiveEntry(
    string Id,
    string SourceId,
    string Title,
    string? Path,
    DateTimeOffset? UpdatedAt,
    string Kind);

public interface IArchiveSource
{
    string SourceId { get; }
    string DisplayName { get; }
    bool Discover();
    IReadOnlyList<ArchiveEntry> List(int limit = 100);
    IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500);
    bool ExportRawSession(string entryId, string destination) => false;
}
