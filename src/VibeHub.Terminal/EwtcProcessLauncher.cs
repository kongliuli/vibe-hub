using System.Windows;
using System.Windows.Controls;
using EasyWindowsTerminalControl;
using EasyWindowsTerminalControl.Internals;
using VibeHub.Core.Models;
using VibeHub.Core.Supervisor;

namespace VibeHub.Terminal;

/// <summary>
/// Launches CLI via EasyWindowsTerminalControl. The WPF control owns ConPTY;
/// UI hosts the control from <see cref="ControlCreated"/>.
/// </summary>
public sealed class EwtcProcessLauncher : IProcessLauncher
{
    public event Action<IPseudoTerminal, EasyTerminalControl, ProcessStartSpec>? ControlCreated;

    public IPseudoTerminal Launch(ProcessStartSpec spec)
    {
        if (Application.Current?.Dispatcher is null)
            throw new InvalidOperationException("EwtcProcessLauncher requires a WPF Dispatcher");

        EwtcPseudoTerminal? terminal = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var control = new EasyTerminalControl
            {
                WorkingDirectory = spec.WorkingDirectory,
                StartupCommandLine = BuildCommandLine(spec)
            };
            TerminalInputTuning.ApplyToControl(control);
            terminal = new EwtcPseudoTerminal(control);
            ControlCreated?.Invoke(terminal, control, spec);
        });

        return terminal!;
    }

    internal static string BuildCommandLine(ProcessStartSpec spec)
    {
        var args = string.Join(" ", spec.Arguments.Select(Quote));
        var exe = Quote(spec.FileName);
        var body = $"{exe} {args}".TrimEnd();

        // cwd via EasyTerminalControl.WorkingDirectory; cmd only when we need env sets
        if (spec.Environment is not { Count: > 0 })
            return body;

        var sets = string.Join(" && ", spec.Environment.Select(kv =>
            $"set \"{SanitizeEnvKey(kv.Key)}={SanitizeEnvValue(kv.Value)}\""));
        return $"cmd.exe /c \"{sets} && {body}\"";
    }

    private static string SanitizeEnvKey(string k)
        => k.Replace("\"", "").Replace("=", "");

    private static string SanitizeEnvValue(string v)
        => v.Replace("\"", "");

    private static string Quote(string s)
        => s.Contains(' ') || s.Contains('"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;

    private sealed class EwtcPseudoTerminal : IPseudoTerminal
    {
        private readonly EasyTerminalControl _control;
        private readonly CancellationTokenSource _watchCancellation = new();
        private readonly object _gate = new();
        private ProcessFactory.WrappedProcess? _process;
        private bool _killRequested;
        private bool _exitRaised;
        private bool _disposed;

        public EwtcPseudoTerminal(EasyTerminalControl control)
        {
            _control = control;
            _ = WatchProcessAsync(_watchCancellation.Token);
        }

        public int? ProcessId
        {
            get { lock (_gate) return _process?.Pid; }
        }

        public bool HasExited
        {
            get
            {
                lock (_gate)
                {
                    if (_exitRaised)
                        return true;
                    try { return _process?.HasExited == true; }
                    catch { return false; }
                }
            }
        }

        public int? ExitCode { get; private set; }
        public event Action? Started;
        public event Action? Exited;

        public void Kill()
        {
            ProcessFactory.WrappedProcess? process;
            lock (_gate)
            {
                if (_exitRaised)
                    return;
                _killRequested = true;
                process = _process;
            }

            if (process is not null)
                KillProcessTree(process);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _watchCancellation.Cancel();
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_control.Parent is Panel panel)
                        panel.Children.Remove(_control);
                });
            }
            catch
            {
                // dispatcher may be shutting down
            }
        }

        private async Task WatchProcessAsync(CancellationToken cancellationToken)
        {
            try
            {
                ProcessFactory.WrappedProcess? process = null;
                while (process is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    process = await _control.Dispatcher.InvokeAsync(() =>
                        _control.ConPTYTerm?.Process as ProcessFactory.WrappedProcess);
                    if (process is null)
                        await Task.Delay(40, cancellationToken);
                }

                bool killRequested;
                lock (_gate)
                {
                    _process = process;
                    killRequested = _killRequested;
                }
                Started?.Invoke();

                if (killRequested)
                    KillProcessTree(process);

                var nativeProcess = process.Process;
                await nativeProcess.WaitForExitAsync(cancellationToken);
                CompleteExit(nativeProcess.ExitCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal disposal while the app is shutting down.
            }
            catch
            {
                // EWTC can tear down the process handle before its background task finishes.
                if (HasExited)
                    CompleteExit(exitCode: null);
            }
        }

        private static void KillProcessTree(ProcessFactory.WrappedProcess process)
        {
            try { process.Kill(EntireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }

        private void CompleteExit(int? exitCode)
        {
            lock (_gate)
            {
                if (_exitRaised)
                    return;
                _exitRaised = true;
                ExitCode = exitCode;
            }

            Exited?.Invoke();
        }
    }
}
