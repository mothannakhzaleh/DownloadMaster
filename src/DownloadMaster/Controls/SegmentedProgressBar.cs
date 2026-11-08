using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DownloadMaster.Models;

namespace DownloadMaster.Controls;

/// <summary>
/// IDM-style multi-connection progress bar: a rounded track with one filled block per segment,
/// each growing from its segment start as that connection downloads. Bind
/// <see cref="Segments"/> to <c>DownloadItem.Segments</c>.
/// </summary>
public sealed class SegmentedProgressBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<SegmentProgress>?> SegmentsProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IReadOnlyList<SegmentProgress>?>(nameof(Segments));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(
            nameof(TrackBrush),
            new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(
            nameof(FillBrush),
            new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));

    /// <summary>Thin divider drawn at each segment boundary; null hides the dividers.</summary>
    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(
            nameof(SeparatorBrush),
            new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, CornerRadius>(nameof(CornerRadius), new CornerRadius(3));

    /// <summary>Height used when the layout does not set one.</summary>
    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, double>(nameof(BarHeight), 6);

    static SegmentedProgressBar()
    {
        AffectsRender<SegmentedProgressBar>(
            SegmentsProperty,
            TrackBrushProperty,
            FillBrushProperty,
            SeparatorBrushProperty,
            CornerRadiusProperty);
        AffectsMeasure<SegmentedProgressBar>(BarHeightProperty);
    }

    public IReadOnlyList<SegmentProgress>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double BarHeight
    {
        get => GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? BarHeight : Math.Min(BarHeight, availableSize.Height);
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var track = new RoundedRect(bounds, CornerRadius);
        if (TrackBrush is { } trackBrush)
            context.DrawRectangle(trackBrush, null, track);

        var segments = Segments;
        if (segments is not { Count: > 0 })
            return;

        using (context.PushClip(track))
        {
            var width = bounds.Width;
            if (FillBrush is { } fill)
            {
                foreach (var segment in segments)
                {
                    var start = Math.Clamp(segment.Start, 0, 1);
                    var end = Math.Clamp(segment.End, start, 1);
                    var filled = Math.Clamp(start + segment.Downloaded, start, end);
                    if (filled <= start)
                        continue;

                    // Snap to whole pixels so neighbouring finished blocks join without hairline gaps.
                    var x0 = Math.Floor(start * width);
                    var x1 = Math.Ceiling(filled * width);
                    context.FillRectangle(fill, new Rect(x0, 0, Math.Max(1, x1 - x0), bounds.Height));
                }
            }

            if (SeparatorBrush is { } separator && segments.Count > 1)
            {
                foreach (var segment in segments)
                {
                    if (segment.Start <= 0 || segment.Start >= 1)
                        continue;

                    var x = Math.Round(segment.Start * width);
                    context.FillRectangle(separator, new Rect(x, 0, 1, bounds.Height));
                }
            }
        }
    }
}
