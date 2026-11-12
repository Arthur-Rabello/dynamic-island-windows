using Windows.ApplicationModel.Appointments;

namespace DynamicIsland.Services;

public sealed record CalendarEvent(DateTime Start, bool AllDay, string Subject);

/// Reads every calendar registered with Windows, read-only.
///
/// This works from an unpackaged process: RequestStoreAsync with
/// AllCalendarsReadOnly returns a usable store without package identity.
/// What it returns is whatever the Windows Calendar / Outlook app has synced,
/// so an account that was never added there simply yields nothing.
///
/// Recurring events arrive already expanded into individual occurrences, which
/// is the main reason to prefer this over parsing ICS by hand.
public sealed class CalendarService
{
    AppointmentStore? _store;
    bool _attempted;

    public bool Available => _store is not null;

    /// Null while the store has never been opened, so the UI can tell
    /// "not loaded yet" apart from "loaded, and there is nothing there".
    public string? LastError { get; private set; }

    async Task<AppointmentStore?> GetStoreAsync()
    {
        if (_store is not null) return _store;
        if (_attempted && _store is null && LastError is not null) return null;

        _attempted = true;
        try
        {
            _store = await AppointmentManager.RequestStoreAsync(
                AppointmentStoreAccessType.AllCalendarsReadOnly);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _store = null;
        }

        return _store;
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadRangeAsync(DateTime startLocal, int days)
    {
        var store = await GetStoreAsync();
        if (store is null) return [];

        try
        {
            var options = new FindAppointmentsOptions();
            options.FetchProperties.Add(AppointmentProperties.Subject);
            options.FetchProperties.Add(AppointmentProperties.StartTime);
            options.FetchProperties.Add(AppointmentProperties.Duration);
            options.FetchProperties.Add(AppointmentProperties.AllDay);

            var start = new DateTimeOffset(DateTime.SpecifyKind(startLocal, DateTimeKind.Local));
            var found = await store.FindAppointmentsAsync(start, TimeSpan.FromDays(days), options);

            return found
                .Select(a => new CalendarEvent(
                    a.StartTime.LocalDateTime,
                    a.AllDay,
                    string.IsNullOrWhiteSpace(a.Subject) ? "(sem título)" : a.Subject))
                .OrderBy(e => e.Start)
                .ToList();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return [];
        }
    }
}
