using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Diagnostics;
using Path = Avalonia.Controls.Shapes.Path;

namespace LiveryGallery.Views;

internal partial class AuthorsDialog : Window
{
    private readonly AuthorCardService _authorCardService;
    private readonly Func<Task<List<LiveryEntry>>> _refreshEntries;
    private List<LiveryEntry> _allEntries;
    private string _searchQuery = "";

    public AuthorsDialog(AuthorCardService authorCardService, List<LiveryEntry> allEntries, Func<Task<List<LiveryEntry>>> refreshEntries)
    {
        InitializeComponent();
        _authorCardService = authorCardService;
        _allEntries = allEntries;
        _refreshEntries = refreshEntries;

        Title = Strings.AuthorsDialogTitle;
        TitleBarText.Text = Strings.AuthorsDialogTitle;
        TitleText.Text = Strings.AuthorsDialogTitle;
        AddCardButton.Content = Strings.AuthorsAddCardButton;
        EmptyText.Text = Strings.AuthorsEmptyHint;
        SearchBox.PlaceholderText = Strings.AuthorsSearchPlaceholder;

        RebuildList();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text ?? "";
        RebuildList();
    }

    private void RebuildList()
    {
        CardsPanel.Children.Clear();

        var allCards = _authorCardService.GetAll();
        var cards = allCards
            .Where(MatchesSearch)
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasAnyCards = allCards.Count > 0;
        EmptyText.IsVisible = cards.Count == 0;
        EmptyText.Text = !hasAnyCards ? Strings.AuthorsEmptyHint : Strings.AuthorsNoSearchResultsHint;
        CardsPanel.IsVisible = cards.Count > 0;

        foreach (var card in cards)
            CardsPanel.Children.Add(BuildCardRow(card));
    }

    private bool MatchesSearch(AuthorCard card)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery)) return true;
        if (card.DisplayName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)) return true;
        return card.Aliases.Any(a => a.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
    }

    private Border BuildCardRow(AuthorCard card)
    {
        var aliasSet = card.Aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var authorEntries = _allEntries.Where(e => aliasSet.Contains(e.AuthorRaw)).ToList();
        int favoritesCount = authorEntries.Count(e => e.IsFavorite);
        int duplicatesCount = authorEntries.Count(e => e.IsDuplicate);
        int possibleDuplicatesCount = authorEntries.Count(e => e.IsPossibleDuplicate);

        var root = new Border
        {
            Background = this.GetThemeBrush("SurfaceBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12),
        };

        var mainStack = new StackPanel();
        root.Child = mainStack;

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var nameText = new TextBlock
        {
            Text = card.DisplayName,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameText, 0);
        headerGrid.Children.Add(nameText);

        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Grid.SetColumn(buttonsPanel, 1);

        var editButton = new Button { Content = Strings.ButtonEdit, Padding = new Thickness(10, 4) };
        editButton.Classes.Add("secondary");
        editButton.Click += (_, _) => EditCard(card);
        buttonsPanel.Children.Add(editButton);

        var deleteButton = new Button { Content = Strings.ButtonDelete, Padding = new Thickness(10, 4) };
        deleteButton.Classes.Add("danger");
        deleteButton.Click += async (_, _) => await DeleteCardAsync(card);
        buttonsPanel.Children.Add(deleteButton);

        headerGrid.Children.Add(buttonsPanel);
        mainStack.Children.Add(headerGrid);

        if (card.Aliases.Count > 1)
        {
            mainStack.Children.Add(new TextBlock
            {
                Text = string.Format(Strings.AuthorCardAliasesFormat, string.Join(", ", card.Aliases)),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = this.GetThemeBrush("TextSecondaryBrush"),
            });
        }

        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            mainStack.Children.Add(new TextBlock
            {
                Text = card.Description,
                FontSize = 12.5,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (card.TwitterUrl is not null || card.YouTubeUrl is not null)
        {
            var linksPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            if (card.TwitterUrl is { } twitterUrl) linksPanel.Children.Add(BuildLinkCircle(twitterUrl, "IconTwitter", Strings.AuthorCardTwitterLabel));
            if (card.YouTubeUrl is { } youTubeUrl) linksPanel.Children.Add(BuildLinkCircle(youTubeUrl, "IconYoutube", Strings.AuthorCardYouTubeLabel));
            mainStack.Children.Add(linksPanel);
        }

        var statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 10, 0, 0) };
        statsPanel.Children.Add(BuildStatChip(Strings.AuthorCardLiveriesCountFormat, authorEntries.Count));
        statsPanel.Children.Add(BuildStatChip(Strings.AuthorCardFavoritesCountFormat, favoritesCount));
        statsPanel.Children.Add(BuildStatChip(Strings.AuthorCardDuplicatesCountFormat, duplicatesCount));
        statsPanel.Children.Add(BuildStatChip(Strings.AuthorCardPossibleDuplicatesCountFormat, possibleDuplicatesCount));
        mainStack.Children.Add(statsPanel);

        return root;
    }

    private Button BuildLinkCircle(string url, string iconKey, string tooltip)
    {
        var button = new Button
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(0),
        };
        button.Classes.Add("icon");
        ToolTip.SetTip(button, tooltip);
        button.Content = new Path
        {
            Data = this.GetThemeGeometry(iconKey),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Fill = this.GetThemeBrush("AccentBrush"),
        };
        button.Click += (_, _) => OpenUrl(url);
        return button;
    }

    private TextBlock BuildStatChip(string format, int count) => new()
    {
        Text = string.Format(format, count),
        FontSize = 11.5,
        Foreground = this.GetThemeBrush("TextSecondaryBrush"),
    };

    private static void OpenUrl(string url) => TrustedUrlLauncher.TryOpen(url);

    private void AddCardButton_Click(object? sender, RoutedEventArgs e) => EditCard(null);

    private async void EditCard(AuthorCard? existingCard)
    {
        string? excludeId = existingCard?.Id;
        var availableAliases = _allEntries
            .Select(e => e.AuthorRaw)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(a =>
            {
                var owner = _authorCardService.FindCardForAlias(a);
                return owner is null || owner.Id == excludeId;
            })
            .ToList();

        var dialog = new AuthorCardEditDialog(_authorCardService, availableAliases, existingCard);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved || dialog.Result is null) return;

        if (await _authorCardService.TrySave(dialog.Result))
        {
            _allEntries = await _refreshEntries();
            RebuildList();
        }
    }

    private async Task DeleteCardAsync(AuthorCard card)
    {
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.AuthorCardDeleteTitle, string.Format(Strings.AuthorCardDeleteMessage, card.DisplayName));
        if (!confirmed) return;

        await _authorCardService.Delete(card.Id);
        _allEntries = await _refreshEntries();
        RebuildList();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
