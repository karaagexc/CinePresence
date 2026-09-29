using System.Windows;
using System.Windows.Controls;
using CinePresence.App.Services;

namespace CinePresence.App;

public sealed class MatchWindow : Window
{
    private readonly AppController controller;
    private readonly ParsedTitle input;
    private readonly TextBox query = new();
    private readonly TextBox season = new() { Width = 80 };
    private readonly TextBox episode = new() { Width = 80 };
    private readonly ListBox results = new() { DisplayMemberPath = "Label" };
    private readonly TextBlock message = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button search = new() { Content = "Search TMDB", Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button apply = new() { Content = "Use this match", Margin = new Thickness(0, 16, 0, 0), IsEnabled = false };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? searching;

    public MatchWindow(AppController controller, ParsedTitle input)
    {
        this.controller = controller; this.input = input;
        Title = "Correct match · CinePresence"; Width = 640; Height = 620; MinWidth = 500; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        var root = new Grid { Margin = new Thickness(24) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = height });
        var heading = new TextBlock { Text = "Find the right movie or series", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        root.Children.Add(heading);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(search, Dock.Right); row.Children.Add(search); row.Children.Add(query); Grid.SetRow(row, 1); root.Children.Add(row);
        query.Text = input.Title; Grid.SetRow(results, 2); root.Children.Add(results);
        var episodeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        episodeRow.Children.Add(new TextBlock { Text = "Season", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); episodeRow.Children.Add(season);
        episodeRow.Children.Add(new TextBlock { Text = "Episode", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) }); episodeRow.Children.Add(episode);
        season.Text = input.Season?.ToString() ?? ""; episode.Text = input.Episode?.ToString() ?? ""; Grid.SetRow(episodeRow, 3); root.Children.Add(episodeRow);
        Grid.SetRow(message, 4); root.Children.Add(message); Grid.SetRow(apply, 5); root.Children.Add(apply); Content = root;
        message.Text = "Episode fields are optional and only apply to series. Corrections are saved for this detected title.";
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await SearchAsync(); };
        results.SelectionChanged += (_, _) => apply.IsEnabled = results.SelectedItem is MediaCandidate;
        apply.Click += async (_, _) => await ApplyAsync();
        Loaded += async (_, _) => await SearchAsync();
        Closed += (_, _) => { lifetime.Cancel(); searching?.Cancel(); searching?.Dispose(); lifetime.Dispose(); };
    }

    private async Task SearchAsync()
    {
        searching?.Cancel(); searching?.Dispose(); searching = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var ct = searching.Token;
        search.IsEnabled = false; message.Text = "Searching TMDB…";
        try
        {
            var matches = await controller.Tmdb.SearchAsync(query.Text.Trim(), null, ct);
            ct.ThrowIfCancellationRequested();
            results.ItemsSource = matches;
            message.Text = matches.Count == 0 ? "No matches. Try a shorter title or its original name." : "Select the correct title. Set season and episode if needed.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { message.Text = ex is ServiceException ? ex.Message : "Search could not finish. Try again."; }
        finally { if (!ct.IsCancellationRequested) search.IsEnabled = true; }
    }

    private async Task ApplyAsync()
    {
        if (results.SelectedItem is not MediaCandidate candidate) return;
        int? s = null, e = null;
        if (candidate.Type == MediaType.Tv && (season.Text.Length > 0 || episode.Text.Length > 0))
        {
            if (!int.TryParse(season.Text, out var ns) || ns < 0 || !int.TryParse(episode.Text, out var ne) || ne <= 0)
            { message.Text = "Enter both a season (0 or higher) and episode (1 or higher), or leave both empty."; return; }
            s = ns; e = ne;
        }
        apply.IsEnabled = false;
        try { await controller.Resolver.CorrectAsync(input, candidate, s, e, lifetime.Token); DialogResult = true; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { message.Text = ex is ServiceException ? ex.Message : "The correction could not be saved. Try again."; apply.IsEnabled = true; }
    }
}
