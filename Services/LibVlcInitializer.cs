namespace MusicApp.Services;

internal static class LibVlcInitializer
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) == 1) return;
        try { LibVLCSharp.Shared.Core.Initialize(); }
        catch { /* libvlc unavailable — surface lazily via MediaPlayer construction */ }
    }
}
