using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace LiveryGallery.Views;

internal static class WindowExtensions
{
    public static void HandleTitleBarDrag(this Window window, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            window.BeginMoveDrag(e);
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
