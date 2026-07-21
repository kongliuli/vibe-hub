using VibeHub.Core.Transcript;

namespace VibeHub.Core.Tests;

public sealed class OpenCodeArchiveReaderTests
{
    [Fact]
    public void ListSessions_ReadsLocalDb_WhenPresent()
    {
        var db = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode", "opencode.db");
        if (!File.Exists(db))
            return; // machine without OpenCode — not a failure

        var reader = new OpenCodeArchiveReader(db);
        var sessions = reader.ListSessions(5);
        Assert.NotEmpty(sessions);

        var msgs = reader.GetMessages(sessions[0].ProviderSessionId, 50);
        // history may be empty for a brand-new session; just ensure no throw
        Assert.NotNull(msgs);
    }
}
