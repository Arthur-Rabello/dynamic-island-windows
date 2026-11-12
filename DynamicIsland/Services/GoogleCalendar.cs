using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace DynamicIsland.Services;

/// Read-only Google Calendar access through the OAuth "installed app" flow:
/// system browser + PKCE + a loopback redirect, no embedded web view.
///
/// The client id/secret are the user's own (a Desktop-app credential from
/// their Google Cloud project), kept in settings.json. Only the refresh token
/// is sensitive, and it is stored DPAPI-encrypted for the current Windows user.
///
/// shortcut: a Cloud project left in "Testing" makes Google expire the refresh
/// token after 7 days; publish it to "In production" (fine for personal use)
/// to stay signed in.
public sealed class GoogleCalendar
{
    const string Scope = "openid email https://www.googleapis.com/auth/calendar.readonly";

    static readonly HttpClient Http = new();
    static readonly string TokenFile = Path.Combine(SettingsStore.Folder, "google.token");

    readonly Settings _settings;
    string? _access;
    DateTime _accessExpires;

    public GoogleCalendar(Settings settings) => _settings = settings;

    public bool SignedIn => File.Exists(TokenFile);

    // ---- sign in / out --------------------------------------------------

    public async Task SignInAsync(CancellationToken ct)
    {
        if (_settings.GoogleClientId.Length == 0 || _settings.GoogleClientSecret.Length == 0)
            throw new InvalidOperationException("Preencha o ID e o segredo do cliente.");

        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        try
        {
            string redirect = $"http://127.0.0.1:{((IPEndPoint)tcp.LocalEndpoint).Port}/";
            string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = Base64Url(RandomNumberGenerator.GetBytes(16));

            string url = "https://accounts.google.com/o/oauth2/v2/auth?" + Query(
                ("client_id", _settings.GoogleClientId), ("redirect_uri", redirect),
                ("response_type", "code"), ("scope", Scope), ("state", state),
                ("code_challenge", challenge), ("code_challenge_method", "S256"),
                ("access_type", "offline"), ("prompt", "consent"));

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            string code = await WaitForCodeAsync(tcp, state, ct);

            using var json = await PostTokenAsync(ct,
                ("grant_type", "authorization_code"), ("code", code),
                ("redirect_uri", redirect), ("code_verifier", verifier));

            var root = json.RootElement;

            // Google's consent screen has one checkbox per permission, and the
            // user can finish the login with the calendar one unticked.
            if (!(root.TryGetProperty("scope", out var granted)
                  && granted.GetString()!.Contains("calendar.readonly")))
                throw new InvalidOperationException(
                    "Faltou marcar a permissão da Google Agenda na tela do Google. Entre de novo e deixe essa caixa marcada.");

            if (!root.TryGetProperty("refresh_token", out var refresh))
                throw new InvalidOperationException("O Google não devolveu um token de acesso contínuo.");

            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllBytes(TokenFile, ProtectedData.Protect(
                Encoding.UTF8.GetBytes(refresh.GetString()!), null, DataProtectionScope.CurrentUser));

            Remember(root);
            _settings.GoogleEmail = EmailFromIdToken(root);
        }
        finally { tcp.Stop(); }
    }

    public async Task SignOutAsync()
    {
        // Best effort: revoke so the grant disappears from the Google account too.
        try
        {
            if (ReadRefreshToken() is { } token)
                await Http.PostAsync("https://oauth2.googleapis.com/revoke",
                    new FormUrlEncodedContent([new("token", token)]));
        }
        catch { }

        try { File.Delete(TokenFile); } catch { }
        _access = null;
        _settings.GoogleEmail = "";
    }

