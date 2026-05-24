using Avalonia.Headless.XUnit;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.ViewModels;

namespace MusicApp.BugHunt;

public class SearchSuggestionsTests
{
    [AvaloniaFact]
    public void Autocomplete_popup_renders_with_album_hit()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 720);

        var vm = (MainWindowViewModel)h.Window!.DataContext!;

        // Drive Autocomplete through the real SearchService so ImagePath gets
        // populated from the seeded Albums table (the 200ms debounce timer in
        // the VM doesn't fire under the headless dispatcher).
        var search = new SearchService(new MusicStoreDbContextFactory());

        h.RunStep("01-popup-album-hit", () =>
        {
            vm.SearchQuery = "utop";
            vm.Suggestions.Clear();
            foreach (var hit in search.Autocomplete("utop"))
                vm.Suggestions.Add(hit);
            vm.IsAutocompleteOpen = vm.Suggestions.Count > 0;
        });
    }
}
