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

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _didYouMean;

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
            if (MinRating is double mr) return $"Рейтинг: {mr:0.0}★ і вище";
            if (PriceFrom.HasValue || PriceTo.HasValue)
            {
                if (PriceFrom.HasValue && PriceTo.HasValue) return $"Ціна: {PriceFrom:0}–{PriceTo:0} ₴";
                if (PriceTo.HasValue) return $"Ціна: до {PriceTo:0} ₴";
                return $"Ціна: від {PriceFrom:0} ₴";
            }
            return "Результати пошуку";
        }
    }

    private string LiftStructuredQueryIntoFilters(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

        var parsed = SearchQueryParser.Parse(raw);
        var leftover = new StringBuilder();

        void AddGenre(string g) { if (!SelectedGenres.Contains(g, Ci)) SelectedGenres.Add(g); }
        void AddArtist(string a) { if (!SelectedArtists.Contains(a, Ci)) SelectedArtists.Add(a); }
        void AddText(string s) { if (leftover.Length > 0) leftover.Append(' '); leftover.Append(s); }

        foreach (var term in parsed.Terms)
        {
            switch (term)
            {
                case FieldTextTerm ft when ft.Field == "genre": AddGenre(ft.Value); break;
                case FieldPhraseTerm fp when fp.Field == "genre": AddGenre(fp.Phrase); break;
                case FieldTextTerm ft when ft.Field == "artist": AddArtist(ft.Value); break;
                case FieldPhraseTerm fp when fp.Field == "artist": AddArtist(fp.Phrase); break;
                case FieldTextTerm ft when ft.Field == "format":
                    SelectedFormatLabel = IsVinyl(ft.Value) ? "LP" : "CD";
                    break;
                case RangeTerm rt when rt.Field == "year":
                    if (rt.Min is double yMin) YearFrom = (int)yMin;
                    if (rt.Max is double yMax) YearTo = (int)yMax;
                    break;
                case RangeTerm rt when rt.Field == "price":
                    if (rt.Min is double pMin) PriceFrom = (decimal)pMin;
                    if (rt.Max is double pMax) PriceTo = (decimal)pMax;
                    break;
                case CompareTerm ct when ct.Field == "rating" && ct.Op is CompareOp.Gte or CompareOp.Gt:
                    MinRating = ct.Value;
                    break;
                case CompareTerm ct when ct.Field == "year" && ct.Op == CompareOp.Equal:
                    YearFrom = YearTo = (int)ct.Value;
                    break;
                // album/track/lyrics restrictions have no dedicated control — re-emit
                // them so SearchService still applies them as FTS text constraints.
                case FieldTextTerm ft when ft.Field is "album" or "track" or "lyrics":
                    AddText($"{ft.Field}:{ft.Value}");
                    break;
                case FieldPhraseTerm fp when fp.Field is "album" or "track" or "lyrics":
                    AddText($"{fp.Field}:\"{fp.Phrase}\"");
                    break;
                case FreeTextTerm fr: AddText(fr.Text); break;
                case PhraseTerm ph: AddText($"\"{ph.Phrase}\""); break;
            }
        }
        return leftover.ToString();
    }

    private static bool IsVinyl(string v) =>
        v.Equals("LP", StringComparison.OrdinalIgnoreCase)
        || v.Equals("Вініл", StringComparison.OrdinalIgnoreCase)
        || v.Equals("Vinyl", StringComparison.OrdinalIgnoreCase);

    private void Reload()
    {
        if (_suppressReload) return;

        var filters = new SearchFilters(
            YearFrom: YearFrom,
            YearTo: YearTo,
            PriceFrom: PriceFrom,
            PriceTo: PriceTo,
            MinRating: MinRating,
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
        OnPropertyChanged(nameof(HasTopResult));
        OnPropertyChanged(nameof(HeaderLabel));
        OnPropertyChanged(nameof(CanCombineGenres));

        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId > 0) _search.RecordHistory(userId, Query, results.TotalCount);
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
                $"Рік: {filters.YearFrom?.ToString() ?? "…"}–{filters.YearTo?.ToString() ?? "…"}", "year"));
        if (filters.PriceFrom.HasValue || filters.PriceTo.HasValue)
            ActiveFilterChips.Add(new ActiveFilterChip(
                $"Ціна: {filters.PriceFrom?.ToString() ?? "…"}–{filters.PriceTo?.ToString() ?? "…"} ₴", "price"));
        if (filters.MinRating is double mr)
            ActiveFilterChips.Add(new ActiveFilterChip($"★ від {mr:0.0}", "rating"));
        if (filters.InStockOnly)
            ActiveFilterChips.Add(new ActiveFilterChip("Тільки в наявності", "stock"));
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
                SelectedFormatLabel = SelectedFormatLabel == bucket.Label ? null : bucket.Label;
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
        SelectedGenres.Clear();
        SelectedArtists.Clear();
        SelectedFormatLabel = null;
        YearFrom = null; YearTo = null;
        PriceFrom = null; PriceTo = null;
        MinRating = null;
        InStockOnly = false;
        _suppressReload = false;
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }

    [RelayCommand]
    private void ApplyDidYouMean()
    {
        if (string.IsNullOrWhiteSpace(DidYouMean)) return;
        Query = DidYouMean!;
        DidYouMean = null;
        Reload();
    }

    [RelayCommand]
    private void SaveQuery()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        var serialized = BuildQueryString();
        var name = string.IsNullOrWhiteSpace(serialized) ? "Збережений запит" : serialized;
        _search.SaveSearch(userId, name, serialized, notifyOnNew: false);
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
