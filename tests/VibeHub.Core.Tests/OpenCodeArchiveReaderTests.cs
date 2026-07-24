using Microsoft.Data.Sqlite;
using VibeHub.Core.Transcript;

namespace VibeHub.Core.Tests;

public sealed class OpenCodeArchiveReaderTests
{
    [Fact]
    public void ListSessions_ReadsFixtureDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-opencode-" + Guid.NewGuid().ToString("n"));
        var db = Path.Combine(root, "opencode.db");
        try
        {
            Directory.CreateDirectory(root);
            using (var connection = new SqliteConnection($"Data Source={db}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE session (
                      id TEXT PRIMARY KEY, title TEXT, directory TEXT,
                      time_created INTEGER, time_updated INTEGER
                    );
                    CREATE TABLE message (
                      id TEXT PRIMARY KEY, session_id TEXT, data TEXT, time_created INTEGER
                    );
                    CREATE TABLE part (
                      id TEXT PRIMARY KEY, message_id TEXT, data TEXT
                    );
                    INSERT INTO session VALUES ('s1', 'Fixture session', 'D:\work', 1000, 2000);
                    INSERT INTO message VALUES ('m1', 's1', '{"role":"user","time":{"created":1000}}', 1000);
                    INSERT INTO message VALUES ('m2', 's1', '{"role":"assistant","time":{"created":2000}}', 2000);
                    INSERT INTO part VALUES ('p1', 'm1', '{"type":"text","text":"hello"}');
                    INSERT INTO part VALUES ('p2', 'm2', '{"type":"text","text":"world"}');
                    """;
                command.ExecuteNonQuery();
            }

            var reader = new OpenCodeArchiveReader(db);
            var session = Assert.Single(reader.ListSessions(5));
            Assert.Equal("Fixture session", session.Title);

            var messages = reader.GetMessages(session.ProviderSessionId, 50);
            Assert.Collection(
                messages,
                message => Assert.Equal(("user", "hello"), (message.Role, message.Content)),
                message => Assert.Equal(("assistant", "world"), (message.Role, message.Content)));

            var export = Path.Combine(root, "session.jsonl");
            Assert.True(reader.ExportSessionRaw(session.ProviderSessionId, export));
            var raw = File.ReadAllLines(export);
            Assert.Equal(5, raw.Length);
            Assert.Contains(raw, line => line.Contains("\"kind\":\"session\"", StringComparison.Ordinal));
            Assert.Equal(2, raw.Count(line => line.Contains("\"kind\":\"message\"", StringComparison.Ordinal)));
            Assert.Equal(2, raw.Count(line => line.Contains("\"kind\":\"part\"", StringComparison.Ordinal)));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
