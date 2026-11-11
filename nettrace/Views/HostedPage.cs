using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetTrace.ViewModels;

namespace NetTrace.Views;

/// <summary>
/// base page: cached by the frame and activates its viewmodel only while visible,
/// so hidden pages do not consume monitoring updates.
/// </summary>
public abstract partial class HostedPage : Page
{
    protected HostedPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected abstract PageViewModel PageModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PageModel.Activate(e.Parameter);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        PageModel.Deactivate();
    }
}
