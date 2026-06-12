using System;
using System.Collections.Generic;
using System.Data.Common;
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
        var hasTextQuery = !string.IsNullOrWhiteSpace(BuildMatchClause(ast));

        using var db = _dbFactory.CreateDbContext();

        var hits = ExecuteFtsHits(db, ast);
        var candidates = RollupToAlbums(db, hits);

        var albums = BuildHits(candidates, filters);
        var facets = ComputeFacets(candidates, filters);

        // Suggest a correction only when the TEXT itself matched nothing.
        // With candidates on hand the spelling is fine (filters may have
        // narrowed them away), and the old `< 3` threshold used to "suggest"
        // the user's own words back next to real results.
        string? didYouMean = null;
        if (hasTextQuery && candidates.Count == 0)
            didYouMean = SuggestSpellingCorrection(db, ast);

        return new SearchResults
        {
            Query = ast,
            RawQuery = query ?? string.Empty,
            Filters = filters,
            Albums = albums,
            Facets = facets,
            DidYouMean = didYouMean,
            // The block duplicates the first card, so it earns its keep only
            // when a text query produced an actual ranking to crown — browse
            // mode and single-result pages get nothing extra.
            TopResult = hasTextQuery && albums.Count >= 2 ? albums[0] : null,
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

        try
        {
            using var cmd = conn.CreateCommand();
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
        catch (DbException)
        {
            // FTS5 has its own query grammar; whatever EscapeTerm let through
            // that it still dislikes must read as "nothing found", not a crash
            // of the whole app from the global search box.
            hits.Clear();
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
                {
                    foreach (var w in FtsWords(ft.Text))
                        parts.Add((ft.Excluded ? "-" : "") + w + "*");
                    break;
                }
                case PhraseTerm p when !string.IsNullOrWhiteSpace(p.Phrase):
                    parts.Add((p.Excluded ? "-" : "") + "\"" + p.Phrase.Replace("\"", "\"\"") + "\"");
                    break;
                // Field operators still bias the text match (prefix/phrase/exclusion)
                // but no longer pin content_type — the rollup decides which albums win.
                case FieldTextTerm ftx when ftx.Field is "artist" or "album" or "track" or "lyrics":
                {
                    foreach (var w in FtsWords(ftx.Value))
                        parts.Add((ftx.Excluded ? "-" : "") + w + "*");
                    break;
                }
                case FieldPhraseTerm fpx when fpx.Field is "artist" or "album" or "track" or "lyrics"
                                              && !string.IsNullOrWhiteSpace(fpx.Phrase):
                    parts.Add((fpx.Excluded ? "-" : "") + "\"" + fpx.Phrase.Replace("\"", "\"\"") + "\"");
                    break;
                // genre/format/year/price/rating are post-filters — ignore here.
            }
        }
        return string.Join(" ", parts);
    }

    // Splits raw user text into FTS-safe tokens. Punctuation SEPARATES words —
    // «AC/DC» becomes [AC, DC] matching the indexed tokens, where the old
    // strip-and-glue version produced «ACDC», a token FTS never indexed. May
    // return an empty list — the caller must DROP such a term; feeding raw
    // `++` / `(((` into MATCH used to crash the app with a SqliteException.
    private static List<string> FtsWords(string raw)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
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
            // numeric/format/stock filters — the cheapest of those becomes the card's
            // primary; with several distinct passing prices the card reads "від …".
            var passing = c.Products
                .Where(p => ProductPasses(p, f))
                .OrderBy(p => p.PriceCents)
                .ToList();
            if (passing.Count == 0) continue;

            var priceIsFrom = passing.Select(p => p.PriceCents).Distinct().Count() > 1;
            hits.Add(new AlbumHit(a, passing[0], c.Score, c.Match, c.MatchedTracks, priceIsFrom));
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

    // Buckets are enumerated over the year-narrowed candidate set, so options
    // never vanish just because another filter zeroed them; each bucket's COUNT
    // honours every active constraint EXCEPT the bucket's own dimension.
    // Within a dimension selections stay invisible to their own counts (picking
    // Rock must not zero Jazz), but a price cap shows up in every counter —
    // the numbers always promise exactly what clicking the bucket would yield.
    private List<FacetGroup> ComputeFacets(List<AlbumCandidate> candidates, SearchFilters filters)
    {
        bool YearOk(AlbumCandidate c) =>
            (filters.YearFrom is not int yf || c.Album.Year >= yf) &&
            (filters.YearTo is not int yt || c.Album.Year <= yt);
        bool GenresOk(AlbumCandidate c) =>
            filters.Genres.Count == 0 || AlbumMatchesGenres(c.Album, filters.Genres, filters.GenresMatchAll);
        bool ArtistsOk(AlbumCandidate c) =>
            filters.Artists.Count == 0 || ArtistMatches(c.Album, filters.Artists);
        bool ProductOk(Product p, bool ignoreFormat = false, bool ignoreStock = false) =>
            p.IsActive
            && (ignoreFormat || filters.Format is not ProductFormat fmt || p.Format == fmt)
            && (filters.PriceFrom is not decimal pf || p.Price >= pf)
            && (filters.PriceTo is not decimal pt || p.Price <= pt)
            && (filters.MinRating is not double mr || p.Rating >= mr)
            && (ignoreStock || !filters.InStockOnly || p.Stock > 0);

        var enumBase = candidates
            .Where(c => YearOk(c) && c.Products.Any(p => p.IsActive))
            .ToList();
        if (enumBase.Count == 0) return new List<FacetGroup>();

        var genreBase = enumBase.Where(c => ArtistsOk(c) && c.Products.Any(p => ProductOk(p))).ToList();
        var artistBase = enumBase.Where(c => GenresOk(c) && c.Products.Any(p => ProductOk(p))).ToList();
        var crossBase = enumBase.Where(c => GenresOk(c) && ArtistsOk(c)).ToList();

        IEnumerable<string> GenresOf(AlbumCandidate c) => c.Album.AlbumGenres
            .Where(ag => ag.Genre?.Name is not null)
            .Select(ag => ag.Genre!.Name)
            .Distinct(Ci);

        var genreCounts = genreBase
            .SelectMany(c => GenresOf(c).Select(name => (Name: name, AlbumId: c.Album.Id)))
            .GroupBy(x => x.Name, Ci)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AlbumId).Distinct().Count(), Ci);
        var genreBuckets = enumBase
            .SelectMany(GenresOf)
            .Distinct(Ci)
            .Select(name => new FacetBucket("genre", name, genreCounts.GetValueOrDefault(name, 0),
                filters.Genres.Contains(name, Ci)))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Label)
            .ToList();

        var artistCounts = artistBase
            .Where(c => c.Album.Artist?.Name is not null)
            .GroupBy(c => c.Album.Artist!.Name, Ci)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Album.Id).Distinct().Count(), Ci);
        // No top-N cap: «Показати всі (N)» must mean ALL — the old Take(12)
        // also dropped the checkbox of any active artist outside the top.
        var artistBuckets = enumBase
            .Where(c => c.Album.Artist?.Name is not null)
            .Select(c => c.Album.Artist!.Name)
            .Distinct(Ci)
            .Select(name => new FacetBucket("artist", name, artistCounts.GetValueOrDefault(name, 0),
                filters.Artists.Contains(name, Ci)))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Label)
            .ToList();

        var formatBuckets = enumBase
            .SelectMany(c => c.Products.Where(p => p.IsActive).Select(p => p.Format))
            .Distinct()
            .Select(fmt => new FacetBucket("format",
                fmt == ProductFormat.Vinyl ? "Вініл LP" : "CD",
                crossBase.Count(c => c.Products.Any(p => p.Format == fmt && ProductOk(p, ignoreFormat: true))),
                filters.Format == fmt))
            .OrderByDescending(b => b.Count)
            .ToList();

        var inStock = crossBase.Count(c => c.Products.Any(p => p.Stock > 0 && ProductOk(p, ignoreStock: true)));

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
        // Compare the WHOLE free-text part against names: «Bobx Dylan» must be
        // measured as one string, the old first-token-only version could never
        // bridge a multi-word artist name.
        var words = ast.Terms.OfType<FreeTextTerm>()
            .Where(t => !t.Excluded && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text)
            .ToList();
        if (words.Count == 0) return null;
        var query = string.Join(" ", words);
        if (query.Length < 3) return null;

        var candidates = new List<string>();
        candidates.AddRange(db.Artists.AsNoTracking().Select(a => a.Name).Take(500).ToList());
        candidates.AddRange(db.Albums.AsNoTracking().Select(a => a.Title).Take(500).ToList());

        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var d = Levenshtein.Distance(query, c);
            if (d < bestDist) { bestDist = d; best = c; }
        }

        if (best is null) return null;
        // Distance 0 means the query already IS that name (case aside) —
        // suggesting it back is noise, not a correction.
        var threshold = Math.Max(2, query.Length / 3);
        if (bestDist == 0 || bestDist > threshold) return null;

        // Only offer a correction that actually finds something.
        return ExecuteFtsHits(db, SearchQueryParser.Parse(best)).Count > 0 ? best : null;
    }

    // === Autocomplete ===

    public IReadOnlyList<AutocompleteHit> Autocomplete(string prefix, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return System.Array.Empty<AutocompleteHit>();

        // Multi-word input: completed words match as full tokens, the word
        // still being typed matches as a prefix — «december 2» → `december 2*`.
        // (The old sanitiser flattened the whole string to «december2», so the
        // moment a second word appeared every suggestion vanished.)
        var words = FtsWords(prefix.Trim());
        if (words.Count == 0 || words.Sum(w => w.Length) < 2)
            return System.Array.Empty<AutocompleteHit>();
        var match = string.Join(" ", words.Select((w, i) => i == words.Count - 1 ? w + "*" : w));

        using var db = _dbFactory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var raw = new List<(string Type, int Id, string Title)>();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT content_type, content_id, title
                FROM SearchIndex
                WHERE SearchIndex MATCH $m
                  AND content_type IN ('artist','album','track')
                ORDER BY -bm25(SearchIndex) DESC
                LIMIT $lim;";
            var p1 = cmd.CreateParameter(); p1.ParameterName = "$m"; p1.Value = match; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "$lim"; p2.Value = max; cmd.Parameters.Add(p2);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                raw.Add((r.GetString(0), r.GetInt32(1), r.GetString(2)));
        }
        catch (DbException)
        {
            return System.Array.Empty<AutocompleteHit>();
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
        var albumInfo = coverAlbumIds.Count == 0
            ? new Dictionary<int, (string? Cover, string Title, string? Artist)>()
            : db.Albums.AsNoTracking()
                .Where(a => coverAlbumIds.Contains(a.Id))
                .Select(a => new { a.Id, a.CoverPath, a.Title, Artist = a.Artist!.Name })
                .ToDictionary(a => a.Id, a => (Cover: a.CoverPath, Title: a.Title, Artist: (string?)a.Artist));

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
                case "album" when albumInfo.TryGetValue(id, out var ai):
                    results.Add(new AutocompleteHit(title, "album", id, ai.Cover,
                        ai.Artist is null ? "альбом" : $"альбом · {ai.Artist}"));
                    break;
                // A track is still bought as its album, but the row must SAY so —
                // a bare «album» label used to send users to a page whose title
                // matched nothing they clicked.
                case "track" when trackAlbumId.TryGetValue(id, out var albumId)
                                  && albumInfo.TryGetValue(albumId, out var ti):
                    results.Add(new AutocompleteHit(title, "track", albumId, ti.Cover,
                        $"трек · з альбому «{ti.Title}»"));
                    break;
                case "artist":
                    results.Add(new AutocompleteHit(title, "artist", id,
                        artistPhotos.GetValueOrDefault(id), "виконавець"));
                    break;
            }
        }

        // An album and its title track would otherwise render two rows with the
        // same text pointing at the same page.
        var seen = new HashSet<(string, int?)>();
        var deduped = new List<AutocompleteHit>(results.Count);
        foreach (var r in results)
            if (seen.Add((r.Text.ToLowerInvariant(), r.EntityId)))
                deduped.Add(r);
        return deduped;
    }

    // Distinct query texts, newest first — feeds the «нещодавні пошуки»
    // suggestions under the empty search box.
    public IReadOnlyList<string> RecentQueries(int userId, int max = 5)
    {
        if (userId <= 0) return System.Array.Empty<string>();
        using var db = _dbFactory.CreateDbContext();
        return db.SearchHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.ExecutedAt).ThenByDescending(h => h.Id)
            .Take(50)
            .Select(h => h.Query)
            .AsEnumerable()
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
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
                // Lift the DSL facets into SearchFilters the same way the results
                // page does — Search(raw) alone ignores them and would count the
                // whole catalogue for facet-only queries like «жанр:"Jazz"».
                var (text, filters) = SavedQueryInterpreter.Lift(s.QueryJson);
                count = Search(text, filters).TotalCount;
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
