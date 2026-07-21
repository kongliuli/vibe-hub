using VibeHub.Core.Inject;
using VibeHub.Core.Migrate;
using VibeHub.Core.Vault;

namespace VibeHub.Core.Tests;

public sealed class MigrationServiceTests
{
    [Fact]
    public void Prepare_And_ApplyToSink_WritesHandoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-mig-" + Guid.NewGuid().ToString("n"));
        try
        {
            var vault = new VaultPaths(Path.Combine(root, "vault"));
            vault.EnsureLayout("p");
            var sessionDir = vault.SessionDir("p", "s1");
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(Path.Combine(sessionDir, "summary.md"), "# Done\n\nMigrated.");

            var sink = new InjectSink(Path.Combine(root, "inject"));
            var mig = new MigrationService(vault, sink);
            var plan = mig.Prepare("p", "s1", "codex", "opencode");
            Assert.Contains("Done", plan.Summary);

            var dir = mig.ApplyToSink(plan);
            Assert.True(File.Exists(Path.Combine(dir, "handoff.md")));
            Assert.Contains("opencode", File.ReadAllText(Path.Combine(dir, "handoff.md")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
