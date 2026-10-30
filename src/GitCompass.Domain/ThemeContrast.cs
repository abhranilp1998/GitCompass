using System.Globalization;

namespace GitCompass.Domain;

/// <summary>Theme colours in one place plus WCAG 2.x contrast maths, so tests can prove text stays readable (AA: 4.5:1 for normal text).</summary>
public static class ThemeContrast
{
    public const double AA = 4.5;

    /// <summary>Order: Window, Surface, Text, Muted, Border, Accent (placeholder; the live accent comes from Windows), Tint, Warning.</summary>
    public static readonly string[] Light = ["#F3F3F3", "#FFFFFF", "#1A1A1A", "#595959", "#E0E0E0", "#0067C0", "#E9E9E9", "#8A4B00"];
    public static readonly string[] Dark = ["#202020", "#2B2B2B", "#F4F4F4", "#BDBDBD", "#454545", "#4CC2FF", "#353535", "#F2BE67"];
    public static readonly string[] Names = ["WindowBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "BorderBrush", "AccentBrush", "TintBrush", "WarningBrush"];

    /// <summary>Liquid Glass preview colours: text, muted and accent foregrounds; page and scene backgrounds; translucent button fill (ARGB).</summary>
    public sealed record GlassPalette(string Text, string Muted, string Accent, string Page, string Scene, string Button);
    public static readonly GlassPalette GlassLight = new("#172A48", "#304664", "#244EC9", "#F4F7FC", "#D7E6F7", "#50FFFFFF");
    public static readonly GlassPalette GlassDark = new("#F8FAFF", "#E2EAF7", "#BEDBFF", "#14243A", "#152444", "#433C5A87");

    public static (byte A, byte R, byte G, byte B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 6) h = "FF" + h;
        if (h.Length != 8) throw new FormatException("Expected #RRGGBB or #AARRGGBB: " + hex);
        byte At(int i) => byte.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (At(0), At(2), At(4), At(6));
    }
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Flattens a translucent colour onto an opaque background (what you actually see).</summary>
    public static string Over(string foreground, string background)
    {
        var f = Parse(foreground); var b = Parse(background); var a = f.A / 255d;
        byte Mix(byte x, byte y) => (byte)Math.Round(x * a + y * (1 - a));
        return Hex(Mix(f.R, b.R), Mix(f.G, b.G), Mix(f.B, b.B));
    }
    public static double Luminance(string hex)
    {
        var c = Parse(hex);
        static double L(byte v) { var s = v / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }
    public static double Ratio(string a, string b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>Darkens (light theme) or lightens (dark theme) a colour, such as the user's Windows accent, until it reaches AA on every given background.</summary>
    public static string EnsureReadable(string color, bool dark, params string[] backgrounds)
    {
        var c = Parse(color); double r = c.R, g = c.G, b = c.B;
        for (var step = 0; step < 40 && backgrounds.Any(bg => Ratio(Hex((byte)r, (byte)g, (byte)b), bg) < AA); step++)
        {
            if (dark) { r += (255 - r) * 0.1; g += (255 - g) * 0.1; b += (255 - b) * 0.1; }
            else { r *= 0.9; g *= 0.9; b *= 0.9; }
        }
        return Hex((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
    }

    /// <summary>Every foreground/background pair the app draws as normal-size text, with its ratio.</summary>
    public static IEnumerable<(string Pair, double Ratio)> Pairs()
    {
        foreach (var (name, p) in new[] { ("Light", Light), ("Dark", Dark) })
            foreach (var fg in new[] { 2, 3, 5, 7 })
                foreach (var bg in new[] { 0, 1, 6 })
                    yield return ($"{name} {Names[fg]} on {Names[bg]}", Ratio(p[fg], p[bg]));
        foreach (var (name, g) in new[] { ("Glass light", GlassLight), ("Glass dark", GlassDark) })
            foreach (var (fgName, fg) in new[] { ("text", g.Text), ("muted", g.Muted), ("accent", g.Accent) })
                foreach (var (bgName, bg) in new[] { ("page", g.Page), ("scene", g.Scene), ("button on page", Over(g.Button, g.Page)), ("button on scene", Over(g.Button, g.Scene)) })
                    yield return ($"{name} {fgName} on {bgName}", Ratio(fg, bg));
    }
}
