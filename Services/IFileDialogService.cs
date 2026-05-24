using System.Collections.Generic;
using System.Threading.Tasks;

namespace MusicApp.Services;

public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns);

public interface IFileDialogService
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter>? filters = null);
    Task<string?> SaveFileAsync(string title, string defaultName, IReadOnlyList<FileFilter>? filters = null);
}
