using VibeHub.Core.Models;

namespace VibeHub.Core.Adapters;

public interface IProviderAdapter
{
    string ProviderId { get; }
    bool Discover();
    ProcessStartSpec BuildStart(string cwd);
    ProcessStartSpec BuildResume(string cwd, string sessionId);
    Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string? cwd, CancellationToken ct = default);
}
