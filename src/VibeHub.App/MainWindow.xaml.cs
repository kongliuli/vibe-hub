using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EasyWindowsTerminalControl;
using Microsoft.Win32;
using VibeHub.Core.Adapters;
using VibeHub.Core.Archive;
using VibeHub.Core.Inject;
using VibeHub.Core.Models;
using VibeHub.Core.Storage;
using VibeHub.Core.Supervisor;
using VibeHub.Core.Distill;
using VibeHub.Core.Migrate;
using VibeHub.Core.Vault;
using VibeHub.Core.Skills;
using VibeHub.Core.Workspace;
using VibeHub.Terminal;
using VibeHub.App.ViewModels;
using VibeHub.App.Services;

namespace VibeHub.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private HubPreferences _preferences = HubPreferencesStore.Load();
    private readonly HubStore _store;
    private readonly OpenCodeAdapter _opencode = new();
    private readonly CodexAdapter _codex = new();
    private readonly ClaudeAdapter _claude = new();
    private readonly CursorAgentAdapter _cursorAgent = new();
    private readonly EwtcProcessLauncher _launcher = new();
    private readonly JobSupervisor _supervisor;
    private readonly ArchiveCatalog _archives = new();
    private readonly InjectSink _injectSink = new();
    private readonly InjectProjector _injectProjector;
    private readonly VaultPaths _vaultPaths = new();
    private readonly VaultIndex _vaultIndex;
    private readonly Harvester _harvester;
    private readonly Distiller _distiller = new();
    private readonly ProcessHeadlessRunner _headless = new();
    private readonly GitChangesService _gitChanges = new();
    private readonly MigrationService _migration;
    private readonly JobAutoHarvester _autoHarvest;
    private string? _lastInjectTarget;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<IPseudoTerminal, EasyTerminalControl> _pendingTerminals = new();
    private readonly Dictionary<string, EasyTerminalControl> _terminalsByJob = new(StringComparer.Ordinal);
    private string? _activeJobId;
    private string? _structuredEntryId;
    private string? _structuredSourceId;
    private string? _structuredPath;
    private Project? _currentProject;
    private CancellationTokenSource? _agentTaskCancellation;
    private DateTime _structuredMtime = DateTime.MinValue;

    public ObservableCollection<SessionRow> Sessions { get; } = new();
    public ObservableCollection<JobRow> Jobs { get; } = new();
    public ObservableCollection<MessageRow> Messages { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        TerminalInputTuning.SuppressHostShortcuts(
            this,
            () => TerminalHost.Child as EasyTerminalControl);
        SessionList.ItemsSource = Sessions;
        JobList.ItemsSource = Jobs;
        MessageList.ItemsSource = Messages;
        ApplyAgentTaskPreferences();
        CwdBox.Text = Directory.Exists(_preferences.DefaultWorkingDirectory)
            ? _preferences.DefaultWorkingDirectory
            : Environment.CurrentDirectory;

        _store = HubStore.OpenDefault();
        SelectWorkspace(CwdBox.Text);
        _vaultIndex = new VaultIndex(_vaultPaths);
        _harvester = new Harvester(_vaultPaths, _vaultIndex);
        _migration = new MigrationService(_vaultPaths, _injectSink);
        _supervisor = new JobSupervisor(_launcher, [_opencode, _codex, _claude, _cursorAgent], _store);
        _supervisor.JobLaunched += OnJobLaunched;
        _autoHarvest = new JobAutoHarvester(_harvester, CurrentProjectId, _distiller.Captures, _archives);
        _supervisor.JobExited += job =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                var cap = _supervisor.GetCapture(job.Id);
                var result = _autoHarvest.TryHarvest(job, cap);
                RefreshJobs();
                _viewModel.AddActivity(
                    "Job 已结束",
                    $"{job.Provider} · exit {job.ExitCode?.ToString() ?? "?"} · {job.Id[..8]}",
                    "Job");
                if (result is not null)
                {
                    StructuredStatus.Text =
                        $"Auto-Harvest {result.Meta.Lifecycle} · {result.Meta.SessionId} · msgs={result.Meta.MessageCount}";
                    _viewModel.AddActivity(
                        "自动 Harvest",
                        $"{result.Meta.Lifecycle} · {result.Meta.MessageCount} messages",
                        "Vault");
                }
            });
        };
        _injectProjector = new InjectProjector(_injectSink);
        _launcher.ControlCreated += OnTerminalCreated;

        SelectProvider(_preferences.DefaultProvider);

        var openCodeReady = _opencode.Discover();
        var codexReady = _codex.Discover();
        var claudeReady = _claude.Discover();
        var cursorReady = _cursorAgent.Discover();
        _viewModel.SetAgentAvailability("opencode", openCodeReady);
        _viewModel.SetAgentAvailability("codex", codexReady);
        _viewModel.SetAgentAvailability("claude", claudeReady);
        _viewModel.SetAgentAvailability("cursor-agent", cursorReady);
        LoadManagedSkills();
        StructuredStatus.Text = cursorReady
            ? "cursor-agent CLI ready"
            : _cursorAgent.InstallHint;

        foreach (var src in _archives.Discovered())
            ArchiveSourceBox.Items.Add(src);
        if (ArchiveSourceBox.Items.Count > 0)
            ArchiveSourceBox.SelectedIndex = 0;

        NavigateTo("workbench");

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => RefreshStructuredIfNeeded();
        _pollTimer.Start();

        Loaded += (_, _) => RefreshArchiveEntries();
        Closed += (_, _) =>
        {
            _agentTaskCancellation?.Cancel();
            _agentTaskCancellation?.Dispose();
            _pollTimer.Stop();
            _vaultIndex.Dispose();
            _store.Dispose();
        };
    }

    private string CurrentProjectId()
        => _currentProject?.Id ?? throw new InvalidOperationException("No current project");

    private void SelectWorkspace(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var project = _store.ListProjects().FirstOrDefault(candidate =>
            string.Equals(Path.GetFullPath(candidate.RootPath), fullRoot, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            project = new Project(
                Guid.NewGuid().ToString("n"),
                fullRoot,
                Path.GetFileName(fullRoot.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : "workspace");
            _store.UpsertProject(project);
        }

        _currentProject = project;
        CwdBox.Text = fullRoot;
        InjectProjectBox.Text = project.Id;
        RefreshWorkspaceData();
    }

    private void RefreshWorkspaceData()
    {
        _viewModel.Projects.Clear();
        foreach (var project in _store.ListProjects())
            _viewModel.Projects.Add(project);

        _viewModel.ProjectEntries.Clear();
        if (_currentProject is not null)
        {
            foreach (var entry in WorkspaceSnapshot.Scan(_currentProject.RootPath))
                _viewModel.ProjectEntries.Add(entry);
        }

        _viewModel.Tasks.Clear();
        if (_currentProject is not null)
        {
            foreach (var task in _store.ListTasks(_currentProject.Id))
                _viewModel.Tasks.Add(new WorkbenchTask(task.Id, task.Title, task.Status, task.Notes));
        }
        TaskEmptyText.Visibility = _viewModel.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = RefreshGitChangesAsync(_currentProject?.RootPath);
    }

    private async Task RefreshGitChangesAsync(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var result = await _gitChanges.GetAsync(root);
        if (!string.Equals(_currentProject?.RootPath, root, StringComparison.OrdinalIgnoreCase)) return;

        _viewModel.Changes.Clear();
        if (!result.IsAvailable)
        {
            GitBranchStatus.Text = "⌘  非 Git 工作区";
            GitChangesStatus.Text = "●  Git 不可用";
            ChangesEmptyText.Text = result.UnavailableReason ?? "Git 不可用";
            return;
        }

        foreach (var change in result.Changes)
            _viewModel.Changes.Add(new WorkbenchChange(change.Path, $"+{change.Added}", $"-{change.Deleted}"));

        GitBranchStatus.Text = $"⌘  {result.Branch}";
        GitChangesStatus.Text = result.Changes.Count == 0 ? "●  工作树干净" : $"●  {result.Changes.Count} 变更";
        ChangesEmptyText.Text = result.Changes.Count == 0 ? "当前工作树没有未提交变更" : "";
    }

    private void OnTerminalCreated(IPseudoTerminal terminal, EasyTerminalControl control, ProcessStartSpec _)
    {
        TerminalInputTuning.ApplyToControl(control);
        _pendingTerminals[terminal] = control;
    }

    private void OnJobLaunched(Job job, IPseudoTerminal terminal)
    {
        void Attach()
        {
            if (!_pendingTerminals.Remove(terminal, out var control))
                return;

            _terminalsByJob[job.Id] = control;
            _activeJobId = job.Id;
            ShowTerminal(job.Id, focus: _preferences.AutoFocusNewTerminal);
            _viewModel.AddActivity("Job 已启动", $"{job.Provider} · {job.Cwd}", "Job");
        }

        if (Dispatcher.CheckAccess()) Attach();
        else Dispatcher.Invoke(Attach);
    }

    private void ShowTerminal(string jobId, bool focus)
    {
        if (!_terminalsByJob.TryGetValue(jobId, out var control))
            return;

        _activeJobId = jobId;
        TerminalHost.Child = control;
        WorkbenchTabs.SelectedItem = TerminalTab;
        if (focus)
            Dispatcher.BeginInvoke(() => control.Focus(), DispatcherPriority.Input);
    }

    private void TerminalFocus_OnClick(object sender, RoutedEventArgs e)
    {
        WorkbenchTabs.SelectedItem = TerminalTab;
        if (TerminalHost.Child is EasyTerminalControl term)
            term.Focus();
    }

    private void JobList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JobList.SelectedItem is JobRow row)
            ShowTerminal(row.Id, focus: true);
    }

    private void TerminalHost_OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TerminalHost.Child is EasyTerminalControl term)
            term.Focus();
    }

    private string SelectedProvider()
        => (ProviderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "opencode";

    private void SelectProvider(string providerId)
    {
        ProviderBox.SelectedItem = ProviderBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Content?.ToString(), providerId, StringComparison.OrdinalIgnoreCase))
            ?? ProviderBox.Items[0];
    }

    private void Navigate_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
            NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        AgentsPage.Visibility = Visibility.Collapsed;
        ProjectsPage.Visibility = Visibility.Collapsed;
        SkillsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;

        var isSessionPage = page is "workbench" or "sessions";
        FeaturePageHost.Visibility = isSessionPage ? Visibility.Collapsed : Visibility.Visible;

        switch (page)
        {
            case "agents":
                SetFeatureHeader("AGENTS", "Agent 中心", "管理 CLI 可用性与进入对应会话；不再与会话上下文混排。");
                if (string.IsNullOrWhiteSpace(AgentTaskCwdBox.Text))
                    AgentTaskCwdBox.Text = CwdBox.Text;
                AgentsPage.Visibility = Visibility.Visible;
                break;
            case "skills":
                SetFeatureHeader("SKILLS", "Skills 管理", "集中查看受管 Skill、目标工具与漂移状态。");
                LoadManagedSkills();
                SkillsPage.Visibility = Visibility.Visible;
                break;
            case "settings":
                SetFeatureHeader("SETTINGS", "设置", "会话默认值与本地数据行为。");
                SettingsProviderBox.SelectedItem = SettingsProviderBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Content?.ToString() == _preferences.DefaultProvider)
                    ?? SettingsProviderBox.Items[0];
                SettingsCwdBox.Text = _preferences.DefaultWorkingDirectory;
                SettingsAutoFocusBox.IsChecked = _preferences.AutoFocusNewTerminal;
                SettingsAgentTaskAgentBox.SelectedItem = SettingsAgentTaskAgentBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag?.ToString() == _preferences.OpenCodeTaskAgent)
                    ?? SettingsAgentTaskAgentBox.Items[0];
                SettingsAgentTaskModelBox.Text = _preferences.OpenCodeTaskModel;
                SettingsStatus.Text = "";
                SettingsPage.Visibility = Visibility.Visible;
                break;
            case "projects":
                SetFeatureHeader("PROJECTS", "项目", "HubStore 项目与真实工作目录。");
                RefreshWorkspaceData();
                ProjectsPage.Visibility = Visibility.Visible;
                break;
            case "vault":
                ShowPlaceholder("VAULT", "Vault", "FTS 搜索和 Harvest 已可用，当前入口仍在会话工作台的结构化标签中。", "Vault 独立页 · PENDING");
                break;
            case "memory":
                ShowPlaceholder("MEMORY", "Memory 与 Handoff", "真实 sink 与投影已可用，独立管理页将在统一项目身份后接入。", "Memory 独立页 · PENDING");
                break;
        }

        foreach (var button in new[]
                 {
                     WorkbenchNavButton, ProjectsNavButton, AgentsNavButton, SessionsNavButton,
                     VaultNavButton, MemoryNavButton, SkillsNavButton, SettingsNavButton
                 })
        {
            var selected = string.Equals(button.Tag?.ToString(), page, StringComparison.OrdinalIgnoreCase);
            button.Background = selected
                ? new SolidColorBrush(Color.FromRgb(0x17, 0x31, 0x5D))
                : Brushes.Transparent;
            button.Foreground = selected
                ? new SolidColorBrush(Color.FromRgb(0x8B, 0xB4, 0xFF))
                : (Brush)FindResource("TextSecondary");
        }
    }

    private void SetFeatureHeader(string eyebrow, string title, string subtitle)
    {
        FeaturePageEyebrow.Text = eyebrow;
        FeaturePageTitle.Text = title;
        FeaturePageSubtitle.Text = subtitle;
    }

    private void ShowPlaceholder(string eyebrow, string title, string body, string placeholderTitle)
    {
        SetFeatureHeader(eyebrow, title, body);
        PlaceholderTitle.Text = placeholderTitle;
        PlaceholderBody.Text = body;
        PlaceholderPage.Visibility = Visibility.Visible;
    }

    private void UseAgent_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerId }) return;
        SelectProvider(providerId);
        NavigateTo("sessions");
        StructuredStatus.Text = $"已选择 {providerId}，可启动新会话";
    }

    private async void RunAgentTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (_agentTaskCancellation is not null)
            return;

        var cwd = AgentTaskCwdBox.Text.Trim();
        var prompt = AgentTaskPromptBox.Text.Trim();
        if (!Directory.Exists(cwd))
        {
            AgentTaskStatus.Text = "工作目录不存在";
            return;
        }
        if (string.IsNullOrWhiteSpace(prompt))
        {
            AgentTaskStatus.Text = "请输入任务";
            return;
        }

        var databaseProblem = _opencode.GetTaskDatabaseProblem();
        if (databaseProblem is not null)
        {
            AgentTaskStatus.Foreground = (Brush)FindResource("Warning");
            AgentTaskStatus.Text = "OpenCode 未就绪";
            AgentTaskOutputBox.Text = databaseProblem;
            return;
        }

        var agent = (AgentTaskAgentBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var agentLabel = string.IsNullOrEmpty(agent) ? "直接模型" : agent;
        _agentTaskCancellation = new CancellationTokenSource();
        AgentTaskRunButton.IsEnabled = false;
        AgentTaskCancelButton.IsEnabled = true;
        AgentTaskStatus.Foreground = (Brush)FindResource("TextSecondary");
        AgentTaskStatus.Text = $"{agentLabel} 运行中…";
        AgentTaskOutputBox.Text = "等待任务输出…";
        _viewModel.AddActivity("Agent 任务已启动", $"{agentLabel} · {cwd}", "Agent");

        try
        {
            var spec = _opencode.BuildTask(cwd, prompt, agent, AgentTaskModelBox.Text.Trim());
            var result = await _headless.RunAsync(spec, _agentTaskCancellation.Token);
            var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
            AgentTaskOutputBox.Text = Tail(output, 30000);
            AgentTaskStatus.Foreground = result.ExitCode == 0
                ? (Brush)FindResource("Success")
                : (Brush)FindResource("Warning");
            AgentTaskStatus.Text = $"{agentLabel} 已结束 · exit {result.ExitCode}";
            _viewModel.AddActivity("Agent 任务已结束", $"{agentLabel} · exit {result.ExitCode}", "Agent");
        }
        catch (OperationCanceledException)
        {
            AgentTaskStatus.Text = $"{agentLabel} 已取消";
            AgentTaskOutputBox.Text = "任务已取消，进程树已终止。";
            _viewModel.AddActivity("Agent 任务已取消", agentLabel, "Agent");
        }
        catch (Exception ex)
        {
            AgentTaskStatus.Foreground = (Brush)FindResource("Warning");
            AgentTaskStatus.Text = $"{agentLabel} 启动失败";
            AgentTaskOutputBox.Text = ex.Message;
            _viewModel.AddActivity("Agent 任务失败", $"{agentLabel} · {ex.Message}", "Agent");
        }
        finally
        {
            _agentTaskCancellation?.Dispose();
            _agentTaskCancellation = null;
            AgentTaskRunButton.IsEnabled = true;
            AgentTaskCancelButton.IsEnabled = false;
        }
    }

    private void CancelAgentTask_OnClick(object sender, RoutedEventArgs e)
    {
        AgentTaskCancelButton.IsEnabled = false;
        AgentTaskStatus.Text = "正在取消…";
        _agentTaskCancellation?.Cancel();
    }

    private static string Tail(string value, int maxLength)
        => value.Length <= maxLength ? value : "…\n" + value[^maxLength..];

    private void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var cwd = SettingsCwdBox.Text.Trim();
        if (!Directory.Exists(cwd))
        {
            SettingsStatus.Foreground = (Brush)FindResource("Warning");
            SettingsStatus.Text = "工作目录不存在";
            return;
        }

        _preferences = new HubPreferences
        {
            DefaultProvider = (SettingsProviderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "opencode",
            DefaultWorkingDirectory = Path.GetFullPath(cwd),
            AutoFocusNewTerminal = SettingsAutoFocusBox.IsChecked == true,
            OpenCodeTaskAgent = (SettingsAgentTaskAgentBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
            OpenCodeTaskModel = SettingsAgentTaskModelBox.Text.Trim()
        };
        HubPreferencesStore.Save(_preferences);
        ApplyAgentTaskPreferences();
        SelectWorkspace(_preferences.DefaultWorkingDirectory);
        SelectProvider(_preferences.DefaultProvider);
        SettingsStatus.Foreground = (Brush)FindResource("Success");
        SettingsStatus.Text = "已保存";
    }

    private void ApplyAgentTaskPreferences()
    {
        AgentTaskAgentBox.SelectedItem = AgentTaskAgentBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == _preferences.OpenCodeTaskAgent)
            ?? AgentTaskAgentBox.Items[0];
        AgentTaskModelBox.Text = _preferences.OpenCodeTaskModel;
    }

    private void LoadManagedSkills()
    {
        _viewModel.Skills.Clear();
        foreach (var record in new SkillManifestStore().Load().Values.OrderBy(record => record.SkillId))
        {
            var enabledTargets = record.Tools
                .Where(pair => pair.Value.Enabled)
                .Select(pair => pair.Key)
                .OrderBy(target => target)
                .ToList();
            _viewModel.Skills.Add(new WorkbenchSkill(
                record.SkillId,
                record.SourcePath,
                enabledTargets.Count == 0 ? "未启用" : string.Join(" · ", enabledTargets),
                enabledTargets.Count == 0 ? "Disabled" : "Enabled"));
        }
    }

    private void UseProject_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string projectId }) return;
        var project = _store.ListProjects().FirstOrDefault(candidate => candidate.Id == projectId);
        if (project is null || !Directory.Exists(project.RootPath)) return;
        SelectWorkspace(project.RootPath);
        NavigateTo("sessions");
    }

    private void AddTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentProject is null) return;
        var title = TaskTitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show("请输入任务标题");
            return;
        }

        var notes = TaskNotesBox.Text.Trim();
        _store.UpsertTask(new TaskItem(
            Guid.NewGuid().ToString("n"),
            _currentProject.Id,
            title,
            "Todo",
            string.IsNullOrEmpty(notes) ? null : notes));
        TaskTitleBox.Clear();
        TaskNotesBox.Clear();
        RefreshWorkspaceData();
    }

    private void AdvanceTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentProject is null || sender is not Button { Tag: string taskId }) return;
        var task = _store.ListTasks(_currentProject.Id).FirstOrDefault(candidate => candidate.Id == taskId);
        if (task is null) return;

        _store.UpsertTask(task with { Status = TaskStatusFlow.Next(task.Status) });
        RefreshWorkspaceData();
    }

    private IArchiveSource? SelectedArchive()
        => ArchiveSourceBox.SelectedItem as IArchiveSource;

    private async void Start_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var cwd = CwdBox.Text.Trim();
            if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd))
            {
                MessageBox.Show("工作目录无效");
                return;
            }

            var job = _supervisor.Start(SelectedProvider(), cwd);
            _activeJobId = job.Id;
            RefreshJobs();
            StructuredStatus.Text = $"Job {job.Id[..8]}… running ({job.Provider})";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Start failed");
        }

        await Task.CompletedTask;
        RefreshArchiveEntries();
    }

    private void Resume_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row
            || row.Provider is not ("opencode" or "codex" or "claude" or "cursor-agent"))
        {
            MessageBox.Show("Resume 仅适用于 OpenCode / Codex / Claude / cursor-agent 会话条目");
            return;
        }

        if (row.Provider == "cursor-agent" && !_cursorAgent.Discover())
        {
            MessageBox.Show(_cursorAgent.InstallHint, "cursor-agent 未安装");
            return;
        }

        try
        {
            var cwd = string.IsNullOrWhiteSpace(CwdBox.Text)
                ? row.Cwd ?? Environment.CurrentDirectory
                : CwdBox.Text.Trim();
            var job = _supervisor.Resume(row.Provider, cwd, row.Id);
            _activeJobId = job.Id;
            _structuredEntryId = row.Id;
            _structuredSourceId = row.Provider;
            _structuredPath = row.Cwd;
            RefreshJobs();
            LoadStructured(force: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Resume failed");
        }
    }

    private void Kill_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeJobId is null)
        {
            MessageBox.Show("没有活动 Job");
            return;
        }

        _supervisor.Kill(_activeJobId);
        RefreshJobs();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshWorkspaceData();
        RefreshArchiveEntries();
    }

    private void BrowseCwd_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "在目标目录中任选一个文件",
            CheckFileExists = false,
            FileName = "选择此文件夹",
            ValidateNames = false
        };
        if (dlg.ShowDialog() == true && Path.GetDirectoryName(dlg.FileName) is { } directory)
            SelectWorkspace(directory);
    }

    private void ArchiveSourceBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshArchiveEntries();
    }

    private void SessionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row) return;
        _structuredEntryId = row.Id;
        _structuredSourceId = row.Provider;
        _structuredPath = row.Cwd;
        if (!string.IsNullOrEmpty(row.Cwd) && Directory.Exists(row.Cwd))
            SelectWorkspace(row.Cwd);
        LoadStructured(force: true);
    }

    private async void Distill_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row)
        {
            MessageBox.Show("先选一条 Archive 条目");
            return;
        }

        var src = _archives.Get(row.Provider);
        if (src is null) return;

        try
        {
            var projectId = CurrentProjectId();
            var msgs = src.GetMessages(row.Id);
            var harvest = _harvester.IngestFromArchive(projectId, src,
                new ArchiveEntry(row.Id, row.Provider, row.Title, row.Cwd, DateTimeOffset.UtcNow, "session"));

            var provider = SelectedProvider();
            DistillArtifact art;
            if (provider is "cursor-agent" or "opencode" or "codex" or "claude")
            {
                var cwd = string.IsNullOrWhiteSpace(CwdBox.Text)
                    ? Environment.CurrentDirectory
                    : CwdBox.Text.Trim();
                StructuredStatus.Text = $"Distill via {provider}…";
                art = await _distiller.DistillViaCliAsync(
                    provider, projectId, harvest.Meta.SessionId, cwd, msgs, _headless);
            }
            else
            {
                art = _distiller.ProposeSummary(projectId, harvest.Meta.SessionId, msgs);
            }

            StructuredStatus.Text = $"Distill 入队 Pending · {art.Id[..8]}…（请打开审阅队列）";
            var review = new ReviewWindow(_distiller, _harvester) { Owner = this };
            review.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Distill failed");
        }
    }

    private void ReviewQueue_OnClick(object sender, RoutedEventArgs e)
    {
        var w = new ReviewWindow(_distiller, _harvester) { Owner = this };
        w.Show();
    }

    private void Migrate_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row)
        {
            MessageBox.Show("先选一条源会话");
            return;
        }

        var w = new MigrationWizardWindow(
            _migration,
            _supervisor,
            [_opencode, _codex, _claude, _cursorAgent],
            CurrentProjectId(),
            row.Id,
            row.Provider,
            CwdBox.Text.Trim())
        {
            Owner = this
        };
        w.ShowDialog();
        RefreshJobs();
    }

    private void VaultSearch_OnClick(object sender, RoutedEventArgs e)
    {
        var q = VaultSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q))
        {
            MessageBox.Show("输入 FTS 关键词");
            return;
        }

        var hits = _vaultIndex.Search(q, 30);
        if (hits.Count == 0)
        {
            StructuredStatus.Text = "FTS: 无命中";
            return;
        }

        Messages.Clear();
        foreach (var h in hits)
        {
            Messages.Add(MessageRow.From(new CanonicalMessage(
                h.SessionId, h.SessionId, h.Role,
                $"[{h.ProjectId}/{h.SessionId}] {h.Snippet}", null)));
        }

        StructuredStatus.Text = $"FTS · {hits.Count} hits for «{q}»";
    }

    private void Harvest_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row)
        {
            MessageBox.Show("先选一条 Archive 条目");
            return;
        }

        var src = _archives.Get(row.Provider);
        if (src is null)
        {
            MessageBox.Show("未知归档源: " + row.Provider);
            return;
        }

        try
        {
            var entry = new ArchiveEntry(
                row.Id, row.Provider, row.Title, row.Cwd, DateTimeOffset.UtcNow, "session");
            var projectId = CurrentProjectId();
            var result = _harvester.IngestFromArchive(projectId, src, entry);
            StructuredStatus.Text =
                $"Harvest {result.Meta.Lifecycle} · msgs={result.Meta.MessageCount} · {result.SessionDir}";
            if (result.Meta.Lifecycle == SessionLifecycle.IngestError)
                MessageBox.Show(result.Meta.Error ?? "ingest-error", "Harvest failed");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Harvest failed");
        }
    }

    private void OpenEntryFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var path = _structuredPath;
        if (string.IsNullOrEmpty(path) && SessionList.SelectedItem is SessionRow row)
            path = row.Cwd;
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("当前条目没有路径");
            return;
        }

        var target = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
        {
            MessageBox.Show("目录不存在: " + path);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{target}\"",
            UseShellExecute = true
        });
    }

    private void RefreshArchiveEntries()
    {
        Sessions.Clear();
        var src = SelectedArchive();
        if (src is null)
        {
            StructuredStatus.Text = "无可用归档源";
            return;
        }

        try
        {
            foreach (var e in src.List(100))
            {
                Sessions.Add(new SessionRow(
                    e.Id,
                    e.SourceId,
                    e.Title,
                    e.Path));
            }

            StructuredStatus.Text = $"{src.DisplayName} · {Sessions.Count} entries";
        }
        catch (Exception ex)
        {
            StructuredStatus.Text = "Archive: " + ex.Message;
        }

        RefreshJobs();
    }

    private void RefreshJobs()
    {
        var selectedId = _activeJobId;
        var jobs = _supervisor.ListJobs().Take(30).ToList();
        Jobs.Clear();
        foreach (var j in jobs)
            Jobs.Add(new JobRow(j.Id, $"{j.State} · {j.Provider} · {j.Id[..8]}"));

        JobList.SelectedItem = Jobs.FirstOrDefault(j => j.Id == selectedId);
        var active = selectedId is null ? null : jobs.FirstOrDefault(job => job.Id == selectedId);
        if (active is null)
        {
            ActiveJobIndicator.Fill = (Brush)FindResource("TextSecondary");
            ActiveJobStatusText.Text = " 无活动 Job";
            return;
        }

        ActiveJobIndicator.Fill = active.State == JobState.Running
            ? (Brush)FindResource("Success")
            : (Brush)FindResource("Warning");
        ActiveJobStatusText.Text = active.Pid is null
            ? $" {active.State}"
            : $" {active.State} · PID {active.Pid}";
    }

    private void RefreshStructuredIfNeeded()
    {
        if (_structuredEntryId is null || _structuredSourceId is null) return;
        LoadStructured(force: false);
    }

    private void LoadStructured(bool force)
    {
        if (_structuredEntryId is null || _structuredSourceId is null) return;
        var src = _archives.Get(_structuredSourceId);
        if (src is null) return;

        try
        {
            DateTime mtime = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(_structuredPath) && File.Exists(_structuredPath))
                mtime = File.GetLastWriteTimeUtc(_structuredPath);
            else if (!string.IsNullOrEmpty(_structuredPath) && Directory.Exists(_structuredPath))
                mtime = Directory.GetLastWriteTimeUtc(_structuredPath);

            if (!force && mtime == _structuredMtime) return;
            _structuredMtime = mtime;

            var msgs = src.GetMessages(_structuredEntryId);
            BindMessages(msgs);
            StructuredStatus.Text =
                $"{src.DisplayName} · {_structuredEntryId} · {msgs.Count} · {mtime:HH:mm:ss}Z";
        }
        catch (Exception ex)
        {
            StructuredStatus.Text = "Structured: " + ex.Message;
        }
    }

    private void BindMessages(IReadOnlyList<CanonicalMessage> msgs)
    {
        Messages.Clear();
        foreach (var m in msgs.TakeLast(200))
            Messages.Add(MessageRow.From(m));
    }

    private void InjectSave_OnClick(object sender, RoutedEventArgs e)
    {
        var pid = CurrentProjectId();
        _injectSink.Write(pid, InjectKind.Memory, InjectMemoryBox.Text);
        _injectSink.Write(pid, InjectKind.Handoff, InjectHandoffBox.Text);
        InjectStatus.Text = $"已写入 sink: {_injectSink.ProjectDir(pid)}";
    }

    private void InjectProjectOpenCode_OnClick(object sender, RoutedEventArgs e)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ProjectTo(Path.Combine(home, ".config", "opencode", "AGENTS.md"));
    }

    private void InjectProjectCodex_OnClick(object sender, RoutedEventArgs e)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ProjectTo(Path.Combine(home, ".codex", "AGENTS.md"));
    }

    private void ProjectTo(string target)
    {
        var pid = CurrentProjectId();
        try
        {
            _injectSink.Write(pid, InjectKind.Memory, InjectMemoryBox.Text);
            _injectSink.Write(pid, InjectKind.Handoff, InjectHandoffBox.Text);
            _injectProjector.Project(pid, [target]);
            _lastInjectTarget = target;
            InjectStatus.Text = $"已投影管理块 → {target}"
                                + (_injectProjector.IsDrifted(target) ? "（注意：哈希漂移）" : "");
        }
        catch (Exception ex)
        {
            InjectStatus.Text = "投影失败: " + ex.Message;
        }
    }

    private void InjectToggleOff_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastInjectTarget))
        {
            InjectStatus.Text = "尚无最近投影目标";
            return;
        }

        _injectProjector.ToggleOff(_lastInjectTarget);
        InjectStatus.Text = "已拆除管理块: " + _lastInjectTarget;
    }

    public sealed record SessionRow(string Id, string Provider, string Title, string? Cwd);
    public sealed record JobRow(string Id, string Display);

    public sealed class MessageRow
    {
        public required string RoleLabel { get; init; }
        public required string Content { get; init; }
        public required Brush BubbleBrush { get; init; }

        public static MessageRow From(CanonicalMessage m)
        {
            var brush = m.Role switch
            {
                "user" => new SolidColorBrush(Color.FromRgb(0x24, 0x3a, 0x2e)),
                "reasoning" => new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x18)),
                "memory" => new SolidColorBrush(Color.FromRgb(0x1e, 0x2a, 0x3a)),
                "skill" => new SolidColorBrush(Color.FromRgb(0x2a, 0x1e, 0x32)),
                "meta" => new SolidColorBrush(Color.FromRgb(0x3a, 0x28, 0x1e)),
                _ => new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2a))
            };
            return new MessageRow
            {
                RoleLabel = m.Role,
                Content = m.Content,
                BubbleBrush = brush
            };
        }
    }
}
