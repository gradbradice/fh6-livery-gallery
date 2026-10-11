using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>Shown once after the first scan of a launch, when liveries appeared or disappeared while the gallery was closed</summary>
internal partial class ChangesSinceLastLaunchDialog : Window
{
    public ChangesSinceLastLaunchDialog(LiveryChanges changes)
    {
        InitializeComponent();
        DataContext = new ChangesSinceLastLaunchViewModel(changes);

        Title = Strings.ChangesDialogTitle;
        TitleBarText.Text = Strings.ChangesDialogTitle;
        DontShowAgainCheckBox.Content = Strings.ChangesDontShowAgainCheckBox;
        CloseDialogButton.Content = Strings.ButtonClose;
    }

    public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private async void LiveryRow_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnAttachedAsync(control);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in ChangesSinceLastLaunchDialog.LiveryRow_AttachedToVisualTree", ex);
        }
    }

    private void LiveryRow_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) ThumbnailLifecycleController.OnDetached(control);
    }

    private async void LiveryRow_DataContextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnDataContextChangedAsync(control);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in ChangesSinceLastLaunchDialog.LiveryRow_DataContextChanged", ex);
        }
    }
}
