using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.Services.Search;

namespace MusicApp.ViewModels;

// A removable filter pill. Value distinguishes individual genre/artist
// selections so removing one chip clears exactly that entry, not the whole set.
public sealed record ActiveFilterChip(string Label, string Field, string? Value = null);

// Wraps a FacetGroup with collapse state: long bucket lists (the artist facet
// grows with the catalog) render only the first CollapsedCount entries — plus
// any active ones, so a checked artist never disappears — behind a
// «Показати всі (N)» toggle.
public partial class FacetGroupViewModel : ObservableObject
{
    private const int CollapsedCount = 8;
    private readonly FacetGroup _group;

    public FacetGroupViewModel(FacetGroup group, bool isExpanded)
    {
        _group = group;
        _isExpanded = isExpanded;
        RebuildVisible();
    }

    [ObservableProperty] private bool _isExpanded;

    public string Field => _group.Field;
    public string Title => _group.Title;
    public bool IsGenre => _group.IsGenre;
    // «Наявність» holds a single self-explanatory checkbox — a section header
    // above it is an empty layer of hierarchy.
    public bool ShowTitle => _group.Field != "stock";
    public ObservableCollection<FacetBucket> VisibleBuckets { get; } = new();
    public bool HasMore => _group.Buckets.Count > CollapsedCount;
    public string ToggleLabel => IsExpanded ? "Згорнути" : $"Показати всі ({_group.Buckets.Count})";

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        RebuildVisible();
        OnPropertyChanged(nameof(ToggleLabel));
    }

    private void RebuildVisible()
    {
        VisibleBuckets.Clear();
        var src = IsExpanded || !HasMore
            ? _group.Buckets
            : _group.Buckets.Take(CollapsedCount)
                .Concat(_group.Buckets.Skip(CollapsedCount).Where(b => b.IsActive));
        foreach (var b in src) VisibleBuckets.Add(b);
    }
}

