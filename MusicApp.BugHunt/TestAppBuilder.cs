using Avalonia;
using Avalonia.Headless;
using MusicApp;
using MusicApp.BugHunt;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Avalonia.Headless uses a single UI dispatcher per app; running [AvaloniaFact]
// tests in parallel deadlocks the host (seen as a 2-minute hang dump).
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]

namespace MusicApp.BugHunt;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false,
            FrameBufferFormat = Avalonia.Platform.PixelFormat.Rgba8888,
        });
}
