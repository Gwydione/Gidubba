using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace TableForge.Dice;

/// <summary>A new activation code: the <see cref="Code"/> the person types on dddice.com, and the <see cref="Secret"/> TableForge polls with (memory only).</summary>
public sealed record DddiceActivationCode(string Code, string Secret, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"dddice activation {Code} (expires {ExpiresAt:u})"; // never the secret
}

/// <summary>
/// One poll of an activation code. <see cref="Token"/> is null while the person has not approved it yet. <see cref="AccountId"/> is
/// the approved account's dddice user uuid when dddice sent one (it identifies the account, not a secret).
/// </summary>
public sealed record DddiceActivationPoll(string? Token, string? DisplayName, string? AccountId = null)
{
    public override string ToString() => Token is null ? "dddice activation: waiting" : $"dddice activation: approved ({DisplayName ?? "Connected"})"; // never the token
}

/// <summary>
/// The dddice REST calls TableForge makes itself. Base https://dddice.com/api/1.0, JSON. Rolling and the realtime connection happen
/// inside dddice-js. Guest calls: a guest user and a room. Account calls: the activation code flow, the Digital Dice Box, and
/// checking/creating the account's room. Nothing sent here contains anything from a TableForge table, and no token is ever logged.
/// External JSON is read defensively (dddice's API is alpha) and turned into small TableForge records straight away.
/// </summary>
public sealed class DddiceRest : IDisposable
{
    private readonly HttpClient _http;

    public DddiceRest(HttpMessageHandler? handler = null, string baseUrl = "https://dddice.com/api/1.0/", TimeSpan? timeout = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ---- guest ------------------------------------------------------------------------------------------------------

    /// <summary>POST /user with no body: an anonymous guest. dddice limits this to about three a minute per address.</summary>
    public async Task<string> CreateGuestTokenAsync(CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Post, "user", new JsonObject(), auth: null, DddiceCaller.Guest, ct);
        return json["data"]?.GetValue<string>() ?? throw new DddiceException("dddice did not give a guest login.");
    }

    /// <summary>POST /room. The token's user becomes the room's "Game Master". The name is generic on purpose.</summary>
    public async Task<string> CreateRoomAsync(string token, CancellationToken ct) => await CreateRoomAsync(token, DddiceCaller.Guest, ct);

    // ---- account ------------------------------------------------------------------------------------------------------

    /// <summary>POST /activate (no login): a short-lived code the person approves on dddice.com while signed in there.</summary>
    public async Task<DddiceActivationCode> CreateActivationAsync(CancellationToken ct)
    {
        var data = DataOf(await SendAsync(HttpMethod.Post, "activate", new JsonObject(), auth: null, DddiceCaller.Activation, ct)) as JsonObject;
        var code = Text(data?["code"]);
        var secret = Text(data?["secret"]);
        if (code is null || secret is null || !TryParseTime(Text(data?["expires_at"]), out var expires))
            throw new DddiceException("dddice sent an activation code Gidubba could not read.");
        return new DddiceActivationCode(code, secret, expires);
    }

    /// <summary>GET /activate/{code} with "Authorization: Secret …". A token appears once the person has approved the code.</summary>
    public async Task<DddiceActivationPoll> PollActivationAsync(DddiceActivationCode activation, CancellationToken ct)
    {
        var auth = new AuthenticationHeaderValue("Secret", activation.Secret);
        if (DataOf(await SendAsync(HttpMethod.Get, $"activate/{Uri.EscapeDataString(activation.Code)}", null, auth, DddiceCaller.Activation, ct)) is not JsonObject data)
            throw new DddiceException("dddice sent an activation answer Gidubba could not read.");
        var token = Text(data["token"]);
        return string.IsNullOrWhiteSpace(token)
            ? new DddiceActivationPoll(null, null)
            : new DddiceActivationPoll(token, DisplayNameOf(data["user"]), UuidOf(data["user"]));
    }

