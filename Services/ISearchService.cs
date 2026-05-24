using System.Collections.Generic;
using MusicApp.Models;
using MusicApp.Services.Search;

namespace MusicApp.Services;

public sealed record SavedSearchSummary(SavedSearch Saved, int CurrentCount);

public interface ISearchService
{
    SearchResults Search(string query, SearchFilters? filters = null);
    IReadOnlyList<AutocompleteHit> Autocomplete(string prefix, int max = 8);

    void RecordHistory(int userId, string query, int resultCount);
    int SaveSearch(int userId, string name, string queryJson, bool notifyOnNew);
    IReadOnlyList<SavedSearch> ListSavedSearches(int userId);
    IReadOnlyList<SavedSearchSummary> ListSavedSearchSummaries(int userId);
    void SetSavedSearchNotify(int id, bool notifyOnNew);
    void DeleteSavedSearch(int id);
}
