using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetTrace.Controls;

/// <summary>a clickable column header that shows the active sort column and direction.</summary>
public sealed partial class SortHeader : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SortHeader), new PropertyMetadata(string.Empty, OnStateChanged));

    public static readonly DependencyProperty ColumnProperty = DependencyProperty.Register(
        nameof(Column), typeof(string), typeof(SortHeader), new PropertyMetadata(string.Empty, OnStateChanged));

    public static readonly DependencyProperty ActiveColumnProperty = DependencyProperty.Register(
        nameof(ActiveColumn), typeof(string), typeof(SortHeader), new PropertyMetadata(string.Empty, OnStateChanged));

    public static readonly DependencyProperty DescendingProperty = DependencyProperty.Register(
        nameof(Descending), typeof(bool), typeof(SortHeader), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(SortHeader), new PropertyMetadata(null));

    public SortHeader()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
        UpdateVisuals();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Column
    {
        get => (string)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    public string ActiveColumn
    {
        get => (string)GetValue(ActiveColumnProperty);
        set => SetValue(ActiveColumnProperty, value);
    }

    public bool Descending
    {
        get => (bool)GetValue(DescendingProperty);
        set => SetValue(DescendingProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SortHeader)d).UpdateVisuals();

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (Command?.CanExecute(Column) == true)
        {
            Command.Execute(Column);
        }
    }

    private void UpdateVisuals()
    {
        bool active = !string.IsNullOrEmpty(Column) && Column == ActiveColumn;
        LabelText.Text = Label;
        Chevron.Glyph = Descending ? "" : "";
        VisualStateManager.GoToState(this, active ? "Active" : "Inactive", false);
        AutomationPropertiesHelper.SetName(HeaderButton, active ? $"{Label}, sorted {(Descending ? "descending" : "ascending")}" : $"sort by {Label}");
    }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}
