using VibeHub.Core.Adapters;
using VibeHub.Core.Models;
using VibeHub.Core.Storage;

namespace VibeHub.Core.Supervisor;

public sealed class JobSupervisor
{
    private readonly IProcessLauncher _launcher;
    private readonly IReadOnlyDictionary<string, IProviderAdapter> _adapters;
    private readonly HubStore _store;
    private readonly Dictionary<string, IPseudoTerminal> _live = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _captureByJob = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Raised when a job process exits (after store update). Used for auto-Harvest.</summary>
    public event Action<Job>? JobExited;

    /// <summary>Raised after a terminal is associated with a persisted job.</summary>
    public event Action<Job, IPseudoTerminal>? JobLaunched;

    public JobSupervisor(
        IProcessLauncher launcher,
        IEnumerable<IProviderAdapter> adapters,
        HubStore store)
    {
        _launcher = launcher;
        _adapters = adapters.ToDictionary(a => a.ProviderId, StringComparer.OrdinalIgnoreCase);
        _store = store;
    }

    public void RegisterCapture(string jobId, string capturePath)
    {
        lock (_gate) _captureByJob[jobId] = capturePath;
    }

    public string? GetCapture(string jobId)
    {
        lock (_gate) return _captureByJob.TryGetValue(jobId, out var p) ? p : null;
    }

    public Job Start(string providerId, string cwd)
    {
        var adapter = GetAdapter(providerId);
        var spec = adapter.BuildStart(cwd);
        return Spawn(providerId, cwd, sessionId: null, spec);
    }

    public Job Resume(string providerId, string cwd, string sessionId)
    {
        var adapter = GetAdapter(providerId);
        var spec = adapter.BuildResume(cwd, sessionId);
        return Spawn(providerId, cwd, sessionId, spec);
    }

    public void Kill(string jobId)
    {
        IPseudoTerminal? pty;
        lock (_gate)
            _live.TryGetValue(jobId, out pty);

        if (pty is null)
            return;

        pty.Kill();
        if (pty.HasExited)
            CompleteExit(jobId, pty);
    }

    public IReadOnlyList<Job> ListJobs() => _store.ListJobs();

    private Job Spawn(string providerId, string cwd, string? sessionId, ProcessStartSpec spec)
    {
        var job = new Job
        {
            Id = Guid.NewGuid().ToString("n"),
            Provider = providerId,
            Cwd = cwd,
            SessionId = sessionId,
            State = JobState.Spawning
        };
        _store.UpsertJob(job);

        try
        {
            var pty = _launcher.Launch(spec);
            lock (_gate) _live[job.Id] = pty;

            pty.Started += () => MarkRunning(job, pty);
            pty.Exited += () => CompleteExit(job.Id, pty, job);

            MarkRunning(job, pty);
            try { JobLaunched?.Invoke(job, pty); }
            catch { /* presentation errors must not fail a launched process */ }
            if (pty.HasExited)
                CompleteExit(job.Id, pty, job);
            return job;
        }
        catch
        {
            job.State = JobState.Failed;
            _store.UpsertJob(job);
            throw;
        }
    }

    private void MarkRunning(Job job, IPseudoTerminal pty)
    {
        lock (_gate)
        {
            if (!_live.ContainsKey(job.Id))
                return;

            job.Pid = pty.ProcessId;
            job.State = JobState.Running;
            _store.UpsertJob(job);
        }
    }

    private void CompleteExit(string jobId, IPseudoTerminal pty, Job? knownJob = null)
    {
        Job? completed;
        lock (_gate)
        {
            if (!_live.Remove(jobId))
                return;

            completed = knownJob ?? _store.GetJob(jobId);
            if (completed is null)
                return;

            completed.Pid ??= pty.ProcessId;
            completed.State = JobState.Exited;
            completed.ExitCode = pty.ExitCode;
            _store.UpsertJob(completed);
        }

        try { JobExited?.Invoke(completed); }
        catch { /* UI/harvest errors must not kill exit path */ }
    }

    private IProviderAdapter GetAdapter(string providerId)
    {
        if (!_adapters.TryGetValue(providerId, out var a))
            throw new InvalidOperationException($"Unknown provider: {providerId}");
        return a;
    }
}
