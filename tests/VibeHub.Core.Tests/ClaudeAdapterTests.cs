using VibeHub.Core.Adapters;

namespace VibeHub.Core.Tests;

public sealed class ClaudeAdapterTests
{
    [Fact]
    public void BuildStartAndResume_UseInteractiveClaudeContract()
    {
        var cli = Path.Combine(Path.GetTempPath(), "claude-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllText(cli, "");
        try
        {
            var adapter = new ClaudeAdapter { CliPathOverride = cli };

            var start = adapter.BuildStart(@"D:\work");
            var resume = adapter.BuildResume(@"D:\work", "session-123");

            Assert.Equal(cli, start.FileName);
            Assert.Empty(start.Arguments);
            Assert.Equal(["--resume", "session-123"], resume.Arguments);
        }
        finally
        {
            File.Delete(cli);
        }
    }

    [Fact]
    public async Task ListSessionsAndReadMessages_UseClaudeProjectJsonl()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibe-hub-claude-" + Guid.NewGuid().ToString("n"));
        var project = Path.Combine(root, "d--work");
        Directory.CreateDirectory(project);
        var sessionId = "11111111-2222-3333-4444-555555555555";
        var transcript = Path.Combine(project, sessionId + ".jsonl");
        File.WriteAllLines(transcript,
        [
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "user",
                sessionId,
                cwd = @"D:\work",
                timestamp = "2026-07-21T01:02:03Z",
                message = new { role = "user", content = "Fix the lifecycle" }
            }),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "assistant",
                sessionId,
                message = new
                {
                    role = "assistant",
                    content = new[] { new { type = "text", text = "Done" } }
                }
            })
        ]);

        try
        {
            var adapter = new ClaudeAdapter { ProjectsRootOverride = root };

            var sessions = await adapter.ListSessionsAsync(@"D:\work");
            var messages = adapter.ReadMessages(sessionId);

            var session = Assert.Single(sessions);
            Assert.Equal(sessionId, session.Id);
            Assert.Equal("Fix the lifecycle", session.Title);
            Assert.Equal(["user", "assistant"], messages.Select(message => message.Role));
            Assert.Equal(["Fix the lifecycle", "Done"], messages.Select(message => message.Content));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
