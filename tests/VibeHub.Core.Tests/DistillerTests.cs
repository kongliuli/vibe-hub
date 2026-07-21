using VibeHub.Core.Distill;
using VibeHub.Core.Models;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Tests;

public sealed class DistillerTests
{
    [Fact]
    public void BuildHeadlessSpec_OpenCode_And_Codex()
    {
        var d = new Distiller(new ReviewQueue(Path.Combine(Path.GetTempPath(), "rq-x.json")));
        var oc = d.BuildHeadlessSpec("opencode", @"D:\w", "summarize");
        Assert.Equal("opencode", oc.FileName);
        Assert.Contains("run", oc.Arguments);
        Assert.Contains("--format", oc.Arguments);

        var cx = d.BuildHeadlessSpec("codex", @"D:\w", "summarize");
        Assert.Equal(["exec", "summarize", "--json"], cx.Arguments);
    }

    [Fact]
    public void ProposeSummary_Review_Approve_WritesVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-dist-" + Guid.NewGuid().ToString("n"));
        try
        {
            var queue = new ReviewQueue(Path.Combine(root, "queue.json"));
            var vault = new VaultPaths(Path.Combine(root, "vault"));
            var harvester = new Harvester(vault);
            harvester.Ingest(new HarvestRequest
            {
                ProjectId = "p",
                SessionId = "s1",
                Provider = "codex",
                Messages =
                [
                    new CanonicalMessage("1", "s1", "user", "hello world", null),
                    new CanonicalMessage("2", "s1", "assistant", "hi there", null)
                ]
            });

            var d = new Distiller(queue, vault);
            var art = d.ProposeSummary("p", "s1",
            [
                new CanonicalMessage("1", "s1", "user", "hello world", null),
                new CanonicalMessage("2", "s1", "assistant", "hi there", null)
            ]);
            Assert.Equal(ReviewStatus.Pending, art.Status);
            // human gate: must decide before apply
            Assert.False(d.ApplyApproved(art.Id, harvester));

            queue.Decide(art.Id, approve: true);
            Assert.True(d.ApplyApproved(art.Id, harvester));
            Assert.True(File.Exists(Path.Combine(vault.SessionDir("p", "s1"), "summary.md")));
            Assert.Equal(SessionLifecycle.Distilled, harvester.ReadMeta("p", "s1")!.Lifecycle);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