    /// <summary>
    /// GET /dice-box, every page (following links.next, which must stay on this API), each theme judged by
    /// <see cref="DddiceThemeCompatibility"/>. Themes without an id are skipped: they could never be chosen.
    /// </summary>
    public async Task<IReadOnlyList<DddiceTheme>> LoadDiceBoxAsync(string token, CancellationToken ct)
    {
        var themes = new List<DddiceTheme>();
        var seenPages = new HashSet<string>();
        string? path = "dice-box";
        while (path is not null)
        {
            if (!seenPages.Add(path) || seenPages.Count > 200) throw MalformedDiceBox();
            var page = await SendAsync(HttpMethod.Get, path, null, Bearer(token), DddiceCaller.Account, ct);
            if (page is not JsonObject || page["data"] is not JsonArray items) throw MalformedDiceBox();
            foreach (var item in items)
                if (DddiceThemeCompatibility.Evaluate(item) is { } theme) themes.Add(theme);
            path = NextPage((page["links"] as JsonObject)?["next"]);
        }
        return themes;
    }

    /// <summary>
    /// GET /user ("Get Authenticated User"): the uuid of the dddice account the token belongs to, which is what a remembered room is
    /// kept under. A token always belongs to the same account, so this is asked once per token. Unreadable answer: an account problem.
    /// </summary>
    public async Task<string> GetAccountIdAsync(string token, CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Get, "user", null, Bearer(token), DddiceCaller.Account, ct);
        return UuidOf(DataOf(json)) ?? throw new DddiceException(DddiceMessages.AccountUnconfirmed) { IsAccountProblem = true };
    }

    /// <summary>
    /// GET /room/{slug}: whether the room can still be used (false for 403/404: gone or no longer ours) and, when dddice says,
    /// the uuid of its user (the account that created it). Any other failure is thrown, so a hiccup never leads to a new room.
    /// </summary>
    public async Task<DddiceRoomCheck> CheckRoomAsync(string token, string slug, CancellationToken ct)
    {
        try
        {
            var json = await SendAsync(HttpMethod.Get, $"room/{Uri.EscapeDataString(slug)}", null, Bearer(token), DddiceCaller.Account, ct);
            return new DddiceRoomCheck(true, DataOf(json) is JsonObject data ? UuidOf(data["user"]) : null);
        }
        catch (DddiceHttpException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return new DddiceRoomCheck(false, null);
        }
    }

    /// <summary>POST /room for a connected account. A 402 ("Subscription Required") is reported as a possible room limit, never retried.</summary>
    public async Task<string> CreateAccountRoomAsync(string token, CancellationToken ct) => await CreateRoomAsync(token, DddiceCaller.Account, ct);

    public static DddiceException MalformedDiceBox() => new("Gidubba could not read your dddice Dice Box.");

    // ---- plumbing ------------------------------------------------------------------------------------------------------

    private async Task<string> CreateRoomAsync(string token, DddiceCaller caller, CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Post, "room", new JsonObject { ["name"] = "Gidubba", ["is_public"] = false }, Bearer(token), caller, ct);
        return Text(DataOf(json) is JsonObject data ? data["slug"] : null) ?? throw new DddiceException("dddice did not create a room.");
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, AuthenticationHeaderValue? auth, DddiceCaller caller, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = auth;
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw Describe(response.StatusCode, path, caller);
            return JsonNode.Parse(text) ?? throw new DddiceException("dddice sent an empty answer.");
        }
        catch (DddiceException) { throw; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new DddiceException("dddice did not answer in time.", inner: null) { IsTemporary = true }; }
        catch (HttpRequestException ex) { throw new DddiceException("Gidubba could not reach dddice. Check the internet connection.", inner: ex) { IsTemporary = true }; }
        catch (System.Text.Json.JsonException ex) { throw new DddiceException("dddice sent an answer Gidubba could not read.", inner: ex); }
    }

    /// <summary>Who is asking decides the wording: guest messages are exactly as they always were; account ones never mention guests.</summary>
    internal enum DddiceCaller { Guest, Account, Activation }

    internal static DddiceException Describe(HttpStatusCode status, string path) => Describe(status, path, DddiceCaller.Guest);

    internal static DddiceException Describe(HttpStatusCode status, string path, DddiceCaller caller) => (status, caller) switch
    {
        (HttpStatusCode.TooManyRequests, DddiceCaller.Guest) => new DddiceHttpException("dddice is limiting new guest sessions right now. Wait a minute and try again.", status) { IsTemporary = true },
        (HttpStatusCode.TooManyRequests, _) => new DddiceHttpException(DddiceMessages.Busy, status) { IsTemporary = true },
        (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, DddiceCaller.Guest) => new DddiceHttpException($"dddice refused the guest login ({(int)status}).", status, isAuthProblem: true),
        (HttpStatusCode.Unauthorized, DddiceCaller.Account) => new DddiceHttpException(DddiceMessages.Expired, status, isAuthProblem: true),
        (HttpStatusCode.Unauthorized, DddiceCaller.Activation) => new DddiceHttpException("dddice did not accept this activation code. You can try again.", status, isAuthProblem: true),
        (HttpStatusCode.PaymentRequired, DddiceCaller.Account) when path == "room" => new DddiceHttpException(DddiceMessages.RoomLimit, status),
        (>= HttpStatusCode.InternalServerError, not DddiceCaller.Guest) => new DddiceHttpException(DddiceMessages.Unavailable, status) { IsTemporary = true },
        _ => new DddiceHttpException($"dddice answered with an error ({(int)status}) while contacting {path.Split('/')[0]}.", status),
    };

    private string? NextPage(JsonNode? next)
    {
        var text = Text(next);
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Only ever follow a page of this same API: a link anywhere else would carry the account token off-site.
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !_http.BaseAddress!.IsBaseOf(uri)) throw MalformedDiceBox();
        return _http.BaseAddress.MakeRelativeUri(uri).ToString();
    }

    /// <summary>The "data" member of an answer, or null when the answer is not the expected object (never an exception).</summary>
    private static JsonNode? DataOf(JsonNode? answer) => answer is JsonObject o ? o["data"] : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A name worth showing for the approved account, if dddice sent one; never an id or e-mail-looking value is invented.</summary>
    private static string? DisplayNameOf(JsonNode? user)
    {
        if (user is not JsonObject) return null;
        foreach (var key in new[] { "username", "name" })
            if (Text(user?[key]) is { } s && !string.IsNullOrWhiteSpace(s)) return s.Trim();
        return null;
    }

    /// <summary>The "uuid" of a dddice user object, or null.</summary>
    private static string? UuidOf(JsonNode? user) =>
        user is JsonObject o && Text(o["uuid"]) is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static bool TryParseTime(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

    public void Dispose() => _http.Dispose();
}

