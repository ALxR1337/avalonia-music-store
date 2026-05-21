using Avalonia.Headless.XUnit;

namespace MusicApp.BugHunt;

public class SmokeTests
{
    [AvaloniaFact]
    public void MainWindow_opens_and_resizes()
    {
        var h = new Harness();
        h.OpenMainWindow();

        h.RunStep("01-initial-1024x720", () => h.SetWindowSize(1024, 720));
        h.RunStep("02-tiny-400x300", () => h.SetWindowSize(400, 300));
    }
}
