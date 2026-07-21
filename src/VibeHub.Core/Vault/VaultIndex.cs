using Microsoft.Data.Sqlite;
using VibeHub.Core.Models;

namespace VibeHub.Core.Vault;

public sealed record VaultSearchHit(
    string ProjectId,
    string SessionId,
    string Role,
    string Snippet);

/// <summary>FTS5 index at vault/index.db over canonical messages.</summary>
public sealed class VaultIndex : IDisposable
{
    private readonly SqliteConnection _conn;

    public VaultIndex(VaultPaths vault)
    {
        Directory.CreateDirectory(vault.Root);
        var path = Path.Combine(vault.Root, "index.db");
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _conn.Open();
        Init();
    }

    public string DbPath
    {
        get
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA database_list";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.GetString(1) == "main")
                    return r.GetString(2);
            }

            return "";
        }
    }

    private void Init()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
              project_id UNINDEXED,
              session_id UNINDEXED,
              role UNINDEXED,
              content,
              tokenize = 'unicode61'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void IndexSession(string projectId, string sessionId, IReadOnlyList<CanonicalMessage> messages)
    {
        using var tx = _conn.BeginTransaction();
        using (var del = _conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM messages_fts WHERE project_id=$p AND session_id=$s";
            del.Parameters.AddWithValue("$p", projectId);
            del.Parameters.AddWithValue("$s", sessionId);
            del.ExecuteNonQuery();
        }

        foreach (var m in messages)
        {
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO messages_fts(project_id, session_id, role, content) VALUES($p,$s,$r,$c)";
            ins.Parameters.AddWithValue("$p", projectId);
            ins.Parameters.AddWithValue("$s", sessionId);
            ins.Parameters.AddWithValue("$r", m.Role);
            ins.Parameters.AddWithValue("$c", m.Content);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<VaultSearchHit> Search(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT project_id, session_id, role, snippet(messages_fts, 3, '[', ']', '…', 12)
            FROM messages_fts
            WHERE messages_fts MATCH $q
            LIMIT $lim
            """;
        cmd.Parameters.AddWithValue("$q", EscapeFts(query));
        cmd.Parameters.AddWithValue("$lim", limit);
        using var r = cmd.ExecuteReader();
        var hits = new List<VaultSearchHit>();
        while (r.Read())
        {
            hits.Add(new VaultSearchHit(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3)));
        }

        return hits;
    }

    private static string EscapeFts(string q)
    {
        // ponytail: quote as one phrase; upgrade = proper FTS query builder
        var cleaned = q.Replace("\"", "\"\"");
        return $"\"{cleaned}\"";
    }

    public void Dispose() => _conn.Dispose();
}
