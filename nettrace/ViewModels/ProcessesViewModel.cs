using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRowViewModel(ProcessIdentity identity)
    {
        Identity = identity;
        Summary = string.Empty;
    }

    public ProcessIdentity Identity { get; }
    public uint Pid => Identity.Pid;
    public string PidText => Identity.Pid.ToString(CultureInfo.InvariantCulture);
    public string Name => Identity.Name;
    public string PathText => Identity.PathText;
    public bool HasPath => Identity.Path is not null;

    [ObservableProperty]
    public partial int ConnectionCount { get; set; }

    [ObservableProperty]
    public partial int TcpCount { get; set; }

    [ObservableProperty]
    public partial int UdpCount { get; set; }

    [ObservableProperty]
    public partial int EstablishedCount { get; set; }

    [ObservableProperty]
    public partial int ListeningCount { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    public string ConnectionCountText => ConnectionCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>accessible name of the list entry.</summary>
    public override string ToString() => $"{Name}, pid {PidText}";

    internal void SetCounts(int tcp, int udp, int established, int listening)
    {
        bool changed = tcp != TcpCount || udp != UdpCount || established != EstablishedCount || listening != ListeningCount;
        if (!changed && Summary.Length > 0)
        {
            return;
        }

        TcpCount = tcp;
        UdpCount = udp;
        EstablishedCount = established;
        ListeningCount = listening;
        ConnectionCount = tcp + udp;
        OnPropertyChanged(nameof(ConnectionCountText));
        Summary = string.Create(CultureInfo.InvariantCulture, $"{tcp} tcp  ·  {udp} udp  ·  {established} established  ·  {listening} listening");
    }
}

public sealed partial class ProcessesViewModel : PageViewModel
{
    private readonly ConnectionMonitorService _monitor;
    private readonly NavigationService _navigation;
    private readonly CoalescingDispatcher _refresh;
    private readonly Dictionary<uint, ProcessRowViewModel> _rows = new();
    private readonly Dictionary<uint, List<ConnectionEntry>> _groups = new();
    private readonly Dictionary<ConnectionEntry, ConnectionRowViewModel> _detailRows = new(ReferenceEqualityComparer.Instance);
    private readonly List<ProcessRowViewModel> _view = new();
    private readonly List<uint> _stale = new();
    private uint? _pendingSelection;

    public ProcessesViewModel(ConnectionMonitorService monitor, NavigationService navigation, DispatcherQueue dispatcher)
    {
        _monitor = monitor;
        _navigation = navigation;
        _refresh = new CoalescingDispatcher(dispatcher, ApplySnapshot);
        SearchText = string.Empty;
        SummaryText = string.Empty;
    }

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();

    public ObservableCollection<ConnectionRowViewModel> SelectedConnections { get; } = new();

