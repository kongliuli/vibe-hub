namespace VibeHub.Core.Storage;

public sealed record WorkspaceEntry(string Name, string RelativePath, bool IsDirectory);

public static class WorkspaceSnapshot
{
    public static IReadOnlyList<WorkspaceEntry> Scan(string root, int limit = 80)
    {
        if (!Directory.Exists(root) || limit <= 0) return [];
        try
        {
            return Directory.EnumerateFileSystemEntries(root)
                .Select(path => new FileSystemInfoBox(path))
                .Where(item => (item.Attributes & FileAttributes.Hidden) == 0)
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(item => new WorkspaceEntry(item.Name, item.Name, item.IsDirectory))
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private sealed class FileSystemInfoBox
    {
        private readonly FileSystemInfo _info;
        public FileSystemInfoBox(string path)
            => _info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        public string Name => _info.Name;
        public FileAttributes Attributes => _info.Attributes;
        public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    }
}
