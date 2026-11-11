using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// A scripted dddice API. Each route answers with its responses in order (the last one repeats). Every request is recorded with
/// its Authorization header, so tests can prove which token or secret was used. Nothing here touches the network.
/// </summary>
internal sealed class FakeDddiceHttp : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _routes = [];

    public List<(string Method, string Path, string? Auth)> Requests { get; } = [];

    /// <summary>Each request's JSON body (null for none), in the same order as <see cref="Requests"/>.</summary>
    public List<string?> Bodies { get; } = [];

    public FakeDddiceHttp On(string method, string path, params (int Status, string Body)[] responses)
    {
        var queue = new Queue<Func<HttpResponseMessage>>();
        foreach (var (status, body) in responses)
            queue.Enqueue(() => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        _routes[$"{method} {path}"] = queue;
        return this;
    }

    /// <summary>The route fails as if the network were down.</summary>
    public FakeDddiceHttp Offline(string method, string path)
    {
        _routes[$"{method} {path}"] = new Queue<Func<HttpResponseMessage>>([() => throw new HttpRequestException("offline")]);
        return this;
    }

    public int Count(string method, string path) => Requests.Count(r => r.Method == method && r.Path == path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery.Replace("/api/1.0/", "");
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        lock (Requests) { Requests.Add((request.Method.Method, path, request.Headers.Authorization?.ToString())); Bodies.Add(body); }
        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var queue))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        var respond = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        return Task.FromResult(respond());
    }
}

internal sealed class MemoryAccountStore(DddiceAccountLoad? initial = null) : IDddiceAccountStore
{
    public DddiceAccountLoad Current { get; private set; } = initial ?? new DddiceAccountLoad(DddiceAccountFileState.None);
    public int Saves { get; private set; }
    public bool Deleted { get; private set; }
    public DddiceAccount? Saved => Current.Account;

    public static MemoryAccountStore With(DddiceAccount account) => new(new DddiceAccountLoad(DddiceAccountFileState.Loaded, account));

    public DddiceAccountLoad Load() => Current;
    public void Save(DddiceAccount account) { Saves++; Current = new DddiceAccountLoad(DddiceAccountFileState.Loaded, account); }
    public void Delete() { Deleted = true; Current = new DddiceAccountLoad(DddiceAccountFileState.None); }
}

/// <summary>Realistic Dice Box JSON, modelled on themes seen in dddice's public theme list.</summary>
internal static class DiceBoxJson
{
    public const string Standard = """["d4","d6","d8","d10","d10x","d12","d20"]""";

    public const string BeesValues = """
        {"d4":[1,2,3,4],"d6":[1,2,3,4,5,6],"d8":[1,2,3,4,5,6,7,8],"d10":[1,2,3,4,5,6,7,8,9,10],
         "d12":[1,2,3,4,5,6,7,8,9,10,11,12],"d20":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20],
         "d10x":[10,20,30,40,50,60,70,80,90,0]}
        """;

    public static string Theme(string id, string name, string dice = Standard, string? values = null) =>
        "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"version\":\"1.0.0\",\"available_dice\":" + dice
        + (values is null ? "" : ",\"values\":" + values) + "}";

    public static string Page(string? next, params string[] themes) =>
        "{\"type\":\"theme[]\",\"data\":[" + string.Join(",", themes) + "],\"links\":{\"first\":null,\"next\":"
        + (next is null ? "null" : "\"" + next + "\"") + "}}";

    public static string Bees => Theme("dddice-bees", "Bees", Standard, BeesValues);
    public static string Blue => Theme("my-blue", "My Blue Dice");
    public static string FromTheDeep => Theme("dddice-from-the-deep", "From the Deep",
        """[{"id":"d3","type":"d6"},{"id":"d4","type":"d4"},{"id":"d6","type":"d6"},{"id":"d8","type":"d8"},{"id":"d10","type":"d10"},{"id":"d10x","type":"d10x"}]""",
        """{"d3":[1,1,2,2,3,3]}""");
    public static string Letter => Theme("dddice-letter", "Letter", """["d4","d6","d8","d10","d12","d20"]""",
        """{"d4":["A","B","C","D"],"d6":["A","B","C","D","E","F"],"d8":["A","B","C","D","E","F","G","H"],"d10":["A","B","C","D","E","F","G","H","I","J"],"d12":["A","B","C","D","E","F","G","H","I","J","K","L"],"d20":["A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","R","S","T","W"]}""");

    public static JsonNode Parse(string json) => JsonNode.Parse(json)!;
}

/// <summary>The locked compatibility rule: exact ids, standard numeric faces, anything odd is incompatible and never a crash.</summary>
public class DddiceThemeCompatibilityTests
{
    private static DddiceTheme Judge(string json) => Assert.IsType<DddiceTheme>(DddiceThemeCompatibility.Evaluate(DiceBoxJson.Parse(json)));

    [Fact]
    public void A_standard_theme_with_no_explicit_faces_is_compatible()
    {
        var theme = Judge(DiceBoxJson.Blue);
        Assert.Equal(("my-blue", "My Blue Dice", true, ""), (theme.Id, theme.Name, theme.IsCompatible, theme.Reason));
    }

    [Fact]
    public void Bees_with_its_real_explicit_faces_is_compatible() => Assert.True(Judge(DiceBoxJson.Bees).IsCompatible);

    [Fact]
    public void Object_entries_are_read_by_id_and_missing_dice_are_named()
    {
        var theme = Judge(DiceBoxJson.FromTheDeep);
        Assert.False(theme.IsCompatible);
        Assert.Equal("Missing d12, d20", theme.Reason);                           // its custom d3 is ignored
    }

    [Fact]
    public void A_theme_without_the_percentile_die_says_so()
    {
        var theme = Judge(DiceBoxJson.Theme("t", "No Tens", """["d4","d6","d8","d10","d12","d20"]"""));
        Assert.Equal("No d10x (percentile)", theme.Reason);
    }

    [Fact]
    public void Letter_faces_are_non_standard()
    {
        Assert.Equal("No d10x (percentile); Non-standard d4, d6, d8, d10, d12, d20 faces", Judge(DiceBoxJson.Letter).Reason);
    }

    [Fact]
    public void Music_note_faces_are_non_standard()
    {
        var theme = Judge(DiceBoxJson.Theme("dddice-music", "Music", DiceBoxJson.Standard, """{"d6":["D3","G3","A3","C4","D4","G4"]}"""));
        Assert.Equal("Non-standard d6 faces", theme.Reason);
    }

    [Fact]
    public void Doubled_faces_on_a_different_mesh_are_non_standard_even_with_a_standard_id()
    {
        var half = DiceBoxJson.Theme("dddice-half", "Half",
            """[{"id":"d2","type":"d4"},{"id":"d3","type":"d6"},{"id":"d4","type":"d8"},{"id":"d5","type":"d10"},{"id":"d6","type":"d12"},{"id":"d10","type":"d20"}]""",
            """{"d4":[1,1,2,2,3,3,4,4],"d6":[1,1,2,2,3,3,4,4,5,5,6,6],"d10":[1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10]}""");
        Assert.Equal("Missing d8, d12, d20; No d10x (percentile); Non-standard d4, d6, d10 faces", Judge(half).Reason);
    }

    [Fact]
    public void Fudge_dice_match_nothing_TableForge_rolls()
    {
        var fudge = DiceBoxJson.Theme("dddice-fudge", "Fudge",
            """[{"id":"r","type":"d6","notation":"dF"},{"id":"o","type":"d6","notation":"dF"}]""", """{"r":[-1,1,0,0,1,-1]}""");
        Assert.Equal("Missing d4, d6, d8, d10, d12, d20; No d10x (percentile)", Judge(fudge).Reason);
    }

