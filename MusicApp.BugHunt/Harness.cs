using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.ViewModels;
using MusicApp.Views;

namespace MusicApp.BugHunt;

/// <summary>
/// Drives MusicApp under Avalonia.Headless. Skips the login flow and builds
/// the post-login MainWindow + services directly.
/// </summary>
public sealed class Harness
{
    public Window? Window { get; private set; }

    public static string ArtifactsDir { get; } = ResolveArtifactsDir();

    public Window OpenMainWindow()
    {
        Directory.CreateDirectory(ArtifactsDir);

        var dbFactory = new MusicStoreDbContextFactory();
        using (var db = dbFactory.CreateDbContext())
            DbSeeder.EnsureSeeded(db);

        var nav = new NavigationService();
        var auth = new AuthService(dbFactory);
        var cart = new CartService(auth, dbFactory);
        var player = new PlayerService();
        var catalog = new CatalogService(dbFactory);

        nav.Register(NavTarget.Catalog,
            _ => new CatalogViewModel(catalog, nav, player, cart));
        nav.Register(NavTarget.SearchResults,
            p => new SearchResultsViewModel(catalog, nav, player, cart, p as string ?? string.Empty));
        nav.Register(NavTarget.Product,
            p => new ProductViewModel(catalog, cart, player, (int)(p ?? 1)));
        nav.Register(NavTarget.Cart,
            _ => new CartViewModel(cart, nav));
        nav.Register(NavTarget.Profile,
            _ => new ProfileViewModel(auth, catalog));
        nav.Register(NavTarget.Orders,
            _ => new OrdersViewModel(catalog, auth));
        nav.Register(NavTarget.Player,
            _ => new PlayerViewModel(player, catalog));
        nav.Register(NavTarget.Admin,
            _ => new AdminViewModel(catalog));

        var vm = new MainWindowViewModel(nav, auth, cart, player, catalog);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Window = window;
        Pump();
        return window;
    }

    public T Find<T>(string name) where T : Control
    {
        EnsureWindow();
        var found = FindByName<T>(Window!, name);
        if (found is null)
            throw new InvalidOperationException(
                $"Control of type {typeof(T).Name} with x:Name='{name}' not found in the visual tree.");
        return found;
    }

    public void Click(string name)
    {
        var btn = Find<Button>(name);
        if (!btn.IsEffectivelyVisible || !btn.IsEffectivelyEnabled)
            throw new InvalidOperationException(
                $"Button '{name}' is not clickable (visible={btn.IsEffectivelyVisible}, enabled={btn.IsEffectivelyEnabled}).");

        if (btn.Command is { } cmd && cmd.CanExecute(btn.CommandParameter))
            cmd.Execute(btn.CommandParameter);
        else
            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Pump();
    }

    public void Type(string name, string text)
    {
        var tb = Find<TextBox>(name);
        tb.Text = text;
        Pump();
    }

    public void SetWindowSize(int w, int h)
    {
        EnsureWindow();
        // Clear the window's own size constraints so the harness can probe arbitrarily
        // small/large sizes (e.g. catalog layout at 320x240). Real users hit the
        // constraints; the harness deliberately bypasses them to find layout breakage.
        Window!.MinWidth = 0;
        Window!.MinHeight = 0;
        Window!.MaxWidth = double.PositiveInfinity;
        Window!.MaxHeight = double.PositiveInfinity;
        Window!.Width = w;
        Window!.Height = h;
        Window.InvalidateMeasure();
        Window.InvalidateArrange();
        Pump();
    }

