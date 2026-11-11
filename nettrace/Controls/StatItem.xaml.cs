using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetTrace.Controls;

/// <summary>a label / value / caption triplet used for headline statistics.</summary>
public sealed partial class StatItem : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatItem), new PropertyMetadata(string.Empty, (d, e) => ((StatItem)d).LabelText.Text = (string)e.NewValue));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(StatItem), new PropertyMetadata(string.Empty, (d, e) => ((StatItem)d).ValueText.Text = (string)e.NewValue));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(StatItem), new PropertyMetadata(string.Empty, (d, e) => ((StatItem)d).CaptionText.Text = (string)e.NewValue));

    public StatItem()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }
}
