using Microsoft.Data.Sqlite;
using VibeHub.Core.Models;

namespace VibeHub.Core.Storage;

public sealed class HubStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public HubStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString());
        _conn.Open();
        InitSchema();
    }

    public static HubStore OpenDefault()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibe-hub", "hub.db");
        return new HubStore(path);
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS project (
              id TEXT PRIMARY KEY,
              root_path TEXT NOT NULL,
              display_name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS task (
              id TEXT PRIMARY KEY,
              project_id TEXT NOT NULL,
              title TEXT NOT NULL,
              status TEXT NOT NULL,
              notes TEXT,
              FOREIGN KEY(project_id) REFERENCES project(id)
            );
            CREATE TABLE IF NOT EXISTS job (
              id TEXT PRIMARY KEY,
              provider TEXT NOT NULL,
              cwd TEXT NOT NULL,
              session_id TEXT,
              pid INTEGER,
              state TEXT NOT NULL,
              exit_code INTEGER,
              started_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void UpsertProject(Project p)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO project(id, root_path, display_name) VALUES($id,$root,$name)
            ON CONFLICT(id) DO UPDATE SET root_path=$root, display_name=$name
            """;
        cmd.Parameters.AddWithValue("$id", p.Id);
        cmd.Parameters.AddWithValue("$root", p.RootPath);
        cmd.Parameters.AddWithValue("$name", p.DisplayName);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Project> ListProjects()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, root_path, display_name FROM project ORDER BY display_name";
        using var r = cmd.ExecuteReader();
        var list = new List<Project>();
        while (r.Read())
            list.Add(new Project(r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public void UpsertTask(TaskItem t)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO task(id, project_id, title, status, notes) VALUES($id,$pid,$title,$status,$notes)
            ON CONFLICT(id) DO UPDATE SET title=$title, status=$status, notes=$notes
            """;
        cmd.Parameters.AddWithValue("$id", t.Id);
        cmd.Parameters.AddWithValue("$pid", t.ProjectId);
        cmd.Parameters.AddWithValue("$title", t.Title);
        cmd.Parameters.AddWithValue("$status", t.Status);
        cmd.Parameters.AddWithValue("$notes", (object?)t.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TaskItem> ListTasks(string? projectId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = projectId is null
            ? "SELECT id, project_id, title, status, notes FROM task ORDER BY title"
            : "SELECT id, project_id, title, status, notes FROM task WHERE project_id=$pid ORDER BY title";
        if (projectId is not null)
            cmd.Parameters.AddWithValue("$pid", projectId);
        using var r = cmd.ExecuteReader();
        var list = new List<TaskItem>();
        while (r.Read())
            list.Add(new TaskItem(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public void UpsertJob(Job job)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO job(id, provider, cwd, session_id, pid, state, exit_code, started_at)
            VALUES($id,$provider,$cwd,$sid,$pid,$state,$exit,$started)
            ON CONFLICT(id) DO UPDATE SET
              session_id=$sid, pid=$pid, state=$state, exit_code=$exit
            """;
        cmd.Parameters.AddWithValue("$id", job.Id);
        cmd.Parameters.AddWithValue("$provider", job.Provider);
        cmd.Parameters.AddWithValue("$cwd", job.Cwd);
        cmd.Parameters.AddWithValue("$sid", (object?)job.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pid", (object?)job.Pid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", job.State.ToString());
        cmd.Parameters.AddWithValue("$exit", (object?)job.ExitCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$started", job.StartedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public Job? GetJob(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, provider, cwd, session_id, pid, state, exit_code, started_at FROM job WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadJob(r) : null;
    }

    public IReadOnlyList<Job> ListJobs()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, provider, cwd, session_id, pid, state, exit_code, started_at FROM job ORDER BY started_at DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<Job>();
        while (r.Read()) list.Add(ReadJob(r));
        return list;
    }

    private static Job ReadJob(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Provider = r.GetString(1),
        Cwd = r.GetString(2),
        SessionId = r.IsDBNull(3) ? null : r.GetString(3),
        Pid = r.IsDBNull(4) ? null : r.GetInt32(4),
        State = Enum.Parse<JobState>(r.GetString(5)),
        ExitCode = r.IsDBNull(6) ? null : r.GetInt32(6),
        StartedAt = DateTimeOffset.Parse(r.GetString(7))
    };

    public void Dispose() => _conn.Dispose();
}
