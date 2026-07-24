using System.Windows;
using System.Windows.Controls;
using VibeHub.Core.Adapters;
using VibeHub.Core.Migrate;
using VibeHub.Core.Supervisor;

namespace VibeHub.App;

public partial class MigrationWizardWindow : Window
{
    private readonly MigrationService _migration;
    private readonly JobSupervisor _supervisor;
    private readonly IReadOnlyDictionary<string, IProviderAdapter> _adapters;
    private MigrationPlan? _plan;
    private string? _projectedTarget;

    public MigrationWizardWindow(
        MigrationService migration,
        JobSupervisor supervisor,
        IEnumerable<IProviderAdapter> adapters,
        string projectId,
        string sessionId,
        string sourceProvider,
        string cwd)
    {
        InitializeComponent();
        _migration = migration;
        _supervisor = supervisor;
        _adapters = adapters.ToDictionary(a => a.ProviderId, StringComparer.OrdinalIgnoreCase);
        ProjectBox.Text = projectId;
        SessionBox.Text = sessionId;
        SourceBox.Text = sourceProvider;
        CwdBox.Text = cwd;
    }

    private string TargetProvider()
        => (TargetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "opencode";

    private void Prepare_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _plan = _migration.Prepare(
                ProjectBox.Text.Trim(),
                SessionBox.Text.Trim(),
                SourceBox.Text.Trim(),
                TargetProvider());
            HandoffBox.Text = _plan.Handoff;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Prepare failed");
        }
    }

    private void Project_OnClick(object sender, RoutedEventArgs e)
    {
        if (_plan is null)
        {
            MessageBox.Show("先点「生成 plan」");
            return;
        }

        try
        {
            _plan = new MigrationPlan
            {
                ProjectId = _plan.ProjectId,
                SessionId = _plan.SessionId,
                SourceProvider = _plan.SourceProvider,
                TargetProvider = TargetProvider(),
                Summary = _plan.Summary,
                Handoff = HandoffBox.Text
            };
            _projectedTarget = _migration.ProjectForTarget(_plan, CwdBox.Text.Trim());
            MessageBox.Show("已投影 → " + _projectedTarget, "迁移");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "投影失败");
        }
    }

    private void StartJob_OnClick(object sender, RoutedEventArgs e)
    {
        var target = TargetProvider();
        if (!_adapters.TryGetValue(target, out var adapter))
        {
            MessageBox.Show("未知目标: " + target);
            return;
        }

        if (!adapter.Discover())
        {
            MessageBox.Show("目标 CLI 未发现: " + target);
            return;
        }

        try
        {
            if (_plan is not null && _projectedTarget is null)
                Project_OnClick(sender, e);

            var cwd = CwdBox.Text.Trim();
            var job = _supervisor.Start(ProjectBox.Text.Trim(), target, cwd);
            MessageBox.Show($"已开 Job {job.Id[..8]}… ({target})", "迁移");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Start failed");
        }
    }
}
