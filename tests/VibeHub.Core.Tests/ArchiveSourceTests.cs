using VibeHub.Core.Archive;

namespace VibeHub.Core.Tests;

public sealed class ArchiveSourceTests
{
    [Fact]
    public void WorkBuddyMemory_ParsesFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vh-wb-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var srcFile = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-workbuddy-memory.md");
            var dest = Path.Combine(dir, "test-uid_memory.md");
            File.Copy(srcFile, dest);

            var src = new WorkBuddyMemorySource(dir);
            Assert.True(src.Discover());
            var list = src.List();
            Assert.Single(list);
            Assert.Equal("memory", list[0].Kind);

            var msgs = src.GetMessages(list[0].Id);
            Assert.Single(msgs);
            Assert.Equal("memory", msgs[0].Role);
            Assert.Contains("工作背景", msgs[0].Content);
            Assert.DoesNotContain("RAW_JSON", msgs[0].Content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TraeSkills_ListsSkillMd()
    {
        var root = Path.Combine(Path.GetTempPath(), "vh-sk-" + Guid.NewGuid().ToString("n"));
        var skillDir = Path.Combine(root, "skills", "sample-skill");
        Directory.CreateDirectory(skillDir);
        try
        {
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-skill", "SKILL.md"),
                Path.Combine(skillDir, "SKILL.md"));

            var src = new TraeSkillsSource([Path.Combine(root, "skills")]);
            Assert.True(src.Discover());
            var list = src.List();
            Assert.Single(list);
            Assert.Equal("skill", list[0].Kind);

            var msgs = src.GetMessages(list[0].Id);
            Assert.Contains("Sample Skill", msgs[0].Content);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TraeEncryptedDb_DetectsNonSqliteMagic()
    {
        var path = Path.Combine(Path.GetTempPath(), "vh-enc-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            File.WriteAllBytes(path, Enumerable.Repeat((byte)0xAB, 64).ToArray());
            Assert.True(TraeEncryptedDbProbe.LooksEncrypted(path));

            var probe = new TraeEncryptedDbProbe([("test", path)]);
            var list = probe.List();
            Assert.Single(list);
            Assert.Equal("encrypted-meta", list[0].Kind);
            Assert.Contains("加密", list[0].Title);

            var msgs = probe.GetMessages("test");
            Assert.Contains("不解密", msgs[0].Content);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TraeEncryptedDb_PlainSqliteMagic_NotEncrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), "vh-sql-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            var magic = "SQLite format 3\0"u8.ToArray();
            var buf = new byte[64];
            magic.CopyTo(buf, 0);
            File.WriteAllBytes(path, buf);
            Assert.False(TraeEncryptedDbProbe.LooksEncrypted(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void KimiVault_ListsMarkdown()
    {
        var vault = Path.Combine(Path.GetTempPath(), "vh-kimi-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(vault, "sections"));
        try
        {
            File.WriteAllText(Path.Combine(vault, "about_user.md"), "# About\nhello");
            File.WriteAllText(Path.Combine(vault, "sections", "taste.md"), "# Taste\ncoffee");

            var src = new KimiMemoryVaultSource(vault);
            var list = src.List();
            Assert.Equal(2, list.Count);

            var about = list.First(e => e.Id.Contains("about_user"));
            var msgs = src.GetMessages(about.Id);
            Assert.Contains("hello", msgs[0].Content);
        }
        finally
        {
            try { Directory.Delete(vault, true); } catch { /* ignore */ }
        }
    }
}
