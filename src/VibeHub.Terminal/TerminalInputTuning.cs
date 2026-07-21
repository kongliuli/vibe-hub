using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyWindowsTerminalControl;

namespace VibeHub.Terminal;

/// <summary>
/// WPF steals navigation / accelerator keys from HwndHost ConPTY.
/// EWTC InputCapture only sets KeyboardNavigation=Contained — not enough for TUI pages
/// (slash menu, pickers, etc.). Inject VT / C0 into ConPTY on PreviewKeyDown.
/// </summary>
public static class TerminalInputTuning
{
    public static void ApplyToControl(EasyTerminalControl term)
    {
        term.Win32InputMode = true;
        ForceNoWpfNavigation(term);
        if (term.Terminal is not null)
            ForceNoWpfNavigation(term.Terminal);

        term.PreviewKeyDown -= OnTermPreviewKeyDown;
        term.PreviewKeyDown += OnTermPreviewKeyDown;

        term.PreviewMouseDown -= OnTermMouseDown;
        term.PreviewMouseDown += OnTermMouseDown;
    }

    public static void SuppressHostShortcuts(Window window, Func<EasyTerminalControl?>? currentTerm = null)
    {
        // Only disable CanExecute so WPF won't run Print UI; actual Ctrl+P is forwarded below.
        void BlockExecute(RoutedUICommand cmd) =>
            window.CommandBindings.Add(new CommandBinding(
                cmd,
                (_, e) => e.Handled = true,
                (_, e) =>
                {
                    e.CanExecute = false;
                    e.Handled = true;
                }));

        BlockExecute(ApplicationCommands.Print);
        BlockExecute(ApplicationCommands.PrintPreview);
        BlockExecute(ApplicationCommands.Find);
        BlockExecute(ApplicationCommands.Replace);

        if (currentTerm is null) return;

        window.PreviewKeyDown += (_, e) =>
        {
            if (IsEditingWpfField()) return;
            var term = currentTerm();
            if (!ShouldForward(term)) return;
            TryInjectSpecialKeys(term!, e);
        };
    }

    private static void OnTermMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is EasyTerminalControl term)
            term.Focus();
    }

    private static void OnTermPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is EasyTerminalControl term)
            TryInjectSpecialKeys(term, e);
    }

    private static bool IsEditingWpfField()
        => Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;

    private static bool ShouldForward(EasyTerminalControl? term)
    {
        if (term is null || !term.IsLoaded || term.Visibility != Visibility.Visible)
            return false;
        // HwndHost focus is flaky for IsKeyboardFocusWithin after TUI page switches
        return term.IsKeyboardFocusWithin || term.IsMouseOver || term.IsFocused
               || (term.Terminal?.IsKeyboardFocusWithin ?? false);
    }

    private static void TryInjectSpecialKeys(EasyTerminalControl term, KeyEventArgs e)
    {
        var pty = term.ConPTYTerm;
        if (pty is null) return;

        var mods = Keyboard.Modifiers;
        var ctrl = (mods & ModifierKeys.Control) == ModifierKeys.Control;
        var alt = (mods & ModifierKeys.Alt) == ModifierKeys.Alt;
        var shift = (mods & ModifierKeys.Shift) == ModifierKeys.Shift;

        try
        {
            // Ctrl+Tab would otherwise flip WPF TabControl away from Terminal
            if (e.Key == Key.Tab && ctrl)
            {
                e.Handled = true;
                return;
            }

            // Ctrl+Letter → C0 (OpenCode: Ctrl+P palette, Ctrl+C interrupt, …)
            if (ctrl && !alt && e.Key is >= Key.A and <= Key.Z)
            {
                e.Handled = true;
                var ch = (char)(e.Key - Key.A + 1);
                pty.WriteToTerm(ch.ToString());
                return;
            }

            string? seq = e.Key switch
            {
                Key.Tab when shift => "\x1b[Z",
                Key.Tab => "\t",
                Key.Return => "\r",
                Key.Escape => "\x1b",
                Key.Back => "\x7f",
                Key.Delete => "\x1b[3~",
                Key.Up => "\x1b[A",
                Key.Down => "\x1b[B",
                Key.Right => "\x1b[C",
                Key.Left => "\x1b[D",
                Key.Home => "\x1b[H",
                Key.End => "\x1b[F",
                Key.PageUp => "\x1b[5~",
                Key.PageDown => "\x1b[6~",
                Key.Insert => "\x1b[2~",
                // `/` on many layouts (US Oem2); inject only when Ctrl/Alt not held
                Key.Oem2 when !ctrl && !alt && !shift => "/",
                Key.Divide when !ctrl && !alt => "/",
                _ => null
            };

            if (seq is null) return;

            e.Handled = true;
            pty.WriteToTerm(seq);
        }
        catch (InvalidOperationException)
        {
            // ConPTY not ready
        }
    }

    private static void ForceNoWpfNavigation(DependencyObject el)
    {
        KeyboardNavigation.SetTabNavigation(el, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(el, KeyboardNavigationMode.None);
        KeyboardNavigation.SetControlTabNavigation(el, KeyboardNavigationMode.None);
    }
}
