using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NetTrace.Models;
using NetTrace.ViewModels;
using Windows.System;

namespace NetTrace.Views;

public sealed partial class DnsPage : HostedPage
{
    public DnsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus(FocusState.Programmatic);
    }

    public DnsViewModel ViewModel { get; } = App.Host.DnsLookup;

    protected override PageViewModel PageModel => ViewModel;

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.LookupCommand.CanExecute(null))
        {
            e.Handled = true;
            _ = ViewModel.LookupCommand.ExecuteAsync(null);
        }
        else if (e.Key == VirtualKey.Escape && ViewModel.IsBusy)
        {
            e.Handled = true;
            ViewModel.CancelCommand.Execute(null);
        }
    }

    private void OnRecentClick(object sender, ItemClickEventArgs e) =>
        ViewModel.RerunCommand.Execute(e.ClickedItem as DnsLookupResult);

    private void OnCopyRecord(object sender, RoutedEventArgs e) =>
        ViewModel.CopyRecordCommand.Execute((sender as FrameworkElement)?.Tag as DnsRecord);
}
