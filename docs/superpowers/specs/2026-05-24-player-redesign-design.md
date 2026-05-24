# Player Page Redesign — Design Spec

**Date:** 2026-05-24
**Status:** Draft (awaiting user review)
**Affected:** `Views/PlayerView.axaml`, `ViewModels/PlayerViewModel.cs`, `Views/MiniPlayerView.axaml`, `ViewModels/MiniPlayerViewModel.cs`, `Services/PlayerService.cs`, `Services/CatalogService.cs`, new likes service, new DB tables.

## Problem

The Player page currently duplicates all playback controls already present in the MiniPlayer at the bottom of the window: Play/Pause, Prev/Next, Seek slider, Volume slider, and a large 280×280 cover. Meanwhile the page has no tracklist, no way to navigate to the artist, and no contextual information about the album.

## Goals

1. Remove duplicated playback controls from the Player page; the MiniPlayer remains the single source of playback controls.
2. Repurpose the Player page as an **album context view**: track list, album metadata, artist navigation, reviews, and discovery of more albums by the artist.
3. Add liking (track + album) and the missing Shuffle/Repeat toggles (backend already supports them in `PlayerService`).
4. Make the MiniPlayer progress bar interactive so seek functionality is preserved (just relocated).

## Out of Scope

- **Playlists**: `Playlist`/`PlaylistTrack` models exist in DB but there is no `IPlaylistService` and no UI for managing playlists. "Add to playlist" requires building the entire playlists feature first; deferred to a separate task.
- **Lyrics**: explicitly rejected by user — no lyrics feature in the program.
- **Dedicated Artist page**: artist link navigates to SearchResults filtered by artist; no new artist detail view.

## Layout

Single-column scrollable main content next to the existing purchased-albums sidebar:

```
┌─ Sidebar (unchanged) ──┐ ┌─ Main scroll content ────────────────────────────┐
│ КУПЛЕНІ АЛЬБОМИ        │ │  ┌─ Album header ─────────────────────────────┐ │
│   [Album row]          │ │  │ [Cover  Shattered Dreams (h1)             │ │
│   [Album row]          │ │  │  160px] Earl Sweatshirt → (link)          │ │
│                        │ │  │         2018 · Hip-Hop · 15 · 25 хв       │ │
│ + Додати файли         │ │  │         ♥ Like  ⇄ Shuffle  ↻ Repeat       │ │
│                        │ │  └────────────────────────────────────────────┘ │
│                        │ │  Опис альбому: … [показати повністю]            │
│                        │ │  ──────                                          │
│                        │ │  Треки                                           │
│                        │ │  ▶ 1. Shattered Dreams           2:21  ♥        │
│                        │ │    2. Red Water                  1:47  ♥        │
│                        │ │    ...                                           │
│                        │ │  ──────                                          │
│                        │ │  Відгуки  ⭐ 4.7 (12)    [Залишити відгук]       │
│                        │ │  • "Геніально!" — Bob 4★                        │
│                        │ │  • ...                          [Показати всі]   │
│                        │ │  ──────                                          │
│                        │ │  Більше від Earl Sweatshirt                      │
│                        │ │  [a1] [a2] [a3] [a4]                            │
│                        │ │                                                  │
└────────────────────────┘ └──────────────────────────────────────────────────┘
                (Below: MiniPlayer — seek now interactive)
```

The sidebar stays exactly as it is — purchased albums + "Додати файли" — and is not part of this redesign.

## Sections

### 1. Album header

- Cover 160×160 (down from 280×280), left.
- Right side, vertical stack:
  - **Title** (`h2` class) — `Album.Title` (or `Track.Title` when no album).
  - **Artist** (linkable text/button) — `Album.Artist.Name`. Click → `NavigateTo(SearchResults, "виконавець:\"<name>\"")`.
  - **Metadata strip** — `Year · Genre · TrackCount · TotalDuration` (sum of track durations).
  - **Action row** — three icon-buttons:
    - **Like (album)** — ♥ toggle, persists to `AlbumLikes`.
    - **Shuffle** — toggles `PlayerService.ShuffleMode`. Visually highlighted when on.
    - **Repeat** — cycles `RepeatMode`: Off → All → One → Off. Icon changes per state (`IconRepeat` dimmed for Off, accent-colored for All, `IconRepeatOne` for One).
- **Description**: `Album.Description`, clamped to 2–3 lines, with "Показати повністю / Згорнути" toggle (no description → block hidden).

