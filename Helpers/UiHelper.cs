using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ColdStart.Helpers;

/// <summary>
/// Provides factory methods for creating styled WPF UI elements.
/// All methods require a <see cref="ThemeManager"/> instance that supplies the current color palette.
/// </summary>
public static class UiHelper
{
    /// <summary>
    /// Creates a styled <see cref="TextBlock"/> with the specified appearance.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="text">The text content (must not be <see langword="null"/>).</param>
    /// <param name="size">Font size in device-independent pixels. Must be positive.</param>
    /// <param name="fg">Foreground brush; defaults to <see cref="ThemeManager.Text"/>.</param>
    /// <param name="weight">Font weight; defaults to <see cref="FontWeights.Normal"/>.</param>
    /// <param name="margin">Optional margin.</param>
    /// <param name="align">Horizontal alignment; defaults to <see cref="HorizontalAlignment.Left"/>.</param>
    /// <returns>A configured <see cref="TextBlock"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/> or <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="size"/> is not positive.
    /// </exception>
    public static TextBlock Txt(
        ThemeManager theme,
        string text,
        double size = 13,
        Brush? fg = null,
        FontWeight? weight = null,
        Thickness? margin = null,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(text);
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Font size must be positive.");

        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = fg ?? theme.Text,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }

    /// <summary>
    /// Creates a styled card <see cref="Border"/> with rounded corners, padding, and theme colors.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <returns>An empty card border ready to accept a <see cref="Border.Child"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/> is <see langword="null"/>.
    /// </exception>
    public static Border Card(ThemeManager theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return new Border
        {
            Background = theme.Surface,
            BorderBrush = theme.Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(0, 0, 0, 14),
        };
    }

    /// <summary>
    /// Creates a statistics card displaying a label, primary value, and optional sub-text.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="label">The card header label (must not be <see langword="null"/>).</param>
    /// <param name="value">The primary display value (must not be <see langword="null"/>).</param>
    /// <param name="sub">Supplementary text shown below the value (must not be <see langword="null"/>).</param>
    /// <param name="color">The foreground brush for the primary value (must not be <see langword="null"/>).</param>
    /// <returns>A fully composed stat card.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is <see langword="null"/>.
    /// </exception>
    public static Border StatCard(ThemeManager theme, string label, string value, string sub, Brush color)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(sub);
        ArgumentNullException.ThrowIfNull(color);

