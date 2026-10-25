using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

/// <summary>
/// The key of a menu item, shown right of its text: <c>views:ShortcutHint.Action="Favorite"</c>. Avalonia's own
/// <see cref="MenuItem.InputGesture"/> writes keys by their names ("D3", "OemComma"), so the item gets a header
/// template with <see cref="GalleryShortcuts.GestureText"/>. The key only shows: the main window runs it
/// </summary>
internal sealed class ShortcutHint : AvaloniaObject
{
    public static readonly AttachedProperty<GalleryShortcut?> ActionProperty =
        AvaloniaProperty.RegisterAttached<ShortcutHint, MenuItem, GalleryShortcut?>("Action");

    private static readonly Dictionary<GalleryShortcut, IDataTemplate> Templates = [];

    static ShortcutHint()
    {
        ActionProperty.Changed.AddClassHandler<MenuItem>((item, e) =>
            item.HeaderTemplate = e.NewValue is GalleryShortcut action ? HeaderTemplate(action) : null);
    }

    public static GalleryShortcut? GetAction(MenuItem item) => item.GetValue(ActionProperty);

    public static void SetAction(MenuItem item, GalleryShortcut? value) => item.SetValue(ActionProperty, value);

    private static IDataTemplate HeaderTemplate(GalleryShortcut action)
    {
        if (Templates.TryGetValue(action, out var template)) return template;
        template = new FuncDataTemplate<object?>((_, _) =>
        {
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, [!TextBlock.TextProperty] = new Binding(".") };
            var gesture = new TextBlock { Classes = { "menuGesture" } };
            // A menu joins the visual tree each time it opens: the key follows a change of language ("Strg" in German)
            gesture.AttachedToVisualTree += (_, _) => gesture.Text = GalleryShortcuts.GestureText(action);
            Grid.SetColumn(gesture, 1);
            return new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { text, gesture } };
        });
        Templates[action] = template;
        return template;
    }
}

/// <summary>
/// A tooltip text with the key of an action (<see cref="ShortcutTip"/>, drawn by the main window's data template):
/// <c>ToolTip.Tip="{Binding LockTooltip, Converter={x:Static views:ShortcutTipConverter.Instance}, ConverterParameter={x:Static services:GalleryShortcut.Lock}}"</c>
/// </summary>
internal sealed class ShortcutTipConverter : IValueConverter
{
    public static ShortcutTipConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } text && parameter is GalleryShortcut action ? new ShortcutTip(text, action) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
