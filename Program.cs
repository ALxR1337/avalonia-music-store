using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia;

namespace MusicApp;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureUkrainianCulture();
        ConfigureLinuxScaling();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Ukrainian (uk-UA) thread + default culture so dates render as "21.05.2026"
    // and prices/numbers use the comma decimal sep + non-breaking-space thousand sep.
    private static void ConfigureUkrainianCulture()
    {
        var culture = new CultureInfo("uk-UA");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // XWayland's xdg_positioner translation mispositions/flickers popups;
            // overlay-render keeps them inside the window. Linux-only, no-op elsewhere.
            .With(new X11PlatformOptions { OverlayPopups = true })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    // XWayland sessions don't propagate the compositor's fractional scale to X11 clients.
    // Avalonia honours AVALONIA_GLOBAL_SCALE_FACTOR — so derive it from common desktop hints
    // and set it before AppBuilder reads its env.
    private static void ConfigureLinuxScaling()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Already set by user — respect it.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS")))
            return;

        // Explicit per-app override: MUSICAPP_SCALE=1.25 dotnet run
        var custom = ParseScale(Environment.GetEnvironmentVariable("MUSICAPP_SCALE"));
        if (custom is { } c)
        {
            Apply(c);
            return;
        }

        var detected =
            FromGdk() ??
            FromQt() ??
            FromNiri() ??
            FromGnomeTextScale() ??
            FromKde() ??
            1.0;

        if (detected > 1.01 || detected < 0.99)
            Apply(detected);

        if (Environment.GetEnvironmentVariable("MUSICAPP_SCALE_DEBUG") == "1")
            Console.WriteLine($"[scale] detected={detected:0.###} AVALONIA_GLOBAL_SCALE_FACTOR={Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") ?? "(unset)"}");
    }

    private static void Apply(double factor) =>
        Environment.SetEnvironmentVariable(
            "AVALONIA_GLOBAL_SCALE_FACTOR",
            factor.ToString("0.###", CultureInfo.InvariantCulture));

    private static double? FromGdk()
    {
        var s = ParseScale(Environment.GetEnvironmentVariable("GDK_SCALE"));
        var d = ParseScale(Environment.GetEnvironmentVariable("GDK_DPI_SCALE"));
        if (s is null && d is null) return null;
        return (s ?? 1.0) * (d ?? 1.0);
    }

    private static double? FromQt() => ParseScale(Environment.GetEnvironmentVariable("QT_SCALE_FACTOR"));

    private static double? FromNiri()
    {
        // niri (Wayland) does not propagate fractional scale to XWayland clients.
        // `niri msg outputs` reports each output and its current Scale; pick the first one.
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "niri",
                Arguments = "msg outputs",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (p is null) return null;
            if (!p.WaitForExit(500)) { p.Kill(true); return null; }
            foreach (var line in p.StandardOutput.ReadToEnd().Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith("Scale:", StringComparison.OrdinalIgnoreCase))
                    return ParseScale(t.Substring("Scale:".Length).Trim());
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static double? FromGnomeTextScale()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "gsettings",
                Arguments = "get org.gnome.desktop.interface text-scaling-factor",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (p is null) return null;
            if (!p.WaitForExit(500)) { p.Kill(true); return null; }
            return ParseScale(p.StandardOutput.ReadToEnd().Trim());
        }
        catch { return null; }
    }

    private static double? FromKde()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(home, ".config", "kdeglobals");
            if (!File.Exists(path)) return null;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("ScaleFactor=", StringComparison.OrdinalIgnoreCase))
                    return ParseScale(line[(line.IndexOf('=') + 1)..]);
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static double? ParseScale(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0.1 && v < 8)
            return v;
        return null;
    }
}
