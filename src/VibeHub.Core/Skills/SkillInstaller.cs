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

        var targetRoot = targetRootOverride ?? ToolSkillsRoot(toolId);
        var target = Path.Combine(targetRoot, skillId);
        Directory.CreateDirectory(targetRoot);

        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);

        CopyDirectory(sourceDir, target);
        var hash = HashDirectory(sourceDir);

        var all = _store.Load();
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

    public void Disable(string skillId, string toolId)
    {
        var all = _store.Load();
        if (!all.TryGetValue(skillId, out var rec)) return;
        if (!rec.Tools.TryGetValue(toolId, out var inst)) return;

        if (Directory.Exists(inst.TargetPath))
            Directory.Delete(inst.TargetPath, recursive: true);

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
