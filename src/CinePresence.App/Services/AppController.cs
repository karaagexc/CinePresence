namespace CinePresence.App.Services;

public sealed class AppController : IAsyncDisposable
{
    private readonly SettingsStore store;
    private readonly HttpClient tmdbHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly HttpClient vlcHttp = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly object snapshotsGate = new();
    private readonly Dictionary<IPlaybackAdapter, IReadOnlyList<PlaybackSnapshot>> snapshots = [];
    private readonly List<Task> loops = [];
    private readonly List<SemaphoreSlim> signals = [];
    private string token = "";
    private string? pinnedSession;
    private VlcOptions vlcOptions = new(false, 8080, "");
    public AppSettings Settings { get; private set; }
    public string Warning => store.Warning;
    public string TmdbToken => token;
    public string VlcPassword => vlcOptions.Password;
    public string ApplicationId => string.IsNullOrWhiteSpace(Settings.DiscordApplicationIdOverride) ? SettingsStore.ReleaseApplicationId() : Settings.DiscordApplicationIdOverride;
    public string DataDirectory => store.DirectoryPath;
    public WindowsMediaAdapter Windows { get; } = new();
    public VlcAdapter Vlc { get; }
    public DiscordPublisher Discord { get; } = new();
    public TmdbClient Tmdb { get; }
    public MediaResolver Resolver { get; }
    public MediaCache Cache { get; }
    public PresenceEngine Engine { get; }
    public string? PinnedSession => pinnedSession;
    public event EventHandler? Changed;

    public AppController(string? dataDirectory = null)
    {
        store = new(dataDirectory); Settings = store.Load();
        token = store.Unprotect(Settings.ProtectedTmdbToken);
        vlcOptions = new(Settings.VlcEnabled, Settings.VlcPort, store.Unprotect(Settings.ProtectedVlcPassword));
        Tmdb = new(tmdbHttp, () => token);
        Cache = new(Path.Combine(store.DirectoryPath, "matches.json"));
        Resolver = new(Tmdb, Cache);
        Vlc = new(vlcHttp, () => vlcOptions);
        Engine = new(Resolver, Discord);
        Engine.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Discord.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Engine.Configure(Settings.SharingEnabled, null, Settings.ExcludedSources);
    }

    public void Start()
    {
        Discord.Connect(ApplicationId);
        StartAdapter(Windows); StartAdapter(Vlc);
    }

    private void StartAdapter(IPlaybackAdapter adapter)
    {
        var signal = new SemaphoreSlim(0, 1); signals.Add(signal);
        adapter.Changed += (_, _) => { try { if (signal.CurrentCount == 0) signal.Release(); } catch (SemaphoreFullException) { } };
        loops.Add(Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                IReadOnlyList<PlaybackSnapshot> current;
                try
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(4));
                    current = await adapter.ReadAsync(readTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception) { current = []; }
                lock (snapshotsGate)
                {
                    snapshots[adapter] = current;
                    Engine.Update(snapshots.Values.SelectMany(x => x).ToList());
                }
                try { await signal.WaitAsync(TimeSpan.FromSeconds(1), lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }));
    }

    public void SaveSettings(string tmdbToken, string applicationIdOverride, bool vlcEnabled, int port, string password, bool startup)
    {
        tmdbToken = tmdbToken.Trim(); applicationIdOverride = applicationIdOverride.Trim();
        if (tmdbToken.Any(char.IsWhiteSpace)) throw new ServiceException("The TMDB token should not contain spaces or line breaks.");
        if (applicationIdOverride.Length > 0 && (!ulong.TryParse(applicationIdOverride, out var id) || id == 0))
            throw new ServiceException("Enter a numeric Discord application ID, or leave the override empty.");
        if (port is < 1 or > 65535) throw new ServiceException("VLC port must be between 1 and 65535.");
        if (vlcEnabled && string.IsNullOrEmpty(password)) throw new ServiceException("Set a VLC HTTP password before enabling its connection.");
        var settings = Settings with
        {
            ProtectedTmdbToken = SettingsStore.Protect(tmdbToken),
            DiscordApplicationIdOverride = applicationIdOverride,
            VlcEnabled = vlcEnabled, VlcPort = port, ProtectedVlcPassword = SettingsStore.Protect(password),
            StartWithWindows = startup, OnboardingComplete = tmdbToken.Length > 0
        };
        if (Settings.StartWithWindows != startup) SettingsStore.SetStartup(startup);
        store.Save(settings); Settings = settings;
        token = tmdbToken; vlcOptions = new(vlcEnabled, port, password);
        Engine.Configure(settings.SharingEnabled, pinnedSession, settings.ExcludedSources, true);
        Discord.Connect(ApplicationId);
        foreach (var signal in signals) { try { signal.Release(); } catch (SemaphoreFullException) { } }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSharing(bool enabled)
    {
        Settings = Settings with { SharingEnabled = enabled };
        Engine.Configure(enabled, pinnedSession, Settings.ExcludedSources);
        store.Save(Settings);
    }

    public void Pin(string? id)
    {
        pinnedSession = id;
        Engine.Configure(Settings.SharingEnabled, pinnedSession, Settings.ExcludedSources);
    }

    public void Exclude(string sourceId, bool exclude)
    {
        var excluded = new HashSet<string>(Settings.ExcludedSources);
        if (exclude) excluded.Add(sourceId); else excluded.Remove(sourceId);
        Settings = Settings with { ExcludedSources = excluded };
        Engine.Configure(Settings.SharingEnabled, pinnedSession, excluded);
        store.Save(Settings);
    }

    public void RefreshMatch() => Engine.Configure(Settings.SharingEnabled, pinnedSession, Settings.ExcludedSources, true);
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); Engine.Dispose();
        await Task.WhenAll(loops).ConfigureAwait(false);
        await Windows.DisposeAsync(); await Vlc.DisposeAsync();
        tmdbHttp.Dispose(); vlcHttp.Dispose(); lifetime.Dispose();
    }
}
