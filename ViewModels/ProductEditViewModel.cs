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

    // Edit mode: identifies WHAT is being edited (album, artist, format) so the
    // admin isn't left with a bare internal id in the header.
    [ObservableProperty] private string? _subtitle;

    // Album / Artist / Genre selectors.
    // When an EXISTING album is picked, its artist and genre are already fixed —
    // the artist/genre selectors are hidden and a summary line shows what the
    // album carries (the service ignores artist input for existing albums).
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

    // Artist/genre cards make sense only when composing a NEW album; an
    // existing album already carries both. The genre card stays visible for a
    // (legacy) genre-less album — otherwise Save would demand a genre the
    // admin has no way to provide.
    public bool ShowArtistSelector => CreateNewAlbum;
    public bool ShowGenreSelector =>
        CreateNewAlbum || (SelectedAlbum is not null && SelectedAlbum.GenreId == 0);

    // One-line recap of what the chosen existing album brings along.
    public string? SelectedAlbumSummary
    {
        get
        {
            if (CreateNewAlbum || SelectedAlbum is null) return null;
            var artist = SelectedAlbum.Artist?.Name ?? "—";
            var genre = SelectedAlbum.Genre?.Name;
            return $"Виконавець: {artist}" +
                   (genre is null ? "" : $" · Жанр: {genre}") +
                   $" · Рік: {SelectedAlbum.Year}";
        }
    }

    partial void OnCreateNewAlbumChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowArtistSelector));
        OnPropertyChanged(nameof(ShowGenreSelector));
        OnPropertyChanged(nameof(SelectedAlbumSummary));
    }

    partial void OnSelectedAlbumChanged(Album? value)
    {
        OnPropertyChanged(nameof(ShowGenreSelector));
        OnPropertyChanged(nameof(SelectedAlbumSummary));
    }

    // Type-to-search filter for the album AutoCompleteBox: matches the title
    // or the artist name, so "dyl" finds Dylan's albums too.
    public global::Avalonia.Controls.AutoCompleteFilterPredicate<object?> AlbumFilter { get; } =
        (search, item) =>
        {
            if (string.IsNullOrWhiteSpace(search) || item is not Album album) return true;
            return (album.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (album.Artist?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        };

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
            Title = "Редагування товару";
            var artist = existing.Album?.Artist?.Name;
            Subtitle = $"«{existing.Album?.Title}»" +
                       (artist is null ? "" : $" — {artist}") +
                       $" · {(existing.Format == ProductFormat.Vinyl ? "Вініл LP" : "CD")} · #{existing.Id}";
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

        _pristine = CurrentState();
    }

    // Snapshot of every editable field; comparing against it tells whether the
    // form has unsaved changes when the admin tries to leave.
    private string _pristine;

    private string CurrentState() => string.Join("",
        SelectedAlbum?.Id, CreateNewAlbum, NewAlbumTitle, NewAlbumYear, NewAlbumDescription,
        SelectedArtist?.Id, CreateNewArtist, NewArtistName,
        SelectedGenre?.Id, CreateNewGenre, NewGenreName,
        Format, Price, Stock, ReleaseYear, Label, IsActive,
        CoverPath, SamplePath, FullPath);

    public bool IsDirty => CurrentState() != _pristine;

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

    // Clearing is offered in ADD mode only: there an empty field honestly means
    // "nothing attached". In edit mode the service treats empty paths as "keep
    // existing" (an album's tracks may carry per-track files), so a clear
    // button would lie about what Save does.
    [RelayCommand] private void ClearCover() => CoverPath = null;
    [RelayCommand] private void ClearSample() => SamplePath = null;
    [RelayCommand] private void ClearFull() => FullPath = null;

    [RelayCommand]
    private void Save()
    {
        try
        {
            if (_existing is not null)
            {
                _catalog.UpdateProduct(_existing.Id, Format, Price, Stock, ReleaseYear, Label,
                    CoverPath, SamplePath, FullPath, IsActive);
            }
            else
            {
                // For an existing album the artist/genre selectors are hidden —
                // derive both from the album itself so the admin never has to
                // re-pick what the album already carries.
                var existingAlbum = CreateNewAlbum ? null : SelectedAlbum;
                var draft = new ProductDraft(
                    ExistingAlbumId: existingAlbum?.Id,
                    NewAlbumTitle: CreateNewAlbum ? NewAlbumTitle : null,
                    NewAlbumYear: NewAlbumYear,
                    NewAlbumDescription: NewAlbumDescription,
                    CoverPath: CoverPath,
                    ExistingArtistId: existingAlbum is not null
                        ? existingAlbum.ArtistId
                        : CreateNewArtist ? null : SelectedArtist?.Id,
                    NewArtistName: CreateNewArtist && existingAlbum is null ? NewArtistName : null,
                    ExistingGenreId: existingAlbum is not null
                        ? (existingAlbum.GenreId > 0 ? existingAlbum.GenreId : SelectedGenre?.Id)
                        : CreateNewGenre ? null : SelectedGenre?.Id,
                    NewGenreName: CreateNewGenre && existingAlbum is null ? NewGenreName : null,
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

    // First Cancel/Назад click on a dirty form asks for confirmation instead of
    // silently discarding the admin's input; the inline bar offers both ways out.
    [ObservableProperty] private bool _isConfirmingClose;

    [RelayCommand]
    private void Cancel()
    {
        if (IsDirty && !IsConfirmingClose)
        {
            IsConfirmingClose = true;
            return;
        }
        DialogResult = false;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void StayEditing() => IsConfirmingClose = false;

    [RelayCommand]
    private void DiscardAndClose()
    {
        DialogResult = false;
        CloseRequested?.Invoke();
    }
}
