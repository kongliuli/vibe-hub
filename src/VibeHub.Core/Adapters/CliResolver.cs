namespace VibeHub.Core.Adapters;

public static class CliResolver
{
    /// <summary>
    /// Cursor agent CLI lives in %LOCALAPPDATA%\cursor-agent (separate from Cursor IDE).
    /// Prefer .cmd/.exe over .ps1 for ConPTY / redirected stdio.
    /// </summary>
    public static string? FindCursorAgent()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cursor-agent");
        foreach (var name in new[] { "agent.cmd", "agent.exe", "cursor-agent.cmd", "cursor-agent.exe" })
        {
            var candidate = Path.Combine(local, name);
            if (File.Exists(candidate)) return candidate;
        }

        return PreferNonPs1("agent") ?? PreferNonPs1("cursor-agent");
    }

    public static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.PS1").Split(';')
            : [""];

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                    return candidate;
            }

            var bare = Path.Combine(dir, command);
            if (File.Exists(bare))
                return bare;
        }

        return null;
    }

    /// <summary>Prefer a real .exe/.cmd over .ps1 wrappers for cleaner stdio.</summary>
    public static string? PreferNonPs1(string command)
    {
        var found = FindOnPath(command);
        if (found is null) return null;
        if (!found.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return found;

        var dir = Path.GetDirectoryName(found)!;
        foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
        {
            var alt = Path.Combine(dir, command + ext);
            if (File.Exists(alt)) return alt;
        }

        // Bun / standalone next to npm shim
        var bun = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".bun", "bin", command + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (File.Exists(bun)) return bun;

        return found;
    }
}
