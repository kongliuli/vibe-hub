using VibeHub.Core.Models;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Tests;

public sealed class VaultIndexTests
{
    [Fact]
    public void Index_And_Search_Fts5()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-fts-" + Guid.NewGuid().ToString("n"));
        try
        {
            var vault = new VaultPaths(root);
            using var index = new VaultIndex(vault);
            index.IndexSession("p1", "s1",
            [
                new CanonicalMessage("1", "s1", "user", "install cursor agent cli", null),
                new CanonicalMessage("2", "s1", "assistant", "done with stream-json", null)
            ]);

            var hits = index.Search("cursor agent");
            Assert.NotEmpty(hits);
            Assert.Equal("s1", hits[0].SessionId);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Harvester_Indexes_On_Ingest()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-hfts-" + Guid.NewGuid().ToString("n"));
        try
        {
            var vault = new VaultPaths(root);
            using var index = new VaultIndex(vault);
            var h = new Harvester(vault, index);
            h.Ingest(new HarvestRequest
            {
                ProjectId = "p",
                SessionId = "sid",
                Provider = "cursor-agent",
                Messages = [new CanonicalMessage("1", "sid", "user", "unique-fts-token-xyzzy", null)]
            });

            Assert.NotEmpty(index.Search("unique-fts-token-xyzzy"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
