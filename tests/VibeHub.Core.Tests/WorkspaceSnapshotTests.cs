using VibeHub.Core.Storage;

namespace VibeHub.Core.Tests;

public sealed class WorkspaceSnapshotTests
{
    [Fact]
    public void Scan_ReturnsDirectoriesFirst_AndHonorsLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibe-hub-tree-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "README.md"), "");
        File.WriteAllText(Path.Combine(root, "z.txt"), "");
        try
        {
            var entries = WorkspaceSnapshot.Scan(root, 2);

            Assert.Equal(["src", "README.md"], entries.Select(entry => entry.Name));
            Assert.True(entries[0].IsDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
