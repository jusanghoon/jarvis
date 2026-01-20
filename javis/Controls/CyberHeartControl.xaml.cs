using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace javis.Controls;

public partial class CyberHeartControl : UserControl
{
    private DispatcherTimer? _animationTimer;
    private float _cycle = 0f; // 0 ~ 2PI

    private float _thinkingT = 0f; // 0..1 (smooth transition)

    private readonly SKColor _baseColorNormal = SKColors.Cyan;
    private readonly SKColor _baseColorThinking = SKColors.Purple;

    public static readonly DependencyProperty IsThinkingProperty =
        DependencyProperty.Register(
            nameof(IsThinking),
            typeof(bool),
            typeof(CyberHeartControl),
            new PropertyMetadata(false, OnIsThinkingChanged));

    public bool IsThinking
    {
        get => (bool)GetValue(IsThinkingProperty);
        set => SetValue(IsThinkingProperty, value);
    }

    public CyberHeartControl()
    {
        InitializeComponent();
        InitializeAnimation();
    }

    public void SetThinkingMode(bool isThinking) => IsThinking = isThinking;

    private static void OnIsThinkingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CyberHeartControl c)
            c.SkiaCanvas?.InvalidateVisual();
    }

    private void InitializeAnimation()
    {
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _animationTimer.Tick += (_, _) =>
        {
            _cycle += 0.05f;
            if (_cycle > MathF.PI * 2f) _cycle = 0f;

            var target = IsThinking ? 1f : 0f;
            _thinkingT += (target - _thinkingT) * 0.08f;

            SkiaCanvas.InvalidateVisual();
        };

        _animationTimer.Start();
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float baseRadius = Math.Min(info.Width, info.Height) / 3f;

        float breathingFactor = (MathF.Sin(_cycle) + 1f) / 2f;
        float currentRadius = baseRadius + (breathingFactor * 10f);

        var targetColor = Lerp(_baseColorNormal, _baseColorThinking, _thinkingT);

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = true;
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(centerX, centerY),
                currentRadius * 1.5f,
                new[] { targetColor.WithAlpha(150), targetColor.WithAlpha(0) },
                new[] { 0.0f, 1.0f },
                SKShaderTileMode.Clamp);

            canvas.DrawCircle(centerX, centerY, currentRadius * 1.5f, paint);
        }

        using (var paint = new SKPaint())
        {
            paint.IsAntialias = true;
            paint.Color = targetColor.WithAlpha(200);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 5;

            canvas.Save();
            canvas.RotateDegrees(_cycle * 20f, centerX, centerY);
            canvas.DrawCircle(centerX, centerY, currentRadius, paint);
            canvas.Restore();

            paint.StrokeWidth = 3;
            canvas.DrawCircle(centerX, centerY, currentRadius / 2f, paint);
        }
    }

    private static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte LerpByte(byte x, byte y) => (byte)(x + (y - x) * t);
        return new SKColor(
            LerpByte(a.Red, b.Red),
            LerpByte(a.Green, b.Green),
            LerpByte(a.Blue, b.Blue),
            LerpByte(a.Alpha, b.Alpha));
    }
}

