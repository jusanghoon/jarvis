using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace javis.UI.Controls;

public sealed class ScaleToFitDecorator : Decorator
{
    public static readonly DependencyProperty DesignWidthProperty =
        DependencyProperty.Register(nameof(DesignWidth), typeof(double), typeof(ScaleToFitDecorator),
            new FrameworkPropertyMetadata(1200d, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty DesignHeightProperty =
        DependencyProperty.Register(nameof(DesignHeight), typeof(double), typeof(ScaleToFitDecorator),
            new FrameworkPropertyMetadata(720d, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(ScaleToFitDecorator),
            new FrameworkPropertyMetadata(Stretch.Uniform, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty UseLayoutRoundingProperty =
        DependencyProperty.Register(nameof(UseLayoutRounding), typeof(bool), typeof(ScaleToFitDecorator),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double DesignWidth
    {
        get => (double)GetValue(DesignWidthProperty);
        set => SetValue(DesignWidthProperty, value);
    }

    public double DesignHeight
    {
        get => (double)GetValue(DesignHeightProperty);
        set => SetValue(DesignHeightProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public bool UseLayoutRounding
    {
        get => (bool)GetValue(UseLayoutRoundingProperty);
        set => SetValue(UseLayoutRoundingProperty, value);
    }

    private static double GetGlobalUiScale()
    {
        try
        {
            var s = javis.Services.RuntimeSettings.Instance.UiScale;
            if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0) return 1;
            return s;
        }
        catch
        {
            return 1;
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
            return new Size(0, 0);

        var uiScale = GetGlobalUiScale();
        var dw = DesignWidth / uiScale;
        var dh = DesignHeight / uiScale;
        Child.Measure(new Size(dw, dh));

        var scale = GetScale(constraint);
        return new Size(dw * scale.scaleX, dh * scale.scaleY);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null)
            return arrangeSize;

        var uiScale = GetGlobalUiScale();
        var dw = DesignWidth / uiScale;
        var dh = DesignHeight / uiScale;

        var scale = GetScale(arrangeSize);

        var childSize = new Size(dw, dh);

        var scaledW = childSize.Width * scale.scaleX;
        var scaledH = childSize.Height * scale.scaleY;

        var offsetX = (arrangeSize.Width - scaledW) / 2.0;
        var offsetY = (arrangeSize.Height - scaledH) / 2.0;

        if (UseLayoutRounding)
        {
            offsetX = Math.Round(offsetX);
            offsetY = Math.Round(offsetY);
        }

        Child.RenderTransform = new ScaleTransform(scale.scaleX, scale.scaleY);
        Child.RenderTransformOrigin = new Point(0, 0);

        Child.Arrange(new Rect(new Point(offsetX / scale.scaleX, offsetY / scale.scaleY), childSize));

        return arrangeSize;
    }

    private (double scaleX, double scaleY) GetScale(Size available)
    {
        var uiScale = GetGlobalUiScale();
        var dw = DesignWidth / uiScale;
        var dh = DesignHeight / uiScale;

        if (dw <= 0 || dh <= 0)
            return (1, 1);

        if (double.IsInfinity(available.Width) || double.IsInfinity(available.Height) || available.Width <= 0 || available.Height <= 0)
            return (1, 1);

        var sx = available.Width / dw;
        var sy = available.Height / dh;

        switch (Stretch)
        {
            case Stretch.None:
                return (1, 1);
            case Stretch.Fill:
                return (sx, sy);
            case Stretch.UniformToFill:
            {
                var s = Math.Max(sx, sy);
                return (s, s);
            }
            case Stretch.Uniform:
            default:
            {
                var s = Math.Min(sx, sy);
                return (s, s);
            }
        }
    }
}
