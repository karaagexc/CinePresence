using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CinePresence.App;

public partial class WatchPopup : Window
{
    private readonly DispatcherTimer dismissTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly CancellationTokenSource artworkLifetime = new();
    private readonly HttpClient images = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Forms.Screen targetScreen;
    private bool dismissing, closed;
    public WatchNotice Notice { get; }

    public WatchPopup(WatchNotice notice, Action<ParsedTitle> changeMatch)
    {
        Notice = notice;
        targetScreen = Forms.Screen.FromHandle(GetForegroundWindow());
        InitializeComponent();
        WatchingTitle.Text = notice.Media.Title;
        WatchingDetails.Text = notice.Description;
        DismissButton.Click += (_, _) => Dismiss();
        ChangeMatchButton.Click += (_, _) => Dismiss(() => changeMatch(notice.Input));
        dismissTimer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => dismissTimer.Stop();
        MouseLeave += (_, _) => RestartTimer();
        IsKeyboardFocusWithinChanged += (_, _) => { if (IsKeyboardFocusWithin) dismissTimer.Stop(); else RestartTimer(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            SetWindowLongPtr(handle, -20, new IntPtr(style | 0x08000000 | 0x00000080)); // NOACTIVATE | TOOLWINDOW
        };
        Loaded += (_, _) => { PlaceOnRight(); AnimateIn(); RestartTimer(); if (ShowArtwork.Source is null) _ = LoadArtworkAsync(); };
        SizeChanged += (_, _) => { if (IsLoaded) PlaceOnRight(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (!closed) PlaceOnRight(); });
        Closed += (_, _) => { closed = true; dismissTimer.Stop(); artworkLifetime.Cancel(); artworkLifetime.Dispose(); images.Dispose(); };
    }

    internal void RefreshDetails(WatchNotice current) => WatchingDetails.Text = current.Description;

    private void RestartTimer()
    {
        if (!dismissing && !closed && !IsMouseOver && !IsKeyboardFocusWithin) dismissTimer.Start();
    }

    private void AnimateIn()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        Animate(PopupSurface, OpacityProperty, 0, 1, 220);
        Animate(PopupOffset, TranslateTransform.YProperty, 14, 0, 260);
        Animate(PopupScale, ScaleTransform.ScaleXProperty, 0.97, 1, 260);
        Animate(PopupScale, ScaleTransform.ScaleYProperty, 0.97, 1, 260);
    }

    private void Dismiss(Action? afterClose = null)
    {
        if (dismissing || closed) return;
        dismissing = true; dismissTimer.Stop(); IsHitTestVisible = false;
        artworkLifetime.Cancel();
        if (afterClose is not null) Closed += (_, _) => afterClose();
        void Finish() { if (!closed) Close(); }
        if (!SystemParameters.ClientAreaAnimation) { Finish(); return; }
        var fade = new DoubleAnimation(PopupSurface.Opacity, 0, TimeSpan.FromMilliseconds(160))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Finish();
        PopupSurface.BeginAnimation(OpacityProperty, fade);
        Animate(PopupOffset, TranslateTransform.YProperty, PopupOffset.Y, 8, 160);
    }

    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to, int milliseconds) =>
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    private async Task LoadArtworkAsync()
    {
        try
        {
            if (!Uri.TryCreate(Notice.Media.PosterUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "image.tmdb.org") return;
            var bytes = await images.GetByteArrayAsync(uri, artworkLifetime.Token);
            if (closed || dismissing) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 160; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            ShowArtwork.Source = bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or NotSupportedException or ArgumentException or ObjectDisposedException)
        { /* Keep the placeholder when artwork is unavailable; never delay the notice. */ }
    }

    private void PlaceOnRight()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var area = targetScreen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var margin = (int)Math.Round(18 * dpi.DpiScaleX);
        // Physical coordinates support secondary monitors with different scaling
        // and taskbars on any edge. SWP_NOACTIVATE preserves the player's focus.
        SetWindowPos(hwnd, new IntPtr(-1), Math.Max(area.Left, area.Right - width - margin),
            Math.Max(area.Top, area.Bottom - height - margin), 0, 0, 0x0001 | 0x0010);
    }

    internal void Capture(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(PopupSurface.ActualWidth), (int)Math.Ceiling(PopupSurface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(PopupSurface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
