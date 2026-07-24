namespace VibeHub.Core.Vault;

public sealed class VaultPaths
{
    public VaultPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "vibe-hub-vault");
    }

    public string Root { get; }

    public string ProjectsRoot => Path.Combine(Root, "projects");

    public string ProjectDir(string projectId) => Path.Combine(ProjectsRoot, Sanitize(projectId));

    public string SessionDir(string projectId, string sessionId)
        => Path.Combine(ProjectDir(projectId), "sessions", Sanitize(sessionId));

    public string RawDir(string projectId, string sessionId)
        => Path.Combine(SessionDir(projectId, sessionId), "raw");

    public string CanonicalPath(string projectId, string sessionId)
        => Path.Combine(SessionDir(projectId, sessionId), "canonical.jsonl");

    public string MetaPath(string projectId, string sessionId)
        => Path.Combine(SessionDir(projectId, sessionId), "meta.json");

    public void EnsureLayout(string projectId)
    {
        Directory.CreateDirectory(ProjectDir(projectId));
        Directory.CreateDirectory(Path.Combine(ProjectDir(projectId), "skill-drafts"));
        Directory.CreateDirectory(Path.Combine(Root, "skills"));
    }

    private static string Sanitize(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            id = id.Replace(c, '_');
        return string.IsNullOrWhiteSpace(id) ? "unknown" : id;
    }
}
