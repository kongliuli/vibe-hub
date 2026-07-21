using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VibeHub.Core.Models;
using VibeHub.Core.Storage;

namespace VibeHub.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    // ponytail: static workbench data proves the MVVM layout; replace each collection when its real service lands.
    public string WorkspaceName => "vibe-hub";
    public string BranchName => "master";
    public string ActiveTask => "完善进程生命周期";
    public int ContextUsage => 48;

    public ObservableCollection<WorkbenchTask> Tasks { get; } =
    [];

    public ObservableCollection<WorkspaceEntry> ProjectEntries { get; } = [];
    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<WorkbenchAgent> Agents { get; } =
    [
        new("opencode", "OpenCode", "检测中", "交互式 TUI 与本地会话", "#708097"),
        new("codex", "Codex", "检测中", "Codex CLI 与 rollout 会话", "#708097"),
        new("claude", "Claude Code", "检测中", "Claude Code CLI 与 projects JSONL", "#708097"),
        new("cursor-agent", "Cursor Agent", "检测中", "Cursor 独立 agent CLI", "#708097")
    ];

    public ObservableCollection<WorkbenchActivity> Activity { get; } = [];

    public ObservableCollection<WorkbenchChange> Changes { get; } = [];

    public ObservableCollection<WorkbenchSkill> Skills { get; } = [];

    public void SetAgentAvailability(string providerId, bool available)
    {
        var index = Agents.ToList().FindIndex(agent => agent.ProviderId == providerId);
        if (index < 0) return;
        var current = Agents[index];
        Agents[index] = current with
        {
            Status = available ? "Ready" : "未安装",
            StatusColor = available ? "#4CC38A" : "#F2B84B"
        };
    }

    public void AddActivity(string title, string detail, string kind)
    {
        Activity.Insert(0, new WorkbenchActivity(title, detail, DateTime.Now.ToString("HH:mm:ss"), kind));
        while (Activity.Count > 12)
            Activity.RemoveAt(Activity.Count - 1);
    }
}

public sealed record WorkbenchTask(string Id, string Title, string Status, string? Notes);
public sealed record WorkbenchAgent(string ProviderId, string Name, string Status, string Description, string StatusColor);
public sealed record WorkbenchActivity(string Title, string Detail, string Time, string Kind);
public sealed record WorkbenchChange(string Path, string Added, string Removed);
public sealed record WorkbenchSkill(string Name, string Source, string Targets, string Status);
