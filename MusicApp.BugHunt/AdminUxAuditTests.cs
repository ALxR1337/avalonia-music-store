using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// One-off UX audit captures for the admin screen: narrow-window layouts,
// the floating status toast, deactivated-product rendering and the
// edit-mode editor. Screenshots land in bug-hunt/artifacts/ with the
// admin-ux- prefix.
public class AdminUxAuditTests
{
    private static (Harness h, AdminViewModel admin) OpenAdmin(int w, int h)
    {
        var harness = new Harness();
        harness.OpenMainWindow(loginAs: "admin", password: "admin");
        harness.SetWindowSize(w, h);
        harness.Nav!.NavigateTo(NavTarget.Admin);
        Dispatcher.UIThread.RunJobs();
        var admin = (AdminViewModel)harness.Nav!.CurrentView!;
        return (harness, admin);
    }

    [AvaloniaFact]
    public void Narrow_window_overview_and_orders()
    {
        var (h, admin) = OpenAdmin(1100, 700);
        h.RunStep("admin-ux-01-overview-1100x700", () => { });

        h.RunStep("admin-ux-02-orders-1100x700",
            () => admin.SelectSectionCommand.Execute("Orders"));

        h.SetWindowSize(900, 650);
        h.RunStep("admin-ux-03-orders-900x650", () => { });

        h.RunStep("admin-ux-04-overview-900x650",
            () => admin.SelectSectionCommand.Execute("Overview"));
    }

    [AvaloniaFact]
    public void Toast_overlays_without_layout_shift()
    {
        var (h, admin) = OpenAdmin(1400, 900);
        admin.SelectSectionCommand.Execute("Products");
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-ux-05-products-before-toast", () => { });

        var victim = admin.Products[0];
        admin.RequestDeactivateProductCommand.Execute(victim);
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-ux-06-deactivate-confirm-inline", () => { });

        admin.ConfirmDeactivateProductCommand.Execute(victim);
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-ux-07-deactivated-with-toast", () => { });

        // Restore for the rest of the suite (process-shared DB).
        var row = admin.Products[0];
        admin.ActivateProductCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Editor_edit_mode_full_window()
    {
        var (h, admin) = OpenAdmin(1400, 900);
        admin.SelectSectionCommand.Execute("Products");
        Dispatcher.UIThread.RunJobs();

        admin.EditProductCommand.Execute(admin.Products[0]);
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-ux-08-editor-edit-1400x900", () => { });

        // Save with no changes — closes cleanly.
        admin.EditingProduct!.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-ux-09-after-editor-save", () => { });
        Assert.False(admin.IsEditingProduct);
    }

    [AvaloniaFact]
    public void Editor_existing_album_hides_artist_genre()
    {
        var (h, admin) = OpenAdmin(1400, 900);
        admin.SelectSectionCommand.Execute("Products");
        Dispatcher.UIThread.RunJobs();
        admin.AddProductCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var editor = admin.EditingProduct!;

        // Existing-album mode: artist selector hidden, summary recaps the album.
        Assert.False(editor.ShowArtistSelector);
        editor.SelectedAlbum = editor.Albums[0];
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(editor.SelectedAlbumSummary);
        h.RunStep("admin-ux-12-editor-album-summary", () => { });

        // New-album mode brings the artist/genre cards back.
        editor.CreateNewAlbum = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ShowArtistSelector);
        Assert.True(editor.ShowGenreSelector);
        h.RunStep("admin-ux-13-editor-new-album", () => { });

        editor.DiscardAndCloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(admin.IsEditingProduct);
    }

    [AvaloniaFact]
    public void Datepicker_custom_period()
    {
        var (h, admin) = OpenAdmin(1400, 900);
        admin.SelectPeriodCommand.Execute("Year");
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-ux-10-period-year", () => { });

        admin.RevenueFrom = System.DateTime.UtcNow.AddYears(-2).Date;
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.IsPeriodCustom);
        h.RunStep("admin-ux-11-period-custom", () => { });
    }
}
