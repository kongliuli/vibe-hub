using VibeHub.Core.Inject;
using VibeHub.Core.Skills;

namespace VibeHub.Core.Tests;

public sealed class InjectSkillsTests
{
    [Fact]
    public void InjectSink_MigratesLegacyFilesWithoutOverwritingVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-inj-migrate-" + Guid.NewGuid().ToString("n"));
        var legacy = Path.Combine(root, "legacy");
        var vaultProjects = Path.Combine(root, "vault", "projects");
        try
        {
            Directory.CreateDirectory(Path.Combine(legacy, "p1"));
            Directory.CreateDirectory(Path.Combine(vaultProjects, "p1"));
            File.WriteAllText(Path.Combine(legacy, "p1", "memory.md"), "legacy memory");
            File.WriteAllText(Path.Combine(legacy, "p1", "handoff.md"), "legacy handoff");
            File.WriteAllText(Path.Combine(vaultProjects, "p1", "handoff.md"), "vault handoff");

            var sink = new InjectSink(vaultProjects, legacy);

            Assert.Equal("legacy memory", sink.Read("p1", InjectKind.Memory));
            Assert.Equal("vault handoff", sink.Read("p1", InjectKind.Handoff));
            Assert.True(File.Exists(Path.Combine(legacy, "p1", "memory.md")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ManagedBlock_Upsert_PreservesUserText()
    {
        var user = "# my agents\n\nhello user\n";
        var once = ManagedBlock.Upsert(user, "from hub v1");
        Assert.Contains("hello user", once);
        Assert.Contains(ManagedBlock.Begin, once);
        Assert.Contains("from hub v1", once);

        var twice = ManagedBlock.Upsert(once, "from hub v2");
        Assert.Contains("hello user", twice);
        Assert.Contains("from hub v2", twice);
        Assert.Equal(1, CountOccurrences(twice, ManagedBlock.Begin));

        var off = ManagedBlock.Remove(twice);
        Assert.Contains("hello user", off);
        Assert.DoesNotContain(ManagedBlock.Begin, off);
    }

    [Fact]
    public void InjectSink_And_Projector_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-inj-" + Guid.NewGuid().ToString("n"));
        var target = Path.Combine(root, "AGENTS.md");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(target, "# hand written\nkeep me\n");

            var sink = new InjectSink(Path.Combine(root, "sink"));
            var projector = new InjectProjector(sink, Path.Combine(root, "proj.json"));
            sink.Write("p1", InjectKind.Memory, "prefers Chinese");
            sink.Write("p1", InjectKind.Handoff, "next: ship P4");

            projector.Project("p1", [target]);
            var text = File.ReadAllText(target);
            Assert.Contains("keep me", text);
            Assert.Contains("prefers Chinese", text);
            Assert.True(File.Exists(target + ".vibe-hub.bak"));

            projector.ToggleOff(target);
            text = File.ReadAllText(target);
            Assert.Contains("keep me", text);
            Assert.DoesNotContain(ManagedBlock.Begin, text);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SkillInstaller_EnableDisable_WithOverrideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-skil-" + Guid.NewGuid().ToString("n"));
        var source = Path.Combine(root, "src", "demo-skill");
        var toolRoot = Path.Combine(root, "tool-skills");
        var manifest = Path.Combine(root, "manifest.json");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "SKILL.md"), "# demo\n");

            var installer = new SkillInstaller(new SkillManifestStore(manifest));
            var dest = installer.Enable("demo-skill", source, "codex", toolRoot);
            Assert.True(File.Exists(Path.Combine(dest, "SKILL.md")));
            Assert.False(installer.IsTargetDrifted("demo-skill", "codex"));

            File.WriteAllText(Path.Combine(dest, "SKILL.md"), "# demo\nchanged\n");
            Assert.True(installer.IsTargetDrifted("demo-skill", "codex"));
            Assert.Throws<InvalidOperationException>(
                () => installer.Enable("demo-skill", source, "codex", toolRoot));
            Assert.Throws<InvalidOperationException>(
                () => installer.Disable("demo-skill", "codex", toolRoot));
            Assert.True(Directory.Exists(dest));

            installer.Repair("demo-skill", "codex", toolRoot);
            Assert.False(installer.IsTargetDrifted("demo-skill", "codex"));
            var backup = Assert.Single(Directory.EnumerateDirectories(
                toolRoot, "demo-skill.vibe-hub-drift-*", SearchOption.TopDirectoryOnly));
            Assert.Contains("changed", File.ReadAllText(Path.Combine(backup, "SKILL.md")));

            installer.Disable("demo-skill", "codex", toolRoot);
            Assert.False(Directory.Exists(dest));

            var outside = Path.Combine(root, "outside");
            Assert.Throws<ArgumentException>(
                () => installer.Enable("..\\outside", source, "codex", toolRoot));
            Assert.False(Directory.Exists(outside));
            Assert.Throws<ArgumentException>(
                () => installer.Enable(".", source, "codex", toolRoot));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    private static int CountOccurrences(string hay, string needle)
    {
        var n = 0;
        for (var i = 0; (i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length)
            n++;
        return n;
    }
}
