using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

public static class ConnectionColumns
{
    public const string Protocol = "protocol";
    public const string Local = "local";
    public const string Remote = "remote";
    public const string State = "state";
    public const string Process = "process";
    public const string Pid = "pid";
    public const string FirstSeen = "first";
}

public sealed partial class ConnectionsViewModel : PageViewModel
{
    private readonly ConnectionMonitorService _monitor;
    private readonly NavigationService _navigation;
    private readonly CoalescingDispatcher _refresh;
    private readonly Dictionary<ConnectionEntry, ConnectionRowViewModel> _rows = new(ReferenceEqualityComparer.Instance);
    private readonly List<ConnectionRowViewModel> _view = new(512);
    private readonly HashSet<ConnectionEntry> _alive = new(ReferenceEqualityComparer.Instance);
    private readonly List<ConnectionEntry> _stale = new();
    private ConnectionSnapshot _snapshot = ConnectionSnapshot.Empty;

    public ConnectionsViewModel(ConnectionMonitorService monitor, NavigationService navigation, DispatcherQueue dispatcher)
    {
        _monitor = monitor;
        _navigation = navigation;
        _refresh = new CoalescingDispatcher(dispatcher, ApplySnapshot);
        SearchText = string.Empty;
        SortColumn = ConnectionColumns.FirstSeen;
        SortDescending = true;
        SummaryText = string.Empty;
    }

    public ObservableCollection<ConnectionRowViewModel> Rows { get; } = new();

    public string[] ProtocolFilters { get; } = ["all protocols", "tcp", "udp"];

    public string[] StateFilters { get; } = ["all states", "established", "listening", "time wait", "close wait", "other"];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial int ProtocolFilterIndex { get; set; }

    [ObservableProperty]
    public partial int StateFilterIndex { get; set; }

    [ObservableProperty]
    public partial bool HideLoopback { get; set; }

