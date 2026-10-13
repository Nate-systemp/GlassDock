using Windows.Media.Control;
using Windows.Storage.Streams;

namespace GlassDock.Windows.Desktop;

public sealed record MediaStatus(string Title, string Artist, bool Playing, TimeSpan Position, TimeSpan Duration,
    bool Previous, bool Toggle, bool Next, IRandomAccessStreamReference? Artwork);

/// <summary>Supported Windows media sessions; unavailable providers remain unavailable.</summary>
public sealed class WindowsMediaControls
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? current;
    public async Task<MediaStatus?> ReadAsync()
    {
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            current = manager.GetCurrentSession();
            if (current is null) return null;
            var properties = await current.TryGetMediaPropertiesAsync();
            var playback = current.GetPlaybackInfo();
            var timeline = current.GetTimelineProperties();
            return new(properties.Title, properties.Artist,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                timeline.Position - timeline.StartTime, timeline.EndTime - timeline.StartTime,
                playback.Controls.IsPreviousEnabled, playback.Controls.IsPlayPauseToggleEnabled,
                playback.Controls.IsNextEnabled, properties.Thumbnail);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        { current = null; return null; }
    }

    public async Task<bool> ExecuteAsync(int action)
    {
        var session = current;
        if (session is null) return false;
        try
        {
            return action switch
            {
                -1 => await session.TrySkipPreviousAsync(),
                1 => await session.TrySkipNextAsync(),
                _ => await session.TryTogglePlayPauseAsync()
            };
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
    }
}
