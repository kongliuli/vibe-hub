using System.Security.Cryptography;
using System.Text;
using VibeHub.Core.Inject;

namespace VibeHub.Core.Skills;

public sealed class SkillInstaller
{
    private readonly SkillManifestStore _store;

    public SkillInstaller(SkillManifestStore? store = null)
        => _store = store ?? new SkillManifestStore();

    public static string ToolSkillsRoot(string toolId)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return toolId.ToLowerInvariant() switch
        {
            "opencode" => Path.Combine(home, ".config", "opencode", "skills"),
            "codex" => Path.Combine(home, ".codex", "skills"),
            "claude" => Path.Combine(home, ".claude", "skills"),
            "cursor" => Path.Combine(home, ".cursor", "skills"),
            _ => throw new ArgumentException("Unknown tool: " + toolId, nameof(toolId))
        };
    }

    public string Enable(string skillId, string sourceDir, string toolId, string? targetRootOverride = null)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(sourceDir);

        var targetRoot = Path.GetFullPath(targetRootOverride ?? ToolSkillsRoot(toolId));
        var target = ResolveTarget(targetRoot, skillId);
        Directory.CreateDirectory(targetRoot);

        var all = _store.Load();
        ToolInstall? current = null;
        if (all.TryGetValue(skillId, out var existing))
            existing.Tools.TryGetValue(toolId, out current);

        var hash = HashDirectory(sourceDir);
        if (Directory.Exists(target))
        {
            if (current is null
                || !current.Enabled
                || !SamePath(current.TargetPath, target)
                || string.IsNullOrEmpty(current.InstalledHash)
                || !string.Equals(HashDirectory(target), current.InstalledHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Skill target is unmanaged or drifted; refusing to overwrite: {target}");
            }
        }

        ReplaceDirectory(sourceDir, target, targetRoot);
        if (!all.TryGetValue(skillId, out var rec))
        {
            rec = new SkillRecord { SkillId = skillId, SourcePath = Path.GetFullPath(sourceDir) };
            all[skillId] = rec;
        }

        rec.SourcePath = Path.GetFullPath(sourceDir);
        rec.SourceHash = hash;
        rec.Tools[toolId] = new ToolInstall
        {
            TargetPath = target,
            Enabled = true,
            InstalledHash = hash,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _store.Save(all);
        return target;
    }

    public void Disable(string skillId, string toolId, string? targetRootOverride = null)
    {
        var all = _store.Load();
        if (!all.TryGetValue(skillId, out var rec)) return;
        if (!rec.Tools.TryGetValue(toolId, out var inst)) return;

        var targetRoot = Path.GetFullPath(targetRootOverride ?? ToolSkillsRoot(toolId));
        var target = ResolveTarget(targetRoot, skillId);
        if (!SamePath(inst.TargetPath, target))
            throw new InvalidOperationException($"Skill target is outside its configured root: {inst.TargetPath}");

        if (Directory.Exists(inst.TargetPath))
        {
            if (string.IsNullOrEmpty(inst.InstalledHash)
                || !string.Equals(HashDirectory(inst.TargetPath), inst.InstalledHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Skill target has local changes; refusing to delete: {inst.TargetPath}");
            }

            Directory.Delete(inst.TargetPath, recursive: true);
        }

        inst.Enabled = false;
        inst.InstalledHash = null;
        inst.UpdatedAt = DateTimeOffset.UtcNow;
        _store.Save(all);
    }

    public bool IsTargetDrifted(string skillId, string toolId)
    {
        var all = _store.Load();
        if (!all.TryGetValue(skillId, out var rec)) return false;
        if (!rec.Tools.TryGetValue(toolId, out var inst) || !inst.Enabled) return false;
        if (!Directory.Exists(inst.TargetPath) || string.IsNullOrEmpty(inst.InstalledHash)) return true;
        return !string.Equals(HashDirectory(inst.TargetPath), inst.InstalledHash, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    private static string ResolveTarget(string targetRoot, string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("Skill id is required", nameof(skillId));

        var target = Path.GetFullPath(Path.Combine(targetRoot, skillId));
        var relative = Path.GetRelativePath(targetRoot, target);
        if (Path.IsPathRooted(relative)
            || relative.Equals(".", StringComparison.Ordinal)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Skill id must be a single directory name", nameof(skillId));
        }

        return target;
    }

    private static void ReplaceDirectory(string source, string target, string targetRoot)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var stage = Path.Combine(targetRoot, $".vibe-hub-stage-{suffix}");
        var backup = Path.Combine(targetRoot, $".vibe-hub-backup-{suffix}");

        try
        {
            CopyDirectory(source, stage);
            if (Directory.Exists(target))
                Directory.Move(target, backup);

            Directory.Move(stage, target);
            if (Directory.Exists(backup))
                TryDeleteDirectory(backup);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }
        finally
        {
            if (Directory.Exists(stage))
                TryDeleteDirectory(stage);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool SamePath(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public static string HashDirectory(string dir)
    {
        var sb = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            var hex = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            sb.Append(rel).Append(':').Append(hex).Append(';');
        }

        return ManagedBlock.Sha256Hex(sb.ToString());
    }
}
