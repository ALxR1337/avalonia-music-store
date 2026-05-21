# MusicApp — agent notes

## What this app is

Desktop Avalonia 12 / .NET 10 storefront for a Ukrainian-language music shop
("Музичний магазин"). Users sign in, browse a catalog of physical music
products (vinyl/CD) grouped by genre and artist, preview samples in a built-in
player, add to cart, place orders, and review purchases. Admin users get a
management area. SQLite via EF Core; auth uses BCrypt. UI text is in Ukrainian.

Code layout (project root):

| Folder        | Contents                                                              |
| ------------- | --------------------------------------------------------------------- |
| `Views/`      | Avalonia XAML windows + user controls (Login, Main, Catalog, Cart, …) |
| `ViewModels/` | CommunityToolkit.Mvvm view models                                     |
| `Services/`   | Auth, Cart, Catalog, Navigation, Player + interfaces                  |
| `Data/`       | EF Core `MusicStoreDbContext`, seeder, migrations                     |
| `Models/`     | EF entities (Album, Artist, Track, Product, Order, User, …)           |
| `Themes/`     | Brushes, control styles                                               |
| `Converters/` | XAML value converters                                                 |
| `Assets/`     | Embedded images / fonts                                               |

Startup: `Program.cs` → `App.OnFrameworkInitializationCompleted` builds
`LoginWindow` first; on successful login it constructs `MainWindow` with all
services wired up. `MainWindow` is a shell (custom title bar, sidebar
navigation, content area driven by `INavigationService`, mini-player at the
bottom).

The DB lives at `$XDG_CONFIG_HOME/MusicStore/store.db` (or `%APPDATA%` on
Windows). It is **shared between the real app and the bug-hunt harness** —
runs leave seeded users/products behind.

## The bug-hunt harness

`MusicApp.BugHunt/` is an xUnit v3 test project that boots the real `App`
under `Avalonia.Headless` with Skia drawing enabled (so frames can be
captured to PNG). It is **not** part of the shipped binary.

### Run it

```bash
# all bug-hunt tests
dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj

# a single test
dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj \
  --filter "FullyQualifiedName~SmokeTests.MainWindow_opens_and_resizes"
```

Tests use `[AvaloniaFact]` / `[AvaloniaTheory]` from `Avalonia.Headless.XUnit`
so they run on the Avalonia UI thread. The framework is wired through
`TestAppBuilder.cs` (assembly-level `[AvaloniaTestApplication(...)]`).

### Artifacts

Everything the harness writes lands in `bug-hunt/artifacts/` at the repo
root, named `<yyyyMMdd-HHmmss-fff>-<label>.png` and `.tree.txt`. The path is
resolved at compile time from `Harness.cs`'s own location, so it works
regardless of where `dotnet test` is invoked from. The directory is
gitignored.

### Harness API (`MusicApp.BugHunt.Harness`)

| Method                              | What it does                                                                                                                     |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `OpenMainWindow()`                  | Builds services + `MainWindowViewModel` directly, instantiates `MainWindow`, shows it. **Skips the login flow.** Returns the window. |
| `Find<T>(name)`                     | Walks the visual tree looking for a control of type `T` with `x:Name == name`. Throws if not found.                              |
| `Click(name)`                       | Finds a `Button` and either executes its `Command` (if set & `CanExecute`) or raises `Button.ClickEvent` so code-behind handlers fire. |
| `Type(name, text)`                  | Finds a `TextBox` and sets `.Text` (then pumps the dispatcher so bindings settle).                                               |
| `SetWindowSize(w, h)`               | **Force-resizes** the window — clears `MinWidth/MinHeight/MaxWidth/MaxHeight` first so the harness can probe sizes the real window would refuse. |
| `Snapshot(label)`                   | Calls `Window.CaptureRenderedFrame()`, saves PNG to `bug-hunt/artifacts/`, returns the path.                                     |
| `DumpTree(label)`                   | Walks the visual tree and writes a text file with `Type Name=… Bounds=x,y wxh IsVisible=… IsEnabled=… Text=… DataContext=…` per node, indented by depth. |
| `RunStep(label, () => { … })`       | Runs the action, then `Snapshot` + `DumpTree` with the same label. The standard unit of a bug-hunt step.                         |

