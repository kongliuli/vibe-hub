using System.Windows;
using System.Windows.Controls;
using VibeHub.Terminal;

namespace TerminalSpike.EWTC;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TerminalInputTuning.ApplyToControl(Term);
        TerminalInputTuning.SuppressHostShortcuts(this, () => Term);
        ApplyCli(CliCombo.SelectedItem as ComboBoxItem);
        Loaded += (_, _) => Term.Focus();
    }

    private void CliCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Term is null) return;
        ApplyCli(CliCombo.SelectedItem as ComboBoxItem);
    }

    private void Restart_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyCli(CliCombo.SelectedItem as ComboBoxItem);
        _ = Term.RestartTerm();
        Dispatcher.BeginInvoke(() =>
        {
            TerminalInputTuning.ApplyToControl(Term);
            Term.Focus();
        });
    }

    private void ApplyCli(ComboBoxItem? item)
    {
        var cli = item?.Content?.ToString() ?? "pwsh.exe";
        Term.StartupCommandLine = cli switch
        {
            "opencode" =>
                "cmd.exe /c \"set \"OPENCODE_DISABLE_AUTOUPDATE=true\" && set \"OPENCODE_DISABLE_MODELS_FETCH=1\" && opencode\"",
            _ => cli
        };
    }
}