### 2. Tracklist

- `ItemsControl` of `Album.Tracks` ordered by `Position`.
- Row layout: `Position. Title ........ Duration ♥`.
- Click anywhere on the row → `PlayerService.PlayAlbum(album, index)` (jumps to that track, starts playback).
- Current track highlighted (subtle background + visible ▶ marker).
- ♥ on the right toggles `TrackLikes` entry for current user. Filled vs outline icon based on `IsTrackLiked(userId, trackId)`.
- **No ⋮ menu** for this iteration (playlists deferred; like is inline).

### 3. Reviews

- Aggregated across all `Product`s of the album (an album can have multiple products: vinyl / CD / digital).
- Header: `⭐ {avg:0.0} ({count})` + button "Залишити відгук" → expands review form.
- Top 3 most recent reviews shown; "Показати всі" expands inline to all.
- Form: rating (1–5 via `NumericUpDown` or 5 star buttons — reuse `ProductView` pattern) + text + Submit.
- `Submit` calls `_catalog.AddReview(primaryProductId, userId, displayName, text, rating)` where `primaryProductId` is the album's first product (lowest `Id` ordering — i.e., the "primary" one).
- Edit/Delete own reviews — reuse `UpdateReview`/`DeleteReview` from `ICatalogService`.
- If no album context (e.g., local file playback), this section is hidden.

### 4. Більше від артиста

