using System;
using System.Collections.Generic;

namespace MusicApp.Services;

public interface ILikesService
{
    event EventHandler? Changed;

    bool IsTrackLiked(int userId, int trackId);
    void LikeTrack(int userId, int trackId);
    void UnlikeTrack(int userId, int trackId);
    IReadOnlyList<int> GetLikedTrackIds(int userId);

    bool IsAlbumLiked(int userId, int albumId);
    void LikeAlbum(int userId, int albumId);
    void UnlikeAlbum(int userId, int albumId);
    IReadOnlyList<int> GetLikedAlbumIds(int userId);
}
