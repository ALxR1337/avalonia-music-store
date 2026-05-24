using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.Services.Search;

namespace MusicApp.ViewModels;

public sealed record ActiveFilterChip(string Label, string Field);

public partial class SearchResultsViewModel : ViewModelBase
{
    private readonly ISearchService _search;
    private readonly INavigationService _nav;
    private readonly IPlayerService _player;
    private readonly ICartService _cart;
    private readonly IAuthService _auth;

    private bool _suppressReload;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _didYouMean;
    [ObservableProperty] private SearchTab _activeTab = SearchTab.All;

    // filter properties
    [ObservableProperty] private string? _selectedGenre;
    [ObservableProperty] private string? _selectedFormatLabel;
    [ObservableProperty] private int? _yearFrom;
    [ObservableProperty] private int? _yearTo;
    [ObservableProperty] private decimal? _priceFrom;
    [ObservableProperty] private decimal? _priceTo;
    [ObservableProperty] private double? _minRating;
    [ObservableProperty] private bool _inStockOnly;

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

        Albums = new ObservableCollection<ScoredAlbum>();
        Artists = new ObservableCollection<ScoredArtist>();
        Tracks = new ObservableCollection<ScoredTrack>();
        Reviews = new ObservableCollection<ScoredReview>();
        Products = new ObservableCollection<Product>();
        Facets = new ObservableCollection<FacetGroup>();
        ActiveFilterChips = new ObservableCollection<ActiveFilterChip>();

        // Pull structured terms (жанр:Rock, формат:CD, рік:1990..2000, …) out of the
        // query string and into the VM filter properties so the sidebar checkboxes,
        // chips, and SearchService all agree on the active constraints.
        _suppressReload = true;
        Query = LiftStructuredQueryIntoFilters(query);
        _suppressReload = false;