public partial class SearchResultsViewModel : ViewModelBase
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private readonly ISearchService _search;
    private readonly INavigationService _nav;
    private readonly IPlayerService _player;
    private readonly ICartService _cart;
    private readonly IAuthService _auth;

    private bool _suppressReload;

    // Last query text written to SearchHistory — facet/spinner changes re-run
    // Reload with the same text and must not append another row each time.
    private string? _lastRecordedQuery;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _didYouMean;

    // Inline confirmation under «Зберегти запит»; reset whenever the filters
    // change (the saved snapshot no longer matches what's on screen).
    [ObservableProperty] private string? _saveQueryFeedback;

    // Results-column scroll position, kept on the VM so back/forward restores
    // it (the NavigationService preserves VM instances, views are rebuilt).
    public double SavedScrollOffset { get; set; }

    // filter properties
    [ObservableProperty] private string? _selectedFormatLabel;
    [ObservableProperty] private int? _yearFrom;
    [ObservableProperty] private int? _yearTo;
    [ObservableProperty] private decimal? _priceFrom;
    [ObservableProperty] private decimal? _priceTo;
    [ObservableProperty] private double? _minRating;
    [ObservableProperty] private bool _inStockOnly;
    // false → будь-який обраний жанр (OR); true → усі обрані жанри разом (AND).
    [ObservableProperty] private bool _genresMatchAll;

    public SearchResultsViewModel(
        ISearchService search,
        INavigationService nav,
        IPlayerService player,
        ICartService cart,
        IAuthService auth,
        string query)
    {
        _search = search;
        _nav = nav;
        _player = player;
        _cart = cart;
        _auth = auth;

        Albums = new ObservableCollection<AlbumHit>();
        Facets = new ObservableCollection<FacetGroupViewModel>();
        ActiveFilterChips = new ObservableCollection<ActiveFilterChip>();
        SelectedGenres = new ObservableCollection<string>();
        SelectedArtists = new ObservableCollection<string>();

        // Pull structured terms (жанр:Rock, виконавець:"…", рік:1990..2000, …) out
        // of the query string into the multi-select sets and numeric filters, so the
        // sidebar facets, chips and SearchService all agree on the active constraints.
        _suppressReload = true;
        Query = LiftStructuredQueryIntoFilters(query);
        _suppressReload = false;

        // The save button flips between guest/user state if the user logs in
        // while the page is open (login overlay sits above every page).
        _auth.CurrentUserChanged += (_, _) =>
        {
            SaveQueryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SaveQueryToolTip));
        };

        Reload();
    }

    public ObservableCollection<AlbumHit> Albums { get; }
    public ObservableCollection<FacetGroupViewModel> Facets { get; }
    public ObservableCollection<ActiveFilterChip> ActiveFilterChips { get; }
    public ObservableCollection<string> SelectedGenres { get; }
    public ObservableCollection<string> SelectedArtists { get; }

    [ObservableProperty] private AlbumHit? _topResult;
    [ObservableProperty] private string? _topResultLabel;
    [ObservableProperty] private string? _topResultSubLabel;
    public bool HasTopResult => TopResult is not null;

    public bool HasDidYouMean => !string.IsNullOrEmpty(DidYouMean);
    public bool HasResults => TotalCount > 0;
    public bool HasNoResults => TotalCount == 0;

    // Browse mode = nothing typed, nothing filtered: the page shows the whole
    // catalogue, so neither the header nor the counter may claim a "search".
    public bool IsBrowseMode => string.IsNullOrWhiteSpace(Query) && ActiveFilterChips.Count == 0;

    public string CountLabel => IsBrowseMode
        ? $"Альбомів у каталозі: {TotalCount}"
        : $"Знайдено альбомів: {TotalCount}";

    // The any/all genre toggle only matters once more than one genre is picked.
    public bool CanCombineGenres => SelectedGenres.Count > 1;

    // Title text — falls back to whichever filter the user actually opened the page
    // with when the text query is empty (genre/artist tile, price shortcut, …).
    public string HeaderLabel
    {
        get
        {
            var hasText = !string.IsNullOrWhiteSpace(Query);
            if (!hasText && SelectedArtists.Count == 1 && SelectedGenres.Count == 0)
                return $"Виконавець: {SelectedArtists[0]}";
            if (hasText) return $"Результати: «{Query}»";
            if (SelectedGenres.Count > 0)
            {
                if (GenresMatchAll && SelectedGenres.Count > 1)
                    return $"Жанри (усі): {string.Join(" + ", SelectedGenres)}";
                return $"Жанр: {string.Join(", ", SelectedGenres)}";
            }
            if (SelectedArtists.Count > 0) return $"Виконавці: {string.Join(", ", SelectedArtists)}";
            if (!string.IsNullOrEmpty(SelectedFormatLabel)) return $"Формат: {SelectedFormatLabel}";
            if (MinRating is double mr and > 0) return $"Рейтинг: {mr:0.0}★ і вище";
            if (PriceFrom.HasValue || PriceTo.HasValue)
            {
                if (PriceFrom.HasValue && PriceTo.HasValue) return $"Ціна: {PriceFrom:0}–{PriceTo:0} ₴";
                if (PriceTo.HasValue) return $"Ціна: до {PriceTo:0} ₴";
                return $"Ціна: від {PriceFrom:0} ₴";
            }
            return "Усі альбоми";
        }
    }

    // The DSL → filters mapping itself lives in SavedQueryInterpreter (shared
    // with saved-search counting); this only mirrors the result into the
    // sidebar's observable state.
    private string LiftStructuredQueryIntoFilters(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

        var (text, lifted) = SavedQueryInterpreter.Lift(raw);

        foreach (var g in lifted.Genres)
            if (!SelectedGenres.Contains(g, Ci)) SelectedGenres.Add(g);
        foreach (var a in lifted.Artists)
            if (!SelectedArtists.Contains(a, Ci)) SelectedArtists.Add(a);
        if (lifted.Format is ProductFormat format)
            SelectedFormatLabel = format == ProductFormat.Vinyl ? "LP" : "CD";
        if (lifted.YearFrom is int yearFrom) YearFrom = yearFrom;
        if (lifted.YearTo is int yearTo) YearTo = yearTo;
        if (lifted.PriceFrom is decimal priceFrom) PriceFrom = priceFrom;
        if (lifted.PriceTo is decimal priceTo) PriceTo = priceTo;
        if (lifted.MinRating is double minRating) MinRating = minRating;
        return text;
    }

    private void Reload()
    {
        if (_suppressReload) return;

        var filters = new SearchFilters(
            YearFrom: YearFrom,
            YearTo: YearTo,
            PriceFrom: PriceFrom,
            PriceTo: PriceTo,
            // «Рейтинг ≥ 0» constrains nothing — the first spinner click lands
            // on 0,0 and must not masquerade as an active filter (chip/header).
            MinRating: MinRating is > 0 ? MinRating : null,
            Format: ParseFormat(SelectedFormatLabel),
            Genres: SelectedGenres.ToList(),
            Artists: SelectedArtists.ToList(),
            InStockOnly: InStockOnly,
            GenresMatchAll: GenresMatchAll);

        var results = _search.Search(Query, filters);

        Albums.Clear();
        foreach (var a in results.Albums) Albums.Add(a);

        // Facet groups are rebuilt on every reload — carry the expand state
        // over so toggling a checkbox doesn't re-collapse the list.
        var expanded = Facets.Where(f => f.IsExpanded).Select(f => f.Field).ToHashSet();
        Facets.Clear();
        foreach (var f in results.Facets) Facets.Add(new FacetGroupViewModel(f, expanded.Contains(f.Field)));

        RebuildChips(filters);

        TotalCount = results.TotalCount;
        DidYouMean = results.DidYouMean;
        TopResult = results.TopResult;
        TopResultLabel = TopResult?.Album.Title;
        TopResultSubLabel = TopResult?.Album.Artist?.Name ?? string.Empty;
        SaveQueryFeedback = null;
        OnPropertyChanged(nameof(HasTopResult));
        OnPropertyChanged(nameof(HeaderLabel));
        OnPropertyChanged(nameof(IsBrowseMode));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(CanCombineGenres));

        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId > 0 && !string.IsNullOrWhiteSpace(Query)
            && !string.Equals(Query, _lastRecordedQuery, StringComparison.Ordinal))
        {
            _search.RecordHistory(userId, Query, results.TotalCount);
            _lastRecordedQuery = Query;
        }
    }

    private void RebuildChips(SearchFilters filters)
    {
        ActiveFilterChips.Clear();
        foreach (var g in filters.Genres)
            ActiveFilterChips.Add(new ActiveFilterChip($"Жанр: {g}", "genre", g));
        foreach (var a in filters.Artists)
            ActiveFilterChips.Add(new ActiveFilterChip($"Виконавець: {a}", "artist", a));
        if (filters.Format is ProductFormat fmt)
            ActiveFilterChips.Add(new ActiveFilterChip($"Формат: {(fmt == ProductFormat.Vinyl ? "LP" : "CD")}", "format"));
        if (filters.YearFrom.HasValue || filters.YearTo.HasValue)
            ActiveFilterChips.Add(new ActiveFilterChip(
                RangeLabel("Рік", filters.YearFrom?.ToString(), filters.YearTo?.ToString(), ""), "year"));
        if (filters.PriceFrom.HasValue || filters.PriceTo.HasValue)
            ActiveFilterChips.Add(new ActiveFilterChip(
                RangeLabel("Ціна", filters.PriceFrom?.ToString("0"), filters.PriceTo?.ToString("0"), " ₴"), "price"));
        if (filters.MinRating is double mr)
            ActiveFilterChips.Add(new ActiveFilterChip($"★ від {mr:0.0}", "rating"));
        if (filters.InStockOnly)
            ActiveFilterChips.Add(new ActiveFilterChip("Тільки в наявності", "stock"));
    }

    // Open-ended ranges say what they mean: «Рік: від 1990», «Ціна: до 600 ₴» —
    // the old «…» placeholder («Ціна: …–600 ₴») read as a rendering glitch.
    private static string RangeLabel(string prefix, string? from, string? to, string suffix)
    {
        if (from is not null && to is not null)
            return from == to ? $"{prefix}: {from}{suffix}" : $"{prefix}: {from}–{to}{suffix}";
        if (from is not null) return $"{prefix}: від {from}{suffix}";
        if (to is not null) return $"{prefix}: до {to}{suffix}";
        return prefix;
    }

    private static ProductFormat? ParseFormat(string? label) => label switch
    {
        "LP" => ProductFormat.Vinyl,
        "Вініл LP" => ProductFormat.Vinyl,
        "CD" => ProductFormat.CD,
        _ => null
    };

    partial void OnSelectedFormatLabelChanged(string? value) { OnPropertyChanged(nameof(HeaderLabel)); Reload(); }
    partial void OnQueryChanged(string value) => OnPropertyChanged(nameof(HeaderLabel));
    partial void OnYearFromChanged(int? value) => Reload();
    partial void OnYearToChanged(int? value) => Reload();
    partial void OnPriceFromChanged(decimal? value) { OnPropertyChanged(nameof(HeaderLabel)); Reload(); }
    partial void OnPriceToChanged(decimal? value) { OnPropertyChanged(nameof(HeaderLabel)); Reload(); }
    partial void OnMinRatingChanged(double? value) { OnPropertyChanged(nameof(HeaderLabel)); Reload(); }
    partial void OnInStockOnlyChanged(bool value) => Reload();
    partial void OnGenresMatchAllChanged(bool value) { OnPropertyChanged(nameof(HeaderLabel)); Reload(); }
    partial void OnDidYouMeanChanged(string? value) => OnPropertyChanged(nameof(HasDidYouMean));
    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(CountLabel));
    }

    private void ToggleInSet(ObservableCollection<string> set, string value)
    {
        var existing = set.FirstOrDefault(x => Ci.Equals(x, value));
        if (existing is not null) set.Remove(existing);
        else set.Add(value);
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }

    [RelayCommand]
    private void ToggleFacet(FacetBucket bucket)
    {
        if (bucket is null) return;
        switch (bucket.Field)
        {
            case "genre": ToggleInSet(SelectedGenres, bucket.Label); break;
            case "artist": ToggleInSet(SelectedArtists, bucket.Label); break;
            case "format":
                // Compare by parsed format, not by label: a DSL query stores
                // «LP» while the bucket says «Вініл LP» — string comparison made
                // the first click on an already-active bucket re-apply it.
                SelectedFormatLabel = ParseFormat(SelectedFormatLabel) == ParseFormat(bucket.Label)
                    ? null : bucket.Label;
                break;
            case "stock": InStockOnly = !InStockOnly; break;
        }
    }

    [RelayCommand]
    private void RemoveFilter(ActiveFilterChip? chip)
    {
        if (chip is null) return;
        _suppressReload = true;
        switch (chip.Field)
        {
            case "genre" when chip.Value is not null:
                var g = SelectedGenres.FirstOrDefault(x => Ci.Equals(x, chip.Value));
                if (g is not null) SelectedGenres.Remove(g);
                break;
            case "artist" when chip.Value is not null:
                var a = SelectedArtists.FirstOrDefault(x => Ci.Equals(x, chip.Value));
                if (a is not null) SelectedArtists.Remove(a);
                break;
            case "format": SelectedFormatLabel = null; break;
            case "year": YearFrom = null; YearTo = null; break;
            case "price": PriceFrom = null; PriceTo = null; break;
            case "rating": MinRating = null; break;
            case "stock": InStockOnly = false; break;
        }
        _suppressReload = false;
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }

    [RelayCommand]
    private void OpenTopResult()
    {
        if (TopResult?.PrimaryProduct is { } prod)
            _nav.NavigateTo(NavTarget.Product, prod.Id);
    }

    [RelayCommand]
    private void OpenAlbum(AlbumHit? hit)
    {
        if (hit?.PrimaryProduct is { } prod)
            _nav.NavigateTo(NavTarget.Product, prod.Id);
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _suppressReload = true;
        ClearFilters();
        _suppressReload = false;
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }

    // Escape hatch for the no-results dead end: drops the text query AND the
    // filters, landing on the browse-all page.
    [RelayCommand]
    private void ShowAll()
    {
        _suppressReload = true;
        Query = string.Empty;
        ClearFilters();
        _suppressReload = false;
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }

    private void ClearFilters()
    {
        SelectedGenres.Clear();
        SelectedArtists.Clear();
        SelectedFormatLabel = null;
        YearFrom = null; YearTo = null;
        PriceFrom = null; PriceTo = null;
        MinRating = null;
        InStockOnly = false;
        GenresMatchAll = false;
    }

    [RelayCommand]
    private void ApplyDidYouMean()
    {
        if (string.IsNullOrWhiteSpace(DidYouMean)) return;
        Query = DidYouMean!;
        DidYouMean = null;
        Reload();
    }

    public bool CanSaveQuery => _auth.CurrentUser is not null;

    public string SaveQueryToolTip => CanSaveQuery
        ? "Зберегти пошук у профілі (Профіль → Збережені запити)"
        : "Увійдіть, щоб зберігати запити";

    [RelayCommand(CanExecute = nameof(CanSaveQuery))]
    private void SaveQuery()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        var serialized = BuildQueryString();
        if (string.IsNullOrWhiteSpace(serialized))
        {
            SaveQueryFeedback = "Немає чого зберігати — порожній запит";
            return;
        }
        if (_search.ListSavedSearches(userId).Any(s => s.QueryJson == serialized))
        {
            SaveQueryFeedback = "Цей запит уже збережено";
            return;
        }
        // HeaderLabel is already the human description of the current search
        // («Жанр: Rock», «Результати: «death»») — better profile row title
        // than the raw DSL string, which stays in QueryJson.
        _search.SaveSearch(userId, HeaderLabel, serialized, notifyOnNew: false);
        SaveQueryFeedback = "Збережено у профілі ✓";
    }

    // Round-trips the current text + multi-select sets + ranges back into the DSL
    // so a saved query re-opens with the same filters via LiftStructuredQueryIntoFilters.
    private string BuildQueryString()
    {
        var sb = new StringBuilder();
        void Add(string s) { if (sb.Length > 0) sb.Append(' '); sb.Append(s); }

        if (!string.IsNullOrWhiteSpace(Query)) Add(Query.Trim());
        foreach (var g in SelectedGenres) Add($"жанр:\"{g}\"");
        foreach (var a in SelectedArtists) Add($"виконавець:\"{a}\"");
        if (SelectedFormatLabel is "LP") Add("формат:lp");
        else if (SelectedFormatLabel is "CD") Add("формат:cd");
        if (YearFrom.HasValue || YearTo.HasValue)
            Add($"рік:{YearFrom}..{YearTo}");
        if (PriceFrom.HasValue || PriceTo.HasValue)
            Add($"ціна:{Num(PriceFrom)}..{Num(PriceTo)}");
        if (MinRating is double mr)
            Add($"рейтинг:>={mr.ToString(CultureInfo.InvariantCulture)}");
        return sb.ToString();

        static string Num(decimal? d) => d?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
