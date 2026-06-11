using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;
using MusicApp.Services.Search;

namespace MusicApp.Services;

public class SearchService : ISearchService
{
    // Ranking model hyperparameters (per maket §8.7).
    private const double WeightBm25 = 0.6;
    private const double WeightPopularity = 0.2;
    private const double WeightRating = 0.1;
    private const double WeightRecency = 0.1;
    private const int MaxFtsHits = 200;
    private const int MaxArtistFacets = 12;

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private readonly IDbContextFactory<MusicStoreDbContext> _dbFactory;

    public SearchService(IDbContextFactory<MusicStoreDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // The store sells albums, so search is album-centric: FTS hits on tracks,
    // lyrics and artists all fold into the albums they belong to. Genre and
    // artist are multi-select OR facets; the numeric/format/stock constraints AND.
    public SearchResults Search(string query, SearchFilters? filters = null)
    {
        filters ??= new SearchFilters();
        var ast = SearchQueryParser.Parse(query);

        using var db = _dbFactory.CreateDbContext();

        var hits = ExecuteFtsHits(db, ast);
        var candidates = RollupToAlbums(db, hits);

        var albums = BuildHits(candidates, filters);
        var facets = ComputeFacets(candidates, filters);

        string? didYouMean = null;
        if (albums.Count < 3 && ast.Terms.OfType<FreeTextTerm>().Any())
            didYouMean = SuggestSpellingCorrection(db, ast);

        return new SearchResults
        {
            Query = ast,
            RawQuery = query ?? string.Empty,
            Filters = filters,
            Albums = albums,
            Facets = facets,
            DidYouMean = didYouMean,
            TopResult = albums.FirstOrDefault(),
            TotalCount = albums.Count
        };
    }

    // === FTS5 execution ===

    private sealed record FtsHit(string ContentType, int ContentId, double Relevance);

    private List<FtsHit> ExecuteFtsHits(MusicStoreDbContext db, SearchQuery ast)
    {
        var matchClause = BuildMatchClause(ast);

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var hits = new List<FtsHit>();

        if (string.IsNullOrWhiteSpace(matchClause))
        {
            // Browse mode (no text constraints): every album is a candidate so
            // genre/artist/year filters over an empty query stay complete.
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 'album' AS ct, Id AS cid FROM Albums;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                hits.Add(new FtsHit(r.GetString(0), r.GetInt32(1), 1.0));
            return hits;
        }

        using (var cmd = conn.CreateCommand())
        {
            // No content_type narrowing beyond dropping reviews: an artist match
            // must still surface that artist's albums (that was the bug behind
            // "виконавець:..." opening an empty profile), and tracks fold into albums.
            cmd.CommandText = @"
                SELECT content_type, content_id, -bm25(SearchIndex) AS relevance
                FROM SearchIndex
                WHERE SearchIndex MATCH $match
                  AND content_type IN ('album','track','artist')
                ORDER BY relevance DESC
                LIMIT " + MaxFtsHits + ";";

            var matchParam = cmd.CreateParameter();
            matchParam.ParameterName = "$match";
            matchParam.Value = matchClause;
            cmd.Parameters.Add(matchParam);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                hits.Add(new FtsHit(r.GetString(0), r.GetInt32(1), r.GetDouble(2)));
        }

        return hits;
    }

    private static string BuildMatchClause(SearchQuery q)
    {
        var parts = new List<string>();

        foreach (var t in q.Terms)
        {
            switch (t)
            {
                case FreeTextTerm ft when !string.IsNullOrWhiteSpace(ft.Text):
                    parts.Add((ft.Excluded ? "-" : "") + EscapeTerm(ft.Text) + "*");
                    break;
                case PhraseTerm p:
                    parts.Add((p.Excluded ? "-" : "") + "\"" + p.Phrase.Replace("\"", "\"\"") + "\"");
                    break;
                // Field operators still bias the text match (prefix/phrase/exclusion)
                // but no longer pin content_type — the rollup decides which albums win.
                case FieldTextTerm ftx when ftx.Field is "artist" or "album" or "track" or "lyrics":
                    parts.Add((ftx.Excluded ? "-" : "") + EscapeTerm(ftx.Value) + "*");
                    break;
                case FieldPhraseTerm fpx when fpx.Field is "artist" or "album" or "track" or "lyrics":
                    parts.Add((fpx.Excluded ? "-" : "") + "\"" + fpx.Phrase.Replace("\"", "\"\"") + "\"");
                    break;
                // genre/format/year/price/rating are post-filters — ignore here.
            }
        }
        return string.Join(" ", parts);
    }

    private static string EscapeTerm(string raw)
    {
        var sb = new StringBuilder();
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        }
        return sb.Length == 0 ? raw : sb.ToString();
    }