        Reload();
    }

    private string LiftStructuredQueryIntoFilters(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

        var parsed = SearchQueryParser.Parse(raw);
        var leftover = new System.Text.StringBuilder();

        foreach (var term in parsed.Terms)
        {
            switch (term)
            {
                case FieldTextTerm ft when ft.Field == "genre":
                    SelectedGenre = ft.Value;
                    break;
                case FieldTextTerm ft when ft.Field == "format":
                    SelectedFormatLabel = ft.Value.Equals("LP", StringComparison.OrdinalIgnoreCase)
                                       || ft.Value.Equals("Вініл", StringComparison.OrdinalIgnoreCase)
                                       || ft.Value.Equals("Vinyl", StringComparison.OrdinalIgnoreCase)
                        ? "LP" : "CD";
                    break;
                case RangeTerm rt when rt.Field == "year":
                    if (rt.Min is double yMin) YearFrom = (int)yMin;
                    if (rt.Max is double yMax) YearTo = (int)yMax;
                    break;
                case RangeTerm rt when rt.Field == "price":
                    if (rt.Min is double pMin) PriceFrom = (decimal)pMin;
                    if (rt.Max is double pMax) PriceTo = (decimal)pMax;
                    break;
                case CompareTerm ct when ct.Field == "rating" && (ct.Op == CompareOp.Gte || ct.Op == CompareOp.Gt):
                    MinRating = ct.Value;
                    break;
                case CompareTerm ct when ct.Field == "year" && ct.Op == CompareOp.Equal:
                    YearFrom = YearTo = (int)ct.Value;
                    break;
                case FreeTextTerm fr:
                    if (leftover.Length > 0) leftover.Append(' ');
                    leftover.Append(fr.Text);
                    break;
                case PhraseTerm ph:
                    if (leftover.Length > 0) leftover.Append(' ');
                    leftover.Append('"').Append(ph.Phrase).Append('"');
                    break;
            }
        }
        return leftover.ToString();
    }

    public ObservableCollection<ScoredAlbum> Albums { get; }
    public ObservableCollection<ScoredArtist> Artists { get; }
    public ObservableCollection<ScoredTrack> Tracks { get; }
    public ObservableCollection<ScoredReview> Reviews { get; }
    public ObservableCollection<Product> Products { get; }
    public ObservableCollection<FacetGroup> Facets { get; }
    public ObservableCollection<ActiveFilterChip> ActiveFilterChips { get; }

    [ObservableProperty] private object? _topResult;
    [ObservableProperty] private string? _topResultLabel;
    [ObservableProperty] private string? _topResultSubLabel;
    public bool HasTopResult => TopResult is not null;

    // Title text — falls back to whichever filter the user actually opened the page with
    // when the text Query is empty (e.g. genre tile click → "Жанр: Rock").
    public string HeaderLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Query)) return $"Результати: «{Query}»";
            if (!string.IsNullOrEmpty(SelectedGenre)) return $"Жанр: {SelectedGenre}";
            if (!string.IsNullOrEmpty(SelectedFormatLabel)) return $"Формат: {SelectedFormatLabel}";
            return "Результати пошуку";
        }
    }

    public int AlbumCount => Albums.Count;
    public int ArtistCount => Artists.Count;
    public int TrackCount => Tracks.Count;
    public int ReviewCount => Reviews.Count;

    public bool ShowAlbums => ActiveTab == SearchTab.All || ActiveTab == SearchTab.Albums;
    public bool ShowArtists => ActiveTab == SearchTab.All || ActiveTab == SearchTab.Artists;
    public bool ShowTracks => ActiveTab == SearchTab.All || ActiveTab == SearchTab.Tracks;
    public bool ShowReviews => ActiveTab == SearchTab.All || ActiveTab == SearchTab.Reviews;
    public bool HasDidYouMean => !string.IsNullOrEmpty(DidYouMean);
    public bool HasResults => TotalCount > 0;
    public bool HasNoResults => TotalCount == 0;
    public bool IsTabAll => ActiveTab == SearchTab.All;
    public bool IsTabAlbums => ActiveTab == SearchTab.Albums;
    public bool IsTabArtists => ActiveTab == SearchTab.Artists;
    public bool IsTabTracks => ActiveTab == SearchTab.Tracks;
    public bool IsTabReviews => ActiveTab == SearchTab.Reviews;

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
            Genre: string.IsNullOrWhiteSpace(SelectedGenre) ? null : SelectedGenre,
            InStockOnly: InStockOnly,
            Tab: ActiveTab);

        var results = _search.Search(Query, filters);

        Albums.Clear();
        foreach (var a in results.Albums) Albums.Add(a);
        Artists.Clear();
        foreach (var a in results.Artists) Artists.Add(a);
        Tracks.Clear();
        foreach (var t in results.Tracks) Tracks.Add(t);
        Reviews.Clear();
        foreach (var r in results.Reviews) Reviews.Add(r);
        Products.Clear();
        foreach (var p in results.Products) Products.Add(p);
        Facets.Clear();
        foreach (var f in results.Facets) Facets.Add(f);

        ActiveFilterChips.Clear();
        if (!string.IsNullOrEmpty(filters.Genre))
            ActiveFilterChips.Add(new ActiveFilterChip($"Жанр: {filters.Genre}", "genre"));
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

        TotalCount = results.TotalCount;
        DidYouMean = results.DidYouMean;
        TopResult = results.TopResult;
        TopResultLabel = TopResult switch
        {
            ScoredAlbum sa => sa.Album.Title,
            ScoredArtist sar => sar.Artist.Name,
            ScoredTrack st => st.Track.Title,
            ScoredReview sr => sr.Review.UserDisplayName ?? "Відгук",
            _ => null
        };
        TopResultSubLabel = TopResult switch
        {
            ScoredAlbum sa => sa.Album.Artist?.Name ?? "",
            ScoredArtist sar => sar.Artist.Country ?? "",
            ScoredTrack st => st.Track.Title,
            ScoredReview sr => sr.Review.Text,
            _ => null
        };
        OnPropertyChanged(nameof(HasTopResult));

        OnPropertyChanged(nameof(AlbumCount));
        OnPropertyChanged(nameof(ArtistCount));
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(ReviewCount));

        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId > 0) _search.RecordHistory(userId, Query, results.TotalCount);
    }

    private static ProductFormat? ParseFormat(string? label) => label switch
    {
        "LP" => ProductFormat.Vinyl,
        "Вініл LP" => ProductFormat.Vinyl,
        "CD" => ProductFormat.CD,
        _ => null
    };

    partial void OnSelectedGenreChanged(string? value)
    {
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }
    partial void OnSelectedFormatLabelChanged(string? value)
    {
        OnPropertyChanged(nameof(HeaderLabel));
        Reload();
    }
    partial void OnQueryChanged(string value) => OnPropertyChanged(nameof(HeaderLabel));
    partial void OnYearFromChanged(int? value) => Reload();
    partial void OnYearToChanged(int? value) => Reload();
    partial void OnPriceFromChanged(decimal? value) => Reload();
    partial void OnPriceToChanged(decimal? value) => Reload();
    partial void OnMinRatingChanged(double? value) => Reload();
    partial void OnInStockOnlyChanged(bool value) => Reload();
    partial void OnActiveTabChanged(SearchTab value)
    {
        OnPropertyChanged(nameof(ShowAlbums));
        OnPropertyChanged(nameof(ShowArtists));
        OnPropertyChanged(nameof(ShowTracks));
        OnPropertyChanged(nameof(ShowReviews));
        OnPropertyChanged(nameof(IsTabAll));
        OnPropertyChanged(nameof(IsTabAlbums));
        OnPropertyChanged(nameof(IsTabArtists));
        OnPropertyChanged(nameof(IsTabTracks));
        OnPropertyChanged(nameof(IsTabReviews));
        Reload();
    }
    partial void OnDidYouMeanChanged(string? value) => OnPropertyChanged(nameof(HasDidYouMean));
    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        if (Enum.TryParse<SearchTab>(tab, true, out var t)) ActiveTab = t;
    }

    [RelayCommand]
    private void ToggleGenre(string genre)
    {
        SelectedGenre = string.Equals(SelectedGenre, genre, StringComparison.OrdinalIgnoreCase) ? null : genre;
    }

    [RelayCommand]
    private void ToggleFormat(string label)
    {
        SelectedFormatLabel = string.Equals(SelectedFormatLabel, label, StringComparison.Ordinal) ? null : label;
    }

    [RelayCommand]
    private void ToggleFacet(FacetBucket bucket)
    {
        if (bucket is null) return;
        switch (bucket.Field)
        {
            case "genre": ToggleGenre(bucket.Label); break;
            case "format": ToggleFormat(bucket.Label); break;
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
            case "genre": SelectedGenre = null; break;
            case "format": SelectedFormatLabel = null; break;
            case "year": YearFrom = null; YearTo = null; break;
            case "price": PriceFrom = null; PriceTo = null; break;
            case "rating": MinRating = null; break;
            case "stock": InStockOnly = false; break;
        }
        _suppressReload = false;
        Reload();
    }

    [RelayCommand]
    private void OpenTopResult()
    {
        switch (TopResult)
        {
            case ScoredAlbum sa:
                var prod = Products.FirstOrDefault(p => p.AlbumId == sa.Album.Id);
                if (prod is not null) _nav.NavigateTo(NavTarget.Product, prod.Id);
                break;
            case ScoredTrack st:
                _player.PlaySample(st.Track);
                break;
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _suppressReload = true;
        SelectedGenre = null;
        SelectedFormatLabel = null;
        YearFrom = null; YearTo = null;
        PriceFrom = null; PriceTo = null;
        MinRating = null;
        InStockOnly = false;
        ActiveTab = SearchTab.All;
        _suppressReload = false;
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
        var name = string.IsNullOrWhiteSpace(Query) ? "Збережений запит" : Query;
        _search.SaveSearch(userId, name, Query ?? string.Empty, notifyOnNew: false);
    }

    [RelayCommand]
    private void OpenProduct(Product product) => _nav.NavigateTo(NavTarget.Product, product.Id);

    [RelayCommand]
    private void OpenAlbumProduct(ScoredAlbum album)
    {
        var product = Products.FirstOrDefault(p => p.AlbumId == album.Album.Id);
        if (product is not null) _nav.NavigateTo(NavTarget.Product, product.Id);
    }

    [RelayCommand]
    private void PlayTrack(ScoredTrack track) => _player.PlaySample(track.Track);
}
