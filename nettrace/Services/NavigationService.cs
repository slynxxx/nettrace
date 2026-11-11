using NetTrace.Models;

namespace NetTrace.Services;

public sealed class NavigationRequest(AppPage page, object? parameter) : EventArgs
{
    public AppPage Page { get; } = page;
    public object? Parameter { get; } = parameter;
}

/// <summary>lets viewmodels request page changes without referencing the window or frame.</summary>
public sealed class NavigationService
{
    public event EventHandler<NavigationRequest>? NavigationRequested;

    public AppPage Current { get; internal set; }

    public void Navigate(AppPage page, object? parameter = null) =>
        NavigationRequested?.Invoke(this, new NavigationRequest(page, parameter));
}
