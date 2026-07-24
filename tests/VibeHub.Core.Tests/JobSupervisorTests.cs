using Microsoft.Data.Sqlite;
using NSubstitute;
using VibeHub.Core.Adapters;
using VibeHub.Core.Models;
using VibeHub.Core.Storage;
using VibeHub.Core.Supervisor;

namespace VibeHub.Core.Tests;

public sealed class JobSupervisorTests
{
    [Fact]
    public void HubStore_MigratesLegacyJobTable()
    {
        var db = NewDatabasePath();
        try
        {
            using (var legacy = new SqliteConnection($"Data Source={db}"))
            {
                legacy.Open();
                using var command = legacy.CreateCommand();
                command.CommandText = "CREATE TABLE job (id TEXT PRIMARY KEY, provider TEXT NOT NULL, cwd TEXT NOT NULL, session_id TEXT, pid INTEGER, state TEXT NOT NULL, exit_code INTEGER, started_at TEXT NOT NULL)";
                command.ExecuteNonQuery();
            }

            using var store = new HubStore(db);
            store.UpsertJob(new Job { Id = "new", ProjectId = "project", Provider = "opencode", Cwd = "C:\\work" });
            Assert.Equal("project", store.GetJob("new")!.ProjectId);
        }
        finally
        {
            DeleteDatabase(db);
        }
    }

    [Fact]
    public void Start_PassesCorrectCwdAndArgs_ForOpenCode()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        pty.ProcessId.Returns(4242);
        ProcessStartSpec? captured = null;
        launcher.Launch(Arg.Do<ProcessStartSpec>(s => captured = s)).Returns(pty);

