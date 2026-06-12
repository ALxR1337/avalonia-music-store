using System.Collections.Generic;
using MusicApp.Models;
using MusicApp.Services.Search;

namespace MusicApp.Services;

public sealed record SavedSearchSummary(SavedSearch Saved, int CurrentCount)
{
    /// <summary>
    /// True when the saved name is something other than the raw DSL string —
    /// only then is a «запит: …» subtitle worth a second line in the profile.
    /// </summary>
    public bool HasDistinctQuery =>
        !string.Equals(Saved.Name, Saved.QueryJson, System.StringComparison.Ordinal);
}

public interface ISearchService
{
    SearchResults Search(string query, SearchFilters? filters = null);
    IReadOnlyList<AutocompleteHit> Autocomplete(string prefix, int max = 8);

    void RecordHistory(int userId, string query, int resultCount);
    IReadOnlyList<string> RecentQueries(int userId, int max = 5);
    int SaveSearch(int userId, string name, string queryJson, bool notifyOnNew);
    IReadOnlyList<SavedSearch> ListSavedSearches(int userId);
    IReadOnlyList<SavedSearchSummary> ListSavedSearchSummaries(int userId);
    void SetSavedSearchNotify(int id, bool notifyOnNew);
    void DeleteSavedSearch(int id);
}
