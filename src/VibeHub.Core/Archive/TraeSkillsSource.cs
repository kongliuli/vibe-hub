using System.Text.RegularExpressions;
using VibeHub.Core.Models;

namespace VibeHub.Core.Archive;

/// <summary>Read-only Trae SKILL.md under ~/.trae-cn/skills and builtin_skills.</summary>
public sealed class TraeSkillsSource : IArchiveSource
{
    private readonly string[] _roots;

    public TraeSkillsSource(IEnumerable<string>? roots = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _roots = (roots ??
        [
            Path.Combine(home, ".trae-cn", "skills"),
            Path.Combine(home, ".trae-cn", "builtin_skills"),
            Path.Combine(home, ".trae-cn", "design_libraries"),
        ]).ToArray();
    }

    public string SourceId => "trae-skills";
    public string DisplayName => "Trae Skills";

    public bool Discover() => _roots.Any(Directory.Exists);

    public IReadOnlyList<ArchiveEntry> List(int limit = 100)
    {
        var list = new List<ArchiveEntry>();
        foreach (var root in _roots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(path)!;
                var name = Path.GetFileName(dir);
                var title = TryReadTitle(path) ?? name;
                list.Add(new ArchiveEntry(
                    path,
                    SourceId,
                    $"{name} — {title}",
                    path,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path)),
                    "skill"));
            }
        }

        return list
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500)
    {
        if (!File.Exists(entryId)) return [];
        // must be under one of our roots
        var full = Path.GetFullPath(entryId);
        if (!_roots.Any(r => Directory.Exists(r)
                             && full.StartsWith(Path.GetFullPath(r), StringComparison.OrdinalIgnoreCase)))
            return [];

        string text;
        try { text = File.ReadAllText(full); }
        catch { return []; }

        return
        [
            new CanonicalMessage(
                full + ":skill",
                full,
                "skill",
                text.Trim(),
                new DateTimeOffset(File.GetLastWriteTimeUtc(full)))
        ];
    }

    private static string? TryReadTitle(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path).Take(40))
            {
                var m = Regex.Match(line, @"^#\s+(.+)");
                if (m.Success) return m.Groups[1].Value.Trim();
                m = Regex.Match(line, @"^name:\s*[""']?([^""'\r\n]+)");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
        }
        catch { }

        return null;
    }
}
