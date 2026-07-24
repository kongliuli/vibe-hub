using System.Windows;
using VibeHub.App.Services;
using VibeHub.App.ViewModels;
using VibeHub.Core.Adapters;
using VibeHub.Core.Archive;
using VibeHub.Core.Distill;
using VibeHub.Core.Inject;
using VibeHub.Core.Migrate;
using VibeHub.Core.Skills;
using VibeHub.Core.Storage;
using VibeHub.Core.Supervisor;
using VibeHub.Core.Vault;
using VibeHub.Core.Workspace;
using VibeHub.Terminal;

namespace VibeHub.App;

public partial class App : Application
{
    private AppComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _composition = new AppComposition();
        var viewModel = new MainWindowViewModel(
            new WorkspaceViewModel(),
            new JobsViewModel(),
            new SessionsViewModel(),
            new ContextViewModel());
        MainWindow = new MainWindow(viewModel, _composition);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _composition?.Dispose();
        base.OnExit(e);
    }
}

// ponytail: concrete composition only; services move directly into child ViewModels in later phases.
internal sealed class AppComposition : IDisposable
{
    public HubPreferences Preferences { get; } = HubPreferencesStore.Load();
    public HubStore Store { get; } = HubStore.OpenDefault();
    public OpenCodeAdapter OpenCode { get; } = new();
    public CodexAdapter Codex { get; } = new();
    public ClaudeAdapter Claude { get; } = new();
    public CursorAgentAdapter CursorAgent { get; } = new();
    public EwtcProcessLauncher Launcher { get; } = new();
    public ArchiveCatalog Archives { get; } = new();
    public InjectSink InjectSink { get; } = new();
    public VaultPaths VaultPaths { get; } = new();
    public Distiller Distiller { get; } = new();
    public ProcessHeadlessRunner Headless { get; } = new();
    public GitChangesService GitChanges { get; } = new();
    public SkillInstaller SkillInstaller { get; } = new();

    public JobSupervisor Supervisor { get; }
    public InjectProjector InjectProjector { get; }
    public VaultIndex VaultIndex { get; }
    public Harvester Harvester { get; }
    public MigrationService Migration { get; }

    public AppComposition()
    {
        VaultIndex = new VaultIndex(VaultPaths);
        Harvester = new Harvester(VaultPaths, VaultIndex);
        Migration = new MigrationService(VaultPaths, InjectSink);
        Supervisor = new JobSupervisor(
            Launcher,
            [OpenCode, Codex, Claude, CursorAgent],
            Store);
        InjectProjector = new InjectProjector(InjectSink);
    }

    public void Dispose()
    {
        VaultIndex.Dispose();
        Store.Dispose();
    }
}
