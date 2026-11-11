using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using NetTrace.ViewModels;

namespace NetTrace.Views;

public sealed partial class ProcessesPage : HostedPage
{
    private bool _syncing;

    public ProcessesPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public ProcessesViewModel ViewModel { get; } = App.Host.ProcessExplorer;

    protected override PageViewModel PageModel => ViewModel;

    private void OnProcessSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        // list reordering can transiently clear the selection; ignore that and keep the viewmodel's choice
        if (ProcessList.SelectedItem is ProcessRowViewModel row)
        {
            ViewModel.SelectedProcess = row;
        }
        else if (e.RemovedItems.Count > 0 && e.AddedItems.Count == 0 && ViewModel.SelectedProcess is not null
                 && ViewModel.Processes.Contains(ViewModel.SelectedProcess))
        {
            SyncSelection();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessesViewModel.SelectedProcess))
        {
            SyncSelection();
        }
    }

    private void SyncSelection()
    {
        if (ReferenceEquals(ProcessList.SelectedItem, ViewModel.SelectedProcess))
        {
            return;
        }

        _syncing = true;
        try
        {
            ProcessList.SelectedItem = ViewModel.SelectedProcess;
            if (ViewModel.SelectedProcess is not null)
            {
                ProcessList.ScrollIntoView(ViewModel.SelectedProcess);
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
