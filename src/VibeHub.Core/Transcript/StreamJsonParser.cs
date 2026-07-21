using System.Text.Json;
using VibeHub.Core.Models;

namespace VibeHub.Core.Transcript;

/// <summary>
/// Parse Claude/Cursor-style <c>--output-format stream-json</c> NDJSON into canonical messages.
/// </summary>
public static class StreamJsonParser
{
    public sealed record ParseResult(
        string? SessionId,
        IReadOnlyList<CanonicalMessage> Messages,
        string? ResultText);

    public static ParseResult Parse(string ndjson, string? fallbackSessionId = null)
    {
        using var reader = new StringReader(ndjson);
        return Parse(reader, fallbackSessionId);
    }

    public static ParseResult ParseFile(string path, string? fallbackSessionId = null)
    {
        using var reader = File.OpenText(path);
        return Parse(reader, fallbackSessionId);
    }

    public static ParseResult Parse(TextReader reader, string? fallbackSessionId = null)
    {
        string? sessionId = fallbackSessionId;
        string? resultText = null;
        var msgs = new List<CanonicalMessage>();
        var i = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                if (type is "system"
                    && root.TryGetProperty("session_id", out var sid)
                    && sid.ValueKind == JsonValueKind.String)
                    sessionId = sid.GetString() ?? sessionId;

                if (type is "system"
                    && root.TryGetProperty("subtype", out var sub)
                    && sub.GetString() == "init"
                    && root.TryGetProperty("session_id", out var sid2))
                    sessionId = sid2.GetString() ?? sessionId;

                sessionId ??= fallbackSessionId ?? "stream";

                if (type is "user" or "assistant")
                {
                    var role = type;
                    var content = ExtractMessageText(root);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        msgs.Add(new CanonicalMessage(
                            $"{sessionId}:{i++}",
                            sessionId,
                            role,
                            content!,
                            null));
                    }
                }
                else if (type is "result")
                {
                    if (root.TryGetProperty("result", out var r))
                        resultText = r.ValueKind == JsonValueKind.String
                            ? r.GetString()
                            : r.ToString();
                }
                else if (type is "tool_call" or "tool_result")
                {
                    var name = root.TryGetProperty("name", out var n) ? n.GetString() : type;
                    var snippet = line.Length > 500 ? line[..500] + "…" : line;
                    msgs.Add(new CanonicalMessage(
                        $"{sessionId}:{i++}",
                        sessionId,
                        "tool",
                        $"[{name}] {snippet}",
                        null));
                }
            }
            catch (JsonException)
            {
                // non-json line: keep as meta crumb
                sessionId ??= fallbackSessionId ?? "stream";
                msgs.Add(new CanonicalMessage(
                    $"{sessionId}:{i++}",
                    sessionId,
                    "meta",
                    line.Length > 400 ? line[..400] + "…" : line,
                    null));
            }
        }

        return new ParseResult(sessionId, msgs, resultText);
    }

    /// <summary>Best-effort final text for Distill summaries.</summary>
    public static string? ExtractResultText(string ndjson)
    {
        var parsed = Parse(ndjson);
        if (!string.IsNullOrWhiteSpace(parsed.ResultText))
            return parsed.ResultText;
        return parsed.Messages.LastOrDefault(m => m.Role is "assistant")?.Content;
    }

    private static string? ExtractMessageText(JsonElement root)
    {
        if (root.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("content", out var content))
                return FlattenContent(content);
        }

        if (root.TryGetProperty("content", out var direct))
            return FlattenContent(direct);

        if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();

        return null;
    }

    private static string? FlattenContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind != JsonValueKind.Array)
            return content.ToString();

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                parts.Add(item.GetString() ?? "");
                continue;
            }

            if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                parts.Add(t.GetString() ?? "");
            else if (item.TryGetProperty("type", out var ty) && ty.GetString() == "text"
                     && item.TryGetProperty("text", out var t2))
                parts.Add(t2.GetString() ?? "");
        }

        var joined = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
