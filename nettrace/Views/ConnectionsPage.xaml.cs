using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetTrace.Models;
using NetTrace.ViewModels;

namespace NetTrace.Views;

public sealed partial class ConnectionsPage : HostedPage
{
    private ConnectionRowViewModel? _menuRow;

    public ConnectionsPage()
    {
        InitializeComponent();
    }

    public ConnectionsViewModel ViewModel { get; } = App.Host.Connections;

    protected override PageViewModel PageModel => ViewModel;

    private void OnRowMenuOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu)
        {
            return;
        }

        _menuRow = (menu.Target as FrameworkElement)?.DataContext as ConnectionRowViewModel;
        bool hasRemote = _menuRow?.Entry.Protocol == NetProtocol.Tcp;
        foreach (MenuFlyoutItemBase item in menu.Items)
        {
            if (item is MenuFlyoutItem flyoutItem && flyoutItem.Text == "copy remote address")
            {
                flyoutItem.IsEnabled = hasRemote;
            }
            else if (item is MenuFlyoutItem lookup && lookup.Text == "look up remote address")
            {
                lookup.IsEnabled = hasRemote && _menuRow?.Entry.IsLocalOnly == false;
            }
        }
    }

    private void OnCopyRow(object sender, RoutedEventArgs e) => ViewModel.CopyRowCommand.Execute(_menuRow);

    private void OnCopyLocal(object sender, RoutedEventArgs e) => ViewModel.CopyLocalCommand.Execute(_menuRow);

    private void OnCopyRemote(object sender, RoutedEventArgs e) => ViewModel.CopyRemoteCommand.Execute(_menuRow);

    private void OnLookupRemote(object sender, RoutedEventArgs e) => ViewModel.LookupRemoteCommand.Execute(_menuRow);

    private void OnShowProcess(object sender, RoutedEventArgs e) => ViewModel.ShowProcessCommand.Execute(_menuRow);
}
