using System.Threading;
using System.Windows;

namespace CodexQuotaHub.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Keep the legacy identifier to prevent duplicate monitors across the product rename.
        _singleInstance = new Mutex(true, @"Local\CodexMeter-" + Environment.UserName, out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show(UiPresentation.Text("AlreadyRunning"), "Codex Quota Hub");
            Shutdown();
            return;
        }

        try
        {
            string? databasePath = null;
            var codexExecutable = "codex";
            for (var i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--db" && i + 1 < e.Args.Length)
                    databasePath = e.Args[++i];
                else if (e.Args[i] == "--codex" && i + 1 < e.Args.Length)
                    codexExecutable = e.Args[++i];
                else
                    throw new ArgumentException(UiPresentation.Text("UnsupportedArgument"));
            }
            MainWindow = new MainWindow(databasePath, codexExecutable);
            MainWindow.Show();
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(UiPresentation.Text("StartupError", error.Message), "Codex Quota Hub");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
