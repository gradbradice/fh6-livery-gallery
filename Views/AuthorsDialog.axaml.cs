using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class AuthorsDialog : Window
{
    private readonly AuthorCardService _authorCardService;
    private readonly AuthorsViewModel _viewModel;

    public AuthorsDialog(AuthorCardService authorCardService, List<LiveryEntry> allEntries, Func<Task<List<LiveryEntry>>> refreshEntries)
    {
        InitializeComponent();
        _authorCardService = authorCardService;
        _viewModel = new AuthorsViewModel(authorCardService, allEntries, refreshEntries);
        DataContext = _viewModel;

        _viewModel.EditCardRequested += async existingCard => await EditCardAsync(existingCard);
        _viewModel.DeleteCardRequested += async row => await DeleteCardAsync(row);

        Title = Strings.AuthorsDialogTitle;
        TitleBarText.Text = Strings.AuthorsDialogTitle;
        TitleText.Text = Strings.AuthorsDialogTitle;
        AddCardButton.Content = Strings.AuthorsAddCardButton;
        SearchBox.PlaceholderText = Strings.AuthorsSearchPlaceholder;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private async Task EditCardAsync(AuthorCard? existingCard)
    {
        var (availableAliases, nameToTags) = _viewModel.BuildAliasIndex(existingCard?.Id);

        var dialog = new AuthorCardEditDialog(_authorCardService, availableAliases, nameToTags, existingCard);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved || dialog.Result is null) return;

        await _viewModel.TrySaveCard(dialog.Result);
    }

    private async Task DeleteCardAsync(AuthorCardRowViewModel row)
    {
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.AuthorCardDeleteTitle, string.Format(Strings.AuthorCardDeleteMessage, row.DisplayName));
        if (!confirmed) return;

        await _viewModel.DeleteCardAsync(row.Card.Id);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