    /// The browser may open a speculative connection that sends nothing, or ask
    /// for a favicon, so keep accepting until the real redirect shows up.
    static async Task<string> WaitForCodeAsync(TcpListener tcp, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await tcp.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();

            var buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer, ct);
            string[] parts = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0].Split(' ');
            if (parts.Length < 2) continue;

            var query = HttpUtility.ParseQueryString(new Uri("http://127.0.0.1" + parts[1]).Query);
            if (query["state"] is null && query["error"] is null)
            {
                await stream.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), ct);
                continue;
            }

            bool ok = query["state"] == state && query["code"] is not null;
            string page = ok
                ? "Pronto! Pode fechar esta aba e voltar para a Dynamic Island."
                : "Não foi possível entrar. Volte para a Dynamic Island e tente de novo.";
            byte[] body = Encoding.UTF8.GetBytes(
                $"<!doctype html><meta charset=utf-8><body style=\"font:16px Segoe UI;padding:3em\">{page}");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), ct);
            await stream.WriteAsync(body, ct);

            if (!ok) throw new InvalidOperationException(query["error"] ?? "Resposta inválida do Google.");
            return query["code"]!;
        }
    }

    // ---- tokens ---------------------------------------------------------

    string? ReadRefreshToken()
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    async Task<JsonDocument> PostTokenAsync(CancellationToken ct, params (string, string)[] fields)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", _settings.GoogleClientId),
            new("client_secret", _settings.GoogleClientSecret)
        };
        form.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(form), ct);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        if (!response.IsSuccessStatusCode)
        {
            string error = doc.RootElement.TryGetProperty("error_description", out var d)
                ? d.GetString()! : response.StatusCode.ToString();
            doc.Dispose();
            throw new InvalidOperationException(error);
        }

        return doc;
    }

    void Remember(JsonElement root)
    {
        _access = root.GetProperty("access_token").GetString();
        _accessExpires = DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60);
    }

    async Task<string?> AccessTokenAsync()
    {
        if (_access is not null && DateTime.UtcNow < _accessExpires) return _access;
        if (ReadRefreshToken() is not { } refresh) return null;

        try
        {
            using var json = await PostTokenAsync(CancellationToken.None,
                ("grant_type", "refresh_token"), ("refresh_token", refresh));
            Remember(json.RootElement);
            return _access;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expired or revoked")
                                                   || ex.Message.Contains("invalid_grant"))
        {
            // The grant is gone (revoked, or the 7-day Testing limit). Forget it
            // so the settings window offers to sign in again instead of failing silently.
            try { File.Delete(TokenFile); } catch { }
            _access = null;
            _settings.GoogleEmail = "";
            SettingsStore.Save(_settings);
            return null;
        }
    }

    // ---- events ---------------------------------------------------------

    public async Task<IReadOnlyList<CalendarEvent>> LoadRangeAsync(DateTime startLocal, int days)
    {
        string? token = await AccessTokenAsync();
        if (token is null) return [];

        // Plain "yyyy-MM-ddTHH:mm:ss+hh:mm": the round-trip "o" format carries 7
        // fractional digits, which is not worth betting Google's parser on.
        static string Rfc3339(DateTime local) =>
            Uri.EscapeDataString(new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local))
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz"));

        string min = Rfc3339(startLocal), max = Rfc3339(startLocal.AddDays(days));

        using var calendars = await GetAsync(token,
            "https://www.googleapis.com/calendar/v3/users/me/calendarList?fields=items(id,selected)");

        var ids = calendars.RootElement.GetProperty("items").EnumerateArray()
            .Where(c => c.TryGetProperty("selected", out var s) && s.GetBoolean())
            .Select(c => c.GetProperty("id").GetString()!)
            .ToList();

        // One calendar the account cannot read (e.g. a colleague's, shared as
        // free/busy only) must not hide all the others.
        var results = await Task.WhenAll(ids.Select(async id =>
        {
            try
            {
                using var events = await GetAsync(token,
                    $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(id)}/events" +
                    $"?singleEvents=true&maxResults=250&timeMin={min}&timeMax={max}&fields=items(summary,start)");

                var list = events.RootElement.TryGetProperty("items", out var items)
                    ? items.EnumerateArray().Select(ToEvent).ToList()
                    : [];
                return (Events: list, Error: (string?)null);
            }
            catch (InvalidOperationException ex) { return (Events: [], Error: ex.Message); }
        }));

        // Every calendar failing is a real problem worth showing; some failing is not.
        if (results.Length > 0 && results.All(r => r.Error is not null))
            throw new InvalidOperationException(results[0].Error);

        return results.SelectMany(r => r.Events).ToList();
    }

    static CalendarEvent ToEvent(JsonElement item)
    {
        string subject = item.TryGetProperty("summary", out var s) && !string.IsNullOrWhiteSpace(s.GetString())
            ? s.GetString()! : "(sem título)";

        var start = item.GetProperty("start");
        return start.TryGetProperty("dateTime", out var dt)
            ? new CalendarEvent(DateTimeOffset.Parse(dt.GetString()!).LocalDateTime, false, subject)
            : new CalendarEvent(DateTime.Parse(start.GetProperty("date").GetString()!), true, subject);
    }

    static async Task<JsonDocument> GetAsync(string token, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        using var response = await Http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // Google's own message ("API has not been used in project…", "Forbidden")
            // says what to fix; the bare status code does not.
            string message = response.StatusCode.ToString();
            try
            {
                using var error = JsonDocument.Parse(body);
                message = error.RootElement.GetProperty("error").GetProperty("message").GetString() ?? message;
            }
            catch { }

            if (message.Contains("insufficient authentication scopes"))
                message = "sem permissão para ler a agenda. Saia da conta Google nas Configurações " +
                          "e entre de novo, marcando a caixa da Google Agenda";

            throw new InvalidOperationException($"{(int)response.StatusCode}: {message}");
        }

        return JsonDocument.Parse(body);
    }

    // ---- helpers --------------------------------------------------------

    static string Query(params (string Key, string Value)[] pairs) =>
        string.Join('&', pairs.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// The id_token is only read for the address to display; it came straight
    /// from Google's token endpoint over TLS, so its signature is not re-checked.
    static string EmailFromIdToken(JsonElement root)
    {
        try
        {
            string payload = root.GetProperty("id_token").GetString()!.Split('.')[1]
                .Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.GetProperty("email").GetString() ?? "";
        }
        catch { return ""; }
    }
}
