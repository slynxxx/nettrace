using Windows.Networking.Connectivity;
using WinRtNetworkInformation = Windows.Networking.Connectivity.NetworkInformation;

namespace NetTrace.Services;

public enum NetworkStatus
{
    Unknown,
    Offline,
    LocalOnly,
    Limited,
    Online,
}

/// <summary>reports the system connectivity level using the windows connection profile api.</summary>
public sealed class NetworkStatusService : IDisposable
{
    private bool _subscribed;

    public NetworkStatusService()
    {
        try
        {
            WinRtNetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _subscribed = true;
        }
        catch (Exception)
        {
            // connectivity notifications are optional; status is still refreshed on demand
        }

        Refresh();
    }

    public event EventHandler? Changed;

    public NetworkStatus Status { get; private set; }

    public string ProfileName { get; private set; } = string.Empty;

    public string StatusText => Status switch
    {
        NetworkStatus.Online => "online",
        NetworkStatus.Limited => "limited connectivity",
        NetworkStatus.LocalOnly => "local network only",
        NetworkStatus.Offline => "offline",
        _ => "unknown",
    };

    public void Refresh()
    {
        try
        {
            ConnectionProfile? profile = WinRtNetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                Status = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
                    ? NetworkStatus.LocalOnly
                    : NetworkStatus.Offline;
                ProfileName = string.Empty;
                return;
            }

            Status = profile.GetNetworkConnectivityLevel() switch
            {
                NetworkConnectivityLevel.InternetAccess => NetworkStatus.Online,
                NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkStatus.Limited,
                NetworkConnectivityLevel.LocalAccess => NetworkStatus.LocalOnly,
                _ => NetworkStatus.Offline,
            };
            ProfileName = profile.ProfileName ?? string.Empty;
        }
        catch (Exception)
        {
            Status = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
                ? NetworkStatus.Unknown
                : NetworkStatus.Offline;
            ProfileName = string.Empty;
        }
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            WinRtNetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
            _subscribed = false;
        }
    }

    private void OnNetworkStatusChanged(object sender)
    {
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
