using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;

namespace NetTrace.ViewModels;

public sealed partial class DnsViewModel : PageViewModel
{
    private readonly DnsService _dns;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _lookupCts;

    public DnsViewModel(DnsService dns, DispatcherQueue dispatcher)
    {
        _dns = dns;
        _dispatcher = dispatcher;
        Query = string.Empty;
        ResultTitle = string.Empty;
        ResultDetails = string.Empty;
        _dns.RecentChanged += (_, _) => _dispatcher.TryEnqueue(ReloadRecent);
    }

    public string[] Modes { get; } = ["auto", "forward", "reverse"];

    public string[] Families { get; } = ["ipv4 + ipv6", "ipv4 only", "ipv6 only"];

    public ObservableCollection<DnsRecord> Records { get; } = new();

    public ObservableCollection<DnsLookupResult> Recent { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookupCommand))]
    public partial string Query { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFamilyEnabled))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    public partial int FamilyIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIdle), nameof(ShowResult), nameof(HasError))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowIdle), nameof(ShowResult))]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIdle), nameof(ShowResult))]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    public partial string ResultTitle { get; set; }

    [ObservableProperty]
    public partial string ResultDetails { get; set; }

    [ObservableProperty]
    public partial bool HasRecent { get; set; }

    public bool HasError => ErrorText is not null && !IsBusy;
    public bool ShowIdle => !IsBusy && !HasResult && ErrorText is null;
    public bool ShowResult => !IsBusy && HasResult;

    /// <summary>reverse lookups have no address family.</summary>
    public bool IsFamilyEnabled => ModeIndex != 2;

    protected override void OnActivated() => ReloadRecent();

    protected override void OnParameter(object parameter)
    {
        if (parameter is string query && query.Length > 0)
        {
            Query = query;
            ModeIndex = 0;
            _ = LookupCommand.ExecuteAsync(null);
        }
    }

    private bool CanLookup() => !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanLookup), AllowConcurrentExecutions = true)]
    private async Task LookupAsync()
    {
        // a newer lookup always supersedes the one in flight
        _lookupCts?.Cancel();
        _lookupCts?.Dispose();
        var cts = new CancellationTokenSource();
        _lookupCts = cts;

        IsBusy = true;
        ErrorText = null;

        try
        {
            DnsLookupResult result = await _dns.LookupAsync(
                Query,
                (DnsLookupMode)ModeIndex,
                (DnsAddressFamily)FamilyIndex,
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            Show(result);
        }
        catch (OperationCanceledException)
        {
            // superseded or cancelled by the user
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                HasResult = false;
                Records.Clear();
                ErrorText = "the lookup failed: " + ex.Message.ToLowerInvariant();
            }
        }
        finally
        {
            if (ReferenceEquals(_lookupCts, cts))
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _lookupCts?.Cancel();
        IsBusy = false;
    }

    [RelayCommand]
    private void Rerun(DnsLookupResult? result)
    {
        if (result is null)
        {
            return;
        }

        Query = result.Query;
        // prefer auto when it would pick the same direction, so the next query is not pinned to a mode
        ModeIndex = DnsService.ResolveMode(result.Query, DnsLookupMode.Auto) == result.Mode
            ? (int)DnsLookupMode.Auto
            : (int)result.Mode;
        FamilyIndex = (int)result.Family;
        _ = LookupCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private static void CopyRecord(DnsRecord? record) => ClipboardHelper.TryCopy(record?.Value);

    [RelayCommand]
    private void CopyAll()
    {
        if (Records.Count > 0)
        {
            ClipboardHelper.TryCopy(string.Join(Environment.NewLine, Records.Select(r => r.Value)));
        }
    }

    [RelayCommand]
    private void ClearRecent() => _dns.ClearRecent();

    private void Show(DnsLookupResult result)
    {
        Records.Clear();
        if (!result.Succeeded)
        {
            HasResult = false;
            ErrorText = result.Error;
            return;
        }

        foreach (DnsRecord record in result.Records)
        {
            Records.Add(record);
        }

        ResultTitle = result.Query;
        string family = result.Mode == DnsLookupMode.Reverse ? string.Empty : result.Family switch
        {
            DnsAddressFamily.IPv4 => "  ·  ipv4",
            DnsAddressFamily.IPv6 => "  ·  ipv6",
            _ => string.Empty,
        };
        string source = result.FromCache ? "cached" : result.Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        ResultDetails = $"{result.ModeText}{family}  ·  {result.Summary}  ·  {source}  ·  {TimeFormatter.Format(result.Timestamp)}";
        HasResult = true;
    }

    private void ReloadRecent()
    {
        Recent.Clear();
        foreach (DnsLookupResult result in _dns.Recent)
        {
            Recent.Add(result);
        }

        HasRecent = Recent.Count > 0;
    }
}
