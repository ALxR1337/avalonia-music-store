using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace MusicApp.Services;

public sealed class SampleGenerator : ISampleGenerator, IDisposable
{
    private static readonly object InitLock = new();
    private static bool _coreInitialized;

    private readonly LibVLC _libvlc;

    public SampleGenerator()
    {
        EnsureCoreInitialized();
        _libvlc = new LibVLC("--no-video", "--quiet");
    }

    public Task GenerateAsync(string sourcePath, int startSeconds, int durationSeconds, string outputPath, CancellationToken cancel = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source audio file not found", sourcePath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Vorbis/Ogg is bundled with libvlc on every platform by default. AAC (mp4a)
        // requires fdk-aac/faac, mp3 requires lame — both absent on common Linux installs.
        var sout = "#transcode{acodec=vorb,ab=128,channels=2,samplerate=44100}" +
                   $":standard{{access=file,mux=ogg,dst={EscapePath(outputPath)}}}";

        var stop = startSeconds + Math.Max(1, durationSeconds);

        using var media = new Media(_libvlc, sourcePath, FromType.FromPath);
        media.AddOption($":sout={sout}");
        media.AddOption($":start-time={startSeconds}");
        media.AddOption($":stop-time={stop}");
        media.AddOption(":sout-keep");
        media.AddOption(":no-sout-display");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new MediaPlayer(media);

        EventHandler<EventArgs> onEnd = (_, _) => tcs.TrySetResult(true);
        EventHandler<EventArgs> onError = (_, _) => tcs.TrySetException(new InvalidOperationException("LibVLC transcoding error"));

        player.EndReached += onEnd;
        player.EncounteredError += onError;

        using var cancelReg = cancel.Register(() => tcs.TrySetCanceled());

        if (!player.Play())
        {
            CleanupPlayer(player, onEnd, onError);
            throw new InvalidOperationException("MediaPlayer.Play() returned false; cannot start transcoding.");
        }

        return tcs.Task.ContinueWith(t =>
        {
            CleanupPlayer(player, onEnd, onError);
            if (t.IsFaulted) throw t.Exception!.InnerException!;
            if (t.IsCanceled) throw new OperationCanceledException(cancel);

            // EndReached fires even when the sout pipeline silently aborts (e.g. missing
            // encoder). A real 30s clip is at least ~50 KB at 128 kbps — anything tiny
            // means the transcode never produced audio frames.
            var size = new FileInfo(outputPath).Length;
            if (size < 4096)
                throw new InvalidOperationException(
                    $"Sample transcoding produced only {size} bytes — likely a missing libvlc " +
                    $"audio encoder. Output: {outputPath}");
        }, TaskScheduler.Default);
    }

    private static void CleanupPlayer(MediaPlayer player,
        EventHandler<EventArgs> onEnd,
        EventHandler<EventArgs> onError)
    {
        player.EndReached -= onEnd;
        player.EncounteredError -= onError;
        try { player.Stop(); } catch { /* ignore */ }
        player.Dispose();
    }

    private static string EscapePath(string path) => path.Replace("\\", "/");

    private static void EnsureCoreInitialized()
    {
        if (_coreInitialized) return;
        lock (InitLock)
        {
            if (_coreInitialized) return;
            Core.Initialize();
            _coreInitialized = true;
        }
    }

    public void Dispose() => _libvlc.Dispose();
}
