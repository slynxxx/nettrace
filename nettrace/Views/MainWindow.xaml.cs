using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using NetTrace.Helpers;
using NetTrace.Models;
using NetTrace.Services;
using Windows.UI;

namespace NetTrace.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly List<NavItem> _items =
    [
        new(AppPage.Overview, "overview", ""),
        new(AppPage.Connections, "connections", ""),
        new(AppPage.Processes, "processes", ""),
        new(AppPage.Dns, "dns lookup", ""),
        new(AppPage.History, "history", ""),
        new(AppPage.Settings, "settings", ""),
    ];

    private bool _syncingSelection;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        ConfigureWindow();

        _host.Dialogs.Attach(this);
        _host.Navigation.NavigationRequested += (_, request) => Navigate(request.Page, request.Parameter);
        _host.Settings.SettingChanged += OnSettingChanged;

        NavList.ItemsSource = _items;
        ApplyTheme();
        UpdateMonitorText();
        Navigate(_host.Settings.Current.StartupPage, null);
    }

    public void Navigate(AppPage page, object? parameter)
    {
        Type type = page switch
        {
            AppPage.Connections => typeof(ConnectionsPage),
            AppPage.Processes => typeof(ProcessesPage),
            AppPage.Dns => typeof(DnsPage),
            AppPage.History => typeof(HistoryPage),
            AppPage.Settings => typeof(SettingsPage),
            _ => typeof(OverviewPage),
        };

        if (ContentFrame.CurrentSourcePageType != type || parameter is not null)
        {
            ContentFrame.Navigate(type, parameter, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }

        _host.Navigation.Current = page;
        _syncingSelection = true;
        NavList.SelectedItem = _items.First(i => i.Page == page);
        _syncingSelection = false;
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || NavList.SelectedItem is not NavItem item)
        {
            return;
        }

        Navigate(item.Page, null);
    }

    private void ConfigureWindow()
    {
        AppWindow.Title = "nettrace";
        try
        {
            if (File.Exists(AppPaths.IconFile))
            {
                AppWindow.SetIcon(AppPaths.IconFile);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
        }

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        double scale = DisplayScale();
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1240 * scale), (int)(800 * scale)));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(900 * scale);
            presenter.PreferredMinimumHeight = (int)(600 * scale);
        }

        // center on the current display's work area
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            var work = area.WorkArea;
            AppWindow.Move(new Windows.Graphics.PointInt32(
                work.X + Math.Max(0, (work.Width - AppWindow.Size.Width) / 2),
                work.Y + Math.Max(0, (work.Height - AppWindow.Size.Height) / 2)));
        }
    }

    private double DisplayScale()
    {
        try
        {
            uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            return dpi > 0 ? dpi / 96d : 1d;
        }
        catch (Exception)
        {
            return 1d;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private void OnSettingChanged(object? sender, string name)
    {
        if (name == nameof(AppSettings.Theme))
        {
            DispatcherQueue.TryEnqueue(ApplyTheme);
        }
        else if (name == nameof(AppSettings.RefreshIntervalSeconds))
        {
            DispatcherQueue.TryEnqueue(UpdateMonitorText);
        }
    }

    private void UpdateMonitorText()
    {
        int seconds = _host.Settings.Current.RefreshIntervalSeconds;
        MonitorText.Text = seconds == 1 ? "monitoring  ·  every second" : $"monitoring  ·  every {seconds} s";
    }

    private void ApplyTheme()
    {
        bool dark = _host.Settings.Current.Theme == AppTheme.Dark;
        RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;

        AppWindowTitleBar bar = AppWindow.TitleBar;
        Color foreground = dark ? Color.FromArgb(0xFF, 0xED, 0xED, 0xED) : Color.FromArgb(0xFF, 0x11, 0x11, 0x13);
        Color inactive = dark ? Color.FromArgb(0xFF, 0x5C, 0x5C, 0x61) : Color.FromArgb(0xFF, 0x9A, 0x9A, 0xA0);
        Color hover = dark ? Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1D) : Color.FromArgb(0xFF, 0xEC, 0xEC, 0xEE);
        Color pressed = dark ? Color.FromArgb(0xFF, 0x22, 0x22, 0x26) : Color.FromArgb(0xFF, 0xE2, 0xE2, 0xE4);

        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = foreground;
    }
}
