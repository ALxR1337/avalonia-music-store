using System.IO;
using System.Text.Json;
using MusicApp.Data;

namespace MusicApp.Services;

/// <summary>
/// Persists the "remember me" session — just the logged-in user's id — to a
/// small JSON file next to the store DB. Honest scope: it lets the app skip
/// the login overlay on next launch (re-loading the user from the local DB),
/// not a real auth token. Path derives from <see cref="MusicStoreDbContext.ResolveDbPath"/>
/// so the BugHunt harness's isolated DB keeps an isolated session file too.
/// </summary>
public sealed class SessionStore
{
    private sealed class State
    {
        public int UserId { get; set; }
    }

    private static string FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(MusicStoreDbContext.ResolveDbPath());
            return Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "session.json");
        }
    }

    public int? LoadUserId()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
            return state is { UserId: > 0 } ? state.UserId : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(int userId)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new State { UserId = userId }));
        }
        catch
        {
            // Best-effort: a failed write just means the user re-logs next launch.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // Best-effort.
        }
    }
}
