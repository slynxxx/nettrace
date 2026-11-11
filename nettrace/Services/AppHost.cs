using Microsoft.UI.Dispatching;
using NetTrace.ViewModels;

namespace NetTrace.Services;

/// <summary>
/// composition root. services and viewmodels are created once and shared for the
/// lifetime of the app, so pages can be recreated without restarting monitoring.
/// </summary>
public sealed class AppHost : IDisposable
{
    public AppHost(DispatcherQueue dispatcher)
    {
        Dispatcher = dispatcher;

        Settings = new SettingsService();
        Processes = new ProcessInfoService();
        Monitor = new ConnectionMonitorService(Processes, Settings);
        Traffic = new TrafficMonitorService(Settings);
        NetworkStatus = new NetworkStatusService();
        Dns = new DnsService();
        Navigation = new NavigationService();
        Dialogs = new DialogService();

        try
        {
            History = new HistoryService(Settings);
            Monitor.SnapshotUpdated += (_, snapshot) => History.Record(snapshot);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            HistoryError = "the history database could not be opened: " + ex.Message.ToLowerInvariant();
        }

        InterfaceSelector = new InterfaceSelectorViewModel(Traffic, Settings, dispatcher);
        Overview = new OverviewViewModel(Monitor, Traffic, NetworkStatus, InterfaceSelector, Navigation, dispatcher);
        Connections = new ConnectionsViewModel(Monitor, Navigation, dispatcher);
        ProcessExplorer = new ProcessesViewModel(Monitor, Navigation, dispatcher);
        DnsLookup = new DnsViewModel(Dns, dispatcher);
        HistoryPage = new HistoryViewModel(History, HistoryError, Settings, Dialogs, Navigation, dispatcher);
        SettingsPage = new SettingsViewModel(Settings, InterfaceSelector, History, dispatcher);
    }

    public DispatcherQueue Dispatcher { get; }

    public SettingsService Settings { get; }
    public ProcessInfoService Processes { get; }
    public ConnectionMonitorService Monitor { get; }
    public TrafficMonitorService Traffic { get; }
    public NetworkStatusService NetworkStatus { get; }
    public DnsService Dns { get; }
    public HistoryService? History { get; }
    public string? HistoryError { get; }
    public NavigationService Navigation { get; }
    public DialogService Dialogs { get; }

    public InterfaceSelectorViewModel InterfaceSelector { get; }
    public OverviewViewModel Overview { get; }
    public ConnectionsViewModel Connections { get; }
    public ProcessesViewModel ProcessExplorer { get; }
    public DnsViewModel DnsLookup { get; }
    public HistoryViewModel HistoryPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public void Start()
    {
        Monitor.Start();
        Traffic.Start();
        if (History is not null)
        {
            _ = History.PruneAsync(CancellationToken.None);
        }
    }

    public void Dispose()
    {
        Monitor.Dispose();
        Traffic.Dispose();
        NetworkStatus.Dispose();
        History?.Dispose();
        Settings.Dispose();
    }
}
