using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;
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

}
