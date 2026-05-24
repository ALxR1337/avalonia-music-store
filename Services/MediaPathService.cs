using System.IO;
using MusicApp.Data;

namespace MusicApp.Services;

public sealed class MediaPathService : IMediaPathService
{
    private readonly string _root;

    public MediaPathService() : this(MusicStoreDbContext.DefaultDbDirectory) { }

    public MediaPathService(string root)
    {
        _root = root;
        Directory.CreateDirectory(Path.Combine(_root, "full"));
        Directory.CreateDirectory(Path.Combine(_root, "samples"));
        Directory.CreateDirectory(Path.Combine(_root, "covers"));
        Directory.CreateDirectory(Path.Combine(_root, "artists"));
    }

    public string FullTrackPath(string relative) => Path.Combine(_root, "full", relative);
    public string SamplePath(string relative) => Path.Combine(_root, "samples", relative);
    public string CoverPath(string relative) => Path.Combine(_root, "covers", relative);
    public string ArtistPhotoPath(string relative) => Path.Combine(_root, "artists", relative);

    public string StoreFullTrack(int albumId, int trackPosition, string sourceFile)
    {
        var ext = Path.GetExtension(sourceFile);
        var relative = Path.Combine(albumId.ToString(), $"{trackPosition:D2}{ext}");
        var dest = FullTrackPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(sourceFile, dest, overwrite: true);
        return relative;
    }

    public string StoreCover(int albumId, byte[] data, string extension)
    {
        var relative = $"{albumId}{NormalizeExt(extension)}";
        var dest = CoverPath(relative);
        File.WriteAllBytes(dest, data);
        return relative;
    }

    public string StoreCover(int albumId, string sourceFile)
    {
        var relative = $"{albumId}{NormalizeExt(Path.GetExtension(sourceFile))}";
        var dest = CoverPath(relative);
        File.Copy(sourceFile, dest, overwrite: true);
        return relative;
    }

    public string StoreArtistPhoto(int artistId, string sourceFile)
    {
        var relative = $"{artistId}{NormalizeExt(Path.GetExtension(sourceFile))}";
        var dest = ArtistPhotoPath(relative);
        File.Copy(sourceFile, dest, overwrite: true);
        return relative;
    }

    public string ReserveSamplePath(int albumId, int trackPosition)
    {
        var relative = Path.Combine(albumId.ToString(), $"{trackPosition:D2}.ogg");
        var abs = SamplePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        return relative;
    }

    private static string NormalizeExt(string ext) =>
        string.IsNullOrEmpty(ext) ? ".jpg" : (ext.StartsWith('.') ? ext : "." + ext);
}
