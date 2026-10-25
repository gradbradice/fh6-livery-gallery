using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>"Free up room": closes with the copies to send to the archive, or null</summary>
internal partial class FreeSpaceDialog : Window
{
    private readonly FreeSpaceViewModel _viewModel;

    public FreeSpaceDialog(FreeSpaceViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = Strings.FreeSpaceTitle;
        TitleBarText.Text = Strings.FreeSpaceTitle;
        TitleText.Text = Strings.FreeSpaceTitle;
        NoteText.Text = Strings.FreeSpaceNote;
        TickAllButton.Content = Strings.ArchiveSelectAllButton;
        UntickAllButton.Content = Strings.ClearSelectionLabel;
        CancelButton.Content = viewModel.HasGroups ? Strings.ButtonCancel : Strings.ButtonClose;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void TickAllButton_Click(object? sender, RoutedEventArgs e) => _viewModel.SetAll(true);

    private void UntickAllButton_Click(object? sender, RoutedEventArgs e) => _viewModel.SetAll(false);

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        var chosen = _viewModel.ToArchive;
        if (chosen.Count > 0) Close(chosen);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        Close(null);
    }
}
