using Avalonia;
using Avalonia.Headless;
using MusicApp;
using MusicApp.BugHunt;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

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