- Horizontal `ScrollViewer`+`ItemsControl` of other albums by the same artist (`ArtistId` match, exclude current `AlbumId`).
- Tile: cover thumbnail + album title. Click → SearchResults filtered by that artist (same target as the header's artist link). User can then drill into a specific product.
- If artist has no other albums, the section is hidden.

### Empty / degraded states

- **No track playing** (`HasTrack == false`): show existing empty state ("Нічого не грає…") centered in main area. Header / tracklist / reviews / artist sections all hidden.
- **Local file via `PlayFile`** (no `CurrentAlbum`): show only the album header **with metadata only** — track title, no artist, no cover gradient, no description, no shuffle/repeat/like buttons. Tracklist, reviews, "more from artist" sections all hidden. (User confirmation: "да, только метаданные".)

## Removed from current Player page

- Play/Pause button
- Prev/Next buttons
- Seek slider (moved to MiniPlayer)
- Volume slider
- 280×280 cover (replaced by 160×160 in header)

## Changes to MiniPlayer

The MiniPlayer's current `ProgressBar` is replaced with an interactive `Slider`, matching the pattern in the current `PlayerView` (drag-to-scrub with `IsScrubbing` flag in the VM, `PointerPressed`/`PointerReleased` for commit).

- Slider is the same height-3 visual, minimal thumb (or thumb-on-hover).
- `MiniPlayerViewModel` mirrors the scrub-suppression pattern from `PlayerViewModel`.
- Time labels remain.

## Backend changes

### DB (new migration)

- `TrackLikes`
  - `UserId int` (FK Users), `TrackId int` (FK Tracks), `CreatedAt datetime`
  - PK composite `(UserId, TrackId)`
- `AlbumLikes`
  - `UserId int` (FK Users), `AlbumId int` (FK Albums), `CreatedAt datetime`
  - PK composite `(UserId, AlbumId)`

### `ILikesService` / `LikesService` (new)

```csharp
public interface ILikesService
{
    bool IsTrackLiked(int userId, int trackId);
    void LikeTrack(int userId, int trackId);
    void UnlikeTrack(int userId, int trackId);
    IReadOnlyList<int> GetLikedTrackIds(int userId);

    bool IsAlbumLiked(int userId, int albumId);
    void LikeAlbum(int userId, int albumId);
    void UnlikeAlbum(int userId, int albumId);
    IReadOnlyList<int> GetLikedAlbumIds(int userId);

    event EventHandler? Changed; // fired on any like/unlike to refresh bindings
}
```

Implementation uses `IDbContextFactory<MusicStoreDbContext>` (same pattern as `CatalogService`).

### `ICatalogService` additions

```csharp
IReadOnlyList<Album> GetAlbumsByArtist(int artistId, int? excludeAlbumId = null);
(double avg, int count) GetAlbumRating(int albumId);                  // aggregate across products
IReadOnlyList<Review> GetReviewsForAlbum(int albumId);                // aggregate across products
int? GetPrimaryProductId(int albumId);                                // first product for AddReview target
```

### `PlayerService` exposed state

Add events so VM can bind:

```csharp
event EventHandler? ShuffleModeChanged;
event EventHandler? RepeatModeChanged;
```

Toggling existing `ShuffleMode` / `RepeatMode` setters already persists; the events let the VM update `IsShuffleOn` / `RepeatMode` properties when toggled.

### Artist navigation (reuse SearchResults)

- `SearchService` already parses `артист:`/`виконавець:`/`artist:` as a structured field (see `SearchQueryParser`).
- "Go to artist" navigates to `NavTarget.SearchResults` with the query `виконавець:"<Artist Name>"`.
- Same pattern is already used for genre tiles in `CatalogViewModel.OpenGenre` (passes `"жанр:<name>"`).
- No changes to `CatalogViewModel` or `SearchService` required.

## ViewModel: `PlayerViewModel` changes

- **Remove**: `PlayPauseCommand`, `NextCommand`, `PreviousCommand`, `Volume`, `Progress`, `PositionText`, `DurationText`, `IsScrubbing`, `CommitSeek`, `OnVolumeChanged`.
  (These move to MiniPlayer or are already there.)
- **Add**:
  - `Album? CurrentAlbum` (with derived properties): `AlbumDescription`, `AlbumYear`, `AlbumGenreName`, `AlbumTrackCount`, `AlbumDurationText`.
  - `ObservableCollection<TrackRowVm> Tracks` (track + likeState + isCurrent).
  - `bool IsAlbumLiked`, `IsShuffleOn`, `RepeatMode` (bound to PlayerService).
  - Commands: `ToggleAlbumLikeCommand`, `ToggleTrackLikeCommand(trackId)`, `ToggleShuffleCommand`, `CycleRepeatCommand`, `PlayTrackCommand(int index)`, `GoToArtistCommand`, `GoToAlbumCommand(int albumId)`.
  - Reviews: `ObservableCollection<Review> Reviews`, `double AvgRating`, `int ReviewCount`, `SubmitReviewCommand` etc. (lift from `ProductViewModel`).
  - More from artist: `ObservableCollection<Album> MoreFromArtist`.
- **DI**: add `ILikesService` and `INavigationService` to constructor.

## File-by-file summary

| File | Change |
|---|---|
| `Models/TrackLike.cs`, `Models/AlbumLike.cs` | New |
| `Data/MusicStoreDbContext.cs` | Add DbSets + entity configs |
| `Data/Migrations/<timestamp>_AddLikes.cs` | New EF migration |
| `Services/ILikesService.cs`, `Services/LikesService.cs` | New |
| `Services/ICatalogService.cs`, `Services/CatalogService.cs` | Add `GetAlbumsByArtist`, `GetAlbumRating`, `GetReviewsForAlbum`, `GetPrimaryProductId` |
| `Services/IPlayerService.cs`, `Services/PlayerService.cs` | Raise `ShuffleModeChanged` / `RepeatModeChanged` |
| `ViewModels/PlayerViewModel.cs` | Major rewrite: remove playback controls, add album context + reviews + artist + likes |
| `Views/PlayerView.axaml` (+ .cs) | New layout per spec |
| `ViewModels/MiniPlayerViewModel.cs` | Add `Progress` setter + `IsScrubbing` + `CommitSeek` |
| `Views/MiniPlayerView.axaml` (+ .cs) | Replace `ProgressBar` with `Slider`, wire pointer events |
| (no changes) | Artist nav reuses SearchResults via `виконавець:"name"` query |
| `App.axaml.cs` | Register `ILikesService` in DI |
| `Themes/Icons.axaml` | Add `IconShuffle`, `IconRepeat`, `IconRepeatOne` (verified missing). `IconHeart`/`IconHeartFilled`/`IconStar` already present. |

## Testing notes

- BugHunt harness already exists (`MusicApp.BugHunt/`). New scenarios to add:
  - Smoke: opening Player with current album shows header, tracklist, reviews, "more from artist" sections.
  - Liking a track persists across reload.
  - Toggling Shuffle/Repeat survives an app restart (already persisted in PlayerSettings; verify wiring).
  - Clicking the artist navigates to Catalog with the filter applied.
  - Local-file playback hides album-only sections.
  - MiniPlayer slider scrub commits a seek.

## Non-goals / explicitly not changing

- No changes to the purchased-albums sidebar contents or behavior.
- No changes to track-row context menus beyond what is described (deferred until playlists are built).
- No new "Like a track from the catalog" entry-points — likes are added only from the Player page tracklist in this iteration.
