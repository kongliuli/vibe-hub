using VibeHub.Core.Transcript;

namespace VibeHub.Core.Tests;

public sealed class CodexRolloutParserTests
{
    [Fact]
    public void ParsesRolloutJsonl_IntoMessages()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-rollout.jsonl");
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        var msgs = CodexRolloutParser.ParseFile(path, "sess-1");

        Assert.Equal(3, msgs.Count);
        Assert.Equal("user", msgs[0].Role);
        Assert.Equal("hello from user", msgs[0].Content);
        Assert.Equal("assistant", msgs[1].Role);
        Assert.Equal("hello from agent", msgs[1].Content);
        Assert.Equal("reasoning", msgs[2].Role);
    }
}
