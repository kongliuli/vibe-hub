using VibeHub.Core.Models;

namespace VibeHub.Core.Archive;

/// <summary>
/// Read-only Kimi daimon memory vault markdown.
/// Skips credential paths under kimi-desktop (never touch token-store.json).
/// </summary>
public sealed class KimiMemoryVaultSource : IArchiveSource
{
    private readonly string _vaultDir;

    public KimiMemoryVaultSource(string? vaultDir = null)
    {
        _vaultDir = vaultDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "kimi-desktop", "daimon-share", "daimon", "agents", "main", "memory", "vault");
    }

    public string SourceId => "kimi-vault";
    public string DisplayName => "Kimi Memory Vault";

    public bool Discover() => Directory.Exists(_vaultDir);

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        if (!Discover()) return [];

        return Directory.EnumerateFiles(_vaultDir, "*.md", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}token", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(limit)
            .Select(path =>
            {
                var rel = Path.GetRelativePath(_vaultDir, path).Replace('\\', '/');
                return new ArchiveEntry(
                    rel,
                    SourceId,
                    rel,
                    path,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path)),
                    "memory");
            })
            .ToList();
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        var path = Path.IsPathRooted(entryId)
            ? entryId
            : Path.GetFullPath(Path.Combine(_vaultDir, entryId.Replace('/', Path.DirectorySeparatorChar)));

        // stay inside vault
        var root = Path.GetFullPath(_vaultDir);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return [];

        string text;
        try { text = File.ReadAllText(path); }
        catch { return []; }

        return
        [
            new CanonicalMessage(
                entryId + ":doc",
                entryId,
                "memory",
                text.Trim(),
                new DateTimeOffset(File.GetLastWriteTimeUtc(path)))
        ];
    }
}