    [Fact]
    public void Only_the_exact_id_counts_never_the_mesh_type_or_notation()
    {
        var loose = DiceBoxJson.Theme("t", "Loose",
            """["d4",{"id":"six","type":"d6","notation":"d6"},"d8","d10","d10x","d12","d20"]""");
        Assert.Equal("Missing d6", Judge(loose).Reason);
    }

    [Fact]
    public void Extra_custom_dice_are_ignored()
    {
        var theme = DiceBoxJson.Theme("t", "Plus", """["d2","d3","d4","d6","d8","d10","d10x","d12","d20","dF"]""", """{"d3":[1,1,2,2,3,3],"d2":["x","y"]}""");
        Assert.True(Judge(theme).IsCompatible);
    }

    [Theory]
    [InlineData("""{"d6":null}""", "Non-standard d6 faces")]
    [InlineData("""{"d6":[1,2,3,4,5]}""", "Non-standard d6 faces")]
    [InlineData("""{"d6":["1","2","3","4","5","6"]}""", "Non-standard d6 faces")]
    [InlineData("""{"d6":[1,2,3,4,5,{"src":"six.svg"}]}""", "Non-standard d6 faces")]
    [InlineData("""{"d6":[1,2,3,4,5,6.5]}""", "Non-standard d6 faces")]
    [InlineData("""{"d6":"123456"}""", "Non-standard d6 faces")]
    [InlineData("""{"d10x":[0,10,20,30,40,50,60,70,80,90]}""", "Non-standard d10x faces")]
    [InlineData("""{"d10x":[1,2,3,4,5,6,7,8,9,10]}""", "Non-standard d10x faces")]
    [InlineData("\"not an object\"", "Dice faces could not be read")]
    public void Malformed_faces_are_incompatible_with_a_reason(string values, string reason)
    {
        var theme = Judge(DiceBoxJson.Theme("t", "Odd", DiceBoxJson.Standard, values));
        Assert.False(theme.IsCompatible);
        Assert.Equal(reason, theme.Reason);
    }

    [Fact]
    public void Standard_faces_written_as_decimals_are_still_standard()
    {
        Assert.True(Judge(DiceBoxJson.Theme("t", "Decimal", DiceBoxJson.Standard, """{"d4":[1.0,2.0,3.0,4.0]}""")).IsCompatible);
    }

    [Fact]
    public void Unexpected_shapes_never_crash()
    {
        Assert.Null(DddiceThemeCompatibility.Evaluate(DiceBoxJson.Parse("[1,2]")));
        Assert.Null(DddiceThemeCompatibility.Evaluate(DiceBoxJson.Parse("""{"name":"No id"}""")));
        Assert.Null(DddiceThemeCompatibility.Evaluate(null));
        Assert.Equal("Dice list could not be read", Judge("""{"id":"t","available_dice":"d6"}""").Reason);
        Assert.Equal("Dice list could not be read", Judge("""{"id":"t"}""").Reason);
        Assert.Equal("Missing d4", Judge("""{"id":"t","name":"Nulls","available_dice":[null,7,[],"d6","d8","d10","d10x","d12","d20"]}""").Reason);
        Assert.Equal("t", Judge("""{"id":"t","available_dice":[]}""").Name);    // no name: the id stands in
    }
}

/// <summary>The account calls in <see cref="DddiceRest"/>, against a scripted API.</summary>
public class DddiceRestAccountTests
{
    private const string Pending = """{"data":{"code":"1234AB","expires_at":"2026-09-26 16:04:24.000000Z","secret":"s3cret","user":null}}""";