        var db = Path.Combine(Path.GetTempPath(), "vibe-hub-test-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);

            var cwd = Path.GetTempPath().TrimEnd('\\', '/');
            var job = supervisor.Start("project-1", "opencode", cwd);

            Assert.Equal(JobState.Running, job.State);
            Assert.Equal(4242, job.Pid);
            Assert.NotNull(captured);
            Assert.Equal("opencode", captured!.FileName);
            Assert.Empty(captured.Arguments);
            Assert.Equal(Path.GetFullPath(cwd), Path.GetFullPath(captured.WorkingDirectory));
            Assert.Equal(JobState.Running, store.GetJob(job.Id)!.State);
            Assert.Equal("project-1", store.GetJob(job.Id)!.ProjectId);
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void JobExited_Fires_OnPtyExit()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        pty.ProcessId.Returns(1);
        launcher.Launch(Arg.Any<ProcessStartSpec>()).Returns(pty);

        var db = Path.Combine(Path.GetTempPath(), "vibe-hub-test-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);
            Job? exited = null;
            supervisor.JobExited += j => exited = j;

            var job = supervisor.Start("project", "opencode", Path.GetTempPath());
            pty.Exited += Raise.Event<Action>();

            Assert.NotNull(exited);
            Assert.Equal(job.Id, exited!.Id);
            Assert.Equal(JobState.Exited, exited.State);
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Started_UpdatesDelayedPid()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        launcher.Launch(Arg.Any<ProcessStartSpec>()).Returns(pty);

        var db = NewDatabasePath();
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);
            var job = supervisor.Start("project", "opencode", Path.GetTempPath());

            Assert.Null(job.Pid);
            pty.ProcessId.Returns(7319);
            pty.Started += Raise.Event<Action>();

            Assert.Equal(7319, job.Pid);
            Assert.Equal(7319, store.GetJob(job.Id)!.Pid);
        }
        finally
        {
            DeleteDatabase(db);
        }
    }

    [Fact]
    public void JobLaunched_CorrelatesEachJobWithItsTerminal()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var first = Substitute.For<IPseudoTerminal>();
        var second = Substitute.For<IPseudoTerminal>();
        launcher.Launch(Arg.Any<ProcessStartSpec>()).Returns(first, second);

        var db = NewDatabasePath();
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);
            var launched = new List<(string JobId, IPseudoTerminal Terminal)>();
            supervisor.JobLaunched += (job, terminal) => launched.Add((job.Id, terminal));

            var firstJob = supervisor.Start("project", "opencode", Path.GetTempPath());
            var secondJob = supervisor.Start("project", "opencode", Path.GetTempPath());

            Assert.Equal(2, launched.Count);
            Assert.Equal((firstJob.Id, first), launched[0]);
            Assert.Equal((secondJob.Id, second), launched[1]);
        }
        finally
        {
            DeleteDatabase(db);
        }
    }

    [Fact]
    public void NaturalExit_PersistsExitCode_AndCompletesOnlyOnce()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        pty.ProcessId.Returns(42);
        pty.ExitCode.Returns(17);
        launcher.Launch(Arg.Any<ProcessStartSpec>()).Returns(pty);

        var db = NewDatabasePath();
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);
            var exitEvents = 0;
            supervisor.JobExited += _ => exitEvents++;
            var job = supervisor.Start("project", "opencode", Path.GetTempPath());

            pty.Exited += Raise.Event<Action>();
            pty.Exited += Raise.Event<Action>();

            var stored = store.GetJob(job.Id)!;
            Assert.Equal(JobState.Exited, stored.State);
            Assert.Equal(17, stored.ExitCode);
            Assert.Equal(1, exitEvents);
        }
        finally
        {
            DeleteDatabase(db);
        }
    }

    [Fact]
    public void Kill_UsesProcessKill_AndCompletesOnlyOnce()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        var hasExited = false;
        pty.HasExited.Returns(_ => hasExited);
        pty.ExitCode.Returns(137);
        pty.When(x => x.Kill()).Do(_ => hasExited = true);
        launcher.Launch(Arg.Any<ProcessStartSpec>()).Returns(pty);

        var db = NewDatabasePath();
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);
            var exitEvents = 0;
            supervisor.JobExited += _ => exitEvents++;
            var job = supervisor.Start("project", "opencode", Path.GetTempPath());

            supervisor.Kill(job.Id);
            pty.Exited += Raise.Event<Action>();

            pty.Received(1).Kill();
            pty.DidNotReceive().Dispose();
            Assert.Equal(JobState.Exited, store.GetJob(job.Id)!.State);
            Assert.Equal(137, store.GetJob(job.Id)!.ExitCode);
            Assert.Equal(1, exitEvents);
        }
        finally
        {
            DeleteDatabase(db);
        }
    }

    [Fact]
    public void AlreadyExitedProcess_IsObservedAfterLaunch()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        pty.ProcessId.Returns(91);
        pty.HasExited.Returns(true);
        pty.ExitCode.Returns(3);
        launcher.Launch(Arg.Any<ProcessStartSpec>()).Returns(pty);

        var db = NewDatabasePath();
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);

            var job = supervisor.Start("project", "opencode", Path.GetTempPath());

            Assert.Equal(JobState.Exited, job.State);
            Assert.Equal(3, job.ExitCode);
            Assert.Equal(JobState.Exited, store.GetJob(job.Id)!.State);
        }
        finally
        {
            DeleteDatabase(db);
        }
    }

    [Fact]
    public void Resume_PassesSessionFlag_ForOpenCode()
    {
        var launcher = Substitute.For<IProcessLauncher>();
        var pty = Substitute.For<IPseudoTerminal>();
        ProcessStartSpec? captured = null;
        launcher.Launch(Arg.Do<ProcessStartSpec>(s => captured = s)).Returns(pty);

        var db = Path.Combine(Path.GetTempPath(), "vibe-hub-test-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            using var store = new HubStore(db);
            var supervisor = new JobSupervisor(launcher, [new FakeOpenCodeAdapter()], store);

            var cwd = Path.GetTempPath().TrimEnd('\\', '/');
            var job = supervisor.Resume("project-2", "opencode", cwd, "ses_abc");

            Assert.NotNull(captured);
            Assert.Equal(["-s", "ses_abc"], captured!.Arguments);
            Assert.Equal(Path.GetFullPath(cwd), Path.GetFullPath(captured.WorkingDirectory));
            Assert.Equal("project-2", store.GetJob(job.Id)!.ProjectId);
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    private sealed class FakeOpenCodeAdapter : IProviderAdapter
    {
        public string ProviderId => "opencode";
        public bool Discover() => true;
        public ProcessStartSpec BuildStart(string cwd) => new("opencode", [], cwd);
        public ProcessStartSpec BuildResume(string cwd, string sessionId)
            => new("opencode", ["-s", sessionId], cwd);
        public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string? cwd, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionInfo>>([]);
    }

    private static string NewDatabasePath()
        => Path.Combine(Path.GetTempPath(), "vibe-hub-test-" + Guid.NewGuid().ToString("n") + ".db");

    private static void DeleteDatabase(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
