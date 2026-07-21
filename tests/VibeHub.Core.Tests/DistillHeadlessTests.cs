using NSubstitute;
using VibeHub.Core.Distill;
using VibeHub.Core.Models;
using VibeHub.Core.Supervisor;
using VibeHub.Core.Transcript;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Tests;

public sealed class DistillHeadlessTests
{
    [Fact]
    public async Task DistillViaCli_UsesRunner_CapturesStream_EnqueuesPending()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-dh-" + Guid.NewGuid().ToString("n"));
        try
        {
            var queue = new ReviewQueue(Path.Combine(root, "q.json"));
            var captures = new StreamJsonCaptureStore(Path.Combine(root, "cap"));
            var vault = new VaultPaths(Path.Combine(root, "vault"));
            var d = new Distiller(queue, vault, captures);

            var runner = Substitute.For<IHeadlessRunner>();
            runner.RunAsync(Arg.Any<ProcessStartSpec>(), Arg.Any<CancellationToken>())
                .Returns(new HeadlessRunResult
                {
                    ExitCode = 0,
                    StdOut = """
                        {"type":"system","subtype":"init","session_id":"c1"}
                        {"type":"result","result":"# Real summary from CLI"}
                        """
                });

            var art = await d.DistillViaCliAsync(
                "cursor-agent", "p", "s1", root,
                [new CanonicalMessage("1", "s1", "user", "do work", null)],
                runner);

            Assert.Equal(ReviewStatus.Pending, art.Status);
            Assert.Contains("Real summary", art.Content);
            Assert.NotEmpty(captures.List("cursor-agent"));
            await runner.Received(1).RunAsync(
                Arg.Is<ProcessStartSpec>(s => s.Arguments.Contains("-p") && s.Arguments.Contains("stream-json")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
