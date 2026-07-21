using VibeHub.Core.Adapters;
using VibeHub.Core.Models;

namespace VibeHub.Core.Tests;

/// <summary>
/// Mirrors VibeHub.Terminal.EwtcProcessLauncher.BuildCommandLine (Core-only, no WPF).
/// </summary>
public sealed class EwtcCommandLineTests
{
    [Fact]
    public void BuildCommandLine_NoEnv_IsBareExe()
    {
        var spec = new ProcessStartSpec(
            @"C:\Program Files\opencode\opencode.exe",
            ["-s", "ses_1"],
            @"D:\My Projects\demo");

        var cmd = BuildCommandLine(spec);

        Assert.Equal(@"""C:\Program Files\opencode\opencode.exe"" -s ses_1", cmd);
    }

    [Fact]
    public void BuildCommandLine_PrefixesOpenCodeDisableAutoupdate()
    {
        var spec = new ProcessStartSpec(
            "opencode",
            [],
            @"D:\work",
            OpenCodeAdapter.LaunchEnvironment);

        var cmd = BuildCommandLine(spec);

        Assert.Contains("set \"OPENCODE_DISABLE_AUTOUPDATE=true\"", cmd);
        Assert.Contains("set \"OPENCODE_DISABLE_MODELS_FETCH=1\"", cmd);
        Assert.Contains("&& opencode", cmd);
        Assert.DoesNotContain("cd /d", cmd); // cwd is EasyTerminalControl.WorkingDirectory
    }

    // keep in sync with EwtcProcessLauncher.BuildCommandLine
    private static string BuildCommandLine(ProcessStartSpec spec)
    {
        var args = string.Join(" ", spec.Arguments.Select(Quote));
        var exe = Quote(spec.FileName);
        var body = $"{exe} {args}".TrimEnd();
        if (spec.Environment is not { Count: > 0 })
            return body;

        var sets = string.Join(" && ", spec.Environment.Select(kv =>
            $"set \"{kv.Key.Replace("\"", "").Replace("=", "")}={kv.Value.Replace("\"", "")}\""));
        return $"cmd.exe /c \"{sets} && {body}\"";
    }

    private static string Quote(string s)
        => s.Contains(' ') || s.Contains('"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;
}
