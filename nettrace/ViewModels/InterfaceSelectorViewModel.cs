using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

/// <summary>shared network interface picker used by the overview and settings pages.</summary>
public sealed partial class InterfaceSelectorViewModel : ObservableObject
{
    private readonly TrafficMonitorService _traffic;
    private readonly SettingsService _settings;
    private readonly DispatcherQueue _dispatcher;
    private bool _syncing;

    public InterfaceSelectorViewModel(TrafficMonitorService traffic, SettingsService settings, DispatcherQueue dispatcher)
    {
        _traffic = traffic;
        _settings = settings;
        _dispatcher = dispatcher;
        Description = string.Empty;
        _traffic.InterfacesChanged += (_, _) => _dispatcher.TryEnqueue(Reload);
        _settings.SettingChanged += (_, name) =>
        {
            if (name == nameof(AppSettings.SelectedInterfaceId))
            {
                _dispatcher.TryEnqueue(SelectCurrent);
            }
        };
        Reload();
    }

    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } = new();

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_syncing || value < 0 || value >= Interfaces.Count)
        {
            return;
        }

        NetworkInterfaceInfo selected = Interfaces[value];
        UpdateDescription(selected);
        if (selected.Id != _settings.Current.SelectedInterfaceId)
        {
            _settings.Update(nameof(AppSettings.SelectedInterfaceId), s => s.SelectedInterfaceId = selected.Id);
        }
    }

    private void Reload()
    {
        _syncing = true;
        try
        {
            IReadOnlyList<NetworkInterfaceInfo> latest = _traffic.Interfaces;
            string selectedId = _settings.Current.SelectedInterfaceId;

            // keep a saved selection visible even while that adapter is absent
            var items = latest.ToList();
            if (selectedId.Length > 0 && items.All(i => i.Id != selectedId))
            {
                items.Add(new NetworkInterfaceInfo(selectedId, "unavailable interface", "the saved interface is not present", "", false, 0));
            }

            Interfaces.Clear();
            foreach (NetworkInterfaceInfo item in items)
            {
                Interfaces.Add(item);
            }
        }
        finally
        {
            _syncing = false;
        }

        SelectCurrent();
    }

    private void SelectCurrent()
    {
        string id = _settings.Current.SelectedInterfaceId;
        int index = 0;
        for (int i = 0; i < Interfaces.Count; i++)
        {
            if (Interfaces[i].Id == id)
            {
                index = i;
                break;
            }
        }

        _syncing = true;
        try
        {
            SelectedIndex = -1;
            SelectedIndex = index;
        }
        finally
        {
            _syncing = false;
        }

        if (index < Interfaces.Count)
        {
            UpdateDescription(Interfaces[index]);
        }
    }

    private void UpdateDescription(NetworkInterfaceInfo info)
    {
        Description = info.Id == NetworkInterfaceInfo.AllInterfacesId
            ? "sum of every active interface, excluding loopback and tunnels"
            : $"{info.Description.ToLowerInvariant()}  ·  {info.TypeText}  ·  {(info.IsUp ? "up" : "down")}{(info.IsUp && info.Speed > 0 ? "  ·  " + ByteFormatter.FormatLinkSpeed(info.Speed) : string.Empty)}";
    }
}
