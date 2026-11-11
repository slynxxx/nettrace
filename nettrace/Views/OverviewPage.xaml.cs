using Microsoft.UI.Xaml.Controls;
using NetTrace.ViewModels;

namespace NetTrace.Views;

public sealed partial class OverviewPage : HostedPage
{
    public OverviewPage()
    {
        InitializeComponent();
    }

    public OverviewViewModel ViewModel { get; } = App.Host.Overview;

    protected override PageViewModel PageModel => ViewModel;

    private void OnRecentClick(object sender, ItemClickEventArgs e) =>
        ViewModel.ShowProcessCommand.Execute(e.ClickedItem as ConnectionRowViewModel);
}
