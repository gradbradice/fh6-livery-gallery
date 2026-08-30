using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;

namespace LiveryGallery.Views;

internal partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleBarText.Text = title;
        MessageText.Text = message;
        YesButton.Content = Strings.ButtonYes;
        NoButton.Content = Strings.ButtonNo;
    }

    // Нативного заголовка окна больше нет (WindowDecorations="BorderOnly" +
    // ExtendClientAreaToDecorationsHint="True" в ConfirmDialog.axaml) — красить
    // через DWM/WindowChromeHelper тут больше нечего, рисуем свой заголовок.
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    public static async Task<bool> AskAsync(Window owner, string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        return await dlg.ShowDialog<bool>(owner);
    }
}
