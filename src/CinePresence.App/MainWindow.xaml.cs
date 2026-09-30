using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CinePresence.App.Services;

namespace CinePresence.App;

public partial class MainWindow : Window
{
    public sealed record SourceRow(string Id, string SourceId, string Heading, string Detail, string Capabilities);
    private readonly AppController controller;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient images = new() { Timeout = TimeSpan.FromSeconds(10) };
    private int refreshQueued;
    private string? posterUrl;
    private MatchWindow? matchDialog;
    public bool AllowClose { get; set; }

    public MainWindow(AppController controller)
    {
        this.controller = controller;
        InitializeComponent();
        TokenBox.Password = controller.TmdbToken;
        AppIdBox.Text = controller.Settings.DiscordApplicationIdOverride;
        AppIdLabel.Text = "Shared application ID: " + SettingsStore.ReleaseApplicationId();
        VlcEnabledBox.IsChecked = controller.Settings.VlcEnabled;
        VlcPortBox.Text = controller.Settings.VlcPort.ToString();
        VlcPasswordBox.Password = controller.VlcPassword;
        StartupBox.IsChecked = controller.Settings.StartWithWindows;
        PopupEnabledBox.IsChecked = controller.Settings.ShowWatchingPopup;
        OnboardingBanner.Visibility = controller.Settings.OnboardingComplete ? Visibility.Collapsed : Visibility.Visible;
        if (!controller.Settings.OnboardingComplete) Tabs.SelectedIndex = 2;
        SaveResult.Text = controller.Warning;
        TmdbLogo.Source = TmdbBranding.Load();
        controller.Changed += Controller_Changed;
        Closed += (_, _) => { controller.Changed -= Controller_Changed; lifetime.Cancel(); images.Dispose(); lifetime.Dispose(); };
        Refresh();
    }

