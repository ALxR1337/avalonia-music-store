using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicApp.Services;

public sealed class MetadataExtractor : IMetadataExtractor
{
    private static readonly string[] AudioExtensions =
        { ".mp3", ".m4a", ".flac", ".ogg", ".opus", ".wav", ".aac" };

    private static readonly string[] CoverNames =
        { "cover.jpg", "cover.jpeg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png" };

    private static readonly Regex LeadingTrackNumber = new(@"^\s*(\d{1,3})\s*[-._)\s]", RegexOptions.Compiled);

    public ParsedAlbum? ParseFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return null;

        var audioFiles = Directory.EnumerateFiles(folderPath)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (audioFiles.Count == 0) return null;

        var tracks = audioFiles.Select(ParseFile).Where(t => t != null).Cast<ParsedTrack>().ToList();
        if (tracks.Count == 0) return null;

        // Pull album-level info from the first track that has tags.
        string artist = string.Empty;
        string title = string.Empty;
        int? year = null;
        string? genre = null;
        byte[]? embeddedCover = null;
        string? embeddedCoverExt = null;

        foreach (var file in audioFiles)
        {
            try
            {
                using var tag = TagLib.File.Create(file);
                if (string.IsNullOrEmpty(artist))
                    artist = tag.Tag.FirstAlbumArtist ?? tag.Tag.FirstPerformer ?? string.Empty;
                if (string.IsNullOrEmpty(title))
                    title = tag.Tag.Album ?? string.Empty;
                if (year is null && tag.Tag.Year > 0) year = (int)tag.Tag.Year;
                if (genre is null) genre = tag.Tag.FirstGenre;
                if (embeddedCover is null && tag.Tag.Pictures.Length > 0)
                {
                    var pic = tag.Tag.Pictures[0];
                    embeddedCover = pic.Data.Data;
                    embeddedCoverExt = MimeToExt(pic.MimeType);
                }
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
                    break;
            }
            catch
            {
                // skip unreadable tag — fall back to filename
            }
        }

        if (string.IsNullOrEmpty(title))
            title = new DirectoryInfo(folderPath).Name;

        var coverFile = CoverNames
            .Select(name => Path.Combine(folderPath, name))
            .FirstOrDefault(File.Exists);

        return new ParsedAlbum
        {
            Artist = artist,
            Title = title,
            Year = year,
            Genre = genre,
            CoverBytes = embeddedCover,
            CoverExtension = embeddedCoverExt,
            CoverFilePath = coverFile,
            Tracks = tracks.OrderBy(t => t.Position).ThenBy(t => t.SourcePath).ToList()
        };
    }

    public ParsedTrack? ParseFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            using var tag = TagLib.File.Create(filePath);
            return new ParsedTrack
            {
                SourcePath = filePath,
                Position = tag.Tag.Track == 0
                    ? GuessPositionFromName(Path.GetFileName(filePath))
                    : (int)tag.Tag.Track,
                Title = !string.IsNullOrWhiteSpace(tag.Tag.Title)
                    ? tag.Tag.Title
                    : Path.GetFileNameWithoutExtension(filePath),
                Duration = tag.Properties?.Duration ?? TimeSpan.Zero,
                Lyrics = string.IsNullOrWhiteSpace(tag.Tag.Lyrics) ? null : tag.Tag.Lyrics
            };
        }
        catch
        {
            return new ParsedTrack
            {
                SourcePath = filePath,
                Position = GuessPositionFromName(Path.GetFileName(filePath)),
                Title = Path.GetFileNameWithoutExtension(filePath),
                Duration = TimeSpan.Zero,
                Lyrics = null
            };
        }
    }

    private static int GuessPositionFromName(string fileName)
    {
        var match = LeadingTrackNumber.Match(fileName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
    }

    private static string MimeToExt(string mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".jpg"
    };
}

internal static class ParsedListExtensions
{
    public static List<T> ToListSafe<T>(this IEnumerable<T>? src) =>
        src is null ? new List<T>() : src.ToList();
}
