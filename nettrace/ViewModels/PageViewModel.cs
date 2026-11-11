using CommunityToolkit.Mvvm.ComponentModel;

namespace NetTrace.ViewModels;

/// <summary>
/// a viewmodel that only consumes live data while its page is on screen.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    public bool IsActive { get; private set; }

    public void Activate(object? parameter)
    {
        if (!IsActive)
        {
            IsActive = true;
            OnActivated();
        }

        if (parameter is not null)
        {
            OnParameter(parameter);
        }
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            OnDeactivated();
        }
    }

    protected virtual void OnActivated()
    {
    }

    protected virtual void OnDeactivated()
    {
    }

    protected virtual void OnParameter(object parameter)
    {
    }
}