    public string[] SortOptions { get; } = ["most connections", "name", "pid"];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShowNoSelection))]
    public partial ProcessRowViewModel? SelectedProcess { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList), nameof(ShowEmpty), nameof(ShowNoSelection))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList), nameof(ShowEmpty))]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;
    public bool HasSelection => SelectedProcess is not null;
    public bool ShowNoSelection => !IsLoading && SelectedProcess is null;
    public bool ShowList => !IsLoading && !IsEmpty;
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
        if (parameter is uint pid)
        {
            SearchText = string.Empty;
            if (_rows.TryGetValue(pid, out ProcessRowViewModel? row))
            {
                SelectedProcess = row;
            }
            else
            {
                _pendingSelection = pid;
            }
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyView();
    partial void OnSortIndexChanged(int value) => ApplyView();
    partial void OnSelectedProcessChanged(ProcessRowViewModel? value) => ApplyDetail();

    [RelayCommand]
    private void Refresh() => _monitor.RequestRefresh();

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedProcess?.Identity.Path is { } path)
        {
            ClipboardHelper.TryCopy(path);
        }
    }

    [RelayCommand]
    private void ShowInConnections()
    {
        if (SelectedProcess is not null)
        {
            _navigation.Navigate(AppPage.Connections, SelectedProcess.PidText);
        }
    }

    [RelayCommand]
    private static void CopyConnection(ConnectionRowViewModel? row) => ClipboardHelper.TryCopy(row?.ToClipboardText());

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

        ErrorText = snapshot.Error;

        foreach (List<ConnectionEntry> list in _groups.Values)
        {
            list.Clear();
        }

        foreach (ConnectionEntry entry in snapshot.Entries)
        {
            if (!_groups.TryGetValue(entry.Pid, out List<ConnectionEntry>? list))
            {
                list = new List<ConnectionEntry>();
                _groups[entry.Pid] = list;
            }

            list.Add(entry);
        }

        _stale.Clear();
        foreach ((uint pid, List<ConnectionEntry> list) in _groups)
        {
            if (list.Count == 0)
            {
                _stale.Add(pid);
                continue;
            }

            ProcessIdentity identity = list[0].Process;
            if (!_rows.TryGetValue(pid, out ProcessRowViewModel? row) || !ReferenceEquals(row.Identity, identity))
            {
                row = new ProcessRowViewModel(identity);
                _rows[pid] = row;
            }

            int tcp = 0, udp = 0, established = 0, listening = 0;
            foreach (ConnectionEntry entry in list)
            {
                if (entry.Protocol == NetProtocol.Tcp)
                {
                    tcp++;
                    if (entry.State == TcpConnectionState.Established)
                    {
                        established++;
                    }
                    else if (entry.State == TcpConnectionState.Listen)
                    {
                        listening++;
                    }
                }
                else
                {
                    udp++;
                }
            }

            row.SetCounts(tcp, udp, established, listening);
        }

        foreach (uint pid in _stale)
        {
            _groups.Remove(pid);
            _rows.Remove(pid);
        }

        IsLoading = false;
        ApplyView();

        if (_pendingSelection is uint pending && _rows.TryGetValue(pending, out ProcessRowViewModel? target))
        {
            _pendingSelection = null;
            SelectedProcess = target;
        }
        else if (SelectedProcess is not null && !_rows.ContainsKey(SelectedProcess.Pid))
        {
            // the selected process exited or released all of its endpoints
            SelectedProcess = null;
        }
        else
        {
            ApplyDetail();
        }

        SummaryText = string.Create(CultureInfo.InvariantCulture,
            $"{_rows.Count} processes with endpoints  ·  updated {TimeFormatter.Format(snapshot.Timestamp)}");
    }

    private void ApplyView()
    {
        if (IsLoading)
        {
            return;
        }

        string search = SearchText.Trim();
        _view.Clear();
        foreach (ProcessRowViewModel row in _rows.Values)
        {
            if (search.Length == 0
                || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.PidText == search
                || (row.Identity.Path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                _view.Add(row);
            }
        }

        Comparison<ProcessRowViewModel> comparison = SortIndex switch
        {
            1 => (a, b) =>
            {
                int r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return r != 0 ? r : a.Pid.CompareTo(b.Pid);
            },
            2 => (a, b) => a.Pid.CompareTo(b.Pid),
            _ => (a, b) =>
            {
                int r = b.ConnectionCount.CompareTo(a.ConnectionCount);
                if (r == 0)
                {
                    r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }

                return r != 0 ? r : a.Pid.CompareTo(b.Pid);
            },
        };
        _view.Sort(comparison);

        ProcessRowViewModel? selected = SelectedProcess;
        Processes.SyncTo(_view);
        if (selected is not null && _view.Contains(selected) && !ReferenceEquals(SelectedProcess, selected))
        {
            SelectedProcess = selected;
        }

        IsEmpty = _view.Count == 0;
    }

    private void ApplyDetail()
    {
        ProcessRowViewModel? selected = SelectedProcess;
        if (selected is null || !_groups.TryGetValue(selected.Pid, out List<ConnectionEntry>? entries))
        {
            SelectedConnections.Clear();
            _detailRows.Clear();
            return;
        }

        var ordered = new List<ConnectionRowViewModel>(entries.Count);
        var alive = new HashSet<ConnectionEntry>(entries, ReferenceEqualityComparer.Instance);
        foreach (ConnectionEntry entry in entries)
        {
            if (_detailRows.TryGetValue(entry, out ConnectionRowViewModel? row))
            {
                row.Update();
            }
            else
            {
                row = new ConnectionRowViewModel(entry);
                _detailRows[entry] = row;
            }

            ordered.Add(row);
        }

        foreach (ConnectionEntry entry in _detailRows.Keys.Where(e => !alive.Contains(e)).ToList())
        {
            _detailRows.Remove(entry);
        }

        ordered.Sort((a, b) =>
        {
            int r = a.Entry.Protocol.CompareTo(b.Entry.Protocol);
            if (r == 0)
            {
                r = StateRank(a.Entry).CompareTo(StateRank(b.Entry));
            }

            return r != 0 ? r : a.Entry.LocalPort.CompareTo(b.Entry.LocalPort);
        });

        SelectedConnections.SyncTo(ordered);
    }

    private static int StateRank(ConnectionEntry entry) => entry.State switch
    {
        TcpConnectionState.Established => 0,
        TcpConnectionState.Listen => 1,
        _ => 2,
    };
}
