using System.Text.Json;
using System.IO;

namespace VibeHub.App.Services;

internal sealed class HubPreferences
{
    public string DefaultProvider { get; set; } = "opencode";
    public string DefaultWorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public bool AutoFocusNewTerminal { get; set; } = true;
    public string OpenCodeTaskAgent { get; set; } = "";
    public string OpenCodeTaskModel { get; set; } = "opencode-go/deepseek-v4-flash";
}

internal static class HubPreferencesStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vibe-hub", "settings.json");

    public static HubPreferences Load()
    {
        if (!File.Exists(Path))
            return new HubPreferences();
        try
        {
            return JsonSerializer.Deserialize<HubPreferences>(File.ReadAllText(Path))
                   ?? new HubPreferences();
        }
        catch (JsonException)
        {
            return new HubPreferences();
        }
    }

    public static void Save(HubPreferences preferences)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(preferences, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
