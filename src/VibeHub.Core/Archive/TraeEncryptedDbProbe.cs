using VibeHub.Core.Models;

namespace VibeHub.Core.Archive;

/// <summary>
/// Metadata-only probe for Trae SQLCipher databases. Never attempts decrypt.
/// </summary>
public sealed class TraeEncryptedDbProbe : IArchiveSource
{
    private readonly (string Id, string Path)[] _dbs;

    public TraeEncryptedDbProbe(IEnumerable<(string Id, string Path)>? dbs = null)
    {
        var app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _dbs = (dbs ??
        [
            ("trae-solo-cn", Path.Combine(app, "TRAE SOLO CN", "ModularData", "ai-agent", "database.db")),
            ("trae-cn", Path.Combine(app, "Trae CN", "ModularData", "ai-agent", "database.db")),
        ]).ToArray();
    }

    public string SourceId => "trae-encrypted";
    public string DisplayName => "Trae DB (encrypted)";

    public bool Discover() => _dbs.Any(d => File.Exists(d.Path));

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        var list = new List<ArchiveEntry>();
        foreach (var (id, path) in _dbs)
        {
            if (!File.Exists(path)) continue;
            var fi = new FileInfo(path);
            var encrypted = LooksEncrypted(path);
            var title = encrypted
                ? $"{id} · {fi.Length / 1024.0 / 1024.0:F1} MB · 加密不可读"
                : $"{id} · {fi.Length / 1024.0 / 1024.0:F1} MB · 未识别为加密（仍不解析）";
            list.Add(new ArchiveEntry(
                id,
                SourceId,
                title,
                path,
                new DateTimeOffset(fi.LastWriteTimeUtc),
                "encrypted-meta"));
            if (list.Count >= limit) break;
        }

        return list;
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        var hit = _dbs.FirstOrDefault(d => d.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase));
        if (hit.Path is null || !File.Exists(hit.Path)) return [];

        var fi = new FileInfo(hit.Path);
        var body =
            $"路径: {hit.Path}\n" +
            $"大小: {fi.Length} bytes\n" +
            $"修改: {fi.LastWriteTimeUtc:O} UTC\n" +
            $"判定: {(LooksEncrypted(hit.Path) ? "SQLCipher 类加密（文件头非 SQLite 魔数）" : "未知")}\n" +
            "策略: 不解密、不解析内容。可在资源管理器中打开所在目录。";

        return
        [
            new CanonicalMessage(
                entryId + ":meta",
                entryId,
                "meta",
                body,
                new DateTimeOffset(fi.LastWriteTimeUtc))
        ];
    }

    /// <summary>SQLite magic is "SQLite format 3\0"; encrypted Trae DBs start with random bytes.</summary>
    public static bool LooksEncrypted(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[16];
            var n = fs.Read(buf);
            if (n < 16) return true;
            // "SQLite format 3\0"
            ReadOnlySpan<byte> magic = "SQLite format 3\0"u8;
            return !buf.SequenceEqual(magic);
        }
        catch
        {
            return true;
        }
    }
}
