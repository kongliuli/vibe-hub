using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VibeHub.Core.Models;

namespace VibeHub.Core.Adapters;

public sealed class OpenCodeAdapter : IProviderAdapter
{
    private string? _cliPath;

    public string? CliPathOverride { get; set; }
    public string? DataRootOverride { get; set; }

    public string ProviderId => "opencode";

    public string DataRoot => DataRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "share", "opencode");

    public string DbPath => Path.Combine(DataRoot, "opencode.db");

    public bool Discover()
    {
        if (CliPathOverride is not null)
        {
            _cliPath = File.Exists(CliPathOverride) ? CliPathOverride : null;
            return _cliPath is not null;
        }

        _cliPath = CliResolver.PreferNonPs1("opencode");
        return _cliPath is not null;
    }

    public bool HasArchive => File.Exists(DbPath);

    /// <summary>
    /// Env for ConPTY/TUI spawn. DISABLE_AUTOUPDATE must be "true"/"1"
    /// (boolean parse); otherwise restart shows a blocking update toast.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LaunchEnvironment { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPENCODE_DISABLE_AUTOUPDATE"] = "true",
            ["OPENCODE_DISABLE_MODELS_FETCH"] = "1",
        };

    public ProcessStartSpec BuildStart(string cwd)
    {
        EnsureCli();
        return new ProcessStartSpec(_cliPath!, [], cwd, LaunchEnvironment);
    }

    public ProcessStartSpec BuildResume(string cwd, string sessionId)
    {
        EnsureCli();
        return new ProcessStartSpec(_cliPath!, ["-s", sessionId], cwd, LaunchEnvironment);
    }

    public ProcessStartSpec BuildTask(string cwd, string prompt, string? agent = null, string? model = null)
    {
        EnsureCli();
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Task prompt is required", nameof(prompt));

        var arguments = new List<string> { "run", "--format", "json" };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }
        if (!string.IsNullOrWhiteSpace(agent))
        {
            arguments.Add("--agent");
            arguments.Add(agent);
        }
        arguments.Add(prompt);
        return new ProcessStartSpec(_cliPath!, arguments, cwd, LaunchEnvironment);
    }

    public string? GetTaskDatabaseProblem()
    {
        if (!File.Exists(DbPath)) return null;

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(session_context_epoch)";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) columns.Add(reader.GetString(1));
        if (columns.Count == 0) return null;

        var missing = new[] { "replacement_seq", "revision" }
            .Where(column => !columns.Contains(column))
            .ToList();
        return missing.Count == 0
            ? null
            : "OpenCode 数据库需要迁移，缺少列: " + string.Join(", ", missing)
              + "。请先备份 opencode.db，再执行兼容迁移。";
    }

    public async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string? cwd, CancellationToken ct = default)
    {
        EnsureCli();
        var psi = new ProcessStartInfo
        {
            FileName = _cliPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        psi.ArgumentList.Add("session");
        psi.ArgumentList.Add("list");
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("json");
        foreach (var (k, v) in LaunchEnvironment)
            psi.Environment[k] = v;

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start opencode session list");
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        if (string.IsNullOrWhiteSpace(stdout))
            return [];

        using var doc = JsonDocument.Parse(stdout);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<SessionInfo>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var id = row.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id)) continue;
            var title = row.TryGetProperty("title", out var t) ? t.GetString() : null;
            var directory = row.TryGetProperty("directory", out var d) ? d.GetString() : null;
            DateTimeOffset? started = null;
            if (row.TryGetProperty("time", out var time) && time.TryGetProperty("created", out var created)
                && created.TryGetInt64(out var ms))
                started = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            else if (row.TryGetProperty("updated", out var upd) && upd.TryGetInt64(out var ums))
                started = DateTimeOffset.FromUnixTimeMilliseconds(ums);

            list.Add(new SessionInfo(id, ProviderId, id, title, directory, started));
        }

        return list;
    }

    private void EnsureCli()
    {
        if (_cliPath is null && !Discover())
            throw new InvalidOperationException("opencode CLI not found on PATH");
    }
}
