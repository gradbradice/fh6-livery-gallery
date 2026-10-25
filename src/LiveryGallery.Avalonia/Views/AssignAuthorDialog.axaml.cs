using Avalonia.Controls;
using Avalonia.Input;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>
/// "Whose livery is it?" for liveries whose own author id says nothing, one or several (one author for all). The result
/// is the chosen card, or null
/// </summary>
internal partial class AssignAuthorDialog : Window
{
    private readonly AssignAuthorViewModel _viewModel;
    private readonly Func<Window, Task<AuthorCard?>> _createCard;

    public AuthorCard? Result { get; private set; }

    /// <param name="entries">The liveries that get one author</param>
    /// <param name="choices">Every author they can be assigned to</param>
    /// <param name="hints">Who they may be by</param>
    /// <param name="cardOfTarget">The card a hint points to</param>
    /// <param name="createCard">Makes a new card (the edit dialog over this one); null when the user cancelled</param>
    /// <param name="skipped">Selected liveries left out because their id is an account's</param>
    public AssignAuthorDialog(IReadOnlyList<LiveryEntry> entries, IReadOnlyList<AuthorCard> choices,
        IReadOnlyList<AuthorHintView> hints, Func<string, AuthorCard?> cardOfTarget, Func<Window, Task<AuthorCard?>> createCard,
        int skipped = 0)
    {
        InitializeComponent();
        _createCard = createCard;
        _viewModel = new AssignAuthorViewModel(entries, choices, hints, cardOfTarget, skipped);
        DataContext = _viewModel;
        _viewModel.CloseRequested += card =>
        {
            Result = card;
            Close(card is not null);
        };
        _viewModel.NewCardRequested += () => _ = CreateCardAsync();

        Title = _viewModel.TitleText;
        TitleBarText.Text = _viewModel.TitleText;
        TitleText.Text = _viewModel.TitleText;
        SearchBox.PlaceholderText = Strings.AssignAuthorSearchWatermark;
        NewCardButton.Content = Strings.AssignAuthorNewCardButton;
        NoResultsText.Text = Strings.AssignAuthorNoResults;
        ConfirmCheckBox.Content = Strings.AssignAuthorConfirmCheckBox;
        SaveButton.Content = Strings.ButtonSave;
        CancelButton.Content = Strings.ButtonCancel;
    }

    private async Task CreateCardAsync()
    {
        try
        {
            if (await _createCard(this) is { } card) _viewModel.AddAndSelect(card);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to create an author card from the assign dialog", ex);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);
}
