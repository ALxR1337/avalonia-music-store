using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicApp.Models;

namespace MusicApp.Services.Search;

/// <summary>
/// Splits a search DSL string into the residual FTS text and the structured
/// <see cref="SearchFilters"/> its facet terms describe. Shared by the results
/// page (which lifts the filters into its sidebar state) and saved-search
/// counting, so both always agree on what a stored query means — counting a
/// saved query through bare Search(raw) used to drop every facet and report
/// the whole catalogue.
/// </summary>
public static class SavedQueryInterpreter
{
    public static (string Text, SearchFilters Filters) Lift(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (raw ?? string.Empty, new SearchFilters());

        var parsed = SearchQueryParser.Parse(raw);
        var leftover = new StringBuilder();
        var genres = new List<string>();
        var artists = new List<string>();
        ProductFormat? format = null;
        int? yearFrom = null, yearTo = null;
        decimal? priceFrom = null, priceTo = null;
        double? minRating = null;

        void AddText(string s)
        {
            if (leftover.Length > 0) leftover.Append(' ');
            leftover.Append(s);
        }
        void AddGenre(string g) { if (!genres.Contains(g, StringComparer.OrdinalIgnoreCase)) genres.Add(g); }
        void AddArtist(string a) { if (!artists.Contains(a, StringComparer.OrdinalIgnoreCase)) artists.Add(a); }

        foreach (var term in parsed.Terms)
        {
            switch (term)
            {
                case FieldTextTerm ft when ft.Field == "genre": AddGenre(ft.Value); break;
                case FieldPhraseTerm fp when fp.Field == "genre": AddGenre(fp.Phrase); break;
                case FieldTextTerm ft when ft.Field == "artist": AddArtist(ft.Value); break;
                case FieldPhraseTerm fp when fp.Field == "artist": AddArtist(fp.Phrase); break;
                case FieldTextTerm ft when ft.Field == "format":
                    format = IsVinyl(ft.Value) ? ProductFormat.Vinyl : ProductFormat.CD;
                    break;
                case RangeTerm rt when rt.Field == "year":
                    if (rt.Min is double yMin) yearFrom = (int)yMin;
                    if (rt.Max is double yMax) yearTo = (int)yMax;
                    break;
                case RangeTerm rt when rt.Field == "price":
                    if (rt.Min is double pMin) priceFrom = (decimal)pMin;
                    if (rt.Max is double pMax) priceTo = (decimal)pMax;
                    break;
                case CompareTerm ct when ct.Field == "rating" && ct.Op is CompareOp.Gte or CompareOp.Gt:
                    minRating = ct.Value;
                    break;
                case CompareTerm ct when ct.Field == "year" && ct.Op == CompareOp.Equal:
                    yearFrom = yearTo = (int)ct.Value;
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

        return (leftover.ToString(), new SearchFilters(
            YearFrom: yearFrom,
            YearTo: yearTo,
            PriceFrom: priceFrom,
            PriceTo: priceTo,
            MinRating: minRating,
            Format: format,
            Genres: genres,
            Artists: artists));
    }

    public static bool IsVinyl(string v) =>
        v.Equals("LP", StringComparison.OrdinalIgnoreCase)
        || v.Equals("Вініл", StringComparison.OrdinalIgnoreCase)
        || v.Equals("Vinyl", StringComparison.OrdinalIgnoreCase);
}
