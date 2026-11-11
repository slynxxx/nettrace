using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

public sealed partial class OverviewViewModel : PageViewModel
{
    private const int RecentCount = 8;

    private readonly ConnectionMonitorService _monitor;
    private readonly TrafficMonitorService _traffic;
    private readonly NetworkStatusService _status;
    private readonly NavigationService _navigation;
    private readonly DispatcherQueue _dispatcher;
    private readonly CoalescingDispatcher _connectionsRefresh;
    private readonly CoalescingDispatcher _trafficRefresh;
    private readonly Dictionary<ConnectionEntry, ConnectionRowViewModel> _recentRows = new(ReferenceEqualityComparer.Instance);

    public OverviewViewModel(
        ConnectionMonitorService monitor,
        TrafficMonitorService traffic,
        NetworkStatusService status,
        InterfaceSelectorViewModel interfaceSelector,
        NavigationService navigation,
        DispatcherQueue dispatcher)
    {
        _monitor = monitor;
        _traffic = traffic;
        _status = status;
        _navigation = navigation;
        _dispatcher = dispatcher;
        InterfaceSelector = interfaceSelector;
        _connectionsRefresh = new CoalescingDispatcher(dispatcher, ApplyConnections);
        _trafficRefresh = new CoalescingDispatcher(dispatcher, ApplyTraffic);

        StatusText = "checking";
        ProfileText = string.Empty;
        TcpText = UdpText = ProcessText = InterfaceText = "—";
        InterfaceCaption = string.Empty;
        DownloadText = UploadText = "—";
        SessionReceivedText = SessionSentText = "—";
        InterfaceTotalsText = string.Empty;
        PeakText = string.Empty;
        History = [];
    }

    public InterfaceSelectorViewModel InterfaceSelector { get; }

    public ObservableCollection<ConnectionRowViewModel> RecentConnections { get; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsOnline { get; set; }

    [ObservableProperty]
    public partial string ProfileText { get; set; }

    [ObservableProperty]
    public partial string TcpText { get; set; }

    [ObservableProperty]
    public partial string UdpText { get; set; }

    [ObservableProperty]
    public partial string ProcessText { get; set; }

    [ObservableProperty]
    public partial string InterfaceText { get; set; }

    [ObservableProperty]
    public partial string InterfaceCaption { get; set; }

    [ObservableProperty]
    public partial string DownloadText { get; set; }

    [ObservableProperty]
    public partial string UploadText { get; set; }

    [ObservableProperty]
    public partial string SessionReceivedText { get; set; }

    [ObservableProperty]
    public partial string SessionSentText { get; set; }

    [ObservableProperty]
    public partial string InterfaceTotalsText { get; set; }

    [ObservableProperty]
    public partial string PeakText { get; set; }

    [ObservableProperty]
    public partial TrafficSample[] History { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrafficError))]
    public partial string? TrafficError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionError))]
    public partial string? ConnectionError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentEmpty))]
    public partial bool HasRecent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecentEmpty))]
    public partial bool IsLoading { get; set; } = true;

    public bool HasTrafficError => TrafficError is not null;
    public bool HasConnectionError => ConnectionError is not null;
    public bool ShowRecentEmpty => !IsLoading && !HasRecent;

    protected override void OnActivated()
    {
        _monitor.SnapshotUpdated += OnSnapshot;
        _traffic.Updated += OnTraffic;
        _traffic.InterfacesChanged += OnInterfacesChanged;
        _status.Changed += OnStatusChanged;
        ApplyStatus();
        ApplyConnections();
        ApplyTraffic();
    }

    protected override void OnDeactivated()
    {
        _monitor.SnapshotUpdated -= OnSnapshot;
        _traffic.Updated -= OnTraffic;
        _traffic.InterfacesChanged -= OnInterfacesChanged;
        _status.Changed -= OnStatusChanged;
    }

    [RelayCommand]
    private void OpenConnections() => _navigation.Navigate(AppPage.Connections);

    [RelayCommand]
    private void OpenProcesses() => _navigation.Navigate(AppPage.Processes);

    [RelayCommand]
    private void ShowProcess(ConnectionRowViewModel? row)
    {
        if (row is not null)
        {
            _navigation.Navigate(AppPage.Processes, row.Pid);
        }
    }

    private void OnSnapshot(object? sender, ConnectionSnapshot e) => _connectionsRefresh.Request();

    private void OnTraffic(object? sender, TrafficState e) => _trafficRefresh.Request();

    private void OnInterfacesChanged(object? sender, EventArgs e) => _trafficRefresh.Request();

    private void OnStatusChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(ApplyStatus);

    private void ApplyStatus()
    {
        StatusText = _status.StatusText;
        IsOnline = _status.Status == NetworkStatus.Online;
        ProfileText = _status.ProfileName.ToLowerInvariant();
    }

    private void ApplyConnections()
    {
        if (!IsActive)
        {
            return;
        }

        ConnectionSnapshot snapshot = _monitor.Latest;
        if (snapshot.IsEmpty)
        {
            return;
        }

        IsLoading = false;
        ConnectionError = snapshot.Error;
        TcpText = snapshot.TcpCount.ToString("N0", CultureInfo.InvariantCulture);
        UdpText = snapshot.UdpCount.ToString("N0", CultureInfo.InvariantCulture);
        ProcessText = snapshot.ProcessCount.ToString("N0", CultureInfo.InvariantCulture);

        IReadOnlyList<ConnectionEntry> recent = _monitor.GetRecentlyDetected(RecentCount);
        var rows = new List<ConnectionRowViewModel>(recent.Count);
        foreach (ConnectionEntry entry in recent)
        {
            if (!_recentRows.TryGetValue(entry, out ConnectionRowViewModel? row))
            {
                row = new ConnectionRowViewModel(entry);
                _recentRows[entry] = row;
            }
            else
            {
                row.Update();
            }

            rows.Add(row);
        }

        foreach (ConnectionEntry stale in _recentRows.Keys.Where(k => !recent.Contains(k)).ToList())
        {
            _recentRows.Remove(stale);
        }

        RecentConnections.SyncTo(rows);
        HasRecent = rows.Count > 0;
    }

    private void ApplyTraffic()
    {
        if (!IsActive)
        {
            return;
        }

        InterfaceText = _traffic.TotalInterfaceCount.ToString(CultureInfo.InvariantCulture);
        InterfaceCaption = $"{_traffic.UpInterfaceCount} up";

        TrafficState? state = _traffic.Latest;
        if (state is null)
        {
            return;
        }

        TrafficError = state.Error;
        DownloadText = ByteFormatter.FormatRate(state.ReceivedPerSecond);
        UploadText = ByteFormatter.FormatRate(state.SentPerSecond);
        SessionReceivedText = ByteFormatter.FormatBytes(state.SessionReceived);
        SessionSentText = ByteFormatter.FormatBytes(state.SessionSent);
        InterfaceTotalsText = $"interface counters  ·  {ByteFormatter.FormatBytes(state.InterfaceReceived)} received  ·  {ByteFormatter.FormatBytes(state.InterfaceSent)} sent";
        PeakText = $"peak  ·  {ByteFormatter.FormatRate(state.PeakReceivedPerSecond)} down  ·  {ByteFormatter.FormatRate(state.PeakSentPerSecond)} up";
        History = state.History;
    }
}
