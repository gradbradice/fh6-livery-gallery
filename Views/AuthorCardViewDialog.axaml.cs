using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;

namespace LiveryGallery.Views;

internal partial class AuthorCardViewDialog : Window
{
    public AuthorCardViewDialog(AuthorCard card, List<LiveryEntry> allEntries)
    {
        InitializeComponent();

        Title = card.DisplayName;
        TitleBarText.Text = card.DisplayName;
        OkButton.Content = Strings.ButtonOk;

        var aliasSet = card.Aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var authorEntries = allEntries.Where(e => aliasSet.Contains(e.AuthorRaw)).ToList();

        var nameText = new TextBlock
        {
            Text = card.DisplayName,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
        };
        ContentPanel.Children.Add(nameText);

        if (card.Aliases.Count > 1)
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = string.Format(Strings.AuthorCardAliasesFormat, string.Join(", ", card.Aliases)),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = this.GetThemeBrush("TextSecondaryBrush"),
            });
        }

        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = card.Description,
                FontSize = 13,
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (card.TwitterUrl is not null || card.YouTubeUrl is not null)
        {
            var linksPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
            if (card.TwitterUrl is { } twitterUrl) linksPanel.Children.Add(
                AuthorLinkButtonFactory.Build(this, twitterUrl, "IconTwitter", Strings.AuthorCardTwitterLabel, diameter: 30, iconSize: 15));
            if (card.YouTubeUrl is { } youTubeUrl) linksPanel.Children.Add(
                AuthorLinkButtonFactory.Build(this, youTubeUrl, "IconYoutube", Strings.AuthorCardYouTubeLabel, diameter: 30, iconSize: 15));
            ContentPanel.Children.Add(linksPanel);
        }

        var statsGrid = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        statsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        statsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var liveriesText = BuildStatLine(Strings.AuthorCardLiveriesCountFormat, authorEntries.Count);
        Grid.SetRow(liveriesText, 0);
        Grid.SetColumn(liveriesText, 0);
        statsGrid.Children.Add(liveriesText);

        var favoritesText = BuildStatLine(Strings.AuthorCardFavoritesCountFormat, authorEntries.Count(e => e.IsFavorite));
        Grid.SetRow(favoritesText, 0);
        Grid.SetColumn(favoritesText, 1);
        statsGrid.Children.Add(favoritesText);

        var duplicatesText = BuildStatLine(Strings.AuthorCardDuplicatesCountFormat, authorEntries.Count(e => e.IsDuplicate));
        duplicatesText.Margin = new Thickness(0, 4, 16, 0);
        Grid.SetRow(duplicatesText, 1);
        Grid.SetColumn(duplicatesText, 0);
        statsGrid.Children.Add(duplicatesText);

        var possibleDuplicatesText = BuildStatLine(Strings.AuthorCardPossibleDuplicatesCountFormat, authorEntries.Count(e => e.IsPossibleDuplicate));
        possibleDuplicatesText.Margin = new Thickness(0, 4, 0, 0);
        Grid.SetRow(possibleDuplicatesText, 1);
        Grid.SetColumn(possibleDuplicatesText, 1);
        statsGrid.Children.Add(possibleDuplicatesText);

        ContentPanel.Children.Add(statsGrid);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private TextBlock BuildStatLine(string format, int count) => new()
    {
        Text = string.Format(format, count),
        FontSize = 13,
        Margin = new Thickness(0, 0, 16, 0),
        Foreground = this.GetThemeBrush("TextSecondaryBrush"),
    };

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