    [ObservableProperty]
    public partial string SortColumn { get; set; }

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    public partial ConnectionRowViewModel? SelectedRow { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTable), nameof(ShowEmpty))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTable), nameof(ShowEmpty))]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; } = "no endpoints match the current filters";

    public bool HasError => ErrorText is not null;
    public bool ShowTable => !IsLoading && !IsEmpty;
    public bool ShowEmpty => !IsLoading && IsEmpty;

    protected override void OnActivated()
    {
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _refresh.Request();
    }

    protected override void OnDeactivated()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
    }

    protected override void OnParameter(object parameter)
    {
        if (parameter is string search)
        {
            ProtocolFilterIndex = 0;
            StateFilterIndex = 0;
            HideLoopback = false;
            SearchText = search;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyView();
    partial void OnProtocolFilterIndexChanged(int value) => ApplyView();
    partial void OnStateFilterIndexChanged(int value) => ApplyView();
    partial void OnHideLoopbackChanged(bool value) => ApplyView();

    [RelayCommand]
    private void Refresh()
    {
        IsRefreshing = true;
        _monitor.RequestRefresh();
    }

    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrEmpty(column))
        {
            return;
        }

        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column is ConnectionColumns.FirstSeen;
        }

        ApplyView();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        ProtocolFilterIndex = 0;
        StateFilterIndex = 0;
        HideLoopback = false;
    }

    [RelayCommand]
    private static void CopyRow(ConnectionRowViewModel? row) => ClipboardHelper.TryCopy(row?.ToClipboardText());

    [RelayCommand]
    private static void CopyRemote(ConnectionRowViewModel? row)
    {
        if (row is not null && row.Entry.Protocol == NetProtocol.Tcp)
        {
            ClipboardHelper.TryCopy(row.Entry.RemoteAddress);
        }
    }

    [RelayCommand]
    private static void CopyLocal(ConnectionRowViewModel? row) => ClipboardHelper.TryCopy(row?.Entry.LocalAddress);

    [RelayCommand]
    private void LookupRemote(ConnectionRowViewModel? row)
    {
        if (row is not null && row.Entry.Protocol == NetProtocol.Tcp && !row.Entry.IsLocalOnly)
        {
            _navigation.Navigate(AppPage.Dns, row.Entry.RemoteAddress);
        }
    }

    [RelayCommand]
    private void ShowProcess(ConnectionRowViewModel? row)
    {
        if (row is not null)
        {
            _navigation.Navigate(AppPage.Processes, row.Pid);
        }
    }

    private void OnSnapshotUpdated(object? sender, ConnectionSnapshot snapshot) => _refresh.Request();

    private void ApplySnapshot()
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

        _snapshot = snapshot;
        IsRefreshing = false;
        ErrorText = snapshot.Error;

        // reuse row viewmodels for endpoints that are still alive
        _alive.Clear();
        foreach (ConnectionEntry entry in snapshot.Entries)
        {
            _alive.Add(entry);
            if (_rows.TryGetValue(entry, out ConnectionRowViewModel? row))
            {
                row.Update();
            }
            else
            {
                _rows[entry] = new ConnectionRowViewModel(entry);
            }
        }

        _stale.Clear();
        foreach (ConnectionEntry entry in _rows.Keys)
        {
            if (!_alive.Contains(entry))
            {
                _stale.Add(entry);
            }
        }

        foreach (ConnectionEntry entry in _stale)
        {
            _rows.Remove(entry);
        }

        IsLoading = false;
        ApplyView();
    }

    private void ApplyView()
    {
        if (_snapshot.IsEmpty)
        {
            return;
        }

        _view.Clear();
        string search = SearchText.Trim();
        foreach (ConnectionRowViewModel row in _rows.Values)
        {
            if (Matches(row, search))
            {
                _view.Add(row);
            }
        }

        _view.Sort(CreateComparer(SortColumn, SortDescending));

        ConnectionRowViewModel? selected = SelectedRow;
        Rows.SyncTo(_view);
        if (selected is not null && _view.Contains(selected))
        {
            SelectedRow = selected;
        }

        IsEmpty = _view.Count == 0;
        EmptyMessage = _rows.Count == 0 ? "no active endpoints were reported" : "no endpoints match the current filters";
        SummaryText = string.Create(CultureInfo.InvariantCulture,
            $"{_view.Count} of {_rows.Count} endpoints  ·  {_snapshot.TcpCount} tcp  ·  {_snapshot.UdpCount} udp  ·  updated {TimeFormatter.Format(_snapshot.Timestamp)}");
    }

    private bool Matches(ConnectionRowViewModel row, string search)
    {
        ConnectionEntry entry = row.Entry;
        if (ProtocolFilterIndex == 1 && entry.Protocol != NetProtocol.Tcp)
        {
            return false;
        }

        if (ProtocolFilterIndex == 2 && entry.Protocol != NetProtocol.Udp)
        {
            return false;
        }

        if (StateFilterIndex > 0)
        {
            if (entry.Protocol == NetProtocol.Udp)
            {
                return false;
            }

            bool stateMatch = StateFilterIndex switch
            {
                1 => entry.State == TcpConnectionState.Established,
                2 => entry.State == TcpConnectionState.Listen,
                3 => entry.State == TcpConnectionState.TimeWait,
                4 => entry.State == TcpConnectionState.CloseWait,
                _ => entry.State is not (TcpConnectionState.Established or TcpConnectionState.Listen or TcpConnectionState.TimeWait or TcpConnectionState.CloseWait),
            };
            if (!stateMatch)
            {
                return false;
            }
        }

        if (HideLoopback && IsLoopback(entry))
        {
            return false;
        }

        if (search.Length == 0)
        {
            return true;
        }

        return entry.LocalEndpoint.Contains(search, StringComparison.OrdinalIgnoreCase)
               || entry.RemoteEndpoint.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || row.State.Contains(search, StringComparison.OrdinalIgnoreCase)
               || entry.ProtocolText.Equals(search, StringComparison.OrdinalIgnoreCase)
               || row.PidText == search;
    }

    private static bool IsLoopback(ConnectionEntry entry) =>
        entry.LocalAddress.StartsWith("127.", StringComparison.Ordinal) || entry.LocalAddress == "::1";

    private static Comparison<ConnectionRowViewModel> CreateComparer(string column, bool descending)
    {
        Comparison<ConnectionRowViewModel> primary = column switch
        {
            ConnectionColumns.Protocol => (a, b) => string.CompareOrdinal(a.Protocol, b.Protocol),
            ConnectionColumns.Local => (a, b) => CompareEndpoint(a.Entry.Key.IsIPv6, a.Entry.Key.LocalAddress, a.Entry.LocalPort, b.Entry.Key.IsIPv6, b.Entry.Key.LocalAddress, b.Entry.LocalPort),
            ConnectionColumns.Remote => (a, b) => CompareEndpoint(a.Entry.Key.IsIPv6, a.Entry.Key.RemoteAddress, a.Entry.RemotePort, b.Entry.Key.IsIPv6, b.Entry.Key.RemoteAddress, b.Entry.RemotePort),
            ConnectionColumns.State => (a, b) => string.CompareOrdinal(a.State, b.State),
            ConnectionColumns.Process => (a, b) => string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase),
            ConnectionColumns.Pid => (a, b) => a.Pid.CompareTo(b.Pid),
            _ => (a, b) => a.Entry.FirstSeen.CompareTo(b.Entry.FirstSeen),
        };

        return (a, b) =>
        {
            int result = primary(a, b);
            if (result == 0)
            {
                // stable tie-breaker so rows do not jump between refreshes
                result = a.Entry.LocalPort.CompareTo(b.Entry.LocalPort);
                if (result == 0)
                {
                    result = a.Entry.RemotePort.CompareTo(b.Entry.RemotePort);
                }

                if (result == 0)
                {
                    result = a.Pid.CompareTo(b.Pid);
                }

                return result;
            }

            return descending ? -result : result;
        };
    }

    private static int CompareEndpoint(bool aV6, UInt128 aAddress, ushort aPort, bool bV6, UInt128 bAddress, ushort bPort)
    {
        int result = aV6.CompareTo(bV6);
        if (result == 0)
        {
            result = aAddress.CompareTo(bAddress);
        }

        return result != 0 ? result : aPort.CompareTo(bPort);
    }
}