/// <summary>A dddice HTTP error, keeping the status so callers can tell "room gone" from "login refused".</summary>
public sealed class DddiceHttpException(string message, HttpStatusCode status, bool isAuthProblem = false) : DddiceException(message, isAuthProblem)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>What GET /room/{slug} said: whether the room can be used, and the uuid of its user (its creator) when dddice sent one.</summary>
public sealed record DddiceRoomCheck(bool IsAccessible, string? OwnerId);

/// <summary>The account-mode messages a person sees, in one place.</summary>
public static class DddiceMessages
{
    public const string Expired = "dddice connection expired. Reconnect in Account…";
    public const string Busy = "dddice is busy; try again shortly.";
    public const string Unavailable = "dddice is temporarily unavailable. Try again shortly.";
    public const string ActivationTimedOut = "Connection timed out. You can try again.";
    public const string ChooseTheme = "Choose a dddice theme in Account…";
    public const string RoomChanged = "dddice room changed. Your OBS dice source may need to be updated.";
    public const string RoomLimit = "dddice would not create a room for Gidubba (402). Your dddice account may have reached its room limit. " +
                                    "Delete rooms you no longer use on dddice.com, then Try again.";
    public const string AccountUnconfirmed = "Gidubba could not confirm which dddice account is connected. Try again shortly, or reconnect in Account…";
    public static string ThemeMissing(string name) => $"Your theme \"{name}\" is no longer in your dddice Dice Box. Choose another in Account…";
    public static string ThemeIncompatible(string name, string reason) => $"Your theme \"{name}\" can't be used by Gidubba ({reason}). Choose another in Account…";
}
