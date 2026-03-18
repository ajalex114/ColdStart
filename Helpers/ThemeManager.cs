using System;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ColdStart.Helpers;

/// <summary>
/// Identifies the application theme mode.
/// </summary>
public enum ThemeMode
{
    /// <summary>Dark color scheme.</summary>
    Dark,

    /// <summary>Light color scheme.</summary>
    Light,

    /// <summary>Follows the Windows system setting.</summary>
    System
}

/// <summary>
/// Manages application theme state and provides color palettes for the current theme.
/// All brush properties are observable so that UI elements can bind directly to them.
/// </summary>
public partial class ThemeManager : ObservableObject
{
    // ── Observable brush properties ──────────────────────────

    /// <summary>Primary background brush.</summary>
    [ObservableProperty] private Brush _bg = B("#0f1117");

    /// <summary>Card / elevated surface brush.</summary>
    [ObservableProperty] private Brush _surface = B("#1a1d27");

    /// <summary>Secondary surface brush (e.g., input fields, button backgrounds).</summary>
    [ObservableProperty] private Brush _surface2 = B("#242835");

    /// <summary>Border brush used on cards and separators.</summary>
    [ObservableProperty] private Brush _bdr = B("#2e3347");

    /// <summary>Primary text brush.</summary>
    [ObservableProperty] private Brush _text = B("#e4e6f0");

    /// <summary>Dimmed / secondary text brush.</summary>
    [ObservableProperty] private Brush _dim = B("#a0a4b8");

    /// <summary>Accent / highlight brush.</summary>
    [ObservableProperty] private Brush _accent = B("#7d9bff");

    /// <summary>Accent background brush (tinted surface behind accent elements).</summary>
    [ObservableProperty] private Brush _accentBg = B("#1c2540");

    /// <summary>Green indicator brush.</summary>
    [ObservableProperty] private Brush _green = B("#4ade80");

    /// <summary>Green background brush.</summary>
    [ObservableProperty] private Brush _greenBg = B("#163a28");

    /// <summary>Yellow indicator brush.</summary>
    [ObservableProperty] private Brush _yellow = B("#fcd34d");

    /// <summary>Yellow background brush.</summary>
    [ObservableProperty] private Brush _yellowBg = B("#3a3318");

    /// <summary>Red indicator brush.</summary>
    [ObservableProperty] private Brush _red = B("#f87171");

    /// <summary>Red background brush.</summary>
    [ObservableProperty] private Brush _redBg = B("#3a1818");

    /// <summary>Orange indicator brush.</summary>
    [ObservableProperty] private Brush _orange = B("#fb923c");

    // ── Theme metadata ──────────────────────────────────────

    /// <summary>The currently selected <see cref="ThemeMode"/>.</summary>
    [ObservableProperty] private ThemeMode _currentTheme = ThemeMode.Dark;

    /// <summary>Human-readable label for the current theme (e.g., "Dark", "Light", "System").</summary>
    [ObservableProperty] private string _themeLabel = "Dark";

    /// <summary>Emoji icon representing the current theme.</summary>
    [ObservableProperty] private string _themeIcon = "🌙";

    // ── Public API ──────────────────────────────────────────

    /// <summary>
    /// Applies the current <see cref="CurrentTheme"/> by setting all brush properties
    /// and updating <see cref="Application.Current"/> resources.
    /// When <see cref="ThemeMode.System"/> is selected the effective theme is detected
    /// from the Windows registry key
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>.
    /// </summary>
    public void ApplyTheme()
    {
        var effective = ResolveEffectiveTheme();
        ApplyBrushes(effective);
        UpdateApplicationResources();
        UpdateThemeMetadata();
    }

    /// <summary>
    /// Cycles the theme in the order Dark → Light → System → Dark, then applies the result.
    /// </summary>
    public void CycleTheme()
    {
        CurrentTheme = CurrentTheme switch
        {
            ThemeMode.Dark => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.System,
            _ => ThemeMode.Dark,
        };
        ApplyTheme();
    }

    /// <summary>
    /// Creates a frozen <see cref="SolidColorBrush"/> from a hexadecimal color string.
    /// </summary>
    /// <param name="hex">
    /// A hex color string such as <c>"#0f1117"</c>. Must not be <see langword="null"/> or empty.
    /// </param>
    /// <returns>A <see cref="SolidColorBrush"/> for the specified color.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="hex"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="hex"/> is empty or whitespace.
    /// </exception>
    public static Brush B(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("Hex color string must not be empty.", nameof(hex));

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    // ── Private helpers ─────────────────────────────────────

    /// <summary>
    /// Resolves <see cref="ThemeMode.System"/> to either Dark or Light
    /// by reading the Windows registry.
    /// </summary>
    private ThemeMode ResolveEffectiveTheme()
    {
        if (CurrentTheme != ThemeMode.System)
            return CurrentTheme;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return (val is int v && v == 0) ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Dark;
        }
    }

    /// <summary>
    /// Sets every brush property according to the resolved theme.
    /// </summary>
    private void ApplyBrushes(ThemeMode effective)
    {
        if (effective == ThemeMode.Light)
        {
            ApplyLightBrushes();
        }
        else
        {
            ApplyDarkBrushes();
        }
    }

    private void ApplyLightBrushes()
    {
        Bg = B("#f5f6fa"); Surface = B("#ffffff"); Surface2 = B("#ecedf3");
        Bdr = B("#cdd0dc"); Text = B("#1a1d27"); Dim = B("#555972");
        Accent = B("#3b5de7"); AccentBg = B("#e0e6ff");
        Green = B("#15803d"); GreenBg = B("#d1fae5");
        Yellow = B("#a16207"); YellowBg = B("#fef3c7");
        Red = B("#b91c1c"); RedBg = B("#fee2e2");
        Orange = B("#c2410c");
    }

    private void ApplyDarkBrushes()
    {
        Bg = B("#0f1117"); Surface = B("#1a1d27"); Surface2 = B("#242835");
        Bdr = B("#2e3347"); Text = B("#e4e6f0"); Dim = B("#a0a4b8");
        Accent = B("#7d9bff"); AccentBg = B("#1c2540");
        Green = B("#4ade80"); GreenBg = B("#163a28");
        Yellow = B("#fcd34d"); YellowBg = B("#3a3318");
        Red = B("#f87171"); RedBg = B("#3a1818");
        Orange = B("#fb923c");
    }

    /// <summary>
    /// Pushes the current brush values into <see cref="Application.Current.Resources"/>
    /// so XAML-bound elements pick up the new theme.
    /// </summary>
    private void UpdateApplicationResources()
    {
        var res = Application.Current.Resources;
        res["BgBrush"] = Bg;
        res["SurfaceBrush"] = Surface;
        res["Surface2Brush"] = Surface2;
        res["BorderBrush"] = Bdr;
        res["TextBrush"] = Text;
        res["TextDimBrush"] = Dim;
        res["AccentBrush"] = Accent;
        res["GreenBrush"] = Green;
        res["YellowBrush"] = Yellow;
        res["RedBrush"] = Red;
        res["OrangeBrush"] = Orange;
    }

    /// <summary>
    /// Updates the human-readable <see cref="ThemeLabel"/> and <see cref="ThemeIcon"/>
    /// properties based on <see cref="CurrentTheme"/>.
    /// </summary>
    private void UpdateThemeMetadata()
    {
        ThemeLabel = CurrentTheme switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.Dark => "Dark",
            _ => "System",
        };

        ThemeIcon = CurrentTheme switch
        {
            ThemeMode.Light => "☀️",
            ThemeMode.Dark => "🌙",
            _ => "💻",
        };
    }
}
