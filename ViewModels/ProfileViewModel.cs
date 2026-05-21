using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class ProfileViewModel : ViewModelBase
{
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _roleLabel = "";

    public ProfileViewModel(IAuthService auth, ICatalogService catalog)
    {
        Username = auth.CurrentUser?.Username ?? "Гість";
        Email = auth.CurrentUser?.Email ?? "не вказано";
        RoleLabel = auth.CurrentUser?.Role switch
        {
            UserRole.Admin => "Адміністратор",
            UserRole.Customer => "Покупець",
            _ => "Гість"
        };

        var userId = auth.CurrentUser?.Id ?? 0;
        Orders = userId > 0
            ? new ObservableCollection<Order>(catalog.GetOrdersFor(userId))
            : new ObservableCollection<Order>();
    }

    public ObservableCollection<Order> Orders { get; }
}
