using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private static readonly int[] RefreshValues = [1, 2, 3, 5, 10];
    private static readonly int[] SampleValues = [500, 1000, 2000, 5000];
    private static readonly int[] RetentionValues = [1, 7, 30, 90, 0];

    private readonly SettingsService _settings;
    private readonly HistoryService? _history;
    private readonly DispatcherQueue _dispatcher;
    private bool _loading;

    public SettingsViewModel(SettingsService settings, InterfaceSelectorViewModel interfaceSelector, HistoryService? history, DispatcherQueue dispatcher)
    {
        _settings = settings;
        _history = history;
        _dispatcher = dispatcher;
        InterfaceSelector = interfaceSelector;
        DatabaseText = string.Empty;
        Load();

        // keep the page in sync when another page changes a shared setting
        _settings.SettingChanged += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            if (!_loading)
            {
                Load();
            }
        });
    }

    public InterfaceSelectorViewModel InterfaceSelector { get; }

    public string[] RefreshOptions { get; } = ["1 second", "2 seconds", "3 seconds", "5 seconds", "10 seconds"];

    public string[] SampleOptions { get; } = ["0.5 seconds", "1 second", "2 seconds", "5 seconds"];

    public string[] StartupPages { get; } = ["overview", "connections", "processes", "dns lookup", "history", "settings"];

    public string[] RetentionOptions { get; } = ["1 day", "7 days", "30 days", "90 days", "keep forever"];

    public string[] Themes { get; } = ["dark", "light"];

    public string Version => AppPaths.Version;

    public string DataDirectory => AppPaths.DataDirectory;

    [ObservableProperty]
    public partial int RefreshIndex { get; set; }

    [ObservableProperty]
    public partial int SampleIndex { get; set; }

    [ObservableProperty]
    public partial int StartupPageIndex { get; set; }

    [ObservableProperty]
    public partial int RetentionIndex { get; set; }

    [ObservableProperty]
    public partial bool HistoryEnabled { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial string DatabaseText { get; set; }

    protected override void OnActivated() => UpdateDatabaseText();

    partial void OnRefreshIndexChanged(int value) =>
        Save(nameof(AppSettings.RefreshIntervalSeconds), value, RefreshValues, (s, v) => s.RefreshIntervalSeconds = v);

    partial void OnSampleIndexChanged(int value) =>
        Save(nameof(AppSettings.TrafficSampleIntervalMs), value, SampleValues, (s, v) => s.TrafficSampleIntervalMs = v);

    partial void OnRetentionIndexChanged(int value) =>
        Save(nameof(AppSettings.HistoryRetentionDays), value, RetentionValues, (s, v) => s.HistoryRetentionDays = v);

    partial void OnStartupPageIndexChanged(int value)
    {
        if (!_loading && value >= 0)
        {
            Commit(nameof(AppSettings.StartupPage), s => s.StartupPage = (AppPage)value);
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (!_loading && value >= 0)
        {
            Commit(nameof(AppSettings.Theme), s => s.Theme = (AppTheme)value);
        }
    }

    partial void OnHistoryEnabledChanged(bool value)
    {
        if (!_loading)
        {
            Commit(nameof(AppSettings.HistoryCollectionEnabled), s => s.HistoryCollectionEnabled = value);
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // explorer is unavailable; the path is still shown and copyable
        }
    }

    [RelayCommand]
    private static void CopyDataFolder() => ClipboardHelper.TryCopy(AppPaths.DataDirectory);

    private void Save(string name, int index, int[] values, Action<AppSettings, int> apply)
    {
        if (_loading || index < 0 || index >= values.Length)
        {
            return;
        }

        int value = values[index];
        Commit(name, s => apply(s, value));
    }

    private void Commit(string name, Action<AppSettings> mutate)
    {
        _loading = true;
        try
        {
            _settings.Update(name, mutate);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Load()
    {
        _loading = true;
        try
        {
            AppSettings s = _settings.Current;
            RefreshIndex = IndexOf(RefreshValues, s.RefreshIntervalSeconds, 1);
            SampleIndex = IndexOf(SampleValues, s.TrafficSampleIntervalMs, 1);
            RetentionIndex = IndexOf(RetentionValues, s.HistoryRetentionDays, 1);
            StartupPageIndex = (int)s.StartupPage;
            ThemeIndex = (int)s.Theme;
            HistoryEnabled = s.HistoryCollectionEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateDatabaseText()
    {
        DatabaseText = _history is null
            ? "history database unavailable"
            : $"history.db  ·  {ByteFormatter.FormatBytes(_history.GetDatabaseSize())}";
    }

    private static int IndexOf(int[] values, int value, int fallback)
    {
        int index = Array.IndexOf(values, value);
        return index >= 0 ? index : fallback;
    }
}