    [Fact]
    public async Task Creating_an_activation_reads_code_secret_and_expiry_without_any_login()
    {
        var http = new FakeDddiceHttp().On("POST", "activate", (201, """{"data":{"code":"1234AB","expires_at":"2026-09-26 16:04:24.000000Z","secret":"s3cret"}}"""));
        using var rest = new DddiceRest(http);

        var activation = await rest.CreateActivationAsync(default);

        Assert.Equal(("1234AB", "s3cret"), (activation.Code, activation.Secret));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 16, 4, 24, TimeSpan.Zero), activation.ExpiresAt);
        Assert.Null(Assert.Single(http.Requests).Auth);
    }

    [Fact]
    public async Task A_pending_poll_has_no_token_and_uses_exactly_the_secret_header()
    {
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB", (200, Pending));
        using var rest = new DddiceRest(http);

        var poll = await rest.PollActivationAsync(new("1234AB", "s3cret", DateTimeOffset.UtcNow.AddMinutes(5)), default);

        Assert.Null(poll.Token);
        Assert.Equal(("GET", "activate/1234AB", "Secret s3cret"), Assert.Single(http.Requests));
    }

    [Theory]
    [InlineData("""{"username":"Allen","uuid":"u-1"}""", "Allen")]
    [InlineData("""{"name":"Allen D."}""", "Allen D.")]
    [InlineData("""{"uuid":"u-1"}""", null)]
    [InlineData("null", null)]
    public async Task An_approved_poll_has_the_token_and_a_display_name_when_one_was_sent(string user, string? name)
    {
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB", (200, """{"data":{"code":"1234AB","token":"acct-token","user":""" + user + "}}"));
        using var rest = new DddiceRest(http);

        var poll = await rest.PollActivationAsync(new("1234AB", "s3cret", DateTimeOffset.UtcNow), default);

        Assert.Equal(("acct-token", name), (poll.Token, poll.DisplayName));
    }

    [Theory]
    [InlineData(401, true, false, "dddice did not accept this activation code. You can try again.")]
    [InlineData(429, false, true, DddiceMessages.Busy)]
    [InlineData(503, false, true, DddiceMessages.Unavailable)]
    public async Task Poll_errors_are_described_for_a_person(int status, bool auth, bool temporary, string message)
    {
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB", (status, "{}"));
        using var rest = new DddiceRest(http);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.PollActivationAsync(new("1234AB", "s", DateTimeOffset.UtcNow), default));
        Assert.Equal((message, auth, temporary), (ex.Message, ex.IsAuthProblem, ex.IsTemporary));
    }

    [Theory]
    [InlineData("""{"data":"nope"}""")]
    [InlineData("not json at all")]
    public async Task A_malformed_activation_answer_is_an_error_not_a_crash(string body)
    {
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB", (200, body)).On("POST", "activate", (201, body));
        using var rest = new DddiceRest(http);

        await Assert.ThrowsAnyAsync<DddiceException>(() => rest.PollActivationAsync(new("1234AB", "s", DateTimeOffset.UtcNow), default));
        await Assert.ThrowsAnyAsync<DddiceException>(() => rest.CreateActivationAsync(default));
    }

    [Fact]
    public async Task The_whole_Dice_Box_is_loaded_by_following_every_page_with_the_account_token()
    {
        var http = new FakeDddiceHttp()
            .On("GET", "dice-box", (200, DiceBoxJson.Page("https://dddice.com/api/1.0/dice-box?page=2", DiceBoxJson.Bees, DiceBoxJson.Letter)))
            .On("GET", "dice-box?page=2", (200, DiceBoxJson.Page("https://dddice.com/api/1.0/dice-box?page=3", DiceBoxJson.Blue)))
            .On("GET", "dice-box?page=3", (200, DiceBoxJson.Page(null, DiceBoxJson.FromTheDeep, """{"name":"no id"}""")));
        using var rest = new DddiceRest(http);

        var themes = await rest.LoadDiceBoxAsync("acct-token", default);

        Assert.Equal(["dddice-bees", "dddice-letter", "my-blue", "dddice-from-the-deep"], themes.Select(t => t.Id).ToArray());
        Assert.Equal([true, false, true, false], themes.Select(t => t.IsCompatible).ToArray());
        Assert.All(http.Requests, r => Assert.Equal("Bearer acct-token", r.Auth));
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task A_next_page_link_off_the_dddice_API_is_never_followed()
    {
        var http = new FakeDddiceHttp().On("GET", "dice-box", (200, DiceBoxJson.Page("https://evil.example/steal", DiceBoxJson.Bees)));
        using var rest = new DddiceRest(http);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.LoadDiceBoxAsync("acct-token", default));
        Assert.Equal("Gidubba could not read your dddice Dice Box.", ex.Message);
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("""{"data":{"not":"a list"}}""")]
    [InlineData("<html>maintenance</html>")]
    public async Task A_malformed_Dice_Box_is_an_error_not_a_crash(string body)
    {
        var http = new FakeDddiceHttp().On("GET", "dice-box", (200, body));
        using var rest = new DddiceRest(http);
        await Assert.ThrowsAnyAsync<DddiceException>(() => rest.LoadDiceBoxAsync("acct-token", default));
    }

    [Theory]
    [InlineData(401, DddiceMessages.Expired, true, false)]
    [InlineData(429, DddiceMessages.Busy, false, true)]
    [InlineData(500, DddiceMessages.Unavailable, false, true)]
    public async Task Dice_Box_errors_are_described_for_a_connected_account(int status, string message, bool auth, bool temporary)
    {
        var http = new FakeDddiceHttp().On("GET", "dice-box", (status, "{}"));
        using var rest = new DddiceRest(http);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.LoadDiceBoxAsync("acct-token", default));
        Assert.Equal((message, auth, temporary), (ex.Message, ex.IsAuthProblem, ex.IsTemporary));
    }

    [Fact]
    public async Task No_network_is_temporary()
    {
        var http = new FakeDddiceHttp().Offline("GET", "dice-box");
        using var rest = new DddiceRest(http);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.LoadDiceBoxAsync("acct-token", default));
        Assert.True(ex.IsTemporary);
        Assert.Equal("Gidubba could not reach dddice. Check the internet connection.", ex.Message);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(404, false)]
    [InlineData(403, false)]
    public async Task A_saved_room_is_usable_unless_dddice_says_it_is_gone_or_not_ours(int status, bool usable)
    {
        var http = new FakeDddiceHttp().On("GET", "room/room-1", (status, """{"data":{"slug":"room-1"}}"""));
        using var rest = new DddiceRest(http);

        Assert.Equal(usable, (await rest.CheckRoomAsync("acct-token", "room-1", default)).IsAccessible);
        Assert.Equal("Bearer acct-token", http.Requests[0].Auth);
    }

    [Fact]
    public async Task Guest_messages_are_exactly_as_before()
    {
        var http = new FakeDddiceHttp().On("POST", "user", (429, "{}")).On("POST", "room", (401, "{}"));
        using var rest = new DddiceRest(http);

        var busy = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.CreateGuestTokenAsync(default));
        Assert.Equal("dddice is limiting new guest sessions right now. Wait a minute and try again.", busy.Message);
        var refused = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.CreateRoomAsync("guest-token", default));
        Assert.Equal(("dddice refused the guest login (401).", true), (refused.Message, refused.IsAuthProblem));
        http.On("POST", "room", (500, "{}"));
        var failed = await Assert.ThrowsAnyAsync<DddiceException>(() => rest.CreateRoomAsync("guest-token", default));
        Assert.Equal(("dddice answered with an error (500) while contacting room.", false), (failed.Message, failed.IsTemporary));
    }
}

/// <summary>Waiting for approval: every 5 seconds, cancellable, never longer than the code lives or 5 minutes. Runs instantly.</summary>
public class DddiceActivationPollerTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Delays.Add(span);
            Now += span;
            return Task.CompletedTask;
        }
    }

    private static DddiceActivationCode Code(Clock clock, TimeSpan life) => new("1234AB", "s3cret", clock.Now + life);

    [Fact]
    public async Task It_polls_at_once_then_every_five_seconds_until_approved()
    {
        var clock = new Clock();
        var polls = 0;
        var poller = new DddiceActivationPoller((_, _) =>
            Task.FromResult(++polls < 3 ? new DddiceActivationPoll(null, null) : new DddiceActivationPoll("acct-token", "Allen")), () => clock.Now, clock.Delay);

        var result = await poller.WaitForApprovalAsync(Code(clock, TimeSpan.FromMinutes(5)), default);

        Assert.Equal(("acct-token", "Allen"), (result.Token, result.DisplayName));
        Assert.Equal(3, polls);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], clock.Delays);
    }

    [Theory]
    [InlineData(300, 60)]   // the code lives 5 minutes: polls at 0, 5, … 295 s
    [InlineData(30, 6)]     // a code that expires sooner stops sooner
    [InlineData(1200, 60)]  // a longer-lived code is still only waited on for 5 minutes
    public async Task It_gives_up_when_the_code_or_five_minutes_runs_out(int lifeSeconds, int expectedPolls)
    {
        var clock = new Clock();
        var polls = 0;
        var poller = new DddiceActivationPoller((_, _) => { polls++; return Task.FromResult(new DddiceActivationPoll(null, null)); }, () => clock.Now, clock.Delay);

        var ex = await Assert.ThrowsAsync<DddiceActivationTimeoutException>(() => poller.WaitForApprovalAsync(Code(clock, TimeSpan.FromSeconds(lifeSeconds)), default));

        Assert.Equal("Connection timed out. You can try again.", ex.Message);
        Assert.Equal(expectedPolls, polls);
        Assert.All(clock.Delays, d => Assert.Equal(TimeSpan.FromSeconds(5), d));
    }

    [Fact]
    public async Task Cancelling_stops_the_wait()
    {
        var clock = new Clock();
        using var cancel = new CancellationTokenSource();
        var polls = 0;
        var poller = new DddiceActivationPoller((_, _) =>
        {
            if (++polls == 2) cancel.Cancel();
            return Task.FromResult(new DddiceActivationPoll(null, null));
        }, () => clock.Now, clock.Delay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poller.WaitForApprovalAsync(Code(clock, TimeSpan.FromMinutes(5)), cancel.Token));
        Assert.Equal(2, polls);
    }

    [Fact]
    public async Task Busy_or_offline_moments_just_mean_polling_again()
    {
        var clock = new Clock();
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB",
            (429, "{}"), (503, "{}"), (200, """{"data":{"code":"1234AB","token":"acct-token","user":{"username":"Allen"}}}"""));
        using var rest = new DddiceRest(http);
        var poller = new DddiceActivationPoller(rest.PollActivationAsync, () => clock.Now, clock.Delay);

        var result = await poller.WaitForApprovalAsync(Code(clock, TimeSpan.FromMinutes(5)), default);

        Assert.Equal("acct-token", result.Token);
        Assert.Equal(3, http.Count("GET", "activate/1234AB"));
        Assert.All(http.Requests, r => Assert.Equal("Secret s3cret", r.Auth));
    }

    [Fact]
    public async Task A_refused_code_stops_at_once()
    {
        var clock = new Clock();
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB", (401, "{}"));
        using var rest = new DddiceRest(http);
        var poller = new DddiceActivationPoller(rest.PollActivationAsync, () => clock.Now, clock.Delay);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => poller.WaitForApprovalAsync(Code(clock, TimeSpan.FromMinutes(5)), default));
        Assert.True(ex.IsAuthProblem);
        Assert.Single(http.Requests);
    }
}

/// <summary>dddice-account.json: DPAPI-protected token, readable metadata, atomic writes, and no crash on a bad file.</summary>
public class DddiceAccountStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tableforge-account-{Guid.NewGuid():N}");
    private const string Token = "tf-live-token-7f3a9c";

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }

    private DddiceAccountStore Store() => new(_folder);

    [Fact]
    public void An_account_round_trips_with_its_theme_room_and_name()
    {
        var account = new DddiceAccount(Token, "Allen", "room-1", "my-blue", "My Blue Dice");
        Store().Save(account);

        var loaded = Store().Load();
        Assert.Equal(DddiceAccountFileState.Loaded, loaded.State);
        Assert.Equal(account, loaded.Account);
        Assert.Equal(Path.Combine(_folder, "dddice-account.json"), Store().FilePath);
        Assert.False(File.Exists(Store().FilePath + ".tmp"));                    // written through a temp file that replaced it
    }

    [Fact]
    public void The_file_never_holds_the_token_in_plain_text()
    {
        Store().Save(new DddiceAccount(Token, null, null, null, null));

        var text = File.ReadAllText(Store().FilePath);
        Assert.DoesNotContain(Token, text);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Token)), text);
        var json = JsonNode.Parse(text)!;
        Assert.Equal(1, (int)json["format"]!);
        Assert.False(string.IsNullOrEmpty((string?)json["protectedToken"]));
        Assert.DoesNotContain(Token, new DddiceAccount(Token, "Allen", null, null, null).ToString());
        Assert.DoesNotContain(Token, new DddiceSession(Token, "room-1", "my-blue", true).ToString());
        Assert.DoesNotContain(Token, new DddiceActivationPoll(Token, "Allen").ToString());
        Assert.DoesNotContain("s3cret-value", new DddiceActivationCode("1234AB", "s3cret-value", DateTimeOffset.UtcNow).ToString());
    }

    [Fact]
    public void No_file_means_no_account()
    {
        Assert.Equal(DddiceAccountFileState.None, Store().Load().State);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("""{"format":2,"protectedToken":"AAAA"}""")]
    [InlineData("""{"format":1}""")]
    [InlineData("""{"format":"one","protectedToken":"AAAA"}""")]
    [InlineData("""{"format":1,"protectedToken":"%%% not base64 %%%"}""")]
    public void A_corrupt_file_is_unreadable_not_a_crash(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Store().FilePath, content);
        Assert.Equal(DddiceAccountFileState.Unreadable, Store().Load().State);
    }

    [Fact]
    public void A_token_that_cannot_be_decrypted_is_unreadable()
    {
        Store().Save(new DddiceAccount(Token, "Allen", "room-1", "my-blue", "My Blue Dice"));
        var json = JsonNode.Parse(File.ReadAllText(Store().FilePath))!;
        var bytes = Convert.FromBase64String((string)json["protectedToken"]!);
        bytes[^5] ^= 0xFF;                                                          // as if protected by someone else, or damaged
        json["protectedToken"] = Convert.ToBase64String(bytes);
        File.WriteAllText(Store().FilePath, json.ToJsonString());

        Assert.Equal(DddiceAccountFileState.Unreadable, Store().Load().State);
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        Store().Save(new DddiceAccount(Token, null, null, null, null));
        Store().Delete();
        Assert.False(File.Exists(Store().FilePath));
        Assert.Equal(DddiceAccountFileState.None, Store().Load().State);
        Store().Delete();                                                           // deleting nothing is fine
    }

    [Fact]
    public void An_account_saved_by_TableForge_1_1_0_still_loads_after_the_rename_to_Gidubba()
    {
        // Exactly what 1.1.0 wrote: the token protected with the fixed entropy, which the rename must never change.
        var entropy = Encoding.UTF8.GetBytes("TableForge dddice account token v1");
        var protectedToken = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(Token), entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Store().FilePath, new JsonObject
        {
            ["format"] = 1, ["protectedToken"] = Convert.ToBase64String(protectedToken),
            ["displayName"] = "Allen", ["roomSlug"] = "room-1", ["themeId"] = "my-blue", ["themeName"] = "My Blue Dice",
        }.ToJsonString());

        var loaded = Store().Load();
        Assert.Equal(DddiceAccountFileState.Loaded, loaded.State);
        Assert.Equal(new DddiceAccount(Token, "Allen", "room-1", "my-blue", "My Blue Dice"), loaded.Account);
    }

    [Fact]
    public void It_lives_in_the_same_data_folder_as_everything_else_so_TABLEFORGE_DATA_DIR_isolates_it()
    {
        var folder = AppDatabase.ResolveDataFolder(_folder);
        Assert.Equal(Path.Combine(Path.GetFullPath(_folder), "dddice-account.json"), new DddiceAccountStore(folder).FilePath);
    }
}

/// <summary>Guest and account sessions: the guest flow is unchanged; an account uses its token, theme and room, and never becomes a guest.</summary>
public class DddiceSessionTests
{
    private static readonly DddiceAccount Saved = new("acct-token", "Allen", "room-1", "my-blue", "My Blue Dice", "acct-1");

    private static FakeDddiceHttp AccountApi(string? roomStatus = null) => new FakeDddiceHttp()
        .On("GET", "dice-box", (200, DiceBoxJson.Page(null, DiceBoxJson.Bees, DiceBoxJson.Blue, DiceBoxJson.Letter)))
        .On("GET", "room/room-1", (roomStatus is null ? 200 : int.Parse(roomStatus), "{}"))
        .On("POST", "room", (201, """{"data":{"slug":"room-2"}}"""));

    [Fact]
    public async Task The_guest_flow_creates_one_guest_and_room_and_uses_Bees()
    {
        var http = new FakeDddiceHttp().On("POST", "user", (201, """{"data":"guest-token"}""")).On("POST", "room", (201, """{"data":{"slug":"guest-room"}}"""));
        using var rest = new DddiceRest(http);
        var source = new DddiceSessionSource(rest, new DddiceConnection(new MemoryAccountStore()));

        var first = await source.GetAsync(default);
        var again = await source.GetAsync(default);

        Assert.Equal(new DddiceSession("guest-token", "guest-room", "dddice-bees", false), first);
        Assert.Equal(first, again);
        Assert.Equal((1, 1), (http.Count("POST", "user"), http.Count("POST", "room")));
    }

    [Fact]
    public async Task A_new_room_is_a_private_room_named_Gidubba()
    {
        var http = new FakeDddiceHttp().On("POST", "room", (201, """{"data":{"slug":"guest-room"}}"""));
        using var rest = new DddiceRest(http);

        Assert.Equal("guest-room", await rest.CreateRoomAsync("guest-token", default));
        var body = JsonNode.Parse(http.Bodies[http.Requests.FindIndex(r => r.Method == "POST" && r.Path == "room")]!)!;
        Assert.Equal("Gidubba", (string?)body["name"]);
        Assert.False((bool)body["is_public"]!);
    }

    [Fact]
    public async Task A_refused_guest_is_replaced_by_a_new_one_as_before()
    {
        var http = new FakeDddiceHttp().On("POST", "user", (201, """{"data":"guest-token"}""")).On("POST", "room", (401, "{}"), (201, """{"data":{"slug":"guest-room"}}"""));
        using var rest = new DddiceRest(http);
        var source = new DddiceSessionSource(rest);

        await Assert.ThrowsAnyAsync<DddiceException>(() => source.GetAsync(default));
        var session = await source.GetAsync(default);

        Assert.Equal("guest-room", session.Room);
        Assert.Equal(2, http.Count("POST", "user"));

        source.AuthFailed(session);                                                 // the page refused it: a new guest next time
        await source.GetAsync(default);
        Assert.Equal(3, http.Count("POST", "user"));
    }

    [Fact]
    public async Task A_connected_account_uses_its_saved_token_room_and_theme()
    {
        var http = AccountApi();
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved);
        var source = new DddiceSessionSource(rest, new DddiceConnection(store));

        var session = await source.GetAsync(default);

        Assert.Equal(new DddiceSession("acct-token", "room-1", "my-blue", true), session);
        Assert.Equal(0, http.Count("POST", "user"));                               // no guest at all
        Assert.Equal(0, http.Count("POST", "room"));                               // the saved room was reused
        Assert.All(http.Requests, r => Assert.Equal("Bearer acct-token", r.Auth));
        Assert.Equal(0, store.Saves);
    }

    [Theory]
    [InlineData("404")]
    [InlineData("403")]
    public async Task A_room_that_is_gone_is_replaced_and_the_new_one_remembered(string status)
    {
        var http = AccountApi(status);
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved);
        var source = new DddiceSessionSource(rest, new DddiceConnection(store));

        var session = await source.GetAsync(default);

        Assert.Equal("room-2", session.Room);
        Assert.Equal("room-2", store.Saved!.RoomSlug);
        Assert.Equal(1, http.Count("POST", "room"));
    }

    [Fact]
    public async Task An_account_without_a_room_gets_one_once_and_reuses_it_next_launch()
    {
        var store = MemoryAccountStore.With(Saved with { RoomSlug = null });
        var http = AccountApi().On("GET", "room/room-2", (200, "{}"));
        using var rest = new DddiceRest(http);

        await new DddiceSessionSource(rest, new DddiceConnection(store)).GetAsync(default);
        var next = await new DddiceSessionSource(rest, new DddiceConnection(store)).GetAsync(default); // TableForge started again

        Assert.Equal("room-2", next.Room);
        Assert.Equal(1, http.Count("POST", "room"));
    }

    [Fact]
    public async Task A_refused_account_is_marked_expired_kept_and_never_turned_into_a_guest()
    {
        var http = new FakeDddiceHttp().On("GET", "dice-box", (401, "{}")).On("POST", "user", (201, """{"data":"guest-token"}"""));
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved);
        var connection = new DddiceConnection(store);
        var source = new DddiceSessionSource(rest, connection);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => source.GetAsync(default));

        Assert.Equal(DddiceMessages.Expired, ex.Message);
        Assert.True(ex.IsAccountProblem);
        Assert.Equal(DddiceConnectionState.Expired, connection.State);
        Assert.False(store.Deleted);
        Assert.Equal(Saved, store.Saved);

        await Assert.ThrowsAnyAsync<DddiceException>(() => source.GetAsync(default)); // still expired: no network, no guest
        Assert.Equal(0, http.Count("POST", "user"));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task No_network_keeps_the_account_as_it_is()
    {
        var http = new FakeDddiceHttp().Offline("GET", "dice-box");
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved);
        var connection = new DddiceConnection(store);

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => new DddiceSessionSource(rest, connection).GetAsync(default));

        Assert.True(ex.IsTemporary);
        Assert.Equal(DddiceConnectionState.Connected, connection.State);
        Assert.Equal(Saved, store.Saved);
    }

    [Fact]
    public async Task A_theme_that_left_the_Dice_Box_is_reported_and_never_replaced()
    {
        var http = AccountApi();
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved with { ThemeId = "gone", ThemeName = "Old Favourite" });

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => new DddiceSessionSource(rest, new DddiceConnection(store)).GetAsync(default));

        Assert.Equal("Your theme \"Old Favourite\" is no longer in your dddice Dice Box. Choose another in Account…", ex.Message);
        Assert.Equal("gone", store.Saved!.ThemeId);
    }

    [Fact]
    public async Task A_theme_that_is_no_longer_compatible_says_why()
    {
        var http = AccountApi();
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved with { ThemeId = "dddice-letter", ThemeName = "Letter" });

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => new DddiceSessionSource(rest, new DddiceConnection(store)).GetAsync(default));

        Assert.Equal("Your theme \"Letter\" can't be used by Gidubba (No d10x (percentile); Non-standard d4, d6, d8, d10, d12, d20 faces). Choose another in Account…", ex.Message);
    }

    [Fact]
    public async Task An_account_with_no_theme_chosen_asks_for_one()
    {
        using var rest = new DddiceRest(AccountApi());
        var store = MemoryAccountStore.With(Saved with { ThemeId = null, ThemeName = null });

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => new DddiceSessionSource(rest, new DddiceConnection(store)).GetAsync(default));
        Assert.Equal(DddiceMessages.ChooseTheme, ex.Message);
    }

    [Fact]
    public async Task An_unreadable_account_file_needs_a_reconnect_and_contacts_nobody()
    {
        var http = new FakeDddiceHttp();
        using var rest = new DddiceRest(http);
        var connection = new DddiceConnection(new MemoryAccountStore(new DddiceAccountLoad(DddiceAccountFileState.Unreadable)));

        Assert.Equal(DddiceConnectionState.Expired, connection.State);
        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => new DddiceSessionSource(rest, connection).GetAsync(default));
        Assert.Equal(DddiceMessages.Expired, ex.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Disconnect_forgets_everything_locally_and_dddice_is_a_guest_again()
    {
        var http = AccountApi().On("POST", "user", (201, """{"data":"guest-token"}""")).On("POST", "room", (201, """{"data":{"slug":"guest-room"}}"""));
        using var rest = new DddiceRest(http);
        var store = MemoryAccountStore.With(Saved);
        var connection = new DddiceConnection(store);
        var changes = new List<DddiceChange>();
        connection.Changed += (_, c) => changes.Add(c);

        connection.Disconnect();
        var session = await new DddiceSessionSource(rest, connection).GetAsync(default);

        Assert.True(store.Deleted);
        Assert.Null(connection.Account);
        Assert.Equal((DddiceConnectionState.Guest, "dddice-bees", false), (connection.State, session.Theme, session.IsAccount));
        Assert.Equal([DddiceChange.Identity], changes);
        Assert.DoesNotContain(http.Requests, r => r.Path.StartsWith("revoke") || r.Method == "DELETE"); // nothing server-side
    }

    [Fact]
    public void Reconnecting_keeps_the_chosen_theme_and_room_and_only_compatible_themes_can_be_chosen()
    {
        var store = MemoryAccountStore.With(Saved);
        var connection = new DddiceConnection(store);
        connection.MarkExpired();

        connection.Connect("new-token", null, "acct-1");

        Assert.Equal(new DddiceAccount("new-token", null, "room-1", "my-blue", "My Blue Dice", "acct-1"), store.Saved);
        Assert.Equal((DddiceConnectionState.Connected, "Connected"), (connection.State, connection.DisplayName));

        connection.SelectTheme(new DddiceTheme("dddice-letter", "Letter", false, "Missing d12"));
        Assert.Equal("my-blue", store.Saved!.ThemeId);
        connection.SelectTheme(new DddiceTheme("dddice-bees", "Bees", true, ""));
        Assert.Equal(("dddice-bees", "Bees"), (store.Saved!.ThemeId, store.Saved.ThemeName));
    }

    [Fact]
    public void A_page_refusal_with_an_account_session_marks_it_expired()
    {
        var connection = new DddiceConnection(MemoryAccountStore.With(Saved));
        using var rest = new DddiceRest(new FakeDddiceHttp());
        new DddiceSessionSource(rest, connection).AuthFailed(new DddiceSession("acct-token", "room-1", "my-blue", true));
        Assert.Equal(DddiceConnectionState.Expired, connection.State);
    }
}

/// <summary>The dice choice with an account: what the sidebar says, and when the dice page is started again.</summary>
public class DiceProviderAccountTests
{
    private static readonly DddiceAccount Saved = new("acct-token", "Allen", "room-1", "my-blue", "My Blue Dice");

    [Fact]
    public void The_sidebar_says_who_dddice_rolls_as()
    {
        var guest = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(new FakeDddiceRoller()), connection: new DddiceConnection(new MemoryAccountStore()));
        Assert.Equal(("dddice: Guest", false, true), (guest.DddiceIdentityText, guest.HasThemeText, guest.ShowDddiceIdentity));

        var connected = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(new FakeDddiceRoller()), connection: new DddiceConnection(MemoryAccountStore.With(Saved)));
        Assert.Equal(("dddice: Allen", "Theme: My Blue Dice"), (connected.DddiceIdentityText, connected.ThemeText));

        var unnamed = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(new FakeDddiceRoller()), connection: new DddiceConnection(MemoryAccountStore.With(Saved with { DisplayName = null })));
        Assert.Equal("dddice: Connected", unnamed.DddiceIdentityText);

        var noAccounts = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(new FakeDddiceRoller()));
        Assert.False(noAccounts.ShowDddiceIdentity);
    }

    [Fact]
    public async Task A_new_identity_restarts_dddice_only_when_it_is_the_chosen_dice()
    {
        var roller = new FakeDddiceRoller();
        var connection = new DddiceConnection(new MemoryAccountStore());
        var vm = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(roller), connection: connection);

        connection.Connect("acct-token", "Allen");                                  // Built-in chosen: forget the page, start nothing
        Assert.Equal((1, 0), (roller.ResetCalls, roller.PrepareCalls));
        Assert.Equal("dddice: Allen", vm.DddiceIdentityText);

        vm.Select(DiceProviderKind.Dddice);
        await Wait.Until(() => vm.State == DiceProviderState.Ready, "dddice ready");
        Assert.Equal(1, roller.PrepareCalls);

        connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        await Wait.Until(() => roller.PrepareCalls == 2 && vm.State == DiceProviderState.Ready, "prepared again with the new theme");
        Assert.Equal(2, roller.ResetCalls);
        Assert.Equal("dddice is ready (theme: My Blue Dice).", vm.Message);

        connection.MarkExpired();                                                    // only shown; no restart loop
        Assert.Equal((2, 2), (roller.ResetCalls, roller.PrepareCalls));
        Assert.Equal("dddice: Connection expired", vm.DddiceIdentityText);

        connection.Disconnect();
        await Wait.Until(() => roller.PrepareCalls == 3 && vm.State == DiceProviderState.Ready, "prepared again as a guest");
        Assert.Equal(("dddice: Guest", ""), (vm.DddiceIdentityText, vm.ThemeText));
    }

    [Fact]
    public async Task An_account_failure_offers_Account_and_Built_in_and_never_guest_dice()
    {
        var roller = new FakeDddiceRoller { PrepareFailure = new DddiceException(DddiceMessages.Expired, isAuthProblem: true) { IsAccountProblem = true } };
        var opened = 0;
        var vm = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(roller),
            connection: new DddiceConnection(MemoryAccountStore.With(Saved)), openAccount: () => opened++);

        vm.Select(DiceProviderKind.Dddice);
        await Wait.Until(() => vm.State == DiceProviderState.Failed, "failed");

        Assert.Equal(DddiceMessages.Expired, vm.Message);
        Assert.True(vm.ShowAccountOnFailure);
        vm.AccountCommand.Execute(null);
        Assert.Equal(1, opened);

        vm.UseBuiltInCommand.Execute(null);                                          // Built-in always works
        Assert.Equal(4, await vm.RollAsync(DiceExpression.Parse("d6"), default));
    }

    [Fact]
    public async Task A_guest_failure_looks_exactly_as_before()
    {
        var roller = new FakeDddiceRoller { PrepareFailure = new DddiceException("Gidubba could not reach dddice. Check the internet connection.") };
        var vm = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(roller), connection: new DddiceConnection(new MemoryAccountStore()));

        vm.Select(DiceProviderKind.Dddice);
        await Wait.Until(() => vm.State == DiceProviderState.Failed, "failed");

        Assert.True(vm.ShowFailure);
        Assert.False(vm.ShowAccountOnFailure);
    }
}

/// <summary>The Account… dialog's behaviour, with a scripted dddice and an instant clock.</summary>
public class DddiceAccountViewModelTests
{
    private const string Activation = """{"data":{"code":"1234AB","expires_at":"2099-01-01 00:00:00.000000Z","secret":"s3cret"}}""";
    private const string Pending = """{"data":{"code":"1234AB","expires_at":"2099-01-01 00:00:00.000000Z","secret":"s3cret","user":null}}""";
    private const string Approved = """{"data":{"code":"1234AB","token":"acct-token","user":{"username":"Allen"}}}""";

    private sealed class Rig
    {
        public FakeDddiceHttp Http { get; } = new();
        public MemoryAccountStore Store { get; init; } = new();
        public List<string> Copied { get; } = [];
        public List<string> Opened { get; } = [];
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Gated { get; init; }
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public DddiceConnection Connection { get; private set; } = null!;
        public DddiceAccountViewModel Vm { get; private set; } = null!;

        public Rig Build()
        {
            Connection = new DddiceConnection(Store);
            Vm = new DddiceAccountViewModel(Connection, new DddiceRest(Http), Copied.Add, Opened.Add, () => Now, (span, ct) =>
            {
                Now += span;
                return Gated ? Gate.Task.WaitAsync(ct) : Task.CompletedTask;
            });
            return this;
        }
    }

    private static Rig Connectable(bool gated = true)
    {
        var rig = new Rig { Gated = gated };
        rig.Http.On("POST", "activate", (201, Activation))
            .On("GET", "activate/1234AB", (200, Pending), (200, Approved))
            .On("GET", "dice-box", (200, DiceBoxJson.Page(null, DiceBoxJson.Bees, DiceBoxJson.Blue, DiceBoxJson.Letter)));
        return rig.Build();
    }

    [Fact]
    public async Task Connecting_shows_the_code_waits_for_approval_then_saves_and_lists_the_Dice_Box()
    {
        var rig = Connectable();
        var vm = rig.Vm;
        Assert.Equal((DddiceAccountState.Disconnected, "Connect", true), (vm.State, vm.ConnectLabel, vm.ShowConnect));

        var connecting = vm.ConnectAsync();
        await Wait.Until(() => vm.State == DddiceAccountState.WaitingForApproval, "waiting");

        Assert.Equal("1234AB", vm.ActivationCode);
        Assert.True(vm.ShowActivation);
        Assert.Equal(0, rig.Store.Saves);                                           // nothing is saved while waiting
        Assert.Empty(rig.Opened);                                                    // the browser opens only when asked
        vm.CopyCodeCommand.Execute(null);
        vm.OpenDddiceCommand.Execute(null);
        Assert.Equal(["1234AB"], rig.Copied);                                       // only the code, never a secret or token
        Assert.Equal(["https://dddice.com/activate"], rig.Opened);

        rig.Gate.SetResult();
        await connecting;

        Assert.Equal(DddiceAccountState.Connected, vm.State);
        Assert.Equal("", vm.ActivationCode);
        Assert.Equal(new DddiceAccount("acct-token", "Allen", null, null, null), rig.Store.Saved);
        Assert.Equal("Connected: Allen", vm.AccountText);
        Assert.Equal(["Bees", "My Blue Dice", "Letter"], vm.Themes.Select(t => t.Name).ToArray());
        Assert.Equal([true, true, false], vm.Themes.Select(t => t.IsEnabled).ToArray());
        Assert.Equal("No d10x (percentile); Non-standard d4, d6, d8, d10, d12, d20 faces", vm.Themes[2].Reason);
        Assert.Equal("Choose a theme for Gidubba's dddice rolls.", vm.Message);
        Assert.Contains(rig.Http.Requests, r => r.Path == "dice-box" && r.Auth == "Bearer acct-token");
    }

    [Fact]
    public async Task Choosing_a_theme_saves_it_and_a_disabled_theme_cannot_be_chosen()
    {
        var rig = Connectable(gated: false);
        await rig.Vm.ConnectAsync();

        rig.Vm.SelectedTheme = rig.Vm.Themes[2];                                    // Letter: disabled
        Assert.Null(rig.Vm.SelectedTheme);
        Assert.Null(rig.Store.Saved!.ThemeId);

        rig.Vm.SelectedTheme = rig.Vm.Themes[1];
        Assert.Equal(("my-blue", "My Blue Dice"), (rig.Store.Saved!.ThemeId, rig.Store.Saved.ThemeName));
        Assert.Equal("Gidubba's dddice rolls now use My Blue Dice.", rig.Vm.Message);
    }

    [Fact]
    public async Task Cancel_and_closing_stop_the_wait_and_keep_nothing()
    {
        var rig = Connectable();
        rig.Http.On("GET", "activate/1234AB", (200, Pending));                    // nobody approves in this test
        var connecting = rig.Vm.ConnectAsync();
        await Wait.Until(() => rig.Vm.State == DddiceAccountState.WaitingForApproval, "waiting");

        rig.Vm.CancelCommand.Execute(null);
        await connecting;

        Assert.Equal((DddiceAccountState.Disconnected, "", 0), (rig.Vm.State, rig.Vm.ActivationCode, rig.Store.Saves));
        var polls = rig.Http.Count("GET", "activate/1234AB");

        var again = rig.Vm.ConnectAsync();
        await Wait.Until(() => rig.Vm.State == DddiceAccountState.WaitingForApproval, "waiting again");
        rig.Vm.Close();                                                              // the dialog closed
        await again;
        Assert.Equal((DddiceAccountState.Disconnected, 0), (rig.Vm.State, rig.Store.Saves));
        Assert.Equal(polls + 1, rig.Http.Count("GET", "activate/1234AB"));         // one poll, then nothing more
    }

    [Fact]
    public async Task A_code_nobody_approves_times_out()
    {
        var rig = new Rig();
        rig.Http.On("POST", "activate", (201, Activation)).On("GET", "activate/1234AB", (200, Pending));
        rig.Build();

        await rig.Vm.ConnectAsync();

        Assert.Equal((DddiceAccountState.Disconnected, "Connection timed out. You can try again.", 0), (rig.Vm.State, rig.Vm.Message, rig.Store.Saves));
        Assert.Equal(60, rig.Http.Count("GET", "activate/1234AB"));                // 5 minutes of 5-second polls
    }

    [Fact]
    public async Task A_busy_dddice_when_starting_says_so()
    {
        var rig = new Rig();
        rig.Http.On("POST", "activate", (429, "{}"));
        rig.Build();

        await rig.Vm.ConnectAsync();
        Assert.Equal((DddiceAccountState.Disconnected, DddiceMessages.Busy), (rig.Vm.State, rig.Vm.Message));
    }

    private static Rig ConnectedRig(DddiceAccount account, params (int, string)[] diceBox)
    {
        var rig = new Rig { Store = MemoryAccountStore.With(account) };
        rig.Http.On("GET", "dice-box", diceBox);
        return rig.Build();
    }

    private static readonly DddiceAccount Saved = new("acct-token", "Allen", "room-1", "my-blue", "My Blue Dice");

    [Fact]
    public async Task Opening_while_connected_loads_the_Dice_Box_once_and_shows_the_saved_theme()
    {
        var rig = ConnectedRig(Saved, (200, DiceBoxJson.Page(null, DiceBoxJson.Bees, DiceBoxJson.Blue)));
        await rig.Vm.OpenedAsync();

        Assert.Equal(("my-blue", ""), (rig.Vm.SelectedTheme!.Theme.Id, rig.Vm.Message));
        Assert.Equal(0, rig.Store.Saves);                                           // showing the saved choice does not re-save it
        Assert.Equal(1, rig.Http.Count("GET", "dice-box"));
        Assert.True(rig.Vm.ShowDisconnect);
    }

    [Fact]
    public async Task A_saved_theme_that_is_gone_or_incompatible_is_explained_not_replaced()
    {
        var gone = ConnectedRig(Saved, (200, DiceBoxJson.Page(null, DiceBoxJson.Bees)));
        await gone.Vm.OpenedAsync();
        Assert.Equal("Your theme \"My Blue Dice\" is no longer in your dddice Dice Box. Choose another in Account…", gone.Vm.Message);
        Assert.Null(gone.Vm.SelectedTheme);
        Assert.Equal("my-blue", gone.Store.Saved!.ThemeId);

        var broken = ConnectedRig(Saved, (200, DiceBoxJson.Page(null, DiceBoxJson.Theme("my-blue", "My Blue Dice", """["d4","d6","d8","d10","d10x","d12"]"""))));
        await broken.Vm.OpenedAsync();
        Assert.Equal("Your theme \"My Blue Dice\" can't be used by Gidubba (Missing d20). Choose another in Account…", broken.Vm.Message);
    }

    [Fact]
    public async Task Refresh_reloads_the_Dice_Box_and_its_failures_keep_the_account()
    {
        var rig = ConnectedRig(Saved,
            (200, DiceBoxJson.Page(null, DiceBoxJson.Blue)),
            (200, DiceBoxJson.Page(null, DiceBoxJson.Blue, DiceBoxJson.Bees)),
            (429, "{}"),
            (200, "{\"data\":7}"),
            (401, "{}"));
        await rig.Vm.OpenedAsync();

        await rig.Vm.RefreshAsync();
        Assert.Equal(2, rig.Vm.Themes.Count);

        await rig.Vm.RefreshAsync();
        Assert.Equal((DddiceAccountState.Connected, DddiceMessages.Busy, 2), (rig.Vm.State, rig.Vm.Message, rig.Vm.Themes.Count));

        await rig.Vm.RefreshAsync();
        Assert.Equal("Gidubba could not read your dddice Dice Box.", rig.Vm.Message);

        await rig.Vm.RefreshAsync();
        Assert.Equal((DddiceAccountState.Expired, "Connection expired. Reconnect.", "Reconnect"), (rig.Vm.State, rig.Vm.Message, rig.Vm.ConnectLabel));
        Assert.Equal(DddiceConnectionState.Expired, rig.Connection.State);
        Assert.Equal(Saved, rig.Store.Saved);                                        // kept until reconnect or disconnect
        Assert.False(rig.Store.Deleted);
    }

    [Fact]
    public async Task Disconnect_forgets_the_account_locally()
    {
        var rig = ConnectedRig(Saved, (200, DiceBoxJson.Page(null, DiceBoxJson.Blue)));
        await rig.Vm.OpenedAsync();

        rig.Vm.DisconnectCommand.Execute(null);

        Assert.True(rig.Store.Deleted);
        Assert.Equal((DddiceAccountState.Disconnected, DddiceConnectionState.Guest, 0), (rig.Vm.State, rig.Connection.State, rig.Vm.Themes.Count));
        Assert.Equal("Disconnected. dddice rolls as a guest again.", rig.Vm.Message);
        Assert.False(rig.Vm.ShowDisconnect);
    }

    [Fact]
    public void An_unreadable_account_file_opens_ready_to_reconnect()
    {
        var rig = new Rig { Store = new MemoryAccountStore(new DddiceAccountLoad(DddiceAccountFileState.Unreadable)) }.Build();
        Assert.Equal((DddiceAccountState.Expired, "Reconnect", "Connection expired. Reconnect."), (rig.Vm.State, rig.Vm.ConnectLabel, rig.Vm.Message));
    }
}

/// <summary>The account surface through real WPF: the sidebar's two lines and Account… button, and the Account dialog.</summary>
[Collection("UI")]
public class DddiceAccountViewTests
{
    private static readonly DddiceAccount Saved = new("acct-token", "Allen", "room-1", "my-blue", "My Blue Dice");

    private static void Pump(Window window, Func<bool>? until = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            window.Dispatcher.Invoke(DispatcherPriority.ContextIdle, () => { });
            window.UpdateLayout();
            if (until is null || until()) return;
            Thread.Sleep(5);
        } while (DateTime.UtcNow < deadline);
        throw new TimeoutException("the window never reached the expected state");
    }

    private static T Show<T>(T window) where T : Window
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false; window.ShowActivated = false;
        window.Show();
        Pump(window);
        return window;
    }

    [Fact]
    public void The_sidebar_shows_Guest_then_the_account_then_Guest_again_and_Account_stays_on_screen()
    {
        Sta.Run(() =>
        {
            using var temp = new TempDatabase();
            using var db = temp.Open();
            var c = db.CreateCollection("Campaign");
            var folders = new[] { "Character Creation", "Combat", "Exploration", "Loot", "Travel", "Weather" }.Select(n => db.CreateFolder(c.Id, n)).ToList();
            for (var i = 0; i < 12; i++)
            {
                var table = db.SaveTable(Fixtures.Table(DiceExpression.Parse("d20"), (1, 20, "Anything"))
                    .Also(t => { t.Name = $"Table {i + 1}"; t.CollectionId = c.Id; t.FolderId = folders[i % folders.Count].Id; }));
                if (i < 5) db.MarkTableUsed(table.Id);
            }

            var store = new MemoryAccountStore();
            var connection = new DddiceConnection(store);
            var opened = 0;
            var providers = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(new FakeDddiceRoller()),
                connection: connection, openAccount: () => opened++);
            var window = Show(new MainWindow { DataContext = new MainViewModel(db, providers) });   // default size
            try
            {
                var pane = (ScrollViewer)window.FindName("LeftScroll");
                void AssertOnScreen(string name)
                {
                    var element = (FrameworkElement)window.FindName(name);
                    Assert.True(element.IsVisible, $"{name} should be visible");
                    var top = element.TranslatePoint(new Point(0, 0), pane).Y;
                    Assert.True(top >= 0 && top + element.ActualHeight <= pane.ViewportHeight, $"{name} is below the fold");
                }
                TextBlock Text(string name) => (TextBlock)window.FindName(name);

                Assert.Equal("dddice: Guest", Text("DddiceIdentityText").Text);
                Assert.False(Text("DddiceThemeText").IsVisible);
                AssertOnScreen("DddiceAccountButton");
                AssertOnScreen("BuiltInRadio");
                AssertOnScreen("DddiceRadio");

                var account = (Button)window.FindName("DddiceAccountButton");
                ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(account)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                Pump(window);
                Assert.Equal(1, opened);

                connection.Connect("acct-token", "Allen");
                connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
                Pump(window);
                Assert.Equal("dddice: Allen", Text("DddiceIdentityText").Text);
                Assert.Equal("Theme: My Blue Dice", Text("DddiceThemeText").Text);
                AssertOnScreen("DddiceAccountButton");
                AssertOnScreen("DddiceRadio");

                connection.Disconnect();
                Pump(window);
                Assert.Equal("dddice: Guest", Text("DddiceIdentityText").Text);
                Assert.False(Text("DddiceThemeText").IsVisible);
                Assert.True(((RadioButton)window.FindName("BuiltInRadio")).IsChecked);  // Built-in stayed the dice throughout
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void The_dialog_shows_the_code_then_the_themes_with_incompatible_ones_disabled_and_has_no_password_field()
    {
        Sta.Run(() =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var http = new FakeDddiceHttp()
                .On("POST", "activate", (201, """{"data":{"code":"1234AB","expires_at":"2099-01-01 00:00:00.000000Z","secret":"s3cret"}}"""))
                .On("GET", "activate/1234AB", (200, """{"data":{"user":null}}"""), (200, """{"data":{"token":"acct-token","user":{"username":"Allen"}}}"""))
                .On("GET", "dice-box", (200, DiceBoxJson.Page(null, DiceBoxJson.Bees, DiceBoxJson.Letter)));
            var store = new MemoryAccountStore();
            var connection = new DddiceConnection(store);
            var vm = new DddiceAccountViewModel(connection, new DddiceRest(http), _ => { }, _ => { }, null, (_, ct) => gate.Task.WaitAsync(ct));
            var window = Show(new DddiceAccountWindow { DataContext = vm });
            // As when a button is really clicked: the dialog's work continues on the UI thread.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                Assert.True(((Button)window.FindName("ConnectButton")).IsVisible);
                Assert.Empty(ViewTests.FindAll<PasswordBox>(window));

                vm.ConnectCommand.Execute(null);
                Pump(window, () => ((TextBlock)window.FindName("ActivationCodeText")).IsVisible);
                Assert.Equal("1234AB", ((TextBlock)window.FindName("ActivationCodeText")).Text);
                Assert.True(((Button)window.FindName("OpenDddiceButton")).IsVisible);
                Assert.True(((Button)window.FindName("CancelActivationButton")).IsVisible);
                Assert.Contains(ViewTests.FindAll<TextBlock>(window), t => t.IsVisible && t.Text == "Waiting for approval…");

                gate.SetResult();
                Pump(window, () => vm.State == DddiceAccountState.Connected && vm.Themes.Count == 2);
                Pump(window);

                var list = (ListBox)window.FindName("ThemeList");
                Assert.True(list.IsVisible);
                var bees = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var letter = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.True(bees.IsEnabled);
                Assert.False(letter.IsEnabled);
                Assert.Contains(ViewTests.FindAll<TextBlock>(window), t => t.IsVisible && t.Text == "No d10x (percentile); Non-standard d4, d6, d8, d10, d12, d20 faces");
                Assert.False(((TextBlock)window.FindName("ActivationCodeText")).IsVisible);
                Assert.True(((Button)window.FindName("DisconnectButton")).IsVisible);
            }
            finally { window.Close(); }

            Assert.Equal(DddiceAccountState.Connected, vm.State);                     // closing after success keeps the connection
            Assert.Equal("acct-token", store.Saved!.Token);
        });
    }
}
