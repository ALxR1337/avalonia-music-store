using System.Collections.Generic;
using MusicApp.Models;

namespace MusicApp.Services.Search;

public enum SearchTab { All, Albums, Artists, Tracks, Reviews }

public sealed record SearchFilters(
    int? YearFrom = null,
    int? YearTo = null,
    decimal? PriceFrom = null,
    decimal? PriceTo = null,
    double? MinRating = null,
    ProductFormat? Format = null,
    string? Genre = null,
    bool InStockOnly = false,
    SearchTab Tab = SearchTab.All);

public sealed record FacetBucket(string Field, string Label, int Count, bool IsActive = false);

public sealed record FacetGroup(string Field, string Title, IReadOnlyList<FacetBucket> Buckets);

public sealed record ScoredAlbum(Album Album, double Score, double Bm25, double Popularity);
public sealed record ScoredTrack(Track Track, double Score, double Bm25);
public sealed record ScoredArtist(Artist Artist, double Score, double Bm25);
public sealed record ScoredReview(Review Review, double Score, double Bm25);

public sealed class SearchResults
{
    public SearchQuery Query { get; init; } = new();
    public string RawQuery { get; init; } = string.Empty;
    public SearchFilters Filters { get; init; } = new();

    public IReadOnlyList<ScoredAlbum> Albums { get; init; } = System.Array.Empty<ScoredAlbum>();
    public IReadOnlyList<ScoredArtist> Artists { get; init; } = System.Array.Empty<ScoredArtist>();
    public IReadOnlyList<ScoredTrack> Tracks { get; init; } = System.Array.Empty<ScoredTrack>();
    public IReadOnlyList<ScoredReview> Reviews { get; init; } = System.Array.Empty<ScoredReview>();
    public IReadOnlyList<Product> Products { get; init; } = System.Array.Empty<Product>();

    public IReadOnlyList<FacetGroup> Facets { get; init; } = System.Array.Empty<FacetGroup>();

    public string? DidYouMean { get; init; }
    public object? TopResult { get; init; }
    public int TotalCount { get; init; }
}

public sealed record AutocompleteHit(string Text, string Kind, int? EntityId = null, string? ImagePath = null);