        var card = new Border
        {
            Background = theme.Surface,
            BorderBrush = theme.Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(Txt(theme, label.ToUpper(), 10, theme.Dim, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
        stack.Children.Add(Txt(theme, value, 26, color, FontWeights.Bold));
        if (!string.IsNullOrEmpty(sub))
            stack.Children.Add(Txt(theme, sub, 11, theme.Dim, margin: new Thickness(0, 2, 0, 0)));
        card.Child = stack;
        return card;
    }

    /// <summary>
    /// Creates a selectable filter chip with an optional colored dot indicator.
    /// The chip visually reflects whether it matches <paramref name="currentValue"/>.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="label">The chip label text (must not be <see langword="null"/>).</param>
    /// <param name="currentValue">The currently selected filter value used to determine active state (must not be <see langword="null"/>).</param>
    /// <param name="dotColor">Optional colored dot shown before the label; ignored when label is <c>"All"</c>.</param>
    /// <param name="onSelect">Callback invoked with <paramref name="label"/> when the chip is clicked (must not be <see langword="null"/>).</param>
    /// <returns>The filter chip element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/>, <paramref name="label"/>, <paramref name="currentValue"/>,
    /// or <paramref name="onSelect"/> is <see langword="null"/>.
    /// </exception>
    public static UIElement FilterChip(ThemeManager theme, string label, string currentValue, Brush? dotColor, Action<string> onSelect)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(currentValue);
        ArgumentNullException.ThrowIfNull(onSelect);

        bool isActive = label == currentValue;
        var chip = CreateChipBorder(theme, isActive);
        var inner = CreateChipContent(theme, label, dotColor, isActive);
        chip.Child = inner;
        AttachChipBehavior(theme, chip, isActive, label, onSelect);
        return chip;
    }

    /// <summary>
    /// Creates a themed <see cref="Button"/> with rounded corners.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="text">The button label (must not be <see langword="null"/>).</param>
    /// <param name="click">Optional click handler.</param>
    /// <returns>A styled button.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/> or <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static Button MakeButton(ThemeManager theme, string text, RoutedEventHandler? click = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(text);

        var btn = new Button
        {
            Content = text,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = theme.Text,
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0),
        };
        btn.Template = CreateButtonTemplate(theme, theme.Surface2);
        if (click != null) btn.Click += click;
        return btn;
    }

    /// <summary>
    /// Creates a <see cref="ControlTemplate"/> for a <see cref="Button"/> with the specified background.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="bg">The background brush for the button chrome (must not be <see langword="null"/>).</param>
    /// <returns>A button control template with rounded corners and a border.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/> or <paramref name="bg"/> is <see langword="null"/>.
    /// </exception>
    public static ControlTemplate CreateButtonTemplate(ThemeManager theme, Brush bg)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(bg);

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, bg);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(16, 8, 16, 8));
        border.SetValue(Border.BorderBrushProperty, theme.Bdr);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;
        return template;
    }

    /// <summary>
    /// Creates a two-column detail row with a dimmed label and a primary-colored value.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="label">The row label (must not be <see langword="null"/>).</param>
    /// <param name="value">The row value (must not be <see langword="null"/>).</param>
    /// <returns>A grid element containing the label/value pair.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/>, <paramref name="label"/>, or <paramref name="value"/>
    /// is <see langword="null"/>.
    /// </exception>
    public static UIElement DetailRow(ThemeManager theme, string label, string value)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = Txt(theme, label, 12, theme.Dim); Grid.SetColumn(lbl, 0);
        var val = Txt(theme, value, 12, theme.Text); Grid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        return grid;
    }

    /// <summary>
    /// Creates a centered loading indicator with a spinner emoji and a message.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="msg">The loading message text (must not be <see langword="null"/>).</param>
    /// <returns>A vertically stacked loader element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/> or <paramref name="msg"/> is <see langword="null"/>.
    /// </exception>
    public static UIElement Loader(ThemeManager theme, string msg)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(msg);

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 60),
        };
        stack.Children.Add(Txt(theme, "⏳", 28, align: HorizontalAlignment.Center));
        stack.Children.Add(Txt(theme, msg, 14, theme.Dim, margin: new Thickness(0, 10, 0, 0),
            align: HorizontalAlignment.Center));
        return stack;
    }

    /// <summary>
    /// Creates a small colored dot followed by a label, typically used in chart legends.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="color">The dot fill brush (must not be <see langword="null"/>).</param>
    /// <param name="label">The legend label text (must not be <see langword="null"/>).</param>
    /// <returns>A horizontal panel with the dot and label.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/>, <paramref name="color"/>, or <paramref name="label"/>
    /// is <see langword="null"/>.
    /// </exception>
    public static UIElement LegendDot(ThemeManager theme, Brush color, string label)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(label);

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        sp.Children.Add(new Border
        {
            Background = color,
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(Txt(theme, label, 12, theme.Dim));
        return sp;
    }

    /// <summary>
    /// Creates a keyboard shortcut hint element consisting of a styled key badge and a label.
    /// </summary>
    /// <param name="theme">The current theme manager (must not be <see langword="null"/>).</param>
    /// <param name="key">The key text (e.g., <c>"Ctrl+R"</c>; must not be <see langword="null"/>).</param>
    /// <param name="label">Description of what the shortcut does (must not be <see langword="null"/>).</param>
    /// <returns>A horizontal panel with the key badge and label.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="theme"/>, <paramref name="key"/>, or <paramref name="label"/>
    /// is <see langword="null"/>.
    /// </exception>
    public static UIElement ShortcutHint(ThemeManager theme, string key, string label)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(label);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var keyBadge = new Border
        {
            Background = theme.Surface2,
            BorderBrush = theme.Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 4, 0),
            Child = Txt(theme, key, 10, theme.Dim, FontWeights.SemiBold),
        };
        panel.Children.Add(keyBadge);
        panel.Children.Add(Txt(theme, label, 11, theme.Dim));
        return panel;
    }

    // ── Private helpers for FilterChip (keeps indentation ≤ 3 levels) ──

    /// <summary>
    /// Creates the outer border element for a filter chip.
    /// </summary>
    private static Border CreateChipBorder(ThemeManager theme, bool isActive)
    {
        return new Border
        {
            Background = isActive ? theme.AccentBg : Brushes.Transparent,
            BorderBrush = isActive ? theme.Accent : theme.Bdr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand,
        };
    }

    /// <summary>
    /// Creates the inner content (optional dot + label) for a filter chip.
    /// </summary>
    private static StackPanel CreateChipContent(ThemeManager theme, string label, Brush? dotColor, bool isActive)
    {
        var inner = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (dotColor != null && label != "All")
        {
            inner.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = dotColor,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var weight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        var fg = isActive ? theme.Accent : theme.Dim;
        inner.Children.Add(Txt(theme, label, 12, fg, weight));
        return inner;
    }

    /// <summary>
    /// Attaches hover and click behavior to a filter chip border.
    /// </summary>
    private static void AttachChipBehavior(ThemeManager theme, Border chip, bool isActive, string label, Action<string> onSelect)
    {
        chip.MouseEnter += (_, _) => { if (!isActive) chip.Background = theme.Surface; };
        chip.MouseLeave += (_, _) => { if (!isActive) chip.Background = Brushes.Transparent; };
        chip.MouseLeftButtonUp += (_, _) => onSelect(label);
    }
}
