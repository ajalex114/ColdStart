using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ColdStart.Helpers;

namespace ColdStart.Tests.Helpers;

/// <summary>
/// Ensures a WPF <see cref="Application"/> instance exists for the test process.
/// ThemeManager.ApplyTheme writes to Application.Current.Resources,
/// which requires a live Application object.
/// </summary>
public sealed class WpfAppFixture : IDisposable
{
    private static readonly object s_lock = new();
    private static bool s_initialized;

    public WpfAppFixture()
    {
        lock (s_lock)
        {
            if (s_initialized) return;
            if (Application.Current == null)
            {
                var ready = new ManualResetEventSlim(false);
                var thread = new Thread(() =>
                {
                    _ = new Application();
                    ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.Wait();
            }
            s_initialized = true;
        }
    }

    public void Dispose() { }
}

public class ThemeManagerTests : IClassFixture<WpfAppFixture>
{
    #region Initial state

    [Fact]
    public void InitialTheme_IsDark_WithCorrectLabelAndIcon()
    {
        var tm = new ThemeManager();

        Assert.Equal(ThemeMode.Dark, tm.CurrentTheme);
        Assert.Equal("Dark", tm.ThemeLabel);
        Assert.Equal("🌙", tm.ThemeIcon);
    }

    #endregion

    #region CycleTheme

    [Fact]
    public void CycleTheme_FromDark_SwitchesToLight()
    {
        var tm = new ThemeManager();

        tm.CycleTheme();

        Assert.Equal(ThemeMode.Light, tm.CurrentTheme);
        Assert.Equal("Light", tm.ThemeLabel);
        Assert.Equal("☀️", tm.ThemeIcon);
    }

    [Fact]
    public void CycleTheme_FromLight_SwitchesToSystem()
    {
        var tm = new ThemeManager();
        tm.CycleTheme(); // Dark → Light

        tm.CycleTheme(); // Light → System

        Assert.Equal(ThemeMode.System, tm.CurrentTheme);
        Assert.Equal("System", tm.ThemeLabel);
        Assert.Equal("💻", tm.ThemeIcon);
    }

    [Fact]
    public void CycleTheme_FromSystem_SwitchesBackToDark()
    {
        var tm = new ThemeManager();
        tm.CycleTheme(); // Dark → Light
        tm.CycleTheme(); // Light → System

        tm.CycleTheme(); // System → Dark

        Assert.Equal(ThemeMode.Dark, tm.CurrentTheme);
        Assert.Equal("Dark", tm.ThemeLabel);
        Assert.Equal("🌙", tm.ThemeIcon);
    }

    #endregion

    #region ApplyTheme

    [Fact]
    public void ApplyTheme_Light_SetsBrushesToLightColors()
    {
        var tm = new ThemeManager();
        tm.CurrentTheme = ThemeMode.Light;

        tm.ApplyTheme();

        var bg = Assert.IsType<SolidColorBrush>(tm.Bg);
        Assert.Equal(Color.FromRgb(0xf5, 0xf6, 0xfa), bg.Color);
    }

    #endregion

    #region B() helper

    [Fact]
    public void B_CreatesSolidColorBrush_WithCorrectColor()
    {
        var brush = ThemeManager.B("#ff0000");

        var scb = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(Colors.Red, scb.Color);
    }

    #endregion

    #region PropertyChanged

    [Fact]
    public void CycleTheme_RaisesPropertyChanged_ForThemeProperties()
    {
        var tm = new ThemeManager();
        var changed = new List<string?>();
        tm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        tm.CycleTheme();

        Assert.Contains("CurrentTheme", changed);
        Assert.Contains("ThemeLabel", changed);
        Assert.Contains("ThemeIcon", changed);
    }

    #endregion
}
