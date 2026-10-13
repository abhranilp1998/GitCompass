using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GitCompass.Desktop;

/// <summary>
/// Refracts a separately sampled background. Apply this to a glass background layer,
/// then place labels and interactive controls above it so text stays sharp.
/// </summary>
public sealed class LiquidRefractionEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> SharedShader = new(LoadShader);

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidRefractionEffect), 0,
            SamplingMode.Bilinear);

    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
        nameof(Strength), typeof(double), typeof(LiquidRefractionEffect),
        new UIPropertyMetadata(0.8, PixelShaderConstantCallback(0), CoerceStrength));

    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio), typeof(double), typeof(LiquidRefractionEffect),
        new UIPropertyMetadata(1.0, PixelShaderConstantCallback(1), CoerceAspectRatio));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(LiquidRefractionEffect),
        new UIPropertyMetadata(0.3, PixelShaderConstantCallback(2), CoerceRadius));

    /// <summary>Hardware shader support and a readable packaged shader are both required.</summary>
    public static bool IsAvailable => !SystemParameters.HighContrast &&
        (RenderCapability.Tier >> 16) >= 1 && RenderCapability.IsPixelShaderVersionSupported(2, 0) &&
        SharedShader.Value is not null;

    public LiquidRefractionEffect()
    {
        if (SharedShader.Value is { } shader)
            PixelShader = shader;

        UpdateShaderValue(InputProperty);
        UpdateShaderValue(StrengthProperty);
        UpdateShaderValue(AspectRatioProperty);
        UpdateShaderValue(RadiusProperty);
    }

    public Brush? Input
    {
        get => (Brush?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <summary>Refraction amount from zero to one.</summary>
    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    /// <summary>Glass layer width divided by its height.</summary>
    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    /// <summary>Corner radius divided by layer height, clamped between 0.001 and 0.5.</summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    private static object CoerceStrength(DependencyObject owner, object value) =>
        value is double number && double.IsFinite(number) ? Math.Clamp(number, 0, 1) : 0.8;

    private static object CoerceAspectRatio(DependencyObject owner, object value) =>
        value is double number && double.IsFinite(number) ? Math.Max(number, 0.01) : 1.0;

    private static object CoerceRadius(DependencyObject owner, object value) =>
        value is double number && double.IsFinite(number) ? Math.Clamp(number, 0.001, 0.5) : 0.3;

    private static PixelShader? LoadShader()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/GitCompass;component/shaders/liquid-refraction.ps",
                UriKind.Absolute);
            using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is null)
                return null;

            var shader = new PixelShader();
            shader.SetStreamSource(stream);
            shader.Freeze();
            return shader;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            NotSupportedException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }
}
