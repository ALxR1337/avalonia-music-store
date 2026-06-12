using System;
using System.Collections.Generic;
using MusicApp.Models;

namespace MusicApp.Services.Search;

// The store sells albums (as Vinyl/CD products), so search is album-centric:
// every match — by album title, by an album's track/lyrics, or by its artist —
// resolves to one or more albums. Genre and artist are multi-select facets
// (OR within a facet); the remaining numeric/bool constraints AND together.
public sealed record SearchFilters(
    int? YearFrom = null,
    int? YearTo = null,
    decimal? PriceFrom = null,
    decimal? PriceTo = null,
    double? MinRating = null,
    ProductFormat? Format = null,
    IReadOnlyList<string>? Genres = null,
    IReadOnlyList<string>? Artists = null,
    bool InStockOnly = false,
    // false → album matches ANY selected genre (union); true → must carry ALL of them.
    bool GenresMatchAll = false)
{
    public IReadOnlyList<string> Genres { get; init; } = Genres ?? Array.Empty<string>();
    public IReadOnlyList<string> Artists { get; init; } = Artists ?? Array.Empty<string>();
}

public sealed record FacetBucket(string Field, string Label, int Count, bool IsActive = false);

public sealed record FacetGroup(string Field, string Title, IReadOnlyList<FacetBucket> Buckets)
{
    public bool IsGenre => Field == "genre";
}

// Why an album surfaced. Combined with OR across all the hits that fold into the
// same album, so a single album can match on several axes at once.
[Flags]
public enum AlbumMatchKind
{
    None = 0,
    Title = 1,
    Description = 2,
    Artist = 4,
    Track = 8,
    Lyrics = 16,
    Browse = 32,
}

public sealed record MatchedTrack(Track Track, AlbumMatchKind Kind);

public sealed record AlbumHit(
    Album Album,
    Product? PrimaryProduct,
    double Score,
    AlbumMatchKind Match,
    IReadOnlyList<MatchedTrack> MatchedTracks,
    // True when several editions at different prices passed the active filters,
    // so the card's price is the cheapest of them, not "the" price.
    bool PriceIsFrom = false)
{
    public bool HasMatchedTracks => MatchedTracks.Count > 0;

    // First track the query hit, for the "↳ знайдено в треку «…»" annotation.
    public Track? FirstMatchedTrack => MatchedTracks.Count > 0 ? MatchedTracks[0].Track : null;

    public decimal? Price => PrimaryProduct?.Price;

    // "від 250 ₴" while multiple editions qualify; the bare price once the
    // filters (e.g. a chosen format) pin the card to a single edition.
    public string? PriceLabel => Price is decimal p
        ? (PriceIsFrom ? $"від {p:0} ₴" : $"{p:0} ₴")
        : null;
}

public sealed class SearchResults
{
    public SearchQuery Query { get; init; } = new();
    public string RawQuery { get; init; } = string.Empty;
    public SearchFilters Filters { get; init; } = new();

    public IReadOnlyList<AlbumHit> Albums { get; init; } = Array.Empty<AlbumHit>();

    public IReadOnlyList<FacetGroup> Facets { get; init; } = Array.Empty<FacetGroup>();

    public string? DidYouMean { get; init; }
    public AlbumHit? TopResult { get; init; }
    public int TotalCount { get; init; }
}

// Kind: "album" | "track" (EntityId still carries the album id — the store
// sells albums) | "artist" | "history". Subtitle is the human-readable,
// localized second line («альбом · Kanye West», «трек · з альбому «…»»).
public sealed record AutocompleteHit(string Text, string Kind, int? EntityId = null, string? ImagePath = null, string? Subtitle = null);
