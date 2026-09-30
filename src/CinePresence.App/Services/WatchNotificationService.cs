using System.Windows.Threading;

namespace CinePresence.App.Services;

internal sealed class WatchNotificationService : IDisposable
{
    private readonly AppController controller;
    private readonly Dispatcher dispatcher;
    private readonly Action<ParsedTitle> changeMatch;
    private readonly WatchNotificationGate gate = new();
    private WatchPopup? popup;
    private int queued;
    private bool disposed;

    public WatchNotificationService(AppController controller, Dispatcher dispatcher, Action<ParsedTitle> changeMatch)
    {
        this.controller = controller; this.dispatcher = dispatcher; this.changeMatch = changeMatch;
        controller.Changed += Changed;
    }

    private void Changed(object? sender, EventArgs e)
    {
        if (disposed || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref queued, 1) != 0) return;
        dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref queued, 0);
            if (disposed) return;
            var view = controller.Engine.View;
            var enabled = controller.Settings.ShowWatchingPopup;
            var current = WatchNotificationGate.Current(view);
            if (!enabled || current?.Key != popup?.Notice.Key) popup?.Close();
            else if (current is not null) popup?.RefreshDetails(current);
            var notice = gate.Next(view, enabled);
            if (notice is null) return;
            popup?.Close();
            var next = new WatchPopup(notice, changeMatch);
            popup = next;
            next.Closed += (_, _) => { if (popup == next) popup = null; };
            next.Show();
        });
    }

    public void Dispose()
    {
        disposed = true; controller.Changed -= Changed; popup?.Close(); popup = null;
    }
}
