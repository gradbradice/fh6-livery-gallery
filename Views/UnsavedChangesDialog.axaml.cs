using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal enum UnsavedChangesChoice
{
    Back,
    SaveAndExit,
    ExitWithoutSaving
}

internal partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleBarText.Text = title;
        MessageText.Text = message;
        BackButton.Content = Strings.ButtonBack;
        SaveAndExitButton.Content = Strings.ButtonSaveAndExit;
        ExitWithoutSavingButton.Content = Strings.ButtonExitWithoutSaving;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void BackButton_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Back);

    private void SaveAndExitButton_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.SaveAndExit);

    private void ExitWithoutSavingButton_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.ExitWithoutSaving);

    public static async Task<UnsavedChangesChoice> AskAsync(Window owner, string title, string message)
    {
        var dlg = new UnsavedChangesDialog(title, message);
        return await dlg.ShowDialog<UnsavedChangesChoice>(owner);
    }
}
