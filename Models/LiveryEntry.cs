using Avalonia.Media.Imaging;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Models;

internal class LiveryEntry
{
    public required string FolderPath { get; init; }
    public required string FolderName { get; init; }
    public required string LiveryName { get; init; }
    public required string Author { get; init; }
    public required int CarId { get; init; }
    public required string CarManufacturerRaw { get; init; }
    public required string CarModelNameRaw { get; init; }
    public int? CarYear { get; init; }
    public bool CarKnown { get; init; }
    public int? CreatedYear { get; init; }
    public int? CreatedMonth { get; init; }
    public DateTime? DownloadDate { get; init; }
    public string? ThumbnailPath { get; init; }

    public string CarManufacturer => CarKnown
        ? CarManufacturerRaw
        : Strings.UnknownManufacturer;

    public string CarModelName => CarKnown
        ? CarModelNameRaw
        : string.Format(Strings.UnknownCarIdFormat, CarId);

    public bool IsFavorite { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTime? DownloadYearMonth => DownloadDate is { } d ? new DateTime(d.Year, d.Month, 1) : null;

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath);

    public Bitmap? Thumbnail
    {
        get
        {
            if (string.IsNullOrEmpty(ThumbnailPath) || !File.Exists(ThumbnailPath))
                return null;

            try
            {
                using var stream = File.OpenRead(ThumbnailPath);
                return Bitmap.DecodeToWidth(stream, 280, BitmapInterpolationMode.MediumQuality);
            }
            catch
            {
                return null;
            }
        }
    }

    public string DateDisplay
    {
        get
        {
            if (DownloadDate is { } d) return d.ToString("dd.MM.yyyy", AppLocalisationService.Culture);
            if (CreatedYear is > 0 && CreatedMonth is >= 1 and <= 12)
                return new DateTime(CreatedYear.Value, CreatedMonth.Value, 1)
                    .ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture);
            return "—";
        }
    }

    private string SearchHaystack =>
        $"{CarManufacturer} {CarModelName} {CarYear} {LiveryName} {Author}".ToLowerInvariant();

    public bool MatchesSearch(string term)
    {
        var tokens = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;

        foreach (var token in tokens)
        {
            if (!SearchHaystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