    // === Roll FTS hits up to albums ===

    private sealed class AlbumCandidate
    {
        public Album Album = null!;
        public List<Product> Products = new();
        public double Bm25;                       // best contributing raw bm25
        public AlbumMatchKind Match;
        public List<MatchedTrack> MatchedTracks = new();
        public double Score;
    }

    private List<AlbumCandidate> RollupToAlbums(MusicStoreDbContext db, List<FtsHit> hits)
    {
        if (hits.Count == 0) return new List<AlbumCandidate>();

        var bm25ByAlbum = hits.Where(h => h.ContentType == "album")
            .GroupBy(h => h.ContentId).ToDictionary(g => g.Key, g => g.Max(h => h.Relevance));
        var bm25ByTrack = hits.Where(h => h.ContentType == "track")
            .GroupBy(h => h.ContentId).ToDictionary(g => g.Key, g => g.Max(h => h.Relevance));
        var bm25ByArtist = hits.Where(h => h.ContentType == "artist")
            .GroupBy(h => h.ContentId).ToDictionary(g => g.Key, g => g.Max(h => h.Relevance));

        var maxBm25 = Math.Max(1e-9, hits.Max(h => h.Relevance));

        var trackIds = bm25ByTrack.Keys.ToList();
        var trackRows = trackIds.Count == 0
            ? new List<Track>()
            : db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id)).ToList();

        var artistIds = bm25ByArtist.Keys.ToList();
        var artistAlbumRows = artistIds.Count == 0
            ? new List<(int AlbumId, int ArtistId)>()
            : db.Albums.AsNoTracking()
                .Where(a => artistIds.Contains(a.ArtistId))
                .Select(a => new { a.Id, a.ArtistId })
                .AsEnumerable()
                .Select(a => (AlbumId: a.Id, a.ArtistId))
                .ToList();

        var acc = new Dictionary<int, AlbumCandidate>();
        AlbumCandidate Get(int albumId)
        {
            if (!acc.TryGetValue(albumId, out var c)) { c = new AlbumCandidate(); acc[albumId] = c; }
            return c;
        }

        foreach (var (albumId, bm) in bm25ByAlbum)
        {
            var c = Get(albumId);
            c.Bm25 = Math.Max(c.Bm25, bm);
            c.Match |= AlbumMatchKind.Title;
        }
        foreach (var t in trackRows)
        {
            var c = Get(t.AlbumId);
            c.Bm25 = Math.Max(c.Bm25, bm25ByTrack.GetValueOrDefault(t.Id, 0.0));
            c.Match |= AlbumMatchKind.Track;
            c.MatchedTracks.Add(new MatchedTrack(t, AlbumMatchKind.Track));
        }
        foreach (var row in artistAlbumRows)
        {
            var c = Get(row.AlbumId);
            c.Bm25 = Math.Max(c.Bm25, bm25ByArtist.GetValueOrDefault(row.ArtistId, 0.0));
            c.Match |= AlbumMatchKind.Artist;
        }

        var albumIds = acc.Keys.ToList();
        if (albumIds.Count == 0) return new List<AlbumCandidate>();

        var albums = db.Albums.AsNoTracking()
            .Include(a => a.Artist)
            .Include(a => a.AlbumGenres).ThenInclude(ag => ag.Genre)
            .Where(a => albumIds.Contains(a.Id))
            .ToList();

        var productsByAlbum = db.Products.AsNoTracking()
            .Where(p => albumIds.Contains(p.AlbumId))
            .ToList()
            .GroupBy(p => p.AlbumId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var salesByAlbum = productsByAlbum
            .ToDictionary(kv => kv.Key, kv => kv.Value.Sum(p => p.SalesCount));
        var maxSales = salesByAlbum.Values.DefaultIfEmpty(0).Max();
        var logMaxSales = maxSales <= 0 ? 1.0 : Math.Log(1.0 + maxSales);

        var list = new List<AlbumCandidate>(albums.Count);
        foreach (var a in albums)
        {
            var c = acc[a.Id];
            c.Album = a;
            c.Products = productsByAlbum.GetValueOrDefault(a.Id, new List<Product>());

            var bmNorm = c.Bm25 / maxBm25;
            var sales = salesByAlbum.GetValueOrDefault(a.Id, 0);
            var pop = Math.Log(1.0 + sales) / logMaxSales;
            var rating = c.Products.Where(p => p.Rating > 0).Select(p => p.Rating).DefaultIfEmpty(0).Average() / 5.0;
            var recency = RecencyFromYear(a.Year);
            c.Score = WeightBm25 * bmNorm + WeightPopularity * pop + WeightRating * rating + WeightRecency * recency;

            list.Add(c);
        }
        return list;
    }

    private static double RecencyFromYear(int year)
    {
        if (year <= 0) return 0.0;
        var days = (DateTime.UtcNow - new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays;
        if (days < 0) days = 0;
        return Math.Exp(-days / 365.0);
    }

    // === Filters → purchasable album hits ===

    private static List<AlbumHit> BuildHits(List<AlbumCandidate> candidates, SearchFilters f)
    {
        var hits = new List<AlbumHit>();
        foreach (var c in candidates)
        {
            var a = c.Album;
            if (f.YearFrom is int yf && a.Year < yf) continue;
            if (f.YearTo is int yt && a.Year > yt) continue;
            if (f.Genres.Count > 0 && !AlbumMatchesGenres(a, f.Genres, f.GenresMatchAll)) continue;
            if (f.Artists.Count > 0 && !ArtistMatches(a, f.Artists)) continue;

            // An album shows up only if it has a purchasable product that clears the
            // numeric/format/stock filters — that product becomes the card's primary.
            var primary = c.Products
                .Where(p => ProductPasses(p, f))
                .OrderBy(p => p.PriceCents)
                .FirstOrDefault();
            if (primary is null) continue;

            hits.Add(new AlbumHit(a, primary, c.Score, c.Match, c.MatchedTracks));
        }
        return hits.OrderByDescending(h => h.Score).ToList();
    }

    private static bool ProductPasses(Product p, SearchFilters f)
    {
        if (!p.IsActive) return false;
        if (f.Format is ProductFormat fmt && p.Format != fmt) return false;
        if (f.PriceFrom is decimal pf && p.Price < pf) return false;
        if (f.PriceTo is decimal pt && p.Price > pt) return false;
        if (f.MinRating is double mr && p.Rating < mr) return false;
        if (f.InStockOnly && p.Stock <= 0) return false;
        return true;
    }

    private static bool AlbumMatchesGenres(Album a, IReadOnlyList<string> genres, bool matchAll)
    {
        var names = new HashSet<string>(
            a.AlbumGenres.Where(ag => ag.Genre?.Name is not null).Select(ag => ag.Genre!.Name), Ci);
        return matchAll ? genres.All(names.Contains) : genres.Any(names.Contains);
    }

    private static bool ArtistMatches(Album a, IReadOnlyList<string> artists)
        => a.Artist?.Name is string n && artists.Contains(n, Ci);

    // === Facets ===

    // Counts are computed over the candidate set narrowed only by the year range,
    // not by the genre/artist/format selections themselves — so toggling one genre
    // does not zero out the other options (correct multi-select facet behaviour).
    private List<FacetGroup> ComputeFacets(List<AlbumCandidate> candidates, SearchFilters filters)
    {
        var facetBase = candidates.Where(c =>
            (filters.YearFrom is not int yf || c.Album.Year >= yf) &&
            (filters.YearTo is not int yt || c.Album.Year <= yt) &&
            c.Products.Any(p => p.IsActive)).ToList();

        if (facetBase.Count == 0) return new List<FacetGroup>();

        var genreBuckets = facetBase
            .SelectMany(c => c.Album.AlbumGenres
                .Where(ag => ag.Genre?.Name is not null)
                .Select(ag => ag.Genre!.Name)
                .Distinct(Ci)
                .Select(name => (Name: name, AlbumId: c.Album.Id)))
            .GroupBy(x => x.Name, Ci)
            .Select(g => new FacetBucket("genre", g.Key, g.Select(x => x.AlbumId).Distinct().Count(),
                filters.Genres.Contains(g.Key, Ci)))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Label)
            .ToList();

        var artistBuckets = facetBase
            .Where(c => c.Album.Artist?.Name is not null)
            .GroupBy(c => c.Album.Artist!.Name, Ci)
            .Select(g => new FacetBucket("artist", g.Key, g.Select(c => c.Album.Id).Distinct().Count(),
                filters.Artists.Contains(g.Key, Ci)))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Label)
            .Take(MaxArtistFacets)
            .ToList();

        var formatBuckets = facetBase
            .SelectMany(c => c.Products.Where(p => p.IsActive)
                .Select(p => (p.Format, AlbumId: c.Album.Id)))
            .GroupBy(x => x.Format)
            .Select(g => new FacetBucket("format", g.Key == ProductFormat.Vinyl ? "Вініл LP" : "CD",
                g.Select(x => x.AlbumId).Distinct().Count(), filters.Format == g.Key))
            .OrderByDescending(b => b.Count)
            .ToList();

        var inStock = facetBase.Count(c => c.Products.Any(p => p.IsActive && p.Stock > 0));

        var groups = new List<FacetGroup>();
        if (genreBuckets.Count > 0) groups.Add(new FacetGroup("genre", "Жанр", genreBuckets));
        if (artistBuckets.Count > 0) groups.Add(new FacetGroup("artist", "Виконавці", artistBuckets));
        if (formatBuckets.Count > 0) groups.Add(new FacetGroup("format", "Формат", formatBuckets));
        groups.Add(new FacetGroup("stock", "Наявність", new List<FacetBucket>
        {
            new("stock", "Тільки в наявності", inStock, filters.InStockOnly)
        }));
        return groups;
    }

    // === Did-you-mean (fuzzy) ===

    private string? SuggestSpellingCorrection(MusicStoreDbContext db, SearchQuery ast)
    {
        var firstFreeText = ast.Terms.OfType<FreeTextTerm>().FirstOrDefault();
        if (firstFreeText is null || firstFreeText.Text.Length < 3) return null;

        var query = firstFreeText.Text;
        var candidates = new List<string>();
        candidates.AddRange(db.Artists.AsNoTracking().Select(a => a.Name).Take(200).ToList());
        candidates.AddRange(db.Albums.AsNoTracking().Select(a => a.Title).Take(200).ToList());

        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var d = Levenshtein.Distance(query, c);
            if (d < bestDist) { bestDist = d; best = c; }
        }

        if (best is null) return null;
        var threshold = Math.Max(2, query.Length / 3);
        return bestDist <= threshold ? best : null;
    }

    // === Autocomplete ===

    public IReadOnlyList<AutocompleteHit> Autocomplete(string prefix, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2)
            return System.Array.Empty<AutocompleteHit>();

        var cleaned = EscapeTerm(prefix.Trim());
        if (cleaned.Length < 2) return System.Array.Empty<AutocompleteHit>();

        using var db = _dbFactory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var raw = new List<(string Type, int Id, string Title)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT content_type, content_id, title
                FROM SearchIndex
                WHERE SearchIndex MATCH $m
                  AND content_type IN ('artist','album','track')
                ORDER BY -bm25(SearchIndex) DESC
                LIMIT $lim;";
            var p1 = cmd.CreateParameter(); p1.ParameterName = "$m"; p1.Value = cleaned + "*"; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "$lim"; p2.Value = max; cmd.Parameters.Add(p2);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                raw.Add((r.GetString(0), r.GetInt32(1), r.GetString(2)));
        }

        if (raw.Count == 0) return System.Array.Empty<AutocompleteHit>();

        // Batch-fetch image paths per kind so each suggestion can render the
        // real cover/photo instead of a generic icon.
        var albumIds = raw.Where(x => x.Type == "album").Select(x => x.Id).ToList();
        var trackIds = raw.Where(x => x.Type == "track").Select(x => x.Id).ToList();
        var artistIds = raw.Where(x => x.Type == "artist").Select(x => x.Id).ToList();

        // A track suggestion resolves to its album (the purchasable unit), so we
        // re-key it onto the album for both cover and navigation.
        var trackAlbumId = trackIds.Count == 0
            ? new Dictionary<int, int>()
            : db.Tracks.AsNoTracking()
                .Where(t => trackIds.Contains(t.Id))
                .Select(t => new { t.Id, t.AlbumId })
                .ToDictionary(x => x.Id, x => x.AlbumId);

        var coverAlbumIds = albumIds.Concat(trackAlbumId.Values).Distinct().ToList();
        var albumCovers = coverAlbumIds.Count == 0
            ? new Dictionary<int, string?>()
            : db.Albums.AsNoTracking()
                .Where(a => coverAlbumIds.Contains(a.Id))
                .Select(a => new { a.Id, a.CoverPath })
                .ToDictionary(a => a.Id, a => a.CoverPath);

        var artistPhotos = artistIds.Count == 0
            ? new Dictionary<int, string?>()
            : db.Artists.AsNoTracking()
                .Where(a => artistIds.Contains(a.Id))
                .Select(a => new { a.Id, a.PhotoPath })
                .ToDictionary(a => a.Id, a => a.PhotoPath);

        var results = new List<AutocompleteHit>(raw.Count);
        foreach (var (type, id, title) in raw)
        {
            switch (type)
            {
                case "album":
                    results.Add(new AutocompleteHit(title, "album", id, albumCovers.GetValueOrDefault(id)));
                    break;
                case "track" when trackAlbumId.TryGetValue(id, out var albumId):
                    results.Add(new AutocompleteHit(title, "album", albumId, albumCovers.GetValueOrDefault(albumId)));
                    break;
                case "artist":
                    results.Add(new AutocompleteHit(title, "artist", id, artistPhotos.GetValueOrDefault(id)));
                    break;
            }
        }
        return results;
    }

    // === History + saved searches ===

    public void RecordHistory(int userId, string query, int resultCount)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(query)) return;
        try
        {
            using var db = _dbFactory.CreateDbContext();
            db.SearchHistory.Add(new SearchHistory
            {
                UserId = userId,
                Query = query.Trim(),
                ExecutedAt = DateTime.UtcNow,
                ResultCount = resultCount
            });
            db.SaveChanges();
        }
        catch { /* non-critical */ }
    }

    public int SaveSearch(int userId, string name, string queryJson, bool notifyOnNew)
    {
        if (userId <= 0) return 0;
        using var db = _dbFactory.CreateDbContext();
        var row = new SavedSearch
        {
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(name) ? queryJson : name,
            QueryJson = queryJson,
            NotifyOnNew = notifyOnNew,
            CreatedAt = DateTime.UtcNow
        };
        db.SavedSearches.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    public IReadOnlyList<SavedSearch> ListSavedSearches(int userId)
    {
        if (userId <= 0) return System.Array.Empty<SavedSearch>();
        using var db = _dbFactory.CreateDbContext();
        return db.SavedSearches.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public IReadOnlyList<SavedSearchSummary> ListSavedSearchSummaries(int userId)
    {
        var rows = ListSavedSearches(userId);
        if (rows.Count == 0) return System.Array.Empty<SavedSearchSummary>();

        var result = new List<SavedSearchSummary>(rows.Count);
        foreach (var s in rows)
        {
            int count = 0;
            try
            {
                count = Search(s.QueryJson).TotalCount;
            }
            catch { /* malformed query stays at 0 */ }
            result.Add(new SavedSearchSummary(s, count));
        }
        return result;
    }

    public void SetSavedSearchNotify(int id, bool notifyOnNew)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.SavedSearches.FirstOrDefault(s => s.Id == id);
        if (row is null) return;
        row.NotifyOnNew = notifyOnNew;
        db.SaveChanges();
    }

    public void DeleteSavedSearch(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.SavedSearches.FirstOrDefault(s => s.Id == id);
        if (row is null) return;
        db.SavedSearches.Remove(row);
        db.SaveChanges();
    }
}
