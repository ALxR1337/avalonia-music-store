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

    private readonly IDbContextFactory<MusicStoreDbContext> _dbFactory;

    public SearchService(IDbContextFactory<MusicStoreDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public SearchResults Search(string query, SearchFilters? filters = null)
    {
        filters ??= new SearchFilters();
        var ast = SearchQueryParser.Parse(query);

        using var db = _dbFactory.CreateDbContext();

        var hits = ExecuteFtsHits(db, ast);
        var (albums, artists, tracks, reviews) = LoadEntities(db, hits);

        var filteredAlbums = ApplyAlbumFilters(albums, filters).ToList();
        var filteredArtistAlbums = ApplyArtistAlbumFilters(artists, db, filters);

        var products = BuildProducts(db, filteredAlbums.Select(s => s.Album.Id).ToList(), filters);

        var facets = ComputeFacets(db, filteredAlbums, filters);

        string? didYouMean = null;
        if (filteredAlbums.Count + filteredArtistAlbums.Count + tracks.Count < 3
            && ast.Terms.OfType<FreeTextTerm>().Any())
        {
            didYouMean = SuggestSpellingCorrection(db, ast);
        }

        var albumsRanked = filteredAlbums.OrderByDescending(s => s.Score).ToList();
        var artistsRanked = filteredArtistAlbums.OrderByDescending(s => s.Score).ToList();
        var tracksRanked = tracks.OrderByDescending(s => s.Score).ToList();
        var reviewsRanked = reviews.OrderByDescending(s => s.Score).ToList();

        object? topResult = filters.Tab switch
        {
            SearchTab.Artists => artistsRanked.FirstOrDefault(),
            SearchTab.Tracks => tracksRanked.FirstOrDefault(),
            SearchTab.Reviews => reviewsRanked.FirstOrDefault(),
            _ => (object?)albumsRanked.FirstOrDefault() ?? artistsRanked.FirstOrDefault()
        };

        var total = albumsRanked.Count + artistsRanked.Count + tracksRanked.Count + reviewsRanked.Count;

        return new SearchResults
        {
            Query = ast,
            RawQuery = query ?? string.Empty,
            Filters = filters,
            Albums = albumsRanked,
            Artists = artistsRanked,
            Tracks = tracksRanked,
            Reviews = reviewsRanked,
            Products = products,
            Facets = facets,
            DidYouMean = didYouMean,
            TopResult = topResult,
            TotalCount = total
        };
    }

    // === FTS5 execution ===

    private sealed record FtsHit(string ContentType, int ContentId, double Relevance);

    private List<FtsHit> ExecuteFtsHits(MusicStoreDbContext db, SearchQuery ast)
    {
        var matchClause = BuildMatchClause(ast, out var typeRestriction);

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var hits = new List<FtsHit>();

        if (string.IsNullOrWhiteSpace(matchClause))
        {
            // No text constraints — fall back to a browse-mode candidate set:
            // newest+most-popular albums and artists, so filters still produce results.
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ct, cid FROM (
                    SELECT 'album' AS ct, Id AS cid, Year AS ord FROM Albums ORDER BY Year DESC LIMIT 50
                )
                UNION ALL
                SELECT ct, cid FROM (
                    SELECT 'artist' AS ct, Id AS cid FROM Artists LIMIT 50
                );";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                hits.Add(new FtsHit(r.GetString(0), r.GetInt32(1), 1.0));
            return hits;
        }

        using (var cmd = conn.CreateCommand())
        {
            var sql = new StringBuilder(@"
                SELECT content_type, content_id, -bm25(SearchIndex) AS relevance
                FROM SearchIndex
                WHERE SearchIndex MATCH $match");
            if (typeRestriction.Count > 0)
            {
                var list = string.Join(",", typeRestriction.Select((_, i) => $"$ct{i}"));
                sql.Append($" AND content_type IN ({list})");
                for (int i = 0; i < typeRestriction.Count; i++)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"$ct{i}";
                    p.Value = typeRestriction[i];
                    cmd.Parameters.Add(p);
                }
            }
            sql.Append(" ORDER BY relevance DESC LIMIT ").Append(MaxFtsHits).Append(';');
            cmd.CommandText = sql.ToString();

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

    private static string BuildMatchClause(SearchQuery q, out List<string> typeRestriction)
    {
        typeRestriction = new List<string>();
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
                case FieldTextTerm ftx when ftx.Field is "artist" or "album" or "track" or "lyrics":
                    parts.Add((ftx.Excluded ? "-" : "") + EscapeTerm(ftx.Value) + "*");
                    if (!ftx.Excluded) typeRestriction.Add(FieldToContentType(ftx.Field));
                    break;
                case FieldPhraseTerm fpx when fpx.Field is "artist" or "album" or "track" or "lyrics":
                    parts.Add((fpx.Excluded ? "-" : "") + "\"" + fpx.Phrase.Replace("\"", "\"\"") + "\"");
                    if (!fpx.Excluded) typeRestriction.Add(FieldToContentType(fpx.Field));
                    break;
                // genre/format/year/price/rating are post-filters — ignore here.
            }
        }
        typeRestriction = typeRestriction.Distinct().ToList();
        return string.Join(" ", parts);
    }

    private static string FieldToContentType(string field) => field switch
    {
        "artist" => "artist",
        "album" => "album",
        "track" or "lyrics" => "track",
        _ => "album"
    };

    private static string EscapeTerm(string raw)
    {
        var sb = new StringBuilder();
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        }
        return sb.Length == 0 ? raw : sb.ToString();
    }

    // === Entity loading ===

    private (
        List<ScoredAlbum> albums,
        List<ScoredArtist> artists,
        List<ScoredTrack> tracks,
        List<ScoredReview> reviews)
        LoadEntities(MusicStoreDbContext db, List<FtsHit> hits)
    {
        var albumIds = hits.Where(h => h.ContentType == "album").Select(h => h.ContentId).Distinct().ToList();
        var artistIds = hits.Where(h => h.ContentType == "artist").Select(h => h.ContentId).Distinct().ToList();
        var trackIds = hits.Where(h => h.ContentType == "track").Select(h => h.ContentId).Distinct().ToList();
        var reviewIds = hits.Where(h => h.ContentType == "review").Select(h => h.ContentId).Distinct().ToList();

        var albumRows = albumIds.Count == 0 ? new List<Album>() : db.Albums.AsNoTracking()
            .Include(a => a.Artist)
            .Include(a => a.Tracks)
            .Include(a => a.AlbumGenres).ThenInclude(ag => ag.Genre)
            .Where(a => albumIds.Contains(a.Id))
            .ToList();

        var artistRows = artistIds.Count == 0 ? new List<Artist>() : db.Artists.AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .ToList();

        var trackRows = trackIds.Count == 0 ? new List<Track>() : db.Tracks.AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .ToList();

        var reviewRows = reviewIds.Count == 0 ? new List<Review>() : db.Reviews.AsNoTracking()
            .Where(r => reviewIds.Contains(r.Id))
            .ToList();

        var bm25ByAlbum = hits.Where(h => h.ContentType == "album").ToDictionary(h => h.ContentId, h => h.Relevance);
        var bm25ByArtist = hits.Where(h => h.ContentType == "artist").ToDictionary(h => h.ContentId, h => h.Relevance);
        var bm25ByTrack = hits.Where(h => h.ContentType == "track").ToDictionary(h => h.ContentId, h => h.Relevance);
        var bm25ByReview = hits.Where(h => h.ContentType == "review").ToDictionary(h => h.ContentId, h => h.Relevance);

        var maxBm25 = hits.Count == 0 ? 1.0 : Math.Max(1e-9, hits.Max(h => h.Relevance));

        // Sales-per-album (for popularity component on album rows)
        var salesPerAlbum = albumIds.Count == 0
            ? new Dictionary<int, int>()
            : db.Products.AsNoTracking()
                .Where(p => albumIds.Contains(p.AlbumId))
                .GroupBy(p => p.AlbumId)
                .Select(g => new { AlbumId = g.Key, Sales = g.Sum(p => p.SalesCount) })
                .ToDictionary(x => x.AlbumId, x => x.Sales);

        var ratingPerAlbum = albumIds.Count == 0
            ? new Dictionary<int, double>()
            : db.Products.AsNoTracking()
                .Where(p => albumIds.Contains(p.AlbumId) && p.Rating > 0)
                .GroupBy(p => p.AlbumId)
                .Select(g => new { AlbumId = g.Key, Rating = g.Average(p => p.Rating) })
                .ToDictionary(x => x.AlbumId, x => x.Rating);

        var maxSales = salesPerAlbum.Values.DefaultIfEmpty(0).Max();
        var logMaxSales = maxSales <= 0 ? 1.0 : Math.Log(1.0 + maxSales);

        var albums = albumRows.Select(a =>
        {
            var bm = bm25ByAlbum.GetValueOrDefault(a.Id, 0.0);
            var bmNorm = bm / maxBm25;
            var sales = salesPerAlbum.GetValueOrDefault(a.Id, 0);
            var pop = Math.Log(1.0 + sales) / logMaxSales;
            var rating = ratingPerAlbum.GetValueOrDefault(a.Id, 0.0) / 5.0;
            var recency = RecencyFromYear(a.Year);
            var score = WeightBm25 * bmNorm + WeightPopularity * pop + WeightRating * rating + WeightRecency * recency;
            return new ScoredAlbum(a, score, bm, pop);
        }).ToList();

        var artists = artistRows.Select(a =>
        {
            var bm = bm25ByArtist.GetValueOrDefault(a.Id, 0.0);
            var bmNorm = bm / maxBm25;
            var score = WeightBm25 * bmNorm;
            return new ScoredArtist(a, score, bm);
        }).ToList();

        var tracks = trackRows.Select(t =>
        {
            var bm = bm25ByTrack.GetValueOrDefault(t.Id, 0.0);
            var bmNorm = bm / maxBm25;
            var score = WeightBm25 * bmNorm;
            return new ScoredTrack(t, score, bm);
        }).ToList();

        var reviews = reviewRows.Select(r =>
        {
            var bm = bm25ByReview.GetValueOrDefault(r.Id, 0.0);
            var bmNorm = bm / maxBm25;
            var score = WeightBm25 * bmNorm;
            return new ScoredReview(r, score, bm);
        }).ToList();

        return (albums, artists, tracks, reviews);
    }

    private static double RecencyFromYear(int year)
    {
        if (year <= 0) return 0.0;
        var days = (DateTime.UtcNow - new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays;
        if (days < 0) days = 0;
        return Math.Exp(-days / 365.0);
    }

    // === Filters ===

    private static IEnumerable<ScoredAlbum> ApplyAlbumFilters(List<ScoredAlbum> albums, SearchFilters f)
    {
        foreach (var s in albums)
        {
            var a = s.Album;
            if (f.YearFrom is int yf && a.Year < yf) continue;
            if (f.YearTo is int yt && a.Year > yt) continue;
            if (!string.IsNullOrEmpty(f.Genre) && !AlbumMatchesGenre(a, f.Genre)) continue;
            yield return s;
        }
    }

    private static bool AlbumMatchesGenre(Album a, string genre)
    {
        if (string.Equals(a.Genre?.Name, genre, StringComparison.OrdinalIgnoreCase)) return true;
        return a.AlbumGenres?.Any(ag =>
            string.Equals(ag.Genre?.Name, genre, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private List<ScoredArtist> ApplyArtistAlbumFilters(List<ScoredArtist> artists, MusicStoreDbContext db, SearchFilters f)
    {
        // Artist hits are not filtered by year/price/etc. — they only carry text relevance.
        return artists;
    }

    private List<Product> BuildProducts(MusicStoreDbContext db, List<int> albumIds, SearchFilters f)
    {
        if (albumIds.Count == 0) return new List<Product>();

        var query = db.Products.AsNoTracking()
            .Include(p => p.Album)!.ThenInclude(a => a!.Artist)
            .Include(p => p.Album)!.ThenInclude(a => a!.AlbumGenres).ThenInclude(ag => ag.Genre)
            .Where(p => albumIds.Contains(p.AlbumId));

        if (f.Format is ProductFormat fmt) query = query.Where(p => p.Format == fmt);
        if (f.PriceFrom is decimal pf)
        {
            var pfCents = (long)Math.Round(pf * 100m, MidpointRounding.AwayFromZero);
            query = query.Where(p => p.PriceCents >= pfCents);
        }
        if (f.PriceTo is decimal pt)
        {
            var ptCents = (long)Math.Round(pt * 100m, MidpointRounding.AwayFromZero);
            query = query.Where(p => p.PriceCents <= ptCents);
        }
        if (f.MinRating is double mr) query = query.Where(p => p.Rating >= mr);
        if (f.InStockOnly) query = query.Where(p => p.Stock > 0);
        if (!string.IsNullOrEmpty(f.Genre))
            query = query.Where(p => p.Album!.AlbumGenres.Any(ag => ag.Genre!.Name == f.Genre));
        if (f.YearFrom is int yf) query = query.Where(p => p.Album!.Year >= yf);
        if (f.YearTo is int yt) query = query.Where(p => p.Album!.Year <= yt);

        return query.OrderBy(p => p.AlbumId).ThenBy(p => p.Format).ToList();
    }

    // === Facets ===

    private List<FacetGroup> ComputeFacets(MusicStoreDbContext db, List<ScoredAlbum> albums, SearchFilters filters)
    {
        if (albums.Count == 0)
            return new List<FacetGroup>();

        var albumIds = albums.Select(s => s.Album.Id).ToList();

        var products = db.Products.AsNoTracking()
            .Include(p => p.Album)!.ThenInclude(a => a!.AlbumGenres).ThenInclude(ag => ag.Genre)
            .Where(p => albumIds.Contains(p.AlbumId))
            .ToList();

        // Each album contributes to every genre it sits in (primary + extras live
        // together in AlbumGenres).
        var genreBuckets = products
            .SelectMany(p =>
            {
                var names = p.Album?.AlbumGenres?
                    .Where(ag => ag.Genre?.Name is not null)
                    .Select(ag => ag.Genre!.Name)
                    ?? Enumerable.Empty<string>();
                return names.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new { Name = name, AlbumId = p.AlbumId });
            })
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FacetBucket("genre", g.Key, g.Select(x => x.AlbumId).Distinct().Count(),
                string.Equals(g.Key, filters.Genre, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(b => b.Count)
            .ToList();

        var formatBuckets = products
            .GroupBy(p => p.Format)
            .Select(g => new FacetBucket("format", g.Key == ProductFormat.Vinyl ? "Вініл LP" : "CD", g.Count(),
                filters.Format == g.Key))
            .ToList();

        var inStock = products.Count(p => p.Stock > 0);

        return new List<FacetGroup>
        {
            new("genre", "Жанр", genreBuckets),
            new("format", "Формат", formatBuckets),
            new("stock", "Наявність", new List<FacetBucket>
            {
                new("stock", "Тільки в наявності", inStock, filters.InStockOnly)
            })
        };
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

        var albumCovers = albumIds.Count == 0
            ? new Dictionary<int, string?>()
            : db.Albums.AsNoTracking()
                .Where(a => albumIds.Contains(a.Id))
                .Select(a => new { a.Id, a.CoverPath })
                .ToDictionary(a => a.Id, a => a.CoverPath);

        var trackCovers = trackIds.Count == 0
            ? new Dictionary<int, string?>()
            : (from t in db.Tracks.AsNoTracking()
               join a in db.Albums.AsNoTracking() on t.AlbumId equals a.Id
               where trackIds.Contains(t.Id)
               select new { t.Id, a.CoverPath })
              .ToDictionary(x => x.Id, x => x.CoverPath);

        var artistPhotos = artistIds.Count == 0
            ? new Dictionary<int, string?>()
            : db.Artists.AsNoTracking()
                .Where(a => artistIds.Contains(a.Id))
                .Select(a => new { a.Id, a.PhotoPath })
                .ToDictionary(a => a.Id, a => a.PhotoPath);

        var results = new List<AutocompleteHit>(raw.Count);
        foreach (var (type, id, title) in raw)
        {
            string? imagePath = type switch
            {
                "album" => albumCovers.TryGetValue(id, out var c) ? c : null,
                "track" => trackCovers.TryGetValue(id, out var c) ? c : null,
                "artist" => artistPhotos.TryGetValue(id, out var p) ? p : null,
                _ => null
            };
            results.Add(new AutocompleteHit(title, type, id, imagePath));
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
