using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MusicApp.Data;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using MusicApp.Views;

namespace MusicApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dbFactory = new MusicStoreDbContextFactory();
            using (var db = dbFactory.CreateDbContext())
            {
                DbSeeder.EnsureSeeded(db);
                Fts5Initializer.Ensure(db);
            }

            // Declared before the nav factories so closures can capture it; the
            // Player factory runs only after navigation starts, when the shell
            // VM below is already assigned.
            MainWindowViewModel? mainVm = null;

            var nav = new NavigationService();
            var auth = new AuthService(dbFactory);
            var catalog = new CatalogService(dbFactory);
            var cart = new CartService(auth, dbFactory, catalog);
            var likes = new LikesService(dbFactory);
            var search = new SearchService(dbFactory);
            var files = new FileDialogService();
            var player = new PlayerService(auth, dbFactory, catalog);

            desktop.Exit += (_, _) => player.Dispose();

            nav.Register(NavTarget.Catalog,
                _ => new CatalogViewModel(catalog, nav, player, cart));
            nav.Register(NavTarget.SearchResults,
                param => new SearchResultsViewModel(search, nav, player, cart, auth, param as string ?? string.Empty));
            nav.Register(NavTarget.Product,
                param => new ProductViewModel(catalog, cart, player, nav, auth, (int)(param ?? 1)));
            nav.Register(NavTarget.Cart,
                _ => new CartViewModel(cart, nav, auth, catalog));
            nav.Register(NavTarget.Profile,
                _ => new ProfileViewModel(auth, catalog, search, nav, cart));
            nav.Register(NavTarget.Player,
                param => new PlayerViewModel(player, catalog, auth, likes, nav, files, param as Album,
                    requestLogin: () => mainVm?.ShowLogin()));
            nav.Register(NavTarget.Admin,
                _ => new AdminViewModel(catalog, files, auth));

            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            // The window is always the main window now; login is an in-app
            // overlay. Restore a "remember me" session if one was saved, else
            // pop the login card on top of the (guest) app.
            var restored = auth.TryRestoreSession();

            mainVm = new MainWindowViewModel(nav, auth, cart, player, catalog, search);
            var mainWindow = new MainWindow { DataContext = mainVm };
            desktop.MainWindow = mainWindow;

            if (!restored)
                mainVm.ShowLogin();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
