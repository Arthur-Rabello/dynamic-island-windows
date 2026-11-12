using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

/// Reads whatever is playing through the Windows global media transport
/// controls: Spotify, a browser tab, a local player, anything that registers
/// a session. Events arrive on thread-pool threads, so callers must marshal
/// Changed back to the UI dispatcher themselves.
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    public byte[]? ArtworkBytes { get; private set; }

    public bool HasTrack => _session is not null && (Title.Length > 0 || Artist.Length > 0);

    /// Identity of the current track plus its play state, so the UI can tell a
    /// real change from a position update it should ignore.
    public string StateKey => $"{Title}|{Artist}|{IsPlaying}";

    public event Action? Changed;

    public async Task InitAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Attach();
            Attach();
        }
        catch { }
    }

    void Attach()
    {
        try
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
            }

            _session = _manager?.GetCurrentSession();

            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackChanged;
            }
            else
            {
                Title = Artist = "";
                ArtworkBytes = null;
                IsPlaying = false;
            }

            _ = RefreshAsync();
        }
        catch { }
    }

    void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();

    void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();

    async Task RefreshAsync()
    {
        var session = _session;
        if (session is null)
        {
            Changed?.Invoke();
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            IsPlaying = playback?.PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var properties = await session.TryGetMediaPropertiesAsync();
            Title = properties?.Title ?? "";
            Artist = properties?.Artist ?? "";
            ArtworkBytes = await ReadThumbnailAsync(properties?.Thumbnail);
        }
        catch { }

        Changed?.Invoke();
    }

    static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;

        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0) return null;

            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);

            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch { return null; }
    }

    public (TimeSpan Position, TimeSpan End) Timeline()
    {
        try
        {
            var timeline = _session?.GetTimelineProperties();
            return timeline is null
                ? (TimeSpan.Zero, TimeSpan.Zero)
                : (timeline.Position, timeline.EndTime);
        }
        catch { return (TimeSpan.Zero, TimeSpan.Zero); }
    }

    public void TogglePlayPause() { try { _ = _session?.TryTogglePlayPauseAsync(); } catch { } }
    public void Next() { try { _ = _session?.TrySkipNextAsync(); } catch { } }
    public void Previous() { try { _ = _session?.TrySkipPreviousAsync(); } catch { } }
}
