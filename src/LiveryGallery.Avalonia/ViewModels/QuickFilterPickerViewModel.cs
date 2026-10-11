using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Globalization;

namespace LiveryGallery.ViewModels;

internal sealed record QuickFilterOption(QuickFilter Filter, string Label, int Count, bool IsUnknown)
{
    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
}

internal sealed partial class QuickFilterPickerViewModel : ObservableObject
{
    private IReadOnlyCollection<LiveryEntry> _entries = [];
    private IReadOnlyList<QuickFilterOption> _cars = [];
    private IReadOnlyList<QuickFilterOption> _manufacturers = [];
    private IReadOnlyList<QuickFilterOption> _authors = [];
    private IReadOnlyList<QuickFilterOption> _countries = [];
    private IReadOnlyList<QuickFilterOption> _carTypes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarKind))]
    [NotifyPropertyChangedFor(nameof(IsManufacturerKind))]
    [NotifyPropertyChangedFor(nameof(IsAuthorKind))]
    [NotifyPropertyChangedFor(nameof(IsCountryKind))]
    [NotifyPropertyChangedFor(nameof(IsCarTypeKind))]
    [NotifyPropertyChangedFor(nameof(HasCarScope))]
    private QuickFilterKind _kind = QuickFilterKind.Manufacturer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCarScope))]
    [NotifyPropertyChangedFor(nameof(CarScopeText))]
    private QuickFilter? _carScope;

    public bool HasCarScope => Kind == QuickFilterKind.Car && CarScope is not null;
    public string CarScopeText => CarScope is { } scope ? string.Format(Strings.QuickFilterPickerCarsOfFormat, scope.DisplayText) : string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoOptions))]
    private IReadOnlyList<QuickFilterOption> _options = [];

    public bool HasNoOptions => Options.Count == 0;

    public bool IsManufacturerKind
    {
        get => Kind == QuickFilterKind.Manufacturer;
        set => SelectKind(QuickFilterKind.Manufacturer, value, nameof(IsManufacturerKind));
    }

    public bool IsCarKind
    {
        get => Kind == QuickFilterKind.Car;
        set => SelectKind(QuickFilterKind.Car, value, nameof(IsCarKind));
    }

    public bool IsAuthorKind
    {
        get => Kind == QuickFilterKind.Author;
        set => SelectKind(QuickFilterKind.Author, value, nameof(IsAuthorKind));
    }

    public bool IsCountryKind
    {
        get => Kind == QuickFilterKind.Country;
        set => SelectKind(QuickFilterKind.Country, value, nameof(IsCountryKind));
    }

    public bool IsCarTypeKind
    {
        get => Kind == QuickFilterKind.CarType;
        set => SelectKind(QuickFilterKind.CarType, value, nameof(IsCarTypeKind));
    }

    private void SelectKind(QuickFilterKind kind, bool isChecked, string propertyName)
    {
        if (isChecked) Kind = kind;
        else OnPropertyChanged(propertyName);
    }

    public void Load(IEnumerable<LiveryEntry> entries, QuickFilter? activeManufacturer)
    {
        _entries = entries as IReadOnlyCollection<LiveryEntry> ?? [.. entries];

        _manufacturers = Build(
            _entries.GroupBy(e => e.CarKnown ? e.CarManufacturerRaw.ToUpperInvariant() : null),
            QuickFilter.ForManufacturer, e => !e.CarKnown, f => f.DisplayText);
        _authors = Build(
            _entries.GroupBy(e => e.Data.IsAuthorUnknown ? null : e.Author.ToUpperInvariant()),
            QuickFilter.ForAuthor, e => e.Data.IsAuthorUnknown, f => f.DisplayText);
        // By the car list's value (one option per country whatever the language), named as the cards show it
        _countries = Build(
            _entries.GroupBy(e => e.CarCountryRaw.Length == 0 ? null : e.CarCountryRaw.ToUpperInvariant()),
            QuickFilter.ForCountry, e => e.CarCountryRaw.Length == 0, f => f.DisplayText);
        _carTypes = Build(
            _entries.GroupBy(e => e.CarTypeRaw.Length == 0 ? null : e.CarTypeRaw.ToUpperInvariant()),
            QuickFilter.ForCarType, e => e.CarTypeRaw.Length == 0, f => f.DisplayText);
        ScopeCars(activeManufacturer);

        SearchText = string.Empty;
        UpdateOptions();
    }

    public void ShowCarsOf(QuickFilter manufacturer)
    {
        ScopeCars(manufacturer);
        SearchText = string.Empty;
        Kind = QuickFilterKind.Car;
        UpdateOptions();
    }

    private void ScopeCars(QuickFilter? manufacturer)
    {
        CarScope = manufacturer;
        var cars = manufacturer is null ? _entries : _entries.Where(manufacturer.Matches);
        Func<QuickFilter, string> label = manufacturer is null ? f => f.FullDisplayText : f => f.DisplayText;
        _cars = Build(cars.GroupBy(e => e.CarId), QuickFilter.ForCar, e => !e.CarKnown, label);
    }

    private static List<QuickFilterOption> Build<TKey>(
        IEnumerable<IGrouping<TKey, LiveryEntry>> groups, Func<LiveryEntry, QuickFilter> createFilter,
        Func<LiveryEntry, bool> isUnknown, Func<QuickFilter, string> label)
    {
        return [.. groups
            .Select(g =>
            {
                var first = g.First();
                var filter = createFilter(first);
                return new QuickFilterOption(filter, label(filter), g.Count(), isUnknown(first));
            })
            .OrderBy(o => o.IsUnknown)
            .ThenBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    partial void OnKindChanged(QuickFilterKind value) => UpdateOptions();
    partial void OnSearchTextChanged(string value) => UpdateOptions();

    private void UpdateOptions()
    {
        var source = Kind switch
        {
            QuickFilterKind.Car => _cars,
            QuickFilterKind.Author => _authors,
            QuickFilterKind.Country => _countries,
            QuickFilterKind.CarType => _carTypes,
            _ => _manufacturers
        };

        string[] tokens = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Options = tokens.Length == 0
            ? source
            : [.. source.Where(o => tokens.All(t => o.Label.Contains(t, StringComparison.CurrentCultureIgnoreCase)))];
    }
}
