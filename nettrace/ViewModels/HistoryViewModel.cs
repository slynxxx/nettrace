using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

public sealed partial class HistoryViewModel : PageViewModel
{
    private const int PageLimit = 1000;

    private readonly HistoryService? _history;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly NavigationService _navigation;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _queryCts;

    public HistoryViewModel(
        HistoryService? history,
        string? historyError,
        SettingsService settings,
        DialogService dialogs,
        NavigationService navigation,
        DispatcherQueue dispatcher)
    {
        _history = history;
        _settings = settings;
        _dialogs = dialogs;
        _navigation = navigation;
        _dispatcher = dispatcher;
        SearchText = string.Empty;
        SummaryText = string.Empty;
        Records = [];
        ErrorText = historyError;
        IsCollectionEnabled = settings.Current.HistoryCollectionEnabled;

        _settings.SettingChanged += (_, name) =>
        {
            if (name == nameof(AppSettings.HistoryCollectionEnabled))
            {
                _dispatcher.TryEnqueue(() => IsCollectionEnabled = _settings.Current.HistoryCollectionEnabled);
            }
        };

        if (_history is not null)
        {
            _history.Changed += (_, _) => _dispatcher.TryEnqueue(() =>
            {
                if (IsActive)
                {
                    _ = LoadAsync(0);
                }
            });
        }
    }

    public string[] ProtocolFilters { get; } = ["all protocols", "tcp", "udp"];

    public string[] Ranges { get; } = ["last hour", "last 24 hours", "last 7 days", "all time"];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial int ProtocolFilterIndex { get; set; }

    [ObservableProperty]
    public partial int RangeIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowTable))]
    public partial IReadOnlyList<HistoryRecord> Records { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowTable))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowEmpty), nameof(ShowTable))]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCollectionOff))]
    public partial bool IsCollectionEnabled { get; set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; } = "no history matches the current filters";

    public bool HasError => ErrorText is not null;
    public bool HasStatus => StatusMessage is not null;
    public bool ShowCollectionOff => !IsCollectionEnabled;
    public bool IsAvailable => _history is not null;
    public bool ShowEmpty => !IsLoading && ErrorText is null && Records.Count == 0;
    public bool ShowTable => ErrorText is null && Records.Count > 0;

    protected override void OnActivated() => _ = LoadAsync(0);

    protected override void OnDeactivated()
    {
        _queryCts?.Cancel();
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync(300);
    partial void OnProtocolFilterIndexChanged(int value) => _ = LoadAsync(0);
    partial void OnRangeIndexChanged(int value) => _ = LoadAsync(0);

    [RelayCommand]
    private Task Refresh() => LoadAsync(0);

    [RelayCommand]
    private void OpenSettings() => _navigation.Navigate(AppPage.Settings);

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        ProtocolFilterIndex = 0;
        RangeIndex = 3;
    }

    [RelayCommand]
    private static void CopyRecord(HistoryRecord? record)
    {
        if (record is not null)
        {
            ClipboardHelper.TryCopy($"{record.Protocol}\t{record.LocalEndpoint}\t{record.RemoteEndpoint}\t{record.State}\t{record.ProcessName}\t{record.Pid}\t{record.FirstSeen:o}\t{record.LastSeen:o}");
        }
    }

    [RelayCommand]
    private Task ExportCsv() => ExportAsync(ExportFormat.Csv);

    [RelayCommand]
    private Task ExportJson() => ExportAsync(ExportFormat.Json);

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (_history is null)
        {
            return;
        }

        bool confirmed = await _dialogs.ConfirmAsync(
            "clear history",
            "every recorded connection will be permanently removed from the local database. live monitoring is not affected.",
            "clear history");
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _history.ClearAsync(CancellationToken.None);
            StatusMessage = "history cleared";
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or ObjectDisposedException)
        {
            StatusMessage = "could not clear history: " + ex.Message.ToLowerInvariant();
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync(0);
    }

    private HistoryQuery BuildQuery(int limit) => new(
        SearchText,
        ProtocolFilterIndex switch { 1 => "tcp", 2 => "udp", _ => null },
        (HistoryRange)RangeIndex,
        limit);

    private async Task LoadAsync(int debounceMs)
    {
        if (_history is null || !IsActive)
        {
            return;
        }

        _queryCts?.Cancel();
        _queryCts?.Dispose();
        var cts = new CancellationTokenSource();
        _queryCts = cts;

        try
        {
            if (debounceMs > 0)
            {
                await Task.Delay(debounceMs, cts.Token);
            }

            IsLoading = true;
            HistoryPage page = await _history.QueryAsync(BuildQuery(PageLimit), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Records = page.Records;
            ErrorText = null;
            EmptyMessage = page.TotalCount == 0
                ? IsCollectionEnabled ? "no connections have been recorded yet" : "no connections have been recorded"
                : "no history matches the current filters";

            string shown = page.MatchingCount > page.Records.Count
                ? $"latest {page.Records.Count:N0} of {page.MatchingCount:N0} matching"
                : $"{page.MatchingCount:N0} matching";
            SummaryText = string.Create(CultureInfo.InvariantCulture,
                $"{shown}  ·  {page.TotalCount:N0} recorded  ·  {ByteFormatter.FormatBytes(_history.GetDatabaseSize())} on disk");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or ObjectDisposedException or InvalidOperationException)
        {
            ErrorText = "history could not be read: " + ex.Message.ToLowerInvariant();
        }
        finally
        {
            if (ReferenceEquals(_queryCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    private async Task ExportAsync(ExportFormat format)
    {
        if (_history is null)
        {
            return;
        }

        string extension = format == ExportFormat.Csv ? ".csv" : ".json";
        string? path;
        try
        {
            path = await _dialogs.PickSaveFileAsync(
                $"nettrace-history-{DateTime.Now:yyyyMMdd-HHmmss}",
                format == ExportFormat.Csv ? "csv" : "json",
                extension);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            StatusMessage = "the save dialog could not be opened: " + ex.Message.ToLowerInvariant();
            return;
        }

        if (path is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "exporting…";
        try
        {
            long count;
            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                count = await _history.ExportAsync(BuildQuery(int.MaxValue), stream, format, CancellationToken.None);
            }

            StatusMessage = $"exported {count:N0} records to {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or ObjectDisposedException)
        {
            StatusMessage = "export failed: " + ex.Message.ToLowerInvariant();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
