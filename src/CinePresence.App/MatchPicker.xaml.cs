using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CinePresence.App.Services;

namespace CinePresence.App;

public partial class MatchPicker : UserControl, IDisposable
{
    private readonly AppController controller;
    private readonly ParsedTitle input;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient images = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    private CancellationTokenSource? searching;
    private bool disposed, saving;
    public event EventHandler? Applied;

    public MatchPicker(AppController controller, ParsedTitle input)
    {
        this.controller = controller; this.input = input;
        InitializeComponent();
        Detected.Text = $"Detected: {input.Title}";
        Query.Text = input.Title; Season.Text = input.Season?.ToString() ?? ""; Episode.Text = input.Episode?.ToString() ?? "";
        Search.Click += async (_, _) => await SearchAsync();
        Query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Results.SelectionChanged += (_, _) =>
        {
            Apply.IsEnabled = !saving && Results.SelectedItem is MatchRow;
            EpisodeFields.IsEnabled = (Results.SelectedItem as MatchRow)?.Candidate.Type == MediaType.Tv;
        };
        Apply.Click += async (_, _) => await ApplyAsync();
        Loaded += async (_, _) => { Query.Focus(); Query.SelectAll(); if (Results.ItemsSource is null) await SearchAsync(); };
    }

    private async Task SearchAsync()
    {
        if (saving || disposed) return;
        searching?.Cancel(); searching?.Dispose(); searching = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var ct = searching.Token;
        Search.IsEnabled = false; Apply.IsEnabled = false; Results.ItemsSource = null; Message.Text = "Searching TMDB…";
        try
        {
            // Both types stay available so a correction can repair a bad page hint.
            var matches = await controller.Tmdb.SearchAsync(Query.Text.Trim(), null, ct);
            ct.ThrowIfCancellationRequested();
            var preferred = input.HasEpisode ? MediaType.Tv : input.TypeHint;
            var rows = matches.OrderByDescending(x => x.Type == preferred).Take(30).Select(x => new MatchRow(x)).ToList();
            Results.ItemsSource = rows;
            Message.Text = rows.Count == 0 ? "No matches. Try a shorter title or its original name." : "Choose a title. Episode fields are optional for series.";
            foreach (var group in rows.Chunk(4)) { ct.ThrowIfCancellationRequested(); await Task.WhenAll(group.Select(x => LoadPosterAsync(x, ct))); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) Message.Text = ex is ServiceException ? ex.Message : "Search could not finish. Try again."; }
        finally { if (!ct.IsCancellationRequested) Search.IsEnabled = true; }
    }

    private async Task LoadPosterAsync(MatchRow row, CancellationToken ct)
    {
        if (row.Candidate.PosterPath is not { Length: > 1 } path || !path.StartsWith('/') || path.StartsWith("//")) return;
        try
        {
            var bytes = await images.GetByteArrayAsync("https://image.tmdb.org/t/p/w92" + path, ct);
            ct.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 92; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); row.Poster = bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or NotSupportedException or ArgumentException or ObjectDisposedException) { }
    }

    private async Task ApplyAsync()
    {
        if (saving || Results.SelectedItem is not MatchRow row) return;
        int? s = null, e = null;
        if (row.Candidate.Type == MediaType.Tv && (Season.Text.Length > 0 || Episode.Text.Length > 0))
        {
            if (!int.TryParse(Season.Text, out var ns) || ns is < 0 or > 999 || !int.TryParse(Episode.Text, out var ne) || ne is < 1 or > 9999)
            { Message.Text = "Enter both season and episode numbers, or leave both empty."; return; }
            s = ns; e = ne;
        }
        saving = true; Apply.IsEnabled = Search.IsEnabled = Results.IsEnabled = false;
        try { await controller.Resolver.CorrectAsync(input, row.Candidate, s, e, lifetime.Token); Applied?.Invoke(this, EventArgs.Empty); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Message.Text = ex is ServiceException ? ex.Message : "The correction could not be saved. Try again."; }
        finally { saving = false; if (!disposed) { Search.IsEnabled = Results.IsEnabled = true; Apply.IsEnabled = Results.SelectedItem is MatchRow; } }
    }

    internal void ShowFixture(ImageSource? poster)
    {
        Results.ItemsSource = new[] { new MatchRow(new(1, MediaType.Tv, "Regular Show", "Regular Show", 2010, null)) { Poster = poster }, new MatchRow(new(2, MediaType.Movie, "Regular Show: The Movie", "", 2015, null)) { Poster = poster } };
        Results.SelectedIndex = 0; Message.Text = "Choose a title. Episode fields are optional for series.";
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        lifetime.Cancel(); searching?.Cancel(); searching?.Dispose(); lifetime.Dispose(); images.Dispose();
    }

    public sealed class MatchRow(MediaCandidate candidate) : INotifyPropertyChanged
    {
        public MediaCandidate Candidate { get; } = candidate;
        public string Detail => $"{(Candidate.Type == MediaType.Tv ? "Series" : "Movie")} · {Candidate.Year?.ToString() ?? "Year unknown"}";
        private ImageSource? poster;
        public ImageSource? Poster { get => poster; set { poster = value; PropertyChanged?.Invoke(this, new(nameof(Poster))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
