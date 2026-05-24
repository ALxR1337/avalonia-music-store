using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class ProductEditViewModel : ViewModelBase
{
    private readonly ICatalogService _catalog;
    private readonly IFileDialogService _files;
    private readonly Product? _existing;

    [ObservableProperty] private string _title = "Новий товар";

    // Album / Artist / Genre selectors
    [ObservableProperty] private Album? _selectedAlbum;
    [ObservableProperty] private bool _createNewAlbum;
    [ObservableProperty] private string _newAlbumTitle = string.Empty;
    [ObservableProperty] private int _newAlbumYear = DateTime.UtcNow.Year;
    [ObservableProperty] private string? _newAlbumDescription;

    [ObservableProperty] private Artist? _selectedArtist;
    [ObservableProperty] private bool _createNewArtist;
    [ObservableProperty] private string _newArtistName = string.Empty;

    [ObservableProperty] private Genre? _selectedGenre;
    [ObservableProperty] private bool _createNewGenre;
    [ObservableProperty] private string _newGenreName = string.Empty;

    // Product fields
    [ObservableProperty] private ProductFormat _format = ProductFormat.Vinyl;

    public bool IsVinyl
    {
        get => Format == ProductFormat.Vinyl;
        set { if (value) Format = ProductFormat.Vinyl; }
    }
    public bool IsCd
    {
        get => Format == ProductFormat.CD;
        set { if (value) Format = ProductFormat.CD; }
    }
    partial void OnFormatChanged(ProductFormat value)
    {
        OnPropertyChanged(nameof(IsVinyl));
        OnPropertyChanged(nameof(IsCd));
    }
    [ObservableProperty] private decimal _price = 250m;
    [ObservableProperty] private int _stock = 1;
    [ObservableProperty] private int _releaseYear = DateTime.UtcNow.Year;
    [ObservableProperty] private string? _label;
    [ObservableProperty] private bool _isActive = true;

    [ObservableProperty] private string? _coverPath;
    [ObservableProperty] private string? _samplePath;
    [ObservableProperty] private string? _fullPath;

    [ObservableProperty] private string? _errorMessage;

    public bool DialogResult { get; private set; }
    public bool IsEditMode => _existing is not null;
    public bool IsAddMode => _existing is null;
    public event Action? CloseRequested;

    public ObservableCollection<Album> Albums { get; }
    public ObservableCollection<Artist> Artists { get; }
    public ObservableCollection<Genre> Genres { get; }

    public ProductEditViewModel(ICatalogService catalog, IFileDialogService files, Product? existing)
    {
        _catalog = catalog;
        _files = files;
        _existing = existing;

        Albums = new ObservableCollection<Album>(catalog.Albums);
        Artists = new ObservableCollection<Artist>(catalog.Artists);
        Genres = new ObservableCollection<Genre>(catalog.Genres);

        if (existing is not null)
        {
            Title = $"Редагування товару #{existing.Id}";
            SelectedAlbum = Albums.FirstOrDefault(a => a.Id == existing.AlbumId);
            SelectedArtist = Artists.FirstOrDefault(a => a.Id == existing.Album?.ArtistId);
            SelectedGenre = Genres.FirstOrDefault(g => g.Id == existing.Album?.GenreId);
            Format = existing.Format;
            Price = existing.Price;
            Stock = existing.Stock;
            ReleaseYear = existing.ReleaseYear;
            Label = existing.Label;
            IsActive = existing.IsActive;
            CoverPath = existing.Album?.CoverPath;
            SamplePath = existing.Album?.Tracks.FirstOrDefault()?.SamplePath;
            FullPath = existing.Album?.Tracks.FirstOrDefault()?.FullPath;
        }
    }

    [RelayCommand]
    private async Task PickCoverAsync() =>
        CoverPath = await _files.OpenFileAsync("Виберіть обкладинку альбому",
            new[] { new FileFilter("Зображення", new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }) });

    [RelayCommand]
    private async Task PickSampleAsync() =>
        SamplePath = await _files.OpenFileAsync("Виберіть семпл (30с)",
            new[] { new FileFilter("Аудіо", new[] { "*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a" }) });

    [RelayCommand]
    private async Task PickFullAsync() =>
        FullPath = await _files.OpenFileAsync("Виберіть повний трек",
            new[] { new FileFilter("Аудіо", new[] { "*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a" }) });

    [RelayCommand]
    private void Save()
    {
        try
        {
            if (_existing is not null)
            {
                _catalog.UpdateProduct(_existing.Id, Format, Price, Stock, ReleaseYear, Label,
                    SamplePath, FullPath, IsActive);
            }
            else
            {
                var draft = new ProductDraft(
                    ExistingAlbumId: CreateNewAlbum ? null : SelectedAlbum?.Id,
                    NewAlbumTitle: CreateNewAlbum ? NewAlbumTitle : null,
                    NewAlbumYear: NewAlbumYear,
                    NewAlbumDescription: NewAlbumDescription,
                    CoverPath: CoverPath,
                    ExistingArtistId: CreateNewArtist ? null : SelectedArtist?.Id,
                    NewArtistName: CreateNewArtist ? NewArtistName : null,
                    ExistingGenreId: CreateNewGenre ? null : SelectedGenre?.Id,
                    NewGenreName: CreateNewGenre ? NewGenreName : null,
                    Format: Format,
                    Price: Price,
                    Stock: Stock,
                    ReleaseYear: ReleaseYear,
                    Label: Label,
                    SamplePath: SamplePath,
                    FullPath: FullPath,
                    IsActive: IsActive);

                _catalog.AddProduct(draft);
            }
            DialogResult = true;
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke();
    }
}
