using System.Globalization;
using Avalonia.Input;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

/// <summary>What a key of the gallery does</summary>
internal enum GalleryShortcut
{
    Search,
    SelectAll,
    Escape,
    Refresh,
    MoveToArchive,
    Favorite,
    Lock,
    Tags,
    Preview,
    View3D,
    FindInGame,
    Settings,
    Archive,
    Backups,
    Statistics,
    Authors,
    ShortcutList,
}

/// <summary>Groups of the list of keys (F1)</summary>
internal enum GalleryShortcutGroup
{
    SearchAndSelection,
    SelectedLiveries,
    Windows,
}

/// <param name="WhileTyping">
/// The key works while the cursor is in a text field. Letters, digits, the space, Delete and Ctrl+A belong to the field
/// </param>
internal sealed record GalleryShortcutKey(GalleryShortcut Action, GalleryShortcutGroup Group, Key Key, KeyModifiers Modifiers, bool WhileTyping);

/// <summary>
/// The gallery's keys: a fixed set. The main window asks <see cref="Match"/> for each key it gets, the menus, the
/// tooltips and the list of keys (F1) show <see cref="GestureText"/>
/// </summary>
internal static class GalleryShortcuts
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    public static IReadOnlyList<GalleryShortcutKey> All { get; } =
    [
        new(GalleryShortcut.Search, GalleryShortcutGroup.SearchAndSelection, Key.F, Ctrl, WhileTyping: true),
        new(GalleryShortcut.SelectAll, GalleryShortcutGroup.SearchAndSelection, Key.A, Ctrl, WhileTyping: false),
        new(GalleryShortcut.Escape, GalleryShortcutGroup.SearchAndSelection, Key.Escape, KeyModifiers.None, WhileTyping: true),
        new(GalleryShortcut.Refresh, GalleryShortcutGroup.SearchAndSelection, Key.F5, KeyModifiers.None, WhileTyping: true),

        new(GalleryShortcut.Favorite, GalleryShortcutGroup.SelectedLiveries, Key.F, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.Lock, GalleryShortcutGroup.SelectedLiveries, Key.L, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.Tags, GalleryShortcutGroup.SelectedLiveries, Key.T, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.Preview, GalleryShortcutGroup.SelectedLiveries, Key.Space, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.View3D, GalleryShortcutGroup.SelectedLiveries, Key.D3, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.FindInGame, GalleryShortcutGroup.SelectedLiveries, Key.G, KeyModifiers.None, WhileTyping: false),
        new(GalleryShortcut.MoveToArchive, GalleryShortcutGroup.SelectedLiveries, Key.Delete, KeyModifiers.None, WhileTyping: false),

        new(GalleryShortcut.Settings, GalleryShortcutGroup.Windows, Key.OemComma, Ctrl, WhileTyping: true),
        new(GalleryShortcut.Archive, GalleryShortcutGroup.Windows, Key.A, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.Backups, GalleryShortcutGroup.Windows, Key.B, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.Statistics, GalleryShortcutGroup.Windows, Key.S, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.Authors, GalleryShortcutGroup.Windows, Key.U, CtrlShift, WhileTyping: true),
        new(GalleryShortcut.ShortcutList, GalleryShortcutGroup.Windows, Key.F1, KeyModifiers.None, WhileTyping: true),
    ];

    public static GalleryShortcutKey Of(GalleryShortcut action) => All.First(s => s.Action == action);

    /// <summary>
    /// The action of a pressed key, or null. <paramref name="physicalKey"/> stands in for a letter or digit the
    /// layout gives no Latin key for, so F is F on any layout. The digits of the number pad count as digits
    /// </summary>
    /// <param name="typing">The cursor is in a text field</param>
    public static GalleryShortcut? Match(Key key, PhysicalKey physicalKey, KeyModifiers modifiers, bool typing)
    {
        key = Normalize(key, physicalKey);
        // Meta (the Windows key) is not a modifier of these keys: Win+L locks the computer, it does not lock a livery
        if ((modifiers & KeyModifiers.Meta) != 0) return null;
        foreach (var shortcut in All)
            if (shortcut.Key == key && shortcut.Modifiers == modifiers && (shortcut.WhileTyping || !typing))
                return shortcut.Action;
        return null;
    }

    private static Key Normalize(Key key, PhysicalKey physicalKey)
    {
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return Key.D0 + (key - Key.NumPad0);
        if (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9) return key;
        if (physicalKey is >= PhysicalKey.A and <= PhysicalKey.Z) return Key.A + (physicalKey - PhysicalKey.A);
        if (physicalKey is >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9) return Key.D0 + (physicalKey - PhysicalKey.Digit0);
        if (physicalKey == PhysicalKey.Comma && key is Key.None or Key.ImeProcessed) return Key.OemComma;
        return key;
    }

    /// <summary>
    /// The key as the keyboard of the interface's language names it, as in the menus of Windows: "Ctrl+Shift+A", "Del",
    /// "3"; in German "Strg+Umschalt+A" and "Entf"
    /// </summary>
    public static string GestureText(GalleryShortcut action) => GestureText(action, Strings.Culture);

    /// <param name="culture">The language of the key names; null, the interface's</param>
    public static string GestureText(GalleryShortcut action, CultureInfo? culture)
    {
        string Text(string name) => Strings.ResourceManager.GetString(name, culture) ?? name;
        var shortcut = Of(action);
        string key = shortcut.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (shortcut.Key - Key.D0))).ToString(),
            Key.OemComma => ",",
            Key.Delete => Text(nameof(Strings.KeyDelete)),
            Key.Escape => "Esc",
            Key.Space => Text(nameof(Strings.KeySpace)),
            _ => shortcut.Key.ToString(),
        };
        string modifiers = (shortcut.Modifiers.HasFlag(KeyModifiers.Control) ? Text(nameof(Strings.KeyCtrl)) + "+" : "")
            + (shortcut.Modifiers.HasFlag(KeyModifiers.Shift) ? Text(nameof(Strings.KeyShift)) + "+" : "");
        return modifiers + key;
    }

    /// <summary>What the key does, for the list of keys</summary>
    public static string Description(GalleryShortcut action) => action switch
    {
        GalleryShortcut.Search => Strings.ShortcutSearch,
        GalleryShortcut.SelectAll => Strings.ShortcutSelectAll,
        GalleryShortcut.Escape => Strings.ShortcutEscape,
        GalleryShortcut.Refresh => Strings.RefreshTooltip,
        GalleryShortcut.MoveToArchive => Strings.ContextMenuMoveToArchive,
        GalleryShortcut.Favorite => Strings.FavoriteToggleTooltip,
        GalleryShortcut.Lock => Strings.ShortcutLock,
        GalleryShortcut.Tags => Strings.ShortcutTags,
        GalleryShortcut.Preview => Strings.ViewPreviewTooltip,
        GalleryShortcut.View3D => Strings.View3DTooltip,
        GalleryShortcut.FindInGame => Strings.ContextMenuNavigateInGame,
        GalleryShortcut.Settings => Strings.SettingsDialogTitle,
        GalleryShortcut.Archive => Strings.MenuOpenArchive,
        GalleryShortcut.Backups => Strings.MenuOpenBackups,
        GalleryShortcut.Statistics => Strings.StatsToggleTooltip,
        GalleryShortcut.Authors => Strings.AuthorsButtonTooltip,
        GalleryShortcut.ShortcutList => Strings.ShortcutsTitle,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public static string GroupTitle(GalleryShortcutGroup group) => group switch
    {
        GalleryShortcutGroup.SearchAndSelection => Strings.ShortcutsGroupSearch,
        GalleryShortcutGroup.SelectedLiveries => Strings.ShortcutsGroupSelected,
        GalleryShortcutGroup.Windows => Strings.ShortcutsGroupWindows,
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };
}

/// <summary>A tooltip with the key of its action shown beside the text (the main window's data template draws it)</summary>
internal sealed record ShortcutTip(string Text, GalleryShortcut Action)
{
    public string Gesture => GalleryShortcuts.GestureText(Action);

    public override string ToString() => Text;
}
