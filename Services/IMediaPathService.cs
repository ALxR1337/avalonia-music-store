namespace MusicApp.Services;

public interface IMediaPathService
{
    string FullTrackPath(string relative);
    string SamplePath(string relative);
    string CoverPath(string relative);
    string ArtistPhotoPath(string relative);

    string StoreFullTrack(int albumId, int trackPosition, string sourceFile);
    string StoreCover(int albumId, byte[] data, string extension);
    string StoreCover(int albumId, string sourceFile);
    string StoreArtistPhoto(int artistId, string sourceFile);
    string ReserveSamplePath(int albumId, int trackPosition);
}
