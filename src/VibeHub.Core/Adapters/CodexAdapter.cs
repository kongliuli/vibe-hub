using System.Text.Json;
using VibeHub.Core.Models;
using VibeHub.Core.Transcript;

namespace VibeHub.Core.Adapters;

public sealed class CodexAdapter : IProviderAdapter
{
    private string? _cliPath;

    public string ProviderId => "codex";

    public string SessionsRoot => SessionsRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "sessions");

    /// <summary>Optional override for tests (path to node + codex.js).</summary>
    public string? CodexJsPath { get; set; }
    public string? SessionsRootOverride { get; set; }

    public bool Discover()
    {
        if (CodexJsPath is not null && File.Exists(CodexJsPath))
        {
            _cliPath = "node";
            return true;
        }

        _cliPath = ResolveCodexEntry();
        return _cliPath is not null;
    }

    public ProcessStartSpec BuildStart(string cwd)
    {
        EnsureCli();
        return BuildSpec([], cwd);
    }

    public ProcessStartSpec BuildResume(string cwd, string sessionId)
    {
        EnsureCli();
        return BuildSpec(["resume", sessionId], cwd);
    }

    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string? cwd, CancellationToken ct = default)
    {
        if (!Directory.Exists(SessionsRoot))
            return Task.FromResult<IReadOnlyList<SessionInfo>>([]);

        var files = Directory.EnumerateFiles(SessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(200);

        var list = new List<SessionInfo>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var id = ExtractSessionId(file);
            if (id is null) continue;

            string? title = null;
            string? sessionCwd = null;
            DateTimeOffset? started = null;

            foreach (var line in File.ReadLines(file).Take(40))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("type", out var typeEl)) continue;
                    var type = typeEl.GetString();
                    if (type == "session_meta" && root.TryGetProperty("payload", out var payload))
                    {
                        if (payload.TryGetProperty("cwd", out var c))
                            sessionCwd = c.GetString();
                        if (root.TryGetProperty("timestamp", out var ts)
                            && DateTimeOffset.TryParse(ts.GetString(), out var dto))
                            started = dto;
                    }
                    else if (type == "event_msg"
                             && root.TryGetProperty("payload", out var ev)
                             && ev.TryGetProperty("type", out var et)
                             && et.GetString() == "user_message"
                             && ev.TryGetProperty("message", out var msg)
                             && title is null)
                    {
                        title = Truncate(msg.GetString(), 80);
                    }
                }
                catch (JsonException)
                {
                    // skip corrupt line
                }
            }

            if (cwd is not null && sessionCwd is not null
                && !string.Equals(Normalize(cwd), Normalize(sessionCwd), StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(new SessionInfo(id, ProviderId, id, title, sessionCwd, started
                ?? new DateTimeOffset(File.GetCreationTimeUtc(file))));
        }

        return Task.FromResult<IReadOnlyList<SessionInfo>>(list);
    }

    public static IReadOnlyList<CanonicalMessage> ParseRolloutFile(string path, string sessionId)
        => CodexRolloutParser.ParseFile(path, sessionId);

    private ProcessStartSpec BuildSpec(IReadOnlyList<string> args, string cwd)
    {
        if (CodexJsPath is not null)
        {
            var full = new List<string> { CodexJsPath };
            full.AddRange(args);
            return new ProcessStartSpec("node", full, cwd);
        }

        return new ProcessStartSpec(_cliPath!, args, cwd);
    }

    private string? ResolveCodexEntry()
    {
        // Prefer node_modules codex.js under Documents\Codex\.tools if present
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, "Documents", "Codex", ".tools", "codex-cli", "node_modules", "@openai", "codex", "bin", "codex.js"),
            Path.Combine(home, "AppData", "Roaming", "npm", "node_modules", "@openai", "codex", "bin", "codex.js"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                CodexJsPath = c;
                return "node";
            }
        }

        return CliResolver.PreferNonPs1("codex");
    }

    private void EnsureCli()
    {
        if (_cliPath is null && !Discover())
            throw new InvalidOperationException("codex CLI not found");
    }

    private static string? ExtractSessionId(string path)
    {
        // rollout-YYYY-MM-DDTHH-mm-ss-<uuid>.jsonl
        var name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "rollout-";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = name[prefix.Length..];
        var lastDash = rest.LastIndexOf('-');
        // uuid has dashes; take from first uuid-like segment — file ends with full uuid
        var idx = rest.IndexOf('-');
        // format: dateTtime-uuid — uuid starts after timestamp which has form 2026-07-21T02-13-11
        // Safer: match 8-4-4-4-12 at end
        var m = System.Text.RegularExpressions.Regex.Match(
            rest, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? Truncate(string? s, int n)
        => s is null ? null : s.Length <= n ? s : s[..n] + "…";

    private static string Normalize(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
}
