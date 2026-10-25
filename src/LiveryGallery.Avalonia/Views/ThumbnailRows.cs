using Avalonia;
using Avalonia.Controls;
using LiveryGallery.Controller;

namespace LiveryGallery.Views;

/// <summary>
/// A row or card that shows a livery's preview: <c>views:ThumbnailRows.Track="True"</c> hands its attach, detach and
/// data context changes to <see cref="ThumbnailLifecycleController"/>, the same in every window
/// </summary>
internal sealed class ThumbnailRows : AvaloniaObject
{
    public static readonly AttachedProperty<bool> TrackProperty =
        AvaloniaProperty.RegisterAttached<ThumbnailRows, Control, bool>("Track");

    static ThumbnailRows() => TrackProperty.Changed.AddClassHandler<Control>(OnTrackChanged);

    public static bool GetTrack(Control control) => control.GetValue(TrackProperty);

    public static void SetTrack(Control control, bool value) => control.SetValue(TrackProperty, value);

    private static void OnTrackChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.AttachedToVisualTree -= OnAttached;
        control.DetachedFromVisualTree -= OnDetached;
        control.DataContextChanged -= OnDataContextChanged;
        if (e.NewValue is not true) return;
        control.AttachedToVisualTree += OnAttached;
        control.DetachedFromVisualTree += OnDetached;
        control.DataContextChanged += OnDataContextChanged;
    }

    private static async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnAttachedAsync(control);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Unhandled exception when a preview row was attached", ex);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) ThumbnailLifecycleController.OnDetached(control);
    }

    private static async void OnDataContextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnDataContextChangedAsync(control);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Unhandled exception when a preview row's data changed", ex);
        }
    }
}
