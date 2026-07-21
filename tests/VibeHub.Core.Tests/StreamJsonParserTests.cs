using VibeHub.Core.Transcript;

namespace VibeHub.Core.Tests;

public sealed class StreamJsonParserTests
{
    [Fact]
    public void Parse_Extracts_Session_User_Assistant_Result()
    {
        var ndjson = """
            {"type":"system","subtype":"init","session_id":"abc123"}
            {"type":"user","message":{"role":"user","content":"hello"}}
            {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"hi there"}]}}
            {"type":"result","result":"## Summary\nDone."}
            """;

        var parsed = StreamJsonParser.Parse(ndjson);
        Assert.Equal("abc123", parsed.SessionId);
        Assert.Contains(parsed.Messages, m => m.Role == "user" && m.Content.Contains("hello"));
        Assert.Contains(parsed.Messages, m => m.Role == "assistant" && m.Content.Contains("hi there"));
        Assert.Equal("## Summary\nDone.", parsed.ResultText);
        Assert.Equal("## Summary\nDone.", StreamJsonParser.ExtractResultText(ndjson));
    }
}
