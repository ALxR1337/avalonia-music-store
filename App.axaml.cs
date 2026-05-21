using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MusicApp.Data;
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
                DbSeeder.EnsureSeeded(db);

            var nav = new NavigationService();
            var auth = new AuthService(dbFactory);
            var cart = new CartService(auth, dbFactory);
            var player = new PlayerService();
            var catalog = new CatalogService(dbFactory);

            nav.Register(NavTarget.Catalog,
                _ => new CatalogViewModel(catalog, nav, player, cart));
            nav.Register(NavTarget.SearchResults,
                param => new SearchResultsViewModel(catalog, nav, player, cart, param as string ?? string.Empty));
            nav.Register(NavTarget.Product,
                param => new ProductViewModel(catalog, cart, player, (int)(param ?? 1)));
            nav.Register(NavTarget.Cart,
                _ => new CartViewModel(cart, nav));
            nav.Register(NavTarget.Profile,
                _ => new ProfileViewModel(auth, catalog));
            nav.Register(NavTarget.Orders,
                _ => new OrdersViewModel(catalog, auth));
            nav.Register(NavTarget.Player,
                _ => new PlayerViewModel(player, catalog));
            nav.Register(NavTarget.Admin,
                _ => new AdminViewModel(catalog));

            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            var loginVm = new LoginViewModel(auth);
            var loginWindow = new LoginWindow { DataContext = loginVm };

            loginVm.RequestClose += () =>
            {
                var mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(nav, auth, cart, player, catalog)
                };
                mainWindow.Show();
                desktop.MainWindow = mainWindow;
                loginWindow.Close();
            };

            desktop.MainWindow = loginWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
