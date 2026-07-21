using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace VibeHub.Core.Workspace;

public sealed record GitChange(string Path, int Added, int Deleted);

public sealed record GitChangesResult(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<GitChange> Changes,
    string? Branch = null);

public sealed class GitChangesService
{
    public async Task<GitChangesResult> GetAsync(string cwd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
            return Unavailable("Working directory does not exist.");

        try
        {
            var root = await RunGitAsync(cwd, ["rev-parse", "--show-toplevel"], ct).ConfigureAwait(false);
            if (root.ExitCode != 0)
                return Unavailable("Not a Git repository.");

            var repositoryRoot = root.Output.Trim();
            var head = await RunGitAsync(repositoryRoot, ["rev-parse", "--verify", "HEAD"], ct).ConfigureAwait(false);
            var changes = new Dictionary<string, GitChange>(StringComparer.Ordinal);
            if (head.ExitCode == 0)
            {
                var diff = await RunGitAsync(repositoryRoot, ["diff", "HEAD", "--numstat", "-z"], ct).ConfigureAwait(false);
                if (diff.ExitCode != 0)
                    return Unavailable("Git could not read the working tree.");
                changes = ReadNumstat(diff.Output).ToDictionary(change => change.Path, StringComparer.Ordinal);
            }

            var status = await RunGitAsync(repositoryRoot, ["status", "--porcelain=v1", "-z"], ct).ConfigureAwait(false);
            if (status.ExitCode != 0)
                return Unavailable("Git could not read the working tree.");

            foreach (var path in ReadStatusPaths(status.Output))
                changes.TryAdd(path, new GitChange(path, 0, 0));

            var branch = await RunGitAsync(repositoryRoot, ["branch", "--show-current"], ct).ConfigureAwait(false);
            return new(
                true,
                null,
                changes.Values.OrderBy(change => change.Path, StringComparer.Ordinal).ToList(),
                string.IsNullOrWhiteSpace(branch.Output) ? "HEAD" : branch.Output.Trim());
        }
        catch (Win32Exception)
        {
            return Unavailable("Git is not installed or unavailable.");
        }
        catch (IOException)
        {
            return Unavailable("Git is unavailable.");
        }
        catch (InvalidOperationException)
        {
            return Unavailable("Git is unavailable.");
        }
    }

    private static GitChangesResult Unavailable(string reason) => new(false, reason, []);

    private static IEnumerable<GitChange> ReadNumstat(string output)
    {
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.Split('\t', 3);
            if (fields.Length != 3 || !int.TryParse(fields[0], out var added) || !int.TryParse(fields[1], out var deleted)) continue;
            yield return new(fields[2], added, deleted);
        }
    }

    private static IEnumerable<string> ReadStatusPaths(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(entry => entry.Length > 3 && entry[2] == ' ' && !entry.StartsWith("!! ", StringComparison.Ordinal))
        .Select(entry => entry[3..]);

    private static async Task<(int ExitCode, string Output)> RunGitAsync(string cwd, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        await error.ConfigureAwait(false);
        return (process.ExitCode, await output.ConfigureAwait(false));
    }
}
