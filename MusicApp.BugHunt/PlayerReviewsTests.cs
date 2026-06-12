using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Coverage for the Player album page's reviews section (Player-Redesign §3):
//   - reviews aggregate across the album's products, top-3 newest first;
//   - «Показати всі» expands to the full list;
//   - the rating label renders only when the album has reviews;
//   - the form is owner-gated and a submit round-trips through the catalog.
public class PlayerReviewsTests
{
    private static PlayerViewModel OpenAlbum(Harness h, MusicApp.Models.Album album)
    {
        h.Nav!.NavigateTo(NavTarget.Player);
        (h.Nav.CurrentView as PlayerViewModel)!.OpenAlbumCommand.Execute(album);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<PlayerViewModel>(h.Nav.CurrentView);
    }

    [AvaloniaFact]
    public void Reviewed_album_shows_top3_rating_label_and_owner_form()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
            .FirstOrDefault(a => h.Catalog.GetReviewsForAlbum(a.Id).Count > 0);
        Assert.NotNull(owned); // SeedTestActivity leaves reviews on purchased albums

        var pvm = OpenAlbum(h, owned!);
        var all = h.Catalog.GetReviewsForAlbum(owned!.Id);

        Assert.True(pvm.HasReviews);
        Assert.Equal(System.Math.Min(3, all.Count), pvm.Reviews.Count);
        Assert.Equal(all.Count > 3, pvm.HasMoreReviews);
        // Newest first.
        Assert.Equal(all.Max(r => r.CreatedAt), pvm.Reviews[0].CreatedAt);
        Assert.False(string.IsNullOrEmpty(pvm.AlbumRatingLabel));
        Assert.StartsWith("★", pvm.AlbumRatingLabel);
        Assert.True(pvm.CanLeaveReview); // demo owns the album and is no guest

        // Tall window so the whole page (reviews included) lands in one frame.
        h.SetWindowSize(1280, 2400);
        h.Snapshot("player-reviews-section");
        h.DumpTree("player-reviews-section");
    }

    [AvaloniaFact]
    public void Show_all_expands_to_every_review()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
            .FirstOrDefault(a => h.Catalog.GetReviewsForAlbum(a.Id).Count > 3);
        if (owned is null) return; // seed has no 4-review album — nothing to expand

        var pvm = OpenAlbum(h, owned);
        Assert.Equal(3, pvm.Reviews.Count);

        pvm.ToggleShowAllReviewsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(h.Catalog.GetReviewsForAlbum(owned.Id).Count, pvm.Reviews.Count);
        Assert.False(pvm.HasMoreReviews);
    }

    [AvaloniaFact]
    public void Submit_review_round_trips_and_clears_the_form()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var userId = h.Auth!.CurrentUser!.Id;
        var owned = h.Catalog!.GetPurchasedAlbums(userId).First();
        var pvm = OpenAlbum(h, owned);
        var before = h.Catalog.GetReviewsForAlbum(owned.Id).Count;

        const string marker = "BugHunt: відгук зі сторінки плеєра";
        pvm.NewReviewText = marker;
        pvm.NewReviewRating = 4;
        int? createdId = null;
        try
        {
            pvm.SubmitReviewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var after = h.Catalog.GetReviewsForAlbum(owned.Id);
            createdId = after.FirstOrDefault(r => r.Text == marker)?.Id;
            Assert.NotNull(createdId);
            Assert.Equal(before + 1, after.Count);
            Assert.Equal(System.Math.Min(3, before + 1), pvm.Reviews.Count);
            Assert.Equal("", pvm.NewReviewText);
            Assert.Contains("додано", pvm.ReviewMessage);
        }
        finally
        {
            // Shared seeded DB per run — drop the test review.
            if (createdId is int id) h.Catalog.DeleteReview(id, userId);
        }
        Assert.Equal(before, h.Catalog.GetReviewsForAlbum(owned.Id).Count);
    }

    [AvaloniaFact]
    public void Guest_on_unowned_album_sees_reviews_but_no_form()
    {
        var h = new Harness();
        h.OpenMainWindow(); // guest
        var album = h.Catalog!.Albums.First(a => a.Tracks.Count > 0);
        var pvm = OpenAlbum(h, album);

        Assert.False(pvm.CanLeaveReview);
        // The list itself stays readable regardless of ownership.
        Assert.Equal(
            System.Math.Min(3, h.Catalog.GetReviewsForAlbum(album.Id).Count),
            pvm.Reviews.Count);
    }
}
