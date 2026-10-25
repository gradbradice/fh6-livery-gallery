using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

/// <summary>A page in the list on the left</summary>
internal sealed record StatisticsPageItem(StatisticsPage Page, string Title, Geometry? Icon);

/// <summary>
/// The statistics window: stays open beside the gallery. Pages on the left, as in the settings. A tile or row sets the
/// gallery's filter; with "Shown" the numbers follow the filter as it changes. Exports the counted liveries and authors
/// as CSV, and saves a chart as a picture (right-click on it)
/// </summary>
internal partial class StatisticsWindow : Window
{
    /// <summary>SemiBold for the account the gallery shows</summary>
    public static readonly IValueConverter BoldWhenTrue =
        new FuncValueConverter<bool, FontWeight>(value => value ? FontWeight.SemiBold : FontWeight.Normal);

    private readonly DispatcherTimer _refresh;
    private readonly List<StatisticsPageItem> _pages;

    public StatisticsViewModel ViewModel { get; }

    public StatisticsWindow(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        _pages =
        [
            Item(StatisticsPage.Overview, "IconStats"),
            Item(StatisticsPage.Health, "IconCheck"),
            Item(StatisticsPage.Collection, "IconBookmark"),
            Item(StatisticsPage.Cars, "IconCarSide"),
            Item(StatisticsPage.Time, "IconCalendarDay"),
            Item(StatisticsPage.Authors, "IconUser"),
            Item(StatisticsPage.Liveries, "IconPalette"),
        ];
        PageList.ItemsSource = _pages;
        PageList.SelectedIndex = _pages.FindIndex(p => p.Page == viewModel.Page);
        // A page opened from the view model (the overview's health lines) is selected in the list too
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatisticsViewModel.Page))
                PageList.SelectedIndex = _pages.FindIndex(p => p.Page == viewModel.Page);
        };

        // The gallery may change many times in a row (typing a search): count once it settles
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            ViewModel.Refresh();
        };
        Closed += (_, _) => _refresh.Stop();

        foreach (var card in this.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("chartCard")))
        {
            var save = new MenuItem { Header = Strings.StatsSaveChart };
            save.Click += (_, _) => _ = SaveChartAsync(card);
            card.ContextMenu = new ContextMenu { Items = { save } };
        }
    }

    private StatisticsPageItem Item(StatisticsPage page, string icon) =>
        new(page, StatisticsViewModel.TitleOf(page), this.TryFindResource(icon, out var value) ? value as Geometry : null);

    /// <summary>The gallery or its filter changed: the numbers are counted again shortly</summary>
    public void RequestRefresh()
    {
        _refresh.Stop();
        _refresh.Start();
    }

    private void PageList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is StatisticsPageItem item) ViewModel.Page = item.Page;
    }

    private void ExportLiveries_Click(object? sender, RoutedEventArgs e) =>
        _ = ExportAsync(Strings.StatsExportLiveriesFileName, ViewModel.ExportLiveries);

    private void ExportAuthors_Click(object? sender, RoutedEventArgs e) =>
        _ = ExportAsync(Strings.StatsExportAuthorsFileName, ViewModel.ExportAuthors);

    private async Task ExportAsync(string name, Action<TextWriter> write)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"{name}-{DateTime.Now:yyyy-MM-dd}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            // With a byte order mark: Excel then reads the names in every language right
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            write(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogError("Failed to export the statistics", ex);
            await InfoDialog.ShowAsync(this, Strings.StatsExport, Strings.StatsExportFailed);
        }
    }

    /// <summary>The chart's card as a PNG, at twice the screen's pixels so it stays sharp</summary>
    private async Task SaveChartAsync(Control card)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"{Strings.StatsTitle}-{DateTime.Now:yyyy-MM-dd}.png",
                DefaultExtension = "png",
                FileTypeChoices = [FilePickerFileTypes.ImagePng],
            });
            if (file is null) return;
            const double scale = 2;
            var size = new PixelSize((int)Math.Ceiling(card.Bounds.Width * scale), (int)Math.Ceiling(card.Bounds.Height * scale));
            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bitmap.Render(card);
            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.LogError("Failed to save a statistics chart", ex);
            await InfoDialog.ShowAsync(this, Strings.StatsSaveChart, Strings.StatsExportFailed);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
