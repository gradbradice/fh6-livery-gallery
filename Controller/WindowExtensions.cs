using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace LiveryGallery.Controller;

internal static class WindowExtensions
{
    public static void HandleTitleBarDrag(this Window window, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            window.BeginMoveDrag(e);
    }

    public static void HandleTitleBarDragOrMaximize(this Window window, PointerPressedEventArgs e, TextBlock? maximizeIcon = null)
    {
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
            window.ToggleMaximize(maximizeIcon);
        else
            window.BeginMoveDrag(e);
    }

    public static void ToggleMaximize(this Window window, TextBlock? maximizeIcon = null)
    {
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        maximizeIcon?.Text = window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    public static IBrush GetThemeBrush(this Window window, string key)
    {
        if (Application.Current?.TryGetResource(key, window.ActualThemeVariant, out var res) == true && res is IBrush brush)
            return brush;
        return Brushes.Gray;
    }

    public static Geometry? GetThemeGeometry(this Window window, string key) =>
        Application.Current?.TryGetResource(key, window.ActualThemeVariant, out var res) == true && res is Geometry geometry
            ? geometry
            : null;
}