The harness pumps `Dispatcher.UIThread.RunJobs()` after each mutation so
layout and bindings settle before the next snapshot.

### Limitations to know

- **Login is skipped.** `auth.CurrentUser` is `null` and `IsAdmin` is `false`,
  so the sidebar's "АДМІН" section and anything `IsVisible="{Binding IsAdmin}"`
  will not render. Tests targeting admin flows will need to log in (or stub
  auth) before opening the window — there's no helper for this yet.
- **DB is shared with the real app.** Tests that mutate users/orders/cart will
  persist into `store.db`. Acceptable for read-only bug hunts; risky for
  destructive ones.
- **`SetWindowSize` ignores the window's MinSize.** Real users hit those
  limits; the harness deliberately doesn't, so layout breakage at tiny sizes
  surfaces.
- **Click via Command bypasses the routed event.** If a button has *both* a
  Command and a Click handler, the harness will execute the Command; it will
  not hit the code-behind handler. Buttons with only a Click handler are
  fine.

## Bug taxonomy — what to report

When exploring the app autonomously, flag any of the following. Each finding
should include the artifact label and a one-line description of what's wrong.

### Layout

- **Clipped text.** A `TextBlock`/`TextBox` whose rendered text is visually
  truncated (no ellipsis, or ellipsis where the design clearly wants the
  whole string). E.g. the user pill rendering "Гість" as "Гіsu" at 400×300.
- **Controls falling off the window.** Bounds extend past the window's right
  or bottom edge — typically the title bar's min/max/close buttons at small
  widths.
- **Overlapping controls.** Two controls at the same z-level with
  intersecting `Bounds` that the design did not intend (e.g. mini-player
  covering the last row of catalog items).
- **Layout breaking below some size.** The catalog renders fine at 1280×800
  but collapses at 800×600: genre tile labels disappear, sidebar steals all
  width, hero text wraps to four lines. Worth flagging the threshold.
- **Sidebar dominance at small widths.** Sidebar is a fixed 240px column;
  any window under ~640px wide leaves <400px for content.

### Data / bindings

- **Empty bindings.** A `TextBlock` whose `Text` is bound to a property but
  renders empty when the underlying VM has data. Compare `DumpTree`'s `Text=`
  field against what the VM exposes.
- **"Гість" where a username should be.** Confirms `auth.CurrentUser` is
  null — expected under the harness, but a bug if it shows after a login flow.
- **Empty `ItemsControl`.** A list whose `ItemsSource` resolves to zero
  items when the seeded DB has matching rows.

### State

- **Controls disabled when they should be enabled** (or vice versa). E.g. an
  "Add to cart" button stuck disabled after login. `DumpTree` reports
  `IsEnabled=0` for each control.
- **Controls referenced in XAML but missing at runtime.** `Find<T>(name)`
  throws — the named control was never realized (often a `DataTemplate`
  scoping issue, or hidden behind an unmet `IsVisible` binding).
- **Mini-player visible without media loaded** (or invisible when a track
  is playing). `IsMiniPlayerVisible` should track `IPlayerService.MediaOpened`.

### Runtime

- **Exceptions logged.** Avalonia logs to trace via `LogToTrace()`. Capture
  output and flag any `Exception thrown:` lines.
- **First-chance binding errors.** `Avalonia.Markup.Xaml` logs binding
  failures with severity Warning when a path doesn't resolve. Treat
  recurring binding warnings as bugs.

### Navigation

- **Sidebar nav button does nothing.** `Click("…")` runs but `CurrentView`
  doesn't change.
- **Page reachable from sidebar but throws on open.** E.g. Admin without
  admin permissions.

## When adding a new step

1. Use `RunStep("NN-short-label", () => { /* mutations */ })` — `NN` keeps
   artifacts sortable.
2. If the step opens a different page, navigate via `Click` on the sidebar
   button (e.g. `Click("…")`) rather than reaching into the VM, so the
   harness exercises the same code path a user would.
3. Don't `Find<T>` for layout assertions — read `DumpTree` output instead.
   `Find` is for *driving* the app (clicks, types); the dump is the
   *observation*.
