using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Screenshot sweep for the catalog UX audit: default size top/bottom,
/// minimum window size, and a tall window showing the full page at once.
/// Assertion-free on purpose — the artifacts are the deliverable.
/// </summary>
public class CatalogUxAuditTests
{
    private static ScrollViewer ContentScroll(Harness h) => h.Find<ScrollViewer>("ContentScroll");

    [AvaloniaFact]
    public void Catalog_ux_audit_screens()
    {
        var h = new Harness();
        h.OpenMainWindow();

        h.SetWindowSize(1280, 800);
        h.RunStep("ux-cat-01-default-top", () => h.Nav!.NavigateTo(NavTarget.Catalog));
        h.RunStep("ux-cat-02-default-bottom", () =>
        {
            var sv = ContentScroll(h);
            sv.Offset = new Vector(0, 100000);
            Dispatcher.UIThread.RunJobs();
        });

        h.SetWindowSize(1024, 640);
        h.RunStep("ux-cat-03-min-top", () =>
        {
            ContentScroll(h).Offset = default;
            Dispatcher.UIThread.RunJobs();
        });
        h.RunStep("ux-cat-04-min-bottom", () =>
        {
            ContentScroll(h).Offset = new Vector(0, 100000);
            Dispatcher.UIThread.RunJobs();
        });

        h.SetWindowSize(1480, 2300);
        h.RunStep("ux-cat-05-tall-full", () => ContentScroll(h).Offset = default);
    }
}
