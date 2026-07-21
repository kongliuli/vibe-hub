using NSubstitute;
using VibeHub.Core.Adapters;
using VibeHub.Core.Models;
using VibeHub.Core.Storage;
using VibeHub.Core.Supervisor;

namespace VibeHub.Core.Tests;

public sealed class CursorAgentAdapterTests
{
    [Fact]
    public void Discover_False_WhenCliMissing()
    {
        var a = new CursorAgentAdapter { CliPathOverride = Path.Combine(Path.GetTempPath(), "no-such-agent.exe") };
        Assert.False(a.Discover());
    }

    [Fact]
    public void BuildStart_And_Resume_Args()
    {
        var fake = Path.Combine(Path.GetTempPath(), "fake-agent-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllText(fake, "");
        try
        {
            var a = new CursorAgentAdapter { CliPathOverride = fake };
            Assert.True(a.Discover());

            var start = a.BuildStart(@"D:\work");
            Assert.Equal(fake, start.FileName);
            Assert.Empty(start.Arguments);

            var resume = a.BuildResume(@"D:\work", "chat-123");
            Assert.Equal(["--resume=chat-123"], resume.Arguments);
        }
        finally
        {
            try { File.Delete(fake); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Supervisor_Start_UsesCursorAgentArgs()
    {
        var fake = Path.Combine(Path.GetTempPath(), "fake-agent-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllText(fake, "");
        var db = Path.Combine(Path.GetTempPath(), "vh-ca-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            var launcher = Substitute.For<IProcessLauncher>();
            var pty = Substitute.For<IPseudoTerminal>();
            ProcessStartSpec? captured = null;
            launcher.Launch(Arg.Do<ProcessStartSpec>(s => captured = s)).Returns(pty);

            using var store = new HubStore(db);
            var adapter = new CursorAgentAdapter { CliPathOverride = fake };
            var supervisor = new JobSupervisor(launcher, [adapter], store);
            supervisor.Start("cursor-agent", Path.GetTempPath());

            Assert.NotNull(captured);
            Assert.Equal(fake, captured!.FileName);
        }
        finally
        {
            try { File.Delete(fake); } catch { /* ignore */ }
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
