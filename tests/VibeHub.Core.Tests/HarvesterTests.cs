using VibeHub.Core.Models;
using VibeHub.Core.Archive;
using VibeHub.Core.Transcript;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Tests;

public sealed class HarvesterTests
{
    [Fact]
    public void Ingest_CopiesRaw_WritesCanonicalAndMeta()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-vault-" + Guid.NewGuid().ToString("n"));
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        var rawFile = Path.Combine(src, "rollout-demo.jsonl");
        File.WriteAllText(rawFile, "{\"type\":\"event_msg\"}\n");

        try
        {
            var vault = new VaultPaths(Path.Combine(root, "vault"));
            var h = new Harvester(vault);
            var result = h.Ingest(new HarvestRequest
            {
                ProjectId = "proj1",
                SessionId = "ses-1",
                Provider = "codex",
                SourcePath = rawFile,
                Messages =
                [
                    new CanonicalMessage("m1", "ses-1", "user", "hi", DateTimeOffset.UtcNow),
                    new CanonicalMessage("m2", "ses-1", "assistant", "yo", DateTimeOffset.UtcNow)
                ]
            });

            Assert.Equal(SessionLifecycle.Harvested, result.Meta.Lifecycle);
            Assert.Equal(2, result.Meta.MessageCount);
            Assert.False(string.IsNullOrEmpty(result.Meta.RawHash));
            Assert.False(string.IsNullOrEmpty(result.Meta.CanonicalHash));
            Assert.True(File.Exists(Path.Combine(vault.RawDir("proj1", "ses-1"), "rollout-demo.jsonl")));
            Assert.True(File.Exists(vault.CanonicalPath("proj1", "ses-1")));
            Assert.True(File.Exists(vault.MetaPath("proj1", "ses-1")));

            var meta = h.ReadMeta("proj1", "ses-1");
            Assert.NotNull(meta);
            Assert.True(meta!.IsHarvested);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Ingest_Empty_MarksIngestError()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-vault-e-" + Guid.NewGuid().ToString("n"));
        try
        {
            var h = new Harvester(new VaultPaths(root));
            var result = h.Ingest(new HarvestRequest
            {
                ProjectId = "p",
                SessionId = "s",
                Provider = "x",
                Messages = []
            });
            Assert.Equal(SessionLifecycle.IngestError, result.Meta.Lifecycle);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Ingest_RawWithoutMessages_MarksIngestError()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-vault-raw-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "raw.jsonl");
        File.WriteAllText(source, "{}");
        try
        {
            var result = new Harvester(new VaultPaths(Path.Combine(root, "vault"))).Ingest(new HarvestRequest
            {
                ProjectId = "p", SessionId = "s", Provider = "x", SourcePath = source, Messages = []
            });
            Assert.Equal(SessionLifecycle.IngestError, result.Meta.Lifecycle);
        }
        finally { try { Directory.Delete(root, true); } catch { /* ignore */ } }
    }

    [Fact]
    public void AutoHarvest_WithoutJobCaptureOrSession_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-capture-" + Guid.NewGuid().ToString("n"));
        try
        {
            var captures = new StreamJsonCaptureStore(root);
            captures.WriteAll(captures.Begin("codex"), "{\"type\":\"event_msg\"}");
            var result = new JobAutoHarvester(new Harvester(new VaultPaths(Path.Combine(root, "vault"))), captures)
                .TryHarvest(new Job { Id = "job", ProjectId = "p", Provider = "codex", Cwd = root });
            Assert.Null(result);
        }
        finally { try { Directory.Delete(root, true); } catch { /* ignore */ } }
    }

    [Fact]
    public void IngestFromArchive_PersistsSessionRawExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-export-" + Guid.NewGuid().ToString("n"));
        try
        {
            var vault = new VaultPaths(Path.Combine(root, "vault"));
            var result = new Harvester(vault).IngestFromArchive("p", new RawArchive(),
                new ArchiveEntry("s", "opencode", "s", null, null, "session"));
            Assert.Equal(SessionLifecycle.Harvested, result.Meta.Lifecycle);
            Assert.True(File.Exists(Path.Combine(vault.RawDir("p", "s"), "session.jsonl")));
        }
        finally { try { Directory.Delete(root, true); } catch { /* ignore */ } }
    }

    private sealed class RawArchive : IArchiveSource
    {
        public string SourceId => "opencode";
        public string DisplayName => "test";
        public bool Discover() => true;
        public IReadOnlyList<ArchiveEntry> List(int limit = 100) => [];
        public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
            => [new("m", entryId, "user", "hello", null)];
        public bool ExportRawSession(string entryId, string destination)
        {
            File.WriteAllText(destination, "{\"kind\":\"session\"}");
            return true;
        }
    }
}