    private void Controller_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref refreshQueued, 0); if (!lifetime.IsCancellationRequested) Refresh(); });
    }

    private void Refresh()
    {
        var view = controller.Engine.View;
        SharingButton.Content = view.Sharing ? "Sharing on" : "Sharing off";
        SharingButton.Style = (Style)FindResource(view.Sharing ? "PrimaryButton" : typeof(Button));
        ConnectionText.Text = controller.Discord.Status;
        ConnectionDot.Fill = new SolidColorBrush(controller.Discord.Connected ? Color.FromRgb(125, 217, 167) : Color.FromRgb(224, 175, 104));
        ShowPlayback(view);
        var selected = (SourceList.SelectedItem as SourceRow)?.Id;
        SourceList.ItemsSource = view.Sources.Select(x => new SourceRow(x.SessionId, x.SourceId,
            x.SourceName + (controller.Settings.ExcludedSources.Contains(x.SourceId) ? " · excluded" : ""),
            $"{x.Status} · {TitleParser.Parse(x.Title, x.Subtitle, x.AlbumTitle)?.Title ?? "Unidentified media"}",
            $"{(x.Adapter == AdapterKind.Browser ? "Browser companion" : x.Adapter == AdapterKind.Windows ? "Windows media session" : "Local VLC connection")} · {(x.IsLive ? "Live stream · no fixed end time" : x.Duration is not null && x.Position is not null ? "Timing available" : "Timing unavailable")}{(x.IsMusic ? " · known audio, skipped" : "")}{(x.TitleFromWindow ? " · title from active tab/window" : "")}{(x.IgnoredReason is not null ? " · " + x.IgnoredReason : x.RequiresConfirmation ? " · confirm movie/show before sharing" : "")}{(string.IsNullOrWhiteSpace(x.Title) ? x.Adapter == AdapterKind.Browser ? " · enter a title in the companion popup" : " · title unavailable: select the playing tab" : "")}")).ToList();
        SourceList.SelectedItem = SourceList.Items.Cast<SourceRow>().FirstOrDefault(x => x.Id == selected);
        WindowsStatus.Text = controller.Windows.Status;
        VlcStatus.Text = controller.Vlc.Status;
        BrowserStatus.Text = controller.Browser.Status;
        SelectionMode.Text = controller.PinnedSession is null ? "Automatic source selection" : "Source pinned · use Automatic to follow another player";
    }

    private void ShowPlayback(EngineView view)
    {
        SourceBadge.Text = view.Source?.SourceName ?? "Waiting for playback";
        MediaTitle.Text = view.Media?.Title ?? (view.Source is not null ? TitleParser.Parse(view.Source)?.Title : null) ?? "Something good is next.";
        var isLive = view.Source?.IsLive == true;
        MediaDescription.Text = (isLive ? "Live · " : "") + (view.Media?.Description ?? "Play a movie or episode in your favorite player.");
        PlaybackMessage.Text = view.Message;
        CorrectButton.IsEnabled = view.Source is not null || view.Sources.Any(x => !x.IsMusic && x.IgnoredReason is null && TitleParser.Parse(x.Title, x.Subtitle, x.AlbumTitle) is not null);
        TmdbButton.IsEnabled = view.Media is not null;
        var position = view.Source?.PositionSeconds(DateTimeOffset.UtcNow);
        var duration = isLive ? null : view.Source?.Duration?.TotalSeconds;
        PlaybackProgress.Visibility = isLive ? Visibility.Collapsed : Visibility.Visible;
        PlaybackProgress.Value = position is not null && duration is > 0 ? Math.Clamp(position.Value / duration.Value * 100, 0, 100) : 0;
        ElapsedText.Text = isLive ? "● LIVE" : FormatTime(position);
        DurationText.Text = isLive ? "" : FormatTime(duration);
        TimingNote.Text = isLive ? "Live broadcast · no fixed end time. Your title and artwork are shared."
            : position is not null && duration is > 0
            ? (view.Source!.PlaybackRate != 1 ? $"{view.Source.PlaybackRate:0.##}× speed · Discord's clock scales with playback." : "Live playback progress · synchronized with your player")
            : "Your player has not shared a complete playback timeline.";
        var url = view.Media?.PosterUrl;
        if (url != posterUrl) { posterUrl = url; Poster.Source = null; if (url is not null) _ = LoadPosterAsync(url); }
    }

    private async Task LoadPosterAsync(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "image.tmdb.org") return;
            var bytes = await images.GetByteArrayAsync(uri, lifetime.Token);
            if (posterUrl != url || lifetime.IsCancellationRequested) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 350; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            Poster.Source = bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or NotSupportedException or ArgumentException) { }
    }

    private static string FormatTime(double? seconds)
    {
        if (seconds is null || !double.IsFinite(seconds.Value)) return "—:—";
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds.Value));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }

    private void Sharing_Click(object sender, RoutedEventArgs e) => TryAction(() => controller.SetSharing(!controller.Settings.SharingEnabled));
    private void AutoSource_Click(object sender, RoutedEventArgs e) => controller.Pin(null);
    private void PinSource_Click(object sender, RoutedEventArgs e) { if (SourceList.SelectedItem is SourceRow row) controller.Pin(row.Id); }
    private void ExcludeSource_Click(object sender, RoutedEventArgs e)
    { if (SourceList.SelectedItem is SourceRow row) TryAction(() => controller.Exclude(row.SourceId, !controller.Settings.ExcludedSources.Contains(row.SourceId))); }
    private void SourceSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        var row = SourceList.SelectedItem as SourceRow;
        PinButton.IsEnabled = row is not null; ExcludeButton.IsEnabled = row is not null;
        ExcludeButton.Content = row is not null && controller.Settings.ExcludedSources.Contains(row.SourceId) ? "Allow selected" : "Exclude selected";
    }

    private async void TestToken_Click(object sender, RoutedEventArgs e)
    {
        ValidateTokenButton.IsEnabled = false; TokenResult.Text = "Testing token…";
        try { await controller.Tmdb.ValidateAsync(TokenBox.Password.Trim(), lifetime.Token); TokenResult.Text = "Token accepted. Save settings to start identifying titles."; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TokenResult.Text = SafeMessage(ex); }
        finally { ValidateTokenButton.IsEnabled = true; }
    }

    private async void TestVlc_Click(object sender, RoutedEventArgs e)
    {
        TestVlcButton.IsEnabled = false; VlcResult.Text = "Connecting to VLC…";
        try
        {
            if (!int.TryParse(VlcPortBox.Text, out var port)) throw new ServiceException("Enter a numeric VLC port.");
            VlcResult.Text = await controller.Vlc.TestAsync(new(true, port, VlcPasswordBox.Password), lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { VlcResult.Text = SafeMessage(ex); }
        finally { TestVlcButton.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(VlcPortBox.Text, out var port)) throw new ServiceException("Enter a numeric VLC port.");
            controller.SaveSettings(TokenBox.Password, AppIdBox.Text, VlcEnabledBox.IsChecked == true, port, VlcPasswordBox.Password, StartupBox.IsChecked == true, PopupEnabledBox.IsChecked == true);
            SaveResult.Text = "Settings saved. CinePresence is ready.";
            OnboardingBanner.Visibility = controller.Settings.OnboardingComplete ? Visibility.Collapsed : Visibility.Visible;
            Refresh();
        }
        catch (Exception ex) { SaveResult.Text = SafeMessage(ex); }
    }
    private void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        var id = string.IsNullOrWhiteSpace(AppIdBox.Text) ? SettingsStore.ReleaseApplicationId() : AppIdBox.Text.Trim();
        controller.Discord.Connect(id);
    }
    private void GetToken_Click(object sender, RoutedEventArgs e) => OpenUrl("https://www.themoviedb.org/settings/api");
    private void ViewTmdb_Click(object sender, RoutedEventArgs e) { if (controller.Engine.View.Media is { } media) OpenUrl(media.Url); }
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("Your browser could not be opened. Check your default browser settings.", "CinePresence"); }
    }
    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear cached matches and all saved match corrections?", "Clear match cache", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        TryAction(() => { controller.Cache.Clear(); controller.RefreshMatch(); SaveResult.Text = "Match cache and corrections cleared."; });
    }
    private void Correct_Click(object sender, RoutedEventArgs e)
    {
        var view = controller.Engine.View;
        var source = view.Source ?? view.Sources.FirstOrDefault(x => !x.IsMusic && x.IgnoredReason is null && TitleParser.Parse(x.Title, x.Subtitle, x.AlbumTitle) is not null);
        if (source is null) return;
        var parsed = TitleParser.Parse(source.Title, source.Subtitle, source.AlbumTitle);
        if (parsed is null) { MessageBox.Show("The player needs to provide a usable title before a correction can be remembered.", "CinePresence"); return; }
        OpenCorrection(parsed);
    }
    public void OpenCorrection(ParsedTitle input)
    {
        Show(); WindowState = WindowState.Normal; Tabs.SelectedIndex = 0; Activate();
        if (matchDialog is not null) { matchDialog.Activate(); return; }
        // Keep the clicked title as the correction target even if playback
        // advances while the user is searching. Never edit a different episode.
        matchDialog = new MatchWindow(controller, input) { Owner = this };
        try { if (matchDialog.ShowDialog() == true) controller.RefreshMatch(); }
        finally { matchDialog = null; }
    }
    private void VlcGuide_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("1. First check Sources while VLC plays a video. If title and timing are already available, HTTP is optional.\n\n2. In VLC: Tools → Preferences → Show settings: All → Interface → Main interfaces. Enable Web.\n\n3. Open Main interfaces → Lua and set a strong Lua HTTP password. Enter the same password here.\n\n4. Restrict the HTTP interface to localhost. With VLC closed, set http-host=127.0.0.1 in %APPDATA%\\vlc\\vlcrc (and http-port=8080, or your chosen port). Preserve all other settings.\n\n5. Restart VLC, play a video, then Test VLC connection. Enable the adapter and Save settings.\n\nCinePresence only contacts 127.0.0.1 and never changes VLC settings automatically. More detail is included in docs/VLC-SETUP.md.", "VLC setup", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void BrowserSetup_Click(object sender, RoutedEventArgs e) => TryAction(() =>
    {
        BrowserSetup.Register();
        Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "browser-setup.html")) { UseShellExecute = true });
        BrowserSetupResult.Text = "Connection prepared. Follow the opened guide to enable the companion, then reload your playing tab.";
    });
    private void BrowserFolder_Click(object sender, RoutedEventArgs e) => TryAction(() =>
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + BrowserSetup.ExtensionDirectory + "\"") { UseShellExecute = true }));
    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(SafeMessage(ex), "CinePresence"); }
    }
    private static string SafeMessage(Exception ex) => ex is ServiceException ? ex.Message : "This action could not finish. Check your connection and local folder permissions.";
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true; Hide();
    }

    // Used by the command-line UI smoke test. No real presence is published.
    public async Task CaptureSmokeAsync(string directory, string? posterPath = null)
    {
        Capture("onboarding.png");
        Tabs.SelectedIndex = 0;
        Capture("now-playing-empty.png");
        var now = DateTimeOffset.UtcNow;
        var source = new PlaybackSnapshot("sample", "vlc", "VLC", AdapterKind.Vlc, "Example.Series.S02E04.mkv", "", "", "", false, PlaybackStatus.Playing, TimeSpan.FromMinutes(17), TimeSpan.FromMinutes(46), 1, now, now);
        ShowPlayback(new(source, new(1, MediaType.Tv, "Example Series", 2026, null, 2, 4, "A New Beginning"), "Preview fixture · no activity published", true, false, [source]));
        Capture("now-playing-fixture.png");
        var live = source with { Title = "Watch Example Live Show live stream for free on stream.example", IsLive = true };
        ShowPlayback(new(live, new(2, MediaType.Tv, "Example Live Show", 2026, null), "Live fixture · no activity published", true, false, [live]));
        Capture("now-playing-live.png");
        var popupClicked = false;
        var notice = new WatchNotice(new("Regular Show", null, 6, 13), new(1, MediaType.Tv, "Regular Show", 2010, null, 6, 13, "Mordecai and Rigby Down Under"), false);
        var popup = new WatchPopup(notice, input => popupClicked = input == notice.Input);
        if (!string.IsNullOrWhiteSpace(posterPath) && File.Exists(posterPath))
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(posterPath)); bitmap.EndInit(); bitmap.Freeze();
            popup.ShowArtwork.Source = bitmap;
        }
        popup.Show(); await Task.Delay(350); popup.Capture(Path.Combine(directory, "watching-popup.png"));
        popup.ChangeMatchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(250);
        if (!popupClicked) throw new InvalidOperationException("Popup did not pass its captured title to correction.");
        var timedPopup = new WatchPopup(notice, _ => { });
        var timer = Stopwatch.StartNew();
        timedPopup.Show();
        while (timedPopup.IsVisible && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(100);
        if (timedPopup.IsVisible) { timedPopup.Close(); throw new InvalidOperationException("Popup did not auto-dismiss."); }
        if (timer.Elapsed.TotalSeconds is < 6.5 or > 9) throw new InvalidOperationException("Popup dismissal timing is outside tolerance.");
        SourceList.ItemsSource = new[] { new SourceRow("sample", "vlc", "VLC", "Playing · Example Series", "Local VLC connection · Timing available") };
        Tabs.SelectedIndex = 1; Capture("sources.png");
        Tabs.SelectedIndex = 2; SettingsScroll.ScrollToEnd(); Capture("settings-bottom.png");
        File.WriteAllText(Path.Combine(directory, "smoke-ok.txt"), $"Rendered app and compact artwork popup. Animated correction action passed; automatic dismissal completed after {timer.Elapsed.TotalSeconds:0.00}s. No external service was contacted.");
        void Capture(string name)
        {
            UpdateLayout();
            var content = (FrameworkElement)Content;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle((Brush)FindResource("BackgroundBrush"), null, new Rect(0, 0, content.ActualWidth + 56, content.ActualHeight + 56));
                var offset = VisualTreeHelper.GetOffset(content);
                var brush = new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(offset.X, offset.Y, content.ActualWidth, content.ActualHeight) };
                context.DrawRectangle(brush, null, new Rect(28, 28, content.ActualWidth, content.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth + 56, (int)content.ActualHeight + 56, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
    }
}
