using NetTrace.ViewModels;

namespace NetTrace.Views;

public sealed partial class SettingsPage : HostedPage
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; } = App.Host.SettingsPage;

    protected override PageViewModel PageModel => ViewModel;
}
