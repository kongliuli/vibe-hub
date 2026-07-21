namespace VibeHub.Core.Inject;

public enum InjectKind
{
    Memory,
    Handoff,
    Context
}

/// <summary>Authoritative inject files under LocalAppData/vibe-hub/inject/&lt;projectId&gt;/.</summary>
public sealed class InjectSink
{
    private readonly string _root;

    public InjectSink(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibe-hub", "inject");
    }

    public string Root => _root;

    public string ProjectDir(string projectId)
        => Path.Combine(_root, Sanitize(projectId));

    public string PathFor(string projectId, InjectKind kind)
        => Path.Combine(ProjectDir(projectId), FileName(kind));

    public void Write(string projectId, InjectKind kind, string content)
    {
        var dir = ProjectDir(projectId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(PathFor(projectId, kind), content ?? "");
    }

    public string? Read(string projectId, InjectKind kind)
    {
        var path = PathFor(projectId, kind);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>Compose the managed-block payload from sink kinds that exist.</summary>
    public string ComposeProjection(string projectId)
    {
        var parts = new List<string> { "# vibe-hub inject", "" };
        foreach (var kind in new[] { InjectKind.Memory, InjectKind.Handoff, InjectKind.Context })
        {
            var body = Read(projectId, kind);
            if (string.IsNullOrWhiteSpace(body)) continue;
            parts.Add($"## {kind}");
            parts.Add(body.Trim());
            parts.Add("");
        }

        return string.Join('\n', parts).TrimEnd() + "\n";
    }

    private static string FileName(InjectKind kind) => kind switch
    {
        InjectKind.Memory => "memory.md",
        InjectKind.Handoff => "handoff.md",
        InjectKind.Context => "context.md",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Sanitize(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '_');
        return string.IsNullOrWhiteSpace(id) ? "default" : id;
    }
}
