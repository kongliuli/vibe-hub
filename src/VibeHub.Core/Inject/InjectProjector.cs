using System.Text.Json;

namespace VibeHub.Core.Inject;

public sealed class InjectProjector
{
    private readonly InjectSink _sink;
    private readonly string _manifestPath;

    public InjectProjector(InjectSink sink, string? manifestPath = null)
    {
        _sink = sink;
        _manifestPath = manifestPath ?? Path.Combine(_sink.Root, "projections.json");
    }

    public void Project(string projectId, IEnumerable<string> targetFiles)
    {
        var payload = _sink.ComposeProjection(projectId);
        var state = Load();

        foreach (var target in targetFiles)
        {
            var full = Path.GetFullPath(target);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string existing = File.Exists(full) ? File.ReadAllText(full) : "";
            if (File.Exists(full) && !state.Targets.ContainsKey(full))
            {
                var bak = full + ".vibe-hub.bak";
                if (!File.Exists(bak))
                    File.Copy(full, bak);
            }

            var next = ManagedBlock.Upsert(existing, payload);
            File.WriteAllText(full, next);
            state.Targets[full] = new ProjectionRecord(projectId, ManagedBlock.Sha256Hex(next), DateTimeOffset.UtcNow);
        }

        Save(state);
    }

    public void ToggleOff(string targetFile)
    {
        var full = Path.GetFullPath(targetFile);
        if (!File.Exists(full)) return;

        var existing = File.ReadAllText(full);
        var next = ManagedBlock.Remove(existing);
        File.WriteAllText(full, next);

        var state = Load();
        state.Targets.Remove(full);
        Save(state);
    }

    public bool IsDrifted(string targetFile)
    {
        var full = Path.GetFullPath(targetFile);
        var state = Load();
        if (!state.Targets.TryGetValue(full, out var rec) || !File.Exists(full))
            return false;
        var now = ManagedBlock.Sha256Hex(File.ReadAllText(full));
        return !string.Equals(now, rec.Hash, StringComparison.OrdinalIgnoreCase);
    }

    private ProjectionState Load()
    {
        if (!File.Exists(_manifestPath))
            return new ProjectionState();
        try
        {
            return JsonSerializer.Deserialize<ProjectionState>(File.ReadAllText(_manifestPath))
                   ?? new ProjectionState();
        }
        catch (JsonException)
        {
            return new ProjectionState();
        }
    }

    private void Save(ProjectionState state)
    {
        var dir = Path.GetDirectoryName(_manifestPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class ProjectionState
    {
        public Dictionary<string, ProjectionRecord> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ProjectionRecord(string ProjectId, string Hash, DateTimeOffset UpdatedAt);
}
