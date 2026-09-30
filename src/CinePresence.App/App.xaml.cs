using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CinePresence.App.Services;
using Forms = System.Windows.Forms;

namespace CinePresence.App;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private AppController? controller;
    private Forms.NotifyIcon? tray;
    private MainWindow? window;
    private WatchNotificationService? notifications;
    private bool quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Isolated smoke mode renders the real WPF window without connecting to external services.
        if (e.Args.Contains("--smoke-test")) { RunSmoke(e.Args); return; }
        if (e.Args.Contains("--diagnose")) { RunDiagnostics(e.Args); return; }
        if (e.Args.Contains("--probe-vlc")) { RunVlcProbe(e.Args); return; }
        if (e.Args.Contains("--register-browser"))
        {
            try { BrowserSetup.Register(); Shutdown(0); } catch (Exception) { Shutdown(1); }
            return;
        }
        mutex = new Mutex(true, "Local\\CinePresence." + Environment.UserName, out var first);
        if (!first) { MessageBox.Show("CinePresence is already running. Open it from the system tray.", "CinePresence"); Shutdown(); return; }
        try
        {
            controller = new();
            window = new(controller);
            MainWindow = window;
            CreateTray();
            notifications = new(controller, Dispatcher, input => { if (!quitting) window.OpenCorrection(input); });
            controller.Start();
            if (!e.Args.Contains("--background") || !controller.Settings.OnboardingComplete) window.Show();
        }
        catch (Exception)
        { MessageBox.Show("CinePresence could not start. Check that its files were fully extracted and your local application-data folder is writable.", "CinePresence"); Quit(); }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open CinePresence", null, (_, _) => Dispatcher.Invoke(() => { window!.Show(); window.WindowState = WindowState.Normal; window.Activate(); }));
        var sharing = new Forms.ToolStripMenuItem("Enable sharing") { CheckOnClick = false, Checked = controller!.Settings.SharingEnabled };
        sharing.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            try { controller.SetSharing(!controller.Settings.SharingEnabled); }
            catch (Exception) { MessageBox.Show("Sharing changed, but the preference could not be saved.", "CinePresence"); }
        });
        menu.Items.Add(sharing); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        tray = new Forms.NotifyIcon { Text = "CinePresence", Icon = CreateIcon(), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { window!.Show(); window.WindowState = WindowState.Normal; window.Activate(); });
        controller.Changed += (_, _) => Dispatcher.BeginInvoke(() => sharing.Checked = controller.Settings.SharingEnabled);
        window!.Icon = Imaging.CreateBitmapSourceFromHIcon(tray.Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private static Icon CreateIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/CinePresence.ico"))!.Stream;
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }

    private async void Quit()
    {
        if (quitting) return;
        quitting = true;
        notifications?.Dispose();
        if (window is not null) window.AllowClose = true;
        if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        if (controller is not null) await controller.DisposeAsync();
        mutex?.Dispose(); Shutdown();
    }

    private async void RunSmoke(string[] args)
    {
        var output = args.SkipWhile(x => x != "--smoke-test").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(output)) { Shutdown(2); return; }
        try
        {
            Directory.CreateDirectory(output);
            controller = new(Path.Combine(output, "isolated-data"));
            window = new(controller) { AllowClose = true };
            window.Show();
            await Task.Delay(400);
            window.UpdateLayout();
            var posterPath = args.SkipWhile(x => x != "--smoke-poster").Skip(1).FirstOrDefault();
            await window.CaptureSmokeAsync(output, posterPath);
            await controller.DisposeAsync();
            window.Close(); Shutdown(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output!, "smoke-error.txt"), ex.ToString());
            Shutdown(1);
        }
    }

    private async void RunDiagnostics(string[] args)
    {
        var output = args.SkipWhile(x => x != "--diagnose").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(output)) { Shutdown(2); return; }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await using var windows = new WindowsMediaAdapter();
            var sessions = await windows.ReadAsync(timeout.Token);
            if (args.Contains("--sample"))
                for (var i = 0; i < 5; i++) { await Task.Delay(2000, timeout.Token); sessions = await windows.ReadAsync(timeout.Token); }
            using var discord = new DiscordPublisher();
            if (!args.Contains("--media-only"))
            {
                discord.Connect(SettingsStore.ReleaseApplicationId());
                for (var i = 0; i < 40 && !discord.Connected; i++) await Task.Delay(100, timeout.Token);
            }
            var lookups = new List<object>();
            if (args.Contains("--resolve"))
            {
                var settingsStore = new SettingsStore();
                var settings = settingsStore.Load();
                var token = settingsStore.Unprotect(settings.ProtectedTmdbToken);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var resolver = new MediaResolver(new TmdbClient(http, () => token), new MediaCache());
                foreach (var session in sessions)
                {
                    var parsed = TitleParser.Parse(session);
                    if (parsed is null) continue;
                    try
                    {
                        var media = await resolver.ResolveAsync(parsed, timeout.Token);
                        lookups.Add(new { source = session.SourceName, hasToken = token.Length > 0, match = media });
                    }
                    catch (ServiceException ex) { lookups.Add(new { source = session.SourceName, hasToken = token.Length > 0, error = ex.Message }); }
                }
            }
            var report = new
            {
                windows = windows.Status,
                sessions = sessions.Select(x => new { player = x.SourceName, state = x.Status.ToString(), title = TitleParser.Parse(x.Title, x.Subtitle, x.AlbumTitle), titleFromWindow = x.TitleFromWindow, hasTitle = !string.IsNullOrWhiteSpace(x.Title), hasPosition = x.Position.HasValue, hasDuration = x.Duration.HasValue, positionSeconds = x.Position?.TotalSeconds, durationSeconds = x.Duration?.TotalSeconds, isLive = x.IsLive, isMusic = x.IsMusic, ignoredReason = x.IgnoredReason, requiresConfirmation = x.RequiresConfirmation, eligible = TitleParser.Parse(x) is not null }),
                lookups,
                discord = discord.Status,
                discordConnected = discord.Connected,
                presencePublished = false
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Shutdown(0);
        }
        catch (Exception ex)
        { File.WriteAllText(output, ex.GetType().Name + ": diagnostic check failed."); Shutdown(1); }
    }

    private async void RunVlcProbe(string[] args)
    {
        var values = args.SkipWhile(x => x != "--probe-vlc").Skip(1).ToArray();
        if (values.Length < 2 || !int.TryParse(values[1], out var port)) { Shutdown(2); return; }
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var password = Environment.GetEnvironmentVariable("CINEPRESENCE_TEST_VLC_PASSWORD") ?? "";
            await using var adapter = new VlcAdapter(http, () => new(true, port, password));
            var readings = await adapter.ReadAsync(default);
            var report = new { status = adapter.Status, sources = readings.Select(x => new { title = TitleParser.Parse(x.Title)?.Title, state = x.Status.ToString(), position = x.Position?.TotalSeconds, duration = x.Duration?.TotalSeconds, rate = x.PlaybackRate, music = x.IsMusic }) };
            File.WriteAllText(values[0], System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Shutdown(0);
        }
        catch (Exception) { Shutdown(1); }
    }
}
