using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetTrace.Helpers;
using NetTrace.Models;
using Windows.Foundation;

namespace NetTrace.Controls;

/// <summary>
/// a two-line realtime throughput graph. download is drawn in the primary tone,
/// upload in the secondary tone. the scale adapts to the visible peak.
/// </summary>
public sealed partial class TrafficGraph : UserControl
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(TrafficSample[]), typeof(TrafficGraph), new PropertyMetadata(null, (d, _) => ((TrafficGraph)d).Redraw()));

    public static readonly DependencyProperty VisibleSamplesProperty = DependencyProperty.Register(
        nameof(VisibleSamples), typeof(int), typeof(TrafficGraph), new PropertyMetadata(120, (d, _) => ((TrafficGraph)d).Redraw()));

    public TrafficGraph()
    {
        InitializeComponent();
    }

    public TrafficSample[]? Samples
    {
        get => (TrafficSample[]?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int VisibleSamples
    {
        get => (int)GetValue(VisibleSamplesProperty);
        set => SetValue(VisibleSamplesProperty, value);
    }

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        double width = PlotArea.ActualWidth;
        double height = PlotArea.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        const double top = 18; // room for the scale label
        double plotHeight = Math.Max(1, height - top - 1);
        double bottom = height - 1;

        SetLine(GridTop, 0, top, width, top);
        SetLine(GridMiddle, 0, top + (plotHeight / 2), width, top + (plotHeight / 2));
        SetLine(GridBottom, 0, bottom, width, bottom);
        MidLabel.Margin = new Thickness(0, top + (plotHeight / 2) + 2, 0, 0);

        TrafficSample[] samples = Samples ?? [];
        int capacity = Math.Max(2, VisibleSamples);
        int count = Math.Min(samples.Length, capacity);
        int start = samples.Length - count;

        double peak = 0;
        for (int i = start; i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Max(samples[i].ReceivedPerSecond, samples[i].SentPerSecond));
        }

        double scale = NiceCeiling(Math.Max(peak, 1024));
        MaxLabel.Text = ByteFormatter.FormatRate(scale);
        MidLabel.Text = ByteFormatter.FormatRate(scale / 2);
        WaitingLabel.Visibility = count < 2 ? Visibility.Visible : Visibility.Collapsed;

        if (count >= 2)
        {
            TimeSpan span = samples[^1].Timestamp - samples[start].Timestamp;
            StartLabel.Text = span.TotalMinutes >= 1
                ? $"{span.TotalMinutes:0.#} min ago"
                : $"{span.TotalSeconds:0} s ago";
        }
        else
        {
            StartLabel.Text = string.Empty;
        }

        double step = width / (capacity - 1);
        double offset = width - ((count - 1) * step);
        Fill(DownloadLine.Points, samples, start, count, step, offset, bottom, plotHeight, scale, download: true);
        Fill(UploadLine.Points, samples, start, count, step, offset, bottom, plotHeight, scale, download: false);
    }

    private static void Fill(
        Microsoft.UI.Xaml.Media.PointCollection points,
        TrafficSample[] samples,
        int start,
        int count,
        double step,
        double offset,
        double bottom,
        double plotHeight,
        double scale,
        bool download)
    {
        points.Clear();
        if (count < 2)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            TrafficSample sample = samples[start + i];
            double value = download ? sample.ReceivedPerSecond : sample.SentPerSecond;
            double y = bottom - (Math.Min(value, scale) / scale * plotHeight);
            points.Add(new Point(offset + (i * step), Math.Round(y, 1)));
        }
    }

    private static void SetLine(Microsoft.UI.Xaml.Shapes.Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = Math.Round(y1) + 0.5;
        line.X2 = x2;
        line.Y2 = Math.Round(y2) + 0.5;
    }

    /// <summary>rounds up to 1, 2 or 5 times a power of 1024-friendly decimal step.</summary>
    private static double NiceCeiling(double value)
    {
        double unit = 1;
        while (value >= unit * 1024)
        {
            unit *= 1024;
        }

        double scaled = value / unit;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(scaled)));
        double normalized = scaled / magnitude;
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude * unit;
    }
}
