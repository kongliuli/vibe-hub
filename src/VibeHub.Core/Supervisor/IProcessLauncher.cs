using VibeHub.Core.Models;

namespace VibeHub.Core.Supervisor;

public interface IPseudoTerminal : IDisposable
{
    int? ProcessId { get; }
    bool HasExited { get; }
    event Action? Started;
    event Action? Exited;
    int? ExitCode { get; }
    void Kill();
}

public interface IProcessLauncher
{
    IPseudoTerminal Launch(ProcessStartSpec spec);
}
