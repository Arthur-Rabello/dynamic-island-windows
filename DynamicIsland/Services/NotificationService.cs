using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Services;

public sealed record IncomingMessage(string App, string Sender, string Preview, DateTime At);

/// Surfaces toast notifications from one chosen app.
///
/// UserNotificationListener works from an unpackaged process once the user
/// allows it, which Windows asks about on the first RequestAccessAsync and
/// then remembers under Settings, Privacy, Notifications.
///
/// It polls rather than using NotificationChanged: that event only fires
/// reliably for packaged apps, and a three-second sweep of the handful of live
/// toasts costs nothing.
public sealed class NotificationService
{
    // Windows shows its own toast immediately, so a slower sweep leaves the
    // island visibly trailing the notification it is echoing.
    static readonly TimeSpan Sweep = TimeSpan.FromMilliseconds(1200);

    readonly DispatcherTimer _timer = new() { Interval = Sweep };
    /// Id to the creation time last seen for it. A chat app keeps one toast
    /// per conversation and replaces it as messages arrive, and the
    /// replacement reuses the id, so an id alone cannot tell a repeat from a
    /// new message: the timestamp can.
    readonly Dictionary<uint, DateTimeOffset> _seen = [];

    UserNotificationListener? _listener;
    bool _primed;

    /// Comma-separated terms; each is matched case-insensitively against both
    /// the app's display name and its AppUserModelId, and any hit lets the
    /// toast through. Empty disables the whole feature.
    public string AppFilter { get; set; } = "";

    public string? LastError { get; private set; }

    /// App name and id of the most recent toast from any source, so the
    /// settings window can tell the user what to type into AppFilter.
    public string LastSeenApp { get; private set; } = "";

    public event Action<IncomingMessage>? Arrived;

    const int Keep = 20;

    /// A list meant for "what did I miss just now" goes stale fast; anything
    /// older than this is clutter rather than history.
    static readonly TimeSpan Forget = TimeSpan.FromHours(2);

    readonly List<IncomingMessage> _recent = [];

    /// Newest first. In memory only: notification text is not something to
    /// leave on disk, and the island has no use for it past this session.
    public IReadOnlyList<IncomingMessage> Recent => _recent;

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                LastError = "O Windows não liberou a leitura de notificações.";
                _listener = null;
                return;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _listener = null;
            return;
        }

        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();
        await PollAsync();
    }

    public void Stop() => _timer.Stop();

    async Task PollAsync()
    {
        if (_listener is null) return;

        var now = DateTime.Now;
        _recent.RemoveAll(m => now - m.At > Forget);

        IReadOnlyList<UserNotification> toasts;
        try { toasts = await _listener.GetNotificationsAsync(NotificationKinds.Toast); }
        catch (Exception ex) { LastError = ex.Message; return; }

        // Forget ids that are gone so a dismissed and re-sent toast counts as
        // new, and the map cannot grow without bound.
        var live = toasts.Select(t => t.Id).ToHashSet();
        foreach (uint id in _seen.Keys.Where(id => !live.Contains(id)).ToList()) _seen.Remove(id);

        foreach (var toast in toasts)
        {
            if (_seen.TryGetValue(toast.Id, out var when) && when == toast.CreationTime) continue;
            _seen[toast.Id] = toast.CreationTime;

            // The first sweep sees every toast already on screen; announcing
            // those would pop the island for messages read hours ago.
            if (!_primed) continue;

            var message = Read(toast);
            if (message is null) continue;

            _recent.Insert(0, message);
            if (_recent.Count > Keep) _recent.RemoveRange(Keep, _recent.Count - Keep);

            Arrived?.Invoke(message);
        }

        _primed = true;
    }

    IncomingMessage? Read(UserNotification toast)
    {
        try
        {
            string app = toast.AppInfo.DisplayInfo.DisplayName ?? "";
            string id = toast.AppInfo.AppUserModelId ?? "";

            LastSeenApp = app.Length > 0 ? $"{app} ({id})" : id;

            if (AppFilter.Length == 0) return null;
            if (!AppFilter
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(term => app.Contains(term, StringComparison.OrdinalIgnoreCase)
                              || id.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return null;

            var binding = toast.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) return null;

            var lines = binding.GetTextElements()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (lines.Count == 0) return null;

            return new IncomingMessage(
                App: app,
                Sender: lines[0],
                Preview: string.Join(" ", lines.Skip(1)),
                At: toast.CreationTime.LocalDateTime);
        }
        catch { return null; }
    }
}
