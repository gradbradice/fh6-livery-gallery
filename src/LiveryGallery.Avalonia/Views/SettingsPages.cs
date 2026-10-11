using Avalonia.Media;

namespace LiveryGallery.Views;

/// <summary>The pages of the settings dialog, in the order of the list on the left</summary>
internal enum SettingsPage
{
    Appearance,
    Folders,
    Gallery,
    FindInGame,
    Server,
    Data,
}

/// <summary>One entry of the list on the left of the settings dialog</summary>
internal sealed record SettingsPageItem(SettingsPage Page, string Title, Geometry? Icon);