    public string Snapshot(string label)
    {
        EnsureWindow();
        Pump();
        var path = Path.Combine(ArtifactsDir, $"{Timestamp()}-{Sanitize(label)}.png");
        var bmp = Window!.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "CaptureRenderedFrame returned null. Ensure UseHeadlessDrawing=false in TestAppBuilder.");
        using (bmp)
            bmp.Save(path);
        return path;
    }

    public string DumpTree(string label)
    {
        EnsureWindow();
        Pump();
        var path = Path.Combine(ArtifactsDir, $"{Timestamp()}-{Sanitize(label)}.tree.txt");
        var sb = new StringBuilder();
        sb.Append("# Window: ").Append(Window!.GetType().FullName)
          .Append("  Size=").Append(Window.Width.ToString("0", CultureInfo.InvariantCulture))
          .Append('x').Append(Window.Height.ToString("0", CultureInfo.InvariantCulture))
          .AppendLine();
        sb.Append("# Captured: ").AppendLine(DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        sb.AppendLine();
        Walk(Window, sb, 0);
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public (string snapshot, string tree) RunStep(string label, Action act)
    {
        act();
        Pump();
        return (Snapshot(label), DumpTree(label));
    }

    // -------- helpers --------

    private void EnsureWindow()
    {
        if (Window is null)
            throw new InvalidOperationException("OpenMainWindow() has not been called yet.");
    }

    private static void Pump()
    {
        // Drain queued dispatcher work and force a render frame so layout/visuals settle
        // before snapshot / tree dump.
        Dispatcher.UIThread.RunJobs();
    }

    private static T? FindByName<T>(Visual root, string name) where T : Control
    {
        if (root is T match && match.Name == name) return match;
        foreach (var child in root.GetVisualChildren())
        {
            if (FindByName<T>(child, name) is { } hit) return hit;
        }
        return null;
    }

    private static void Walk(Visual node, StringBuilder sb, int depth)
    {
        Indent(sb, depth);
        sb.Append(node.GetType().Name);

        if (node is StyledElement se && !string.IsNullOrEmpty(se.Name))
            sb.Append("  Name=").Append(se.Name);

        if (node is Control c)
        {
            var b = c.Bounds;
            sb.Append("  Bounds=")
              .Append(b.X.ToString("0", CultureInfo.InvariantCulture)).Append(',')
              .Append(b.Y.ToString("0", CultureInfo.InvariantCulture)).Append(' ')
              .Append(b.Width.ToString("0", CultureInfo.InvariantCulture)).Append('x')
              .Append(b.Height.ToString("0", CultureInfo.InvariantCulture));
            sb.Append("  IsVisible=").Append(c.IsVisible ? "1" : "0");
            sb.Append("  IsEnabled=").Append(c.IsEnabled ? "1" : "0");

            var text = ExtractText(c);
            if (!string.IsNullOrEmpty(text))
                sb.Append("  Text=\"").Append(Escape(text!)).Append('"');

            if (c.DataContext is { } dc)
                sb.Append("  DataContext=").Append(dc.GetType().Name);
        }

        sb.AppendLine();

        foreach (var child in node.GetVisualChildren())
            Walk(child, sb, depth + 1);
    }

    private static string? ExtractText(Control c) => c switch
    {
        TextBlock tb => tb.Text,
        TextBox tx => tx.Text,
        ContentControl cc when cc.Content is string s => s,
        HeaderedContentControl hcc when hcc.Header is string s => s,
        _ => null,
    };

    private static void Indent(StringBuilder sb, int depth)
    {
        for (var i = 0; i < depth; i++) sb.Append("  ");
    }

    private static string Escape(string s)
    {
        var trimmed = s.Length > 80 ? s.Substring(0, 77) + "..." : s;
        return trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    private static string Sanitize(string label)
    {
        var sb = new StringBuilder(label.Length);
        foreach (var ch in label)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        return sb.ToString();
    }

    private static string Timestamp() =>
        DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

    private static string ResolveArtifactsDir([CallerFilePath] string callerPath = "")
    {
        // callerPath is the compile-time path of THIS file (Harness.cs) inside MusicApp.BugHunt/.
        // The repo root is its parent's parent.
        var projDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(projDir, ".."));
        return Path.Combine(repoRoot, "bug-hunt", "artifacts");
    }

}
