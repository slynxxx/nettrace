using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetTrace.Controls;

/// <summary>shared empty, loading and error presentation for every page.</summary>
public sealed partial class EmptyState : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(EmptyState), new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null, OnChanged));

    public EmptyState()
    {
        InitializeComponent();
        Update();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((EmptyState)d).Update();

    private void Update()
    {
        Icon.Glyph = Glyph;
        Icon.Visibility = IsLoading ? Visibility.Collapsed : Visibility.Visible;
        Spinner.IsActive = IsLoading;
        Spinner.Visibility = IsLoading ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = Title;
        MessageText.Text = Message;
        MessageText.Visibility = string.IsNullOrEmpty(Message) ? Visibility.Collapsed : Visibility.Visible;
        ActionButton.Content = ActionText;
        ActionButton.Visibility = ActionCommand is not null && !string.IsNullOrEmpty(ActionText) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (ActionCommand?.CanExecute(null) == true)
        {
            ActionCommand.Execute(null);
        }
    }
}
