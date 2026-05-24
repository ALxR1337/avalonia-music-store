using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MusicApp.Services;

public class FileDialogService : IFileDialogService
{
    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter>? filters = null)
    {
        var top = TopLevel();
        if (top is null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ToFileTypes(filters)
        };
        var picked = await top.StorageProvider.OpenFilePickerAsync(options);
        return picked.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> SaveFileAsync(string title, string defaultName, IReadOnlyList<FileFilter>? filters = null)
    {
        var top = TopLevel();
        if (top is null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultName,
            FileTypeChoices = ToFileTypes(filters)
        };
        var picked = await top.StorageProvider.SaveFilePickerAsync(options);
        return picked?.Path.LocalPath;
    }

    private static IReadOnlyList<FilePickerFileType>? ToFileTypes(IReadOnlyList<FileFilter>? filters)
    {
        if (filters is null || filters.Count == 0) return null;
        return filters.Select(f => new FilePickerFileType(f.Name)
        {
            Patterns = f.Patterns.ToList()
        }).ToList();
    }

    private static TopLevel? TopLevel()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
