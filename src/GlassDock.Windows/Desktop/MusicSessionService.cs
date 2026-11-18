using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace GlassDock.Windows.Desktop;

public enum MusicCommand { Previous, PlayPause, Next }

public sealed record MusicPlayback(bool Playing = false, bool CanPrevious = false, bool CanToggle = false,
    bool CanNext = false, byte[]? Artwork = null, string? Title = null);

// The owner calls this service on the UI thread. Windows events can arrive on any thread.
public sealed class MusicSessionService(SynchronizationContext dispatcher) : IDisposable
{
    private const ulong MaxArtworkBytes = 4 * 1024 * 1024;
    private readonly CancellationTokenSource lifetime = new();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private CancellationTokenSource? artworkRead;
    private readonly HashSet<Session> observed = [];
    private Session? selected;
    private bool disposed, refreshing, pending, artworkDirty = true, commanding;
    private int queued, metadataChanged;
    public MusicPlayback Playback { get; private set; } = new();
    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        if (disposed || manager is not null) return;
        try
        {
            var result = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(lifetime.Token);
            if (disposed) return;
            manager = result;
            manager.CurrentSessionChanged += CurrentChanged;
            manager.SessionsChanged += SessionsChanged;
            await RefreshAsync();
        }
        catch (Exception error) when (Unavailable(error)) { }
    }

    public async Task ExecuteAsync(MusicCommand command)
    {
        if (disposed || commanding || selected is not { } session) return;
        commanding = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var info = session.GetPlaybackInfo();
            var controls = info.Controls;
            var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            switch (command)
            {
                case MusicCommand.Previous when controls.IsPreviousEnabled:
                    await session.TrySkipPreviousAsync().AsTask(request.Token);
                    break;
                case MusicCommand.Next when controls.IsNextEnabled:
                    await session.TrySkipNextAsync().AsTask(request.Token);
                    break;
                case MusicCommand.PlayPause when controls.IsPlayPauseToggleEnabled:
                    await session.TryTogglePlayPauseAsync().AsTask(request.Token);
                    break;
                case MusicCommand.PlayPause when playing && controls.IsPauseEnabled:
                    await session.TryPauseAsync().AsTask(request.Token);
                    break;
                case MusicCommand.PlayPause when !playing && controls.IsPlayEnabled:
                    await session.TryPlayAsync().AsTask(request.Token);
                    break;
            }
        }
        catch (Exception error) when (Unavailable(error)) { }
        finally { commanding = false; if (!disposed) QueueRefresh(); }
    }

    private void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => QueueRefresh();
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => QueueRefresh();
    private void PlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args) => QueueRefresh();
    private void MediaChanged(Session sender, MediaPropertiesChangedEventArgs args)
    {
        Interlocked.Exchange(ref metadataChanged, 1);
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        dispatcher.Post(async _ =>
        {
            Interlocked.Exchange(ref queued, 0);
            if (Interlocked.Exchange(ref metadataChanged, 0) != 0)
            {
                artworkDirty = true;
                artworkRead?.Cancel();
            }
            await RefreshAsync();
        }, null);
    }

    private async Task RefreshAsync()
    {
        if (disposed || manager is null) return;
        pending = true;
        if (refreshing) return;
        refreshing = true;
        try
        {
            while (pending && !disposed)
            {
                pending = false;
                try { await ReadAsync(manager); }
                catch (Exception error) when (Unavailable(error)) { if (!disposed) Publish(new()); }
            }
        }
        finally { refreshing = false; }
    }

    private async Task ReadAsync(GlobalSystemMediaTransportControlsSessionManager source)
    {
        var current = source.GetCurrentSession();
        var sessions = source.GetSessions().ToHashSet();
        if (current is not null) sessions.Add(current);
        foreach (var old in observed.Where(session => !sessions.Contains(session)).ToArray())
        {
            old.PlaybackInfoChanged -= PlaybackChanged;
            observed.Remove(old);
        }

        Session? best = null;
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? bestInfo = null;
        var bestRank = 0;
        foreach (var session in sessions)
        {
            try
            {
                if (observed.Add(session)) session.PlaybackInfoChanged += PlaybackChanged;
                var info = session.GetPlaybackInfo();
                var isCurrent = Equals(session, current);
                if (info.PlaybackType != MediaPlaybackType.Music && !(info.PlaybackType is null && isCurrent)) continue;
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed) continue;
                var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var rank = (playing ? 2 : 0) + (isCurrent ? 2 : 1);
                if (rank <= bestRank) continue;
                best = session;
                bestInfo = info;
                bestRank = rank;
            }
            catch (Exception error) when (Unavailable(error)) { }
        }

        var changedSession = !Equals(best, selected);
        if (changedSession)
        {
            if (selected is not null) selected.MediaPropertiesChanged -= MediaChanged;
            selected = best;
            if (best is not null) best.MediaPropertiesChanged += MediaChanged;
            artworkDirty = true;
        }

        if (best is null || bestInfo is null) { Publish(new()); return; }
        var isPlaying = bestInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var controls = bestInfo.Controls;
        var state = new MusicPlayback(isPlaying, controls.IsPreviousEnabled,
            controls.IsPlayPauseToggleEnabled || (isPlaying ? controls.IsPauseEnabled : controls.IsPlayEnabled),
            controls.IsNextEnabled, isPlaying && !changedSession ? Playback.Artwork : null,
            isPlaying && !changedSession ? Playback.Title : null);
        Publish(state);
        if (!isPlaying) { artworkDirty = true; return; }
        if (!artworkDirty) return;

        artworkDirty = false;
        byte[]? artwork;
        string? title = null;
        using var read = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        artworkRead = read;
        try
        {
            var properties = await best.TryGetMediaPropertiesAsync().AsTask(read.Token);
            title = properties?.Title?.Trim();
            artwork = properties?.Thumbnail is { } thumbnail ? await ReadArtworkAsync(thumbnail, read.Token) : null;
        }
        catch (Exception error) when (Unavailable(error)) { artwork = null; }
        finally { artworkRead = null; }
        if (disposed) return;
        if (pending || Volatile.Read(ref queued) != 0) { artworkDirty = true; return; }
        Publish(state with { Artwork = artwork, Title = title });
    }

    private static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference thumbnail, CancellationToken cancellationToken)
    {
        using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken);
        var length = stream.Size;
        if (length is 0 or > MaxArtworkBytes) return null;
        var size = (uint)length;
        using var reader = new DataReader(stream);
        if (await reader.LoadAsync(size).AsTask(cancellationToken) != size) return null;
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private void Publish(MusicPlayback state)
    {
        if (Playback == state) return;
        Playback = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Unavailable(Exception error) =>
        error is COMException or UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException
            or OperationCanceledException or NotSupportedException;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        artworkRead?.Cancel();
        if (manager is not null)
        {
            manager.CurrentSessionChanged -= CurrentChanged;
            manager.SessionsChanged -= SessionsChanged;
            manager = null;
        }
        if (selected is not null) selected.MediaPropertiesChanged -= MediaChanged;
        foreach (var session in observed) session.PlaybackInfoChanged -= PlaybackChanged;
        observed.Clear();
        selected = null;
        Playback = new();
    }
}
