using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NetTrace.Services;
using NetTrace.Views;

namespace NetTrace;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static AppHost Host { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Host = new AppHost(DispatcherQueue.GetForCurrentThread());
        _window = new MainWindow(Host);
        _window.Closed += (_, _) => Host.Dispose();
        Host.Start();
        _window.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // keep monitoring alive for recoverable ui errors and leave a trace for diagnosis
        System.Diagnostics.Debug.WriteLine($"unhandled ui exception: {e.Exception}");
        try
        {
            File.AppendAllText(
                Path.Combine(Helpers.AppPaths.DataDirectory, "errors.log"),
                $"{DateTimeOffset.Now:o} {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        e.Handled = true;
    }
}
