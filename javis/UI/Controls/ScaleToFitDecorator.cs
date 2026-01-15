using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace javis.UI.Controls;

public sealed class ScaleToFitDecorator : Decorator
{
    public static readonly DependencyProperty AlignToTopLeftProperty =
        DependencyProperty.Register(nameof(AlignToTopLeft), typeof(bool), typeof(ScaleToFitDecorator),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

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

    public bool AlignToTopLeft
    {
        get => (bool)GetValue(AlignToTopLeftProperty);
        set => SetValue(AlignToTopLeftProperty, value);
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

    private static bool IsInDesignMode(DependencyObject obj)
    {
        try
        {
            return DesignerProperties.GetIsInDesignMode(obj);
        }
        catch
        {
            return false;
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
            return new Size(0, 0);

        // Design-time: avoid scaling transforms so the VS/Blend designer feels more "freeform".
        // Runtime behavior remains unchanged.
        if (IsInDesignMode(this))
        {
            try { Child.RenderTransform = Transform.Identity; } catch { }
            try { Child.ClearValue(RenderTransformOriginProperty); } catch { }
            Child.Measure(constraint);
            return Child.DesiredSize;
        }

        try
        {
            if (javis.Services.RuntimeSettings.Instance.DisableScaleToFit)
            {
                // Keep layout working but avoid fractional scaling blur by snapping scale to 5% steps.
                var uiScale0 = GetGlobalUiScale();
                var dw0 = DesignWidth / uiScale0;
                var dh0 = DesignHeight / uiScale0;
                Child.Measure(new Size(dw0, dh0));

                var scale0 = GetScale(constraint);
                var snap0 = Math.Round(scale0.scaleX / 0.05) * 0.05;
                if (snap0 <= 0) snap0 = 1;
                return new Size(dw0 * snap0, dh0 * snap0);
            }
        }
        catch { }

        var uiScale = GetGlobalUiScale();
        var dw = DesignWidth / uiScale;
        var dh = DesignHeight / uiScale;
        Child.Measure(new Size(dw, dh));

        var scale = GetScale(constraint);
        var s = SnapScale(scale.scaleX);
        return new Size(dw * s, dh * s);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null)
            return arrangeSize;

        // Design-time: arrange without applying scaling so the designer can directly manipulate sizes.
        if (IsInDesignMode(this))
        {
            try { Child.RenderTransform = Transform.Identity; } catch { }
            try { Child.ClearValue(RenderTransformOriginProperty); } catch { }
            Child.Arrange(new Rect(new Point(0, 0), arrangeSize));
            return arrangeSize;
        }

        try
        {
            if (javis.Services.RuntimeSettings.Instance.DisableScaleToFit)
            {
                // Keep layout working but avoid fractional scaling blur by snapping scale to 5% steps.
                var uiScale0 = GetGlobalUiScale();
                var dw0 = DesignWidth / uiScale0;
                var dh0 = DesignHeight / uiScale0;

                var scale0 = GetScale(arrangeSize);
                var snap0 = Math.Round(scale0.scaleX / 0.05) * 0.05;
                if (snap0 <= 0) snap0 = 1;

                var childSize0 = new Size(dw0, dh0);
                var scaledW0 = childSize0.Width * snap0;
                var scaledH0 = childSize0.Height * snap0;
                var offsetX0 = AlignToTopLeft ? 0 : (arrangeSize.Width - scaledW0) / 2.0;
                var offsetY0 = AlignToTopLeft ? 0 : (arrangeSize.Height - scaledH0) / 2.0;
                if (UseLayoutRounding)
                {
                    offsetX0 = Math.Round(offsetX0);
                    offsetY0 = Math.Round(offsetY0);
                }

                Child.RenderTransform = new ScaleTransform(snap0, snap0);
                Child.RenderTransformOrigin = new Point(0, 0);
                Child.Arrange(new Rect(new Point(offsetX0 / snap0, offsetY0 / snap0), childSize0));
                return arrangeSize;
            }
        }
        catch { }

        var uiScale = GetGlobalUiScale();
        var dw = DesignWidth / uiScale;
        var dh = DesignHeight / uiScale;

        var scale = GetScale(arrangeSize);
        var s = SnapScale(scale.scaleX);

        var childSize = new Size(dw, dh);

        var scaledW = childSize.Width * s;
        var scaledH = childSize.Height * s;

        var offsetX = AlignToTopLeft ? 0 : (arrangeSize.Width - scaledW) / 2.0;
        var offsetY = AlignToTopLeft ? 0 : (arrangeSize.Height - scaledH) / 2.0;

        if (UseLayoutRounding)
        {
            offsetX = Math.Round(offsetX);
            offsetY = Math.Round(offsetY);
        }

        Child.RenderTransform = new ScaleTransform(s, s);
        Child.RenderTransformOrigin = new Point(0, 0);

        Child.Arrange(new Rect(new Point(offsetX / s, offsetY / s), childSize));

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

        throw new InvalidOperationException("Unexpected stretch mode.");
    }

    private static double SnapScale(double s)
    {
        if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0) return 1;

        // If we're very close to 1.0, lock to 1.0 for sharper text.
        if (Math.Abs(s - 1.0) < 0.03) return 1;

        // Otherwise snap to 5% steps to avoid "micro scales" (e.g. 0.9732) that blur text.
        var snapped = Math.Round(s / 0.05) * 0.05;
        if (snapped <= 0) return 1;
        return snapped;
    }
}
