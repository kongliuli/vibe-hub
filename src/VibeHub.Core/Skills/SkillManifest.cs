using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeHub.Core.Skills;

public sealed class SkillRecord
{
    public required string SkillId { get; set; }
    public required string SourcePath { get; set; }
    public string? SourceHash { get; set; }
    public Dictionary<string, ToolInstall> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ToolInstall
{
    public required string TargetPath { get; set; }
    public bool Enabled { get; set; }
    public string? InstalledHash { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class SkillManifestStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SkillManifestStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibe-hub", "skills-manifest.json");
    }

    public string ManifestPath => _path;

    public Dictionary<string, SkillRecord> Load()
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, SkillRecord>>(File.ReadAllText(_path), JsonOpts)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(Dictionary<string, SkillRecord> data)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOpts));
    }
}
