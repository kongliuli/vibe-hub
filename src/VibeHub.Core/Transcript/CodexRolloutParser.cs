using System.Text.Json;
using VibeHub.Core.Models;

namespace VibeHub.Core.Transcript;

public static class CodexRolloutParser
{
    public static IReadOnlyList<CanonicalMessage> ParseFile(string path, string sessionId)
    {
        var messages = new List<CanonicalMessage>();
        var i = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) continue;
                if (typeEl.GetString() != "event_msg") continue;
                if (!root.TryGetProperty("payload", out var payload)) continue;
                if (!payload.TryGetProperty("type", out var et)) continue;

                var etype = et.GetString();
                string? role = etype switch
                {
                    "user_message" => "user",
                    "agent_message" => "assistant",
                    "agent_reasoning" => "reasoning",
                    _ => null
                };
                if (role is null) continue;

                string? text = null;
                if (payload.TryGetProperty("message", out var msg))
                    text = msg.GetString();
                else if (payload.TryGetProperty("text", out var t))
                    text = t.GetString();
                if (string.IsNullOrEmpty(text)) continue;

                DateTimeOffset? ts = null;
                if (root.TryGetProperty("timestamp", out var tsEl)
                    && DateTimeOffset.TryParse(tsEl.GetString(), out var dto))
                    ts = dto;

                messages.Add(new CanonicalMessage(
                    $"{sessionId}:{i++}",
                    sessionId,
                    role,
                    text,
                    ts));
            }
            catch (JsonException)
            {
                // skip
            }
        }

        return messages;
    }
}
