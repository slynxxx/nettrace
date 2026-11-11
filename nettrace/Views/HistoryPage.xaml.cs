using Microsoft.UI.Xaml;
using NetTrace.Models;
using NetTrace.ViewModels;

namespace NetTrace.Views;

public sealed partial class HistoryPage : HostedPage
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    public HistoryViewModel ViewModel { get; } = App.Host.HistoryPage;

    protected override PageViewModel PageModel => ViewModel;

    private void OnCopyRecord(object sender, RoutedEventArgs e) =>
        ViewModel.CopyRecordCommand.Execute((sender as FrameworkElement)?.Tag as HistoryRecord);
}
