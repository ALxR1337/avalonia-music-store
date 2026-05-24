using System;
using System.Collections.Generic;

namespace MusicApp.Services;

public interface IMetadataExtractor
{
    ParsedAlbum? ParseFolder(string folderPath);
    ParsedTrack? ParseFile(string filePath);
}

public sealed class ParsedAlbum
{
    public string Artist { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int? Year { get; init; }
    public string? Genre { get; init; }
    public byte[]? CoverBytes { get; init; }
    public string? CoverExtension { get; init; }
    public string? CoverFilePath { get; init; }
    public List<ParsedTrack> Tracks { get; init; } = new();
}

public sealed class ParsedTrack
{
    public string SourcePath { get; init; } = string.Empty;
    public int Position { get; init; }
    public string Title { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string? Lyrics { get; init; }
}
