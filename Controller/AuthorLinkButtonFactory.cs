using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LiveryGallery.Services;
using Path = Avalonia.Controls.Shapes.Path;

namespace LiveryGallery.Controller;

internal static class AuthorLinkButtonFactory
{
    public static Button Build(Window owner, string url, string iconKey, string tooltip, double diameter, double iconSize)
    {
        var button = new Button
        {
            Width = diameter,
            Height = diameter,
            CornerRadius = new CornerRadius(diameter / 2),
            Padding = new Thickness(0),
        };
        button.Classes.Add("icon");
        ToolTip.SetTip(button, tooltip);
        button.Content = new Path
        {
            Data = owner.GetThemeGeometry(iconKey),
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform,
            Fill = owner.GetThemeBrush("AccentBrush"),
        };
        button.Click += (_, _) => TrustedUrlLauncher.TryOpen(url);
        return button;
    }
}
