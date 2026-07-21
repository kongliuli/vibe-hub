using System.Text.Json;
using Microsoft.Data.Sqlite;
using VibeHub.Core.Models;

namespace VibeHub.Core.Transcript;

/// <summary>
/// Read-only OpenCode archive. SQLite WAL mode supports concurrent readers without copying the database.
/// </summary>
public sealed class OpenCodeArchiveReader
{
    private readonly string _sourceDbPath;

    public OpenCodeArchiveReader(string sourceDbPath) => _sourceDbPath = sourceDbPath;

    public IReadOnlyList<SessionInfo> ListSessions(int limit = 100)
    {
        return WithReadOnlyConnection(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, title, directory, time_created
                FROM session
                ORDER BY COALESCE(time_updated, time_created) DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            var list = new List<SessionInfo>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var title = r.IsDBNull(1) ? null : r.GetString(1);
                var dir = r.IsDBNull(2) ? null : r.GetString(2);
                DateTimeOffset? started = null;
                if (!r.IsDBNull(3) && r.GetValue(3) is long ms)
                    started = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                list.Add(new SessionInfo(id, "opencode", id, title, dir, started));
            }

            return list;
        });
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string sessionId, int limit = 500)
    {
        return WithReadOnlyConnection(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT m.id, m.data, p.data
                FROM message m
                LEFT JOIN part p ON p.message_id = m.id
                  AND json_extract(p.data, '$.type') IN ('text', 'reasoning')
                WHERE m.session_id = $sid
                ORDER BY m.time_created, p.id
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.Parameters.AddWithValue("$limit", limit);

            var byMsg = new Dictionary<string, CanonicalMessage>(StringComparer.Ordinal);
            var order = new List<string>();

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var mid = r.GetString(0);
                var mdata = r.IsDBNull(1) ? "{}" : r.GetString(1);
                var pdata = r.IsDBNull(2) ? null : r.GetString(2);

                var role = "assistant";
                DateTimeOffset? ts = null;
                try
                {
                    using var md = JsonDocument.Parse(mdata);
                    if (md.RootElement.TryGetProperty("role", out var roleEl))
                        role = roleEl.GetString() ?? role;
                    if (md.RootElement.TryGetProperty("time", out var time)
                        && time.TryGetProperty("created", out var created)
                        && created.TryGetInt64(out var ms))
                        ts = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                }
                catch (JsonException) { }

                string? text = null;
                if (pdata is not null)
                {
                    try
                    {
                        using var pd = JsonDocument.Parse(pdata);
                        if (pd.RootElement.TryGetProperty("text", out var t))
                            text = t.GetString();
                        if (pd.RootElement.TryGetProperty("type", out var ty)
                            && ty.GetString() == "reasoning")
                            role = "reasoning";
                    }
                    catch (JsonException) { }
                }

                if (!byMsg.TryGetValue(mid, out var existing))
                {
                    byMsg[mid] = new CanonicalMessage(mid, sessionId, role, text ?? "", ts);
                    order.Add(mid);
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    byMsg[mid] = existing with
                    {
                        Content = string.IsNullOrEmpty(existing.Content)
                            ? text!
                            : existing.Content + "\n" + text
                    };
                }
            }

            return (IReadOnlyList<CanonicalMessage>)order.Select(id => byMsg[id]).ToList();
        });
    }

    private T WithReadOnlyConnection<T>(Func<SqliteConnection, T> action)
    {
        if (!File.Exists(_sourceDbPath))
            throw new FileNotFoundException("opencode.db not found", _sourceDbPath);

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _sourceDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        conn.Open();
        using (var timeout = conn.CreateCommand())
        {
            timeout.CommandText = "PRAGMA busy_timeout=2000";
            timeout.ExecuteNonQuery();
        }
        return action(conn);
    }
}
