using System.Diagnostics;
using VibeHub.Core.Workspace;

namespace VibeHub.Core.Tests;

public sealed class GitChangesServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsTrackedAndUntrackedChanges()
    {
        var root = CreateTempDirectory();
        try
        {
            RunGit(root, "init");
            RunGit(root, "config", "user.email", "test@example.com");
            RunGit(root, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "before\n");
            RunGit(root, "add", "tracked.txt");
            RunGit(root, "commit", "-m", "initial");
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "after\nmore\n");
            File.WriteAllText(Path.Combine(root, "untracked.txt"), "new\n");

            var result = await new GitChangesService().GetAsync(root);

            Assert.True(result.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(result.Branch));
            Assert.Contains(result.Changes, change => change.Path == "tracked.txt" && change.Added == 2 && change.Deleted == 1);
            Assert.Contains(result.Changes, change => change.Path == "untracked.txt" && change.Added == 0 && change.Deleted == 0);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsUnavailableOutsideGitRepository()
    {
        var root = CreateTempDirectory();
        try
        {
            var result = await new GitChangesService().GetAsync(root);

            Assert.False(result.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
            Assert.Empty(result.Changes);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibe-hub-git-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RunGit(string cwd, params string[] arguments)
    {
        var info = new ProcessStartInfo { FileName = "git", WorkingDirectory = cwd, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private static void DeleteTempDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
