using CommunityToolkit.Mvvm.ComponentModel;
using NetTrace.Helpers;
using NetTrace.Models;

namespace NetTrace.ViewModels;

/// <summary>a table row bound to a live <see cref="ConnectionEntry"/>; only changing fields notify.</summary>
public sealed partial class ConnectionRowViewModel : ObservableObject
{
    public ConnectionRowViewModel(ConnectionEntry entry)
    {
        Entry = entry;
        FirstSeenText = TimeFormatter.Format(entry.FirstSeen);
        State = entry.StateText;
        ProcessName = entry.Process.Name;
        Pid = entry.Pid;
    }

    public ConnectionEntry Entry { get; }

    public string Protocol => Entry.ProtocolText;
    public string LocalEndpoint => Entry.LocalEndpoint;
    public string RemoteEndpoint => Entry.Protocol == NetProtocol.Udp ? "—" : Entry.RemoteEndpoint;
    public string FirstSeenText { get; }

    [ObservableProperty]
    public partial string State { get; set; }

    [ObservableProperty]
    public partial string ProcessName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PidText))]
    public partial uint Pid { get; set; }

    public string PidText => Pid.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Update()
    {
        string state = Entry.StateText;
        if (state != State)
        {
            State = state;
        }

        if (Entry.Pid != Pid)
        {
            Pid = Entry.Pid;
        }

        if (Entry.Process.Name != ProcessName)
        {
            ProcessName = Entry.Process.Name;
        }
    }

    /// <summary>accessible name of the table row.</summary>
    public override string ToString() => $"{Protocol} {LocalEndpoint} to {RemoteEndpoint}, {State}, {ProcessName}";

    public string ToClipboardText() =>
        $"{Protocol}\t{LocalEndpoint}\t{RemoteEndpoint}\t{State}\t{ProcessName}\t{Pid}\t{FirstSeenText}";
}
