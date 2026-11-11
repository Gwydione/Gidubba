using System.IO;
using System.Text.Json.Nodes;
using TableForge.Dice;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// 1.5.1: an account's dddice room is reused across Disconnect → Connect and restarts, only ever for the account it belongs to, and
/// a room that dddice will not create (402) is reported as a possible room limit and never retried by itself. No network: every
/// dddice answer is scripted, and no file outside a per-test temp folder is touched.
/// </summary>
public class DddiceRoomReuseTests : IDisposable
{
    private const string AccountA = "uuid-a";
    private const string AccountB = "uuid-b";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gidubba-room-reuse-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static string User(string id) => "{\"data\":{\"uuid\":\"" + id + "\",\"username\":\"someone\"}}";
    private static string Room(string slug, string? owner) =>
        "{\"data\":{\"slug\":\"" + slug + "\"" + (owner is null ? "" : ",\"user\":{\"uuid\":\"" + owner + "\",\"username\":\"someone\"}") + "}}";

    /// <summary>One dddice account's API: its Dice Box, its uuid, and the rooms it can see (room-a is A's, room-b is B's).</summary>
    private static FakeDddiceHttp Api(string me, string newRoom = "room-new") => new FakeDddiceHttp()
        .On("GET", "dice-box", (200, DiceBoxJson.Page(null, DiceBoxJson.Bees, DiceBoxJson.Blue)))
        .On("GET", "user", (200, User(me)))
        .On("GET", "room/room-a", me == AccountA ? (200, Room("room-a", AccountA)) : (403, "{}"))
        .On("GET", "room/room-b", me == AccountB ? (200, Room("room-b", AccountB)) : (403, "{}"))
        .On("GET", "room/" + newRoom, (200, Room(newRoom, me)))
        .On("POST", "room", (201, "{\"data\":{\"slug\":\"" + newRoom + "\"}}"));

    private static async Task<DddiceSession> Prepare(DddiceConnection connection, FakeDddiceHttp http)
    {
        using var rest = new DddiceRest(http);
        return await connection.PrepareSessionAsync(rest, default);
    }

    private static DddiceConnection Connected(IDddiceRoomMemory rooms, string token, string? accountId, string? room = null) =>
        new(MemoryAccountStore.With(new DddiceAccount(token, "Allen", room, "my-blue", "My Blue Dice", accountId)), rooms);

    // ---- reconnecting ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Disconnect_then_Connect_with_the_same_account_reuses_its_room()
    {
        var rooms = new InMemoryDddiceRoomMemory();
        var connection = Connected(rooms, "token-1", AccountA, "room-a");
        await Prepare(connection, Api(AccountA));

        connection.Disconnect();
        connection.Connect("token-2", "Allen", AccountA);                     // a new activation of the same dddice account
        connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        var http = Api(AccountA);
        var session = await Prepare(connection, http);

        Assert.Equal("room-a", session.Room);
        Assert.Equal(0, http.Count("POST", "room"));
        Assert.Equal(0, http.Count("GET", "user"));                           // the activation already said who this is
        Assert.False(connection.RoomChanged);
    }

    [Fact]
    public async Task When_the_activation_does_not_say_who_it_is_the_account_is_asked_once_and_its_room_reused()
    {
        var rooms = new InMemoryDddiceRoomMemory();
        rooms.Remember(AccountA, "room-a");
        var store = new MemoryAccountStore();
        var connection = new DddiceConnection(store, rooms);
        connection.Connect("token-2", "Allen");
        connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));

        var http = Api(AccountA);
        Assert.Equal("room-a", (await Prepare(connection, http)).Room);
        await Prepare(connection, http);

        Assert.Equal(0, http.Count("POST", "room"));
        Assert.Equal(1, http.Count("GET", "user"));                           // once per token, then kept with the account
        Assert.Equal(AccountA, store.Saved!.AccountId);
        Assert.All(http.Requests, r => Assert.Equal("Bearer token-2", r.Auth));
    }

    [Fact]
    public async Task Reconnecting_an_expired_account_keeps_its_room()
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-1", AccountA, "room-a");
        connection.MarkExpired();
        connection.Connect("token-2", "Allen", AccountA);

        var http = Api(AccountA);
        Assert.Equal("room-a", (await Prepare(connection, http)).Room);
        Assert.Equal(0, http.Count("POST", "room"));
    }

    // ---- restarting --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_room_survives_Disconnect_and_a_restart_but_the_credentials_do_not()
    {
        var accounts = new DddiceAccountStore(_folder);
        var rooms = new DddiceRoomMemoryStore(_folder);
        var first = new DddiceConnection(accounts, rooms);
        first.Connect("secret-token-1", "Allen", AccountA);
        first.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        Assert.Equal("room-new", (await Prepare(first, Api(AccountA))).Room);

        first.Disconnect();
        Assert.False(File.Exists(accounts.FilePath));                         // the token is gone
        var roomFile = File.ReadAllText(rooms.FilePath);
        Assert.DoesNotContain("secret-token", roomFile);
        Assert.Equal("room-new", (string?)JsonNode.Parse(roomFile)!["rooms"]![AccountA]);

        // Gidubba restarts, then the same account is connected again.
        var second = new DddiceConnection(new DddiceAccountStore(_folder), new DddiceRoomMemoryStore(_folder));
        Assert.Equal(DddiceConnectionState.Guest, second.State);
        second.Connect("secret-token-2", "Allen", AccountA);
        second.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        var http = Api(AccountA);
        Assert.Equal("room-new", (await Prepare(second, http)).Room);
        Assert.Equal(0, http.Count("POST", "room"));

        // And a plain restart while connected reuses it too, without asking who the account is again.
        var third = new DddiceConnection(new DddiceAccountStore(_folder), new DddiceRoomMemoryStore(_folder));
        var again = Api(AccountA);
        Assert.Equal("room-new", (await Prepare(third, again)).Room);
        Assert.Equal((0, 0), (again.Count("POST", "room"), again.Count("GET", "user")));
    }

    [Fact]
    public async Task An_account_file_from_1_5_0_learns_its_account_and_keeps_its_room()
    {
        var store = MemoryAccountStore.With(new DddiceAccount("token-1", "Allen", "room-a", "my-blue", "My Blue Dice"));
        var rooms = new InMemoryDddiceRoomMemory();
        var http = Api(AccountA);

        Assert.Equal("room-a", (await Prepare(new DddiceConnection(store, rooms), http)).Room);

        Assert.Equal(0, http.Count("POST", "room"));
        Assert.Equal(AccountA, store.Saved!.AccountId);
        Assert.Equal("room-a", rooms.Find(AccountA));                         // so a later Disconnect → Connect keeps it
    }

    [Fact]
    public void A_corrupt_or_missing_room_file_is_simply_empty()
    {
        var rooms = new DddiceRoomMemoryStore(_folder);
        Assert.Null(rooms.Find(AccountA));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(rooms.FilePath, "{ not json");
        Assert.Null(rooms.Find(AccountA));

        rooms.Remember(AccountA, "room-a");                                    // a good write replaces it
        rooms.Remember(AccountB, "room-b");
        Assert.Equal(("room-a", "room-b"), (rooms.Find(AccountA), rooms.Find(AccountB)));
    }

    [Fact]
    public async Task The_room_file_holds_only_the_format_and_account_ids_mapped_to_room_ids()
    {
        var accounts = new DddiceAccountStore(_folder);
        var rooms = new DddiceRoomMemoryStore(_folder);
        var connection = new DddiceConnection(accounts, rooms);
        connection.Connect("secret-token-1", "Allen", AccountA);
        connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        await Prepare(connection, Api(AccountA));
        connection.Disconnect();

        var text = File.ReadAllText(rooms.FilePath);
        var json = Assert.IsType<JsonObject>(JsonNode.Parse(text));
        Assert.Equal(["format", "rooms"], json.Select(p => p.Key).Order());
        Assert.Equal(1, (int)json["format"]!);
        var map = Assert.IsType<JsonObject>(json["rooms"]);
        Assert.Equal([(AccountA, "room-new")], map.Select(p => (p.Key, (string)p.Value!)));
        foreach (var secret in new[] { "secret-token", "token", "Allen", "my-blue", "protected", "passcode", "s3cret" })
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([DddiceRoomMemoryStore.FileName], Directory.GetFiles(_folder).Select(Path.GetFileName));   // no token file or temp file left
    }

    [Fact]
    public void The_room_file_lives_in_the_data_folder_so_TABLEFORGE_DATA_DIR_isolates_it()
    {
        var folder = TableForge.Data.AppDatabase.ResolveDataFolder(_folder);
        Assert.Equal(Path.Combine(Path.GetFullPath(_folder), "dddice-rooms.json"), new DddiceRoomMemoryStore(folder).FilePath);
        Assert.Equal(Path.GetDirectoryName(new DddiceAccountStore(folder).FilePath), Path.GetDirectoryName(new DddiceRoomMemoryStore(folder).FilePath));
    }

    [Fact]
    public async Task A_read_only_room_file_never_stops_dddice_and_is_left_as_it_was()
    {
        var rooms = new DddiceRoomMemoryStore(_folder);
        rooms.Remember(AccountB, "room-b");
        File.SetAttributes(rooms.FilePath, FileAttributes.ReadOnly);
        try
        {
            var connection = new DddiceConnection(MemoryAccountStore.With(new DddiceAccount("token-a", "Allen", null, "my-blue", "My Blue Dice", AccountA)), rooms);

            var session = await Prepare(connection, Api(AccountA));               // the new room cannot be remembered on disk

            Assert.Equal("room-new", session.Room);
            Assert.Equal("room-new", connection.Account!.RoomSlug);              // still remembered in the account file
            Assert.Equal(("room-b", (string?)null), (rooms.Find(AccountB), rooms.Find(AccountA)));
            Assert.False(File.Exists(rooms.FilePath + ".tmp"));                    // no half-written file left behind
        }
        finally { File.SetAttributes(rooms.FilePath, FileAttributes.Normal); }
    }

    [Fact]
    public async Task A_room_file_locked_by_another_program_never_stops_dddice()
    {
        var rooms = new DddiceRoomMemoryStore(_folder);
        rooms.Remember(AccountA, "room-a");
        using (new FileStream(rooms.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var connection = new DddiceConnection(MemoryAccountStore.With(new DddiceAccount("token-a", "Allen", "room-a", "my-blue", "My Blue Dice", AccountA)), rooms);
            Assert.Equal("room-a", (await Prepare(connection, Api(AccountA))).Room);
        }
        Assert.Equal("room-a", rooms.Find(AccountA));
    }

    [Fact]
    public async Task A_room_memory_that_throws_anything_never_stops_dddice()
    {
        var connection = Connected(new ThrowingRoomMemory(), "token-a", AccountA, "room-a");
        Assert.Equal("room-a", (await Prepare(connection, Api(AccountA))).Room);
    }

    private sealed class ThrowingRoomMemory : IDddiceRoomMemory
    {
        public string? Find(string accountId) => null;
        public void Remember(string accountId, string roomSlug) => throw new InvalidOperationException("disk on fire");
    }

    // ---- switching accounts ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_different_account_never_gets_the_previous_accounts_room_and_switching_back_reuses_each_room()
    {
        var rooms = new InMemoryDddiceRoomMemory();
        var connection = Connected(rooms, "token-a", AccountA, "room-a");
        await Prepare(connection, Api(AccountA));

        connection.Disconnect();
        connection.Connect("token-b", "Beth", AccountB);
        connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        var asB = Api(AccountB, newRoom: "room-b");
        Assert.Equal("room-b", (await Prepare(connection, asB)).Room);
        Assert.DoesNotContain(asB.Requests, r => r.Path == "room/room-a");    // A's room is not even looked at
        Assert.Equal(1, asB.Count("POST", "room"));
        Assert.False(connection.RoomChanged);                                  // B's first room is not a change

        connection.Disconnect();
        connection.Connect("token-a2", "Allen", AccountA);
        connection.SelectTheme(new DddiceTheme("my-blue", "My Blue Dice", true, ""));
        var backToA = Api(AccountA);
        Assert.Equal("room-a", (await Prepare(connection, backToA)).Room);
        Assert.Equal(0, backToA.Count("POST", "room"));
    }

    [Fact]
    public async Task An_expired_account_reconnected_as_someone_else_does_not_inherit_the_room()
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-a", AccountA, "room-a");
        connection.MarkExpired();
        connection.Connect("token-b", "Beth", AccountB);

        var http = Api(AccountB, newRoom: "room-b");
        Assert.Equal("room-b", (await Prepare(connection, http)).Room);
        Assert.DoesNotContain(http.Requests, r => r.Path == "room/room-a");
    }

    [Theory]
    [InlineData("uuid-b", false)]     // dddice names another user: not this account's room
    [InlineData(null, false)]         // dddice does not say: an unconfirmed room is not reused
    [InlineData("uuid-a", true)]      // dddice confirms it is this account's
    public async Task A_room_whose_account_is_not_known_yet_is_reused_only_when_dddice_confirms_it(string? owner, bool reused)
    {
        // A 1.5.0 file (no account uuid) or an activation that did not say who it was: the room is only a candidate.
        var store = MemoryAccountStore.With(new DddiceAccount("token-1", "Allen", "room-x", "my-blue", "My Blue Dice"));
        var http = Api(AccountA).On("GET", "room/room-x", (200, Room("room-x", owner)));

        var session = await Prepare(new DddiceConnection(store, new InMemoryDddiceRoomMemory()), http);

        Assert.Equal(reused ? "room-x" : "room-new", session.Room);
        Assert.Equal(reused ? 0 : 1, http.Count("POST", "room"));
    }

    [Fact]
    public async Task A_remembered_room_that_dddice_says_belongs_to_someone_else_is_replaced()
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-a", AccountA, "room-a");
        var http = Api(AccountA).On("GET", "room/room-a", (200, Room("room-a", AccountB)));

        Assert.Equal("room-new", (await Prepare(connection, http)).Room);
        Assert.True(connection.RoomChanged);
    }

    [Fact]
    public async Task A_remembered_room_is_still_trusted_when_dddice_does_not_name_its_user()
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-a", AccountA, "room-a");
        var http = Api(AccountA).On("GET", "room/room-a", (200, "{\"data\":{\"slug\":\"room-a\"}}"));

        Assert.Equal("room-a", (await Prepare(connection, http)).Room);
        Assert.Equal(0, http.Count("POST", "room"));
    }

    // ---- unavailable rooms ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task A_room_that_is_gone_is_replaced_once_and_the_new_room_remembered_for_the_account(int status)
    {
        var rooms = new InMemoryDddiceRoomMemory();
        var connection = Connected(rooms, "token-a", AccountA, "room-a");
        var http = Api(AccountA).On("GET", "room/room-a", (status, "{}"));

        Assert.Equal("room-new", (await Prepare(connection, http)).Room);
        await Prepare(connection, http);                                       // prepared again: the new room is reused

        Assert.Equal(1, http.Count("POST", "room"));
        Assert.Equal("room-new", rooms.Find(AccountA));
        Assert.True(connection.RoomChanged);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    [InlineData(401)]
    [InlineData(400)]
    public async Task A_room_check_that_fails_for_any_other_reason_never_creates_a_room(int status)
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-a", AccountA, "room-a");
        var http = Api(AccountA).On("GET", "room/room-a", (status, "{}"));

        await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));

        Assert.Equal(0, http.Count("POST", "room"));
        Assert.Equal("room-a", connection.Account?.RoomSlug);
    }

    [Fact]
    public async Task No_network_during_the_room_check_never_creates_a_room()
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-a", AccountA, "room-a");
        var http = Api(AccountA).Offline("GET", "room/room-a");

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));

        Assert.True(ex.IsTemporary);
        Assert.Equal(0, http.Count("POST", "room"));
    }

    [Fact]
    public async Task An_account_dddice_cannot_identify_is_reported_and_no_room_is_created()
    {
        var store = MemoryAccountStore.With(new DddiceAccount("token-1", "Allen", "room-a", "my-blue", "My Blue Dice"));
        var connection = new DddiceConnection(store, new InMemoryDddiceRoomMemory());
        var http = Api(AccountA).On("GET", "user", (200, "{\"data\":{}}"));

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));

        Assert.Equal(DddiceMessages.AccountUnconfirmed, ex.Message);
        Assert.True(ex.IsAccountProblem);
        Assert.Equal(0, http.Count("POST", "room"));
        Assert.Equal(DddiceConnectionState.Connected, connection.State);    // nothing was refused: try again later
    }

    [Fact]
    public async Task A_refused_token_when_asking_who_the_account_is_marks_it_expired()
    {
        var store = MemoryAccountStore.With(new DddiceAccount("token-1", "Allen", "room-a", "my-blue", "My Blue Dice"));
        var connection = new DddiceConnection(store, new InMemoryDddiceRoomMemory());
        var http = Api(AccountA).On("GET", "user", (401, "{}"));

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));

        Assert.Equal(DddiceMessages.Expired, ex.Message);
        Assert.Equal(DddiceConnectionState.Expired, connection.State);
        Assert.Equal(0, http.Count("POST", "room"));
    }

    // ---- 402 -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_402_when_creating_a_room_explains_the_room_limit_and_is_never_retried_by_itself()
    {
        var rooms = new InMemoryDddiceRoomMemory();
        var connection = Connected(rooms, "token-a", AccountA);
        var http = Api(AccountA).On("POST", "room", (402, "{\"message\":\"Subscription Required\"}"));

        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));

        Assert.Equal(DddiceMessages.RoomLimit, ex.Message);
        Assert.False(ex.IsAuthProblem);
        Assert.False(ex.IsTemporary);
        Assert.Equal(1, http.Count("POST", "room"));                           // one attempt, no automatic retry
        Assert.Equal(DddiceConnectionState.Connected, connection.State);    // the account is fine; it is not "expired"
        Assert.Null(connection.Account!.RoomSlug);
        Assert.Null(rooms.Find(AccountA));

        // Only the person's own Try again makes another (single) attempt.
        await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));
        Assert.Equal(2, http.Count("POST", "room"));
    }

    [Fact]
    public async Task The_402_message_reaches_the_dice_choice_and_Try_again_is_the_only_way_to_retry()
    {
        var connection = Connected(new InMemoryDddiceRoomMemory(), "token-a", AccountA);
        var http = Api(AccountA).On("POST", "room", (402, "{}"));
        var failure = await Assert.ThrowsAnyAsync<DddiceException>(() => Prepare(connection, http));
        var roller = new FakeDddiceRoller { PrepareFailure = failure };
        var vm = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(roller), connection: connection);

        vm.IsDddiceSelected = true;
        await vm.PrepareDddiceAsync();

        Assert.Equal(DiceProviderState.Failed, vm.State);
        Assert.Equal(DddiceMessages.RoomLimit, vm.Message);
        Assert.True(vm.ShowFailure);                                           // Try again / Built-in are offered; nothing retries by itself
        Assert.Equal(1, http.Count("POST", "room"));
    }

    [Fact]
    public async Task A_402_anywhere_else_or_for_a_guest_is_described_as_before()
    {
        using var account = new DddiceRest(new FakeDddiceHttp().On("GET", "dice-box", (402, "{}")));
        var ex = await Assert.ThrowsAnyAsync<DddiceException>(() => account.LoadDiceBoxAsync("token-a", default));
        Assert.Equal("dddice answered with an error (402) while contacting dice-box.", ex.Message);

        using var guest = new DddiceRest(new FakeDddiceHttp().On("POST", "room", (402, "{}")));
        ex = await Assert.ThrowsAnyAsync<DddiceException>(() => guest.CreateRoomAsync("guest-token", default));
        Assert.Equal("dddice answered with an error (402) while contacting room.", ex.Message);
    }

    // ---- activation and guests -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_approved_activation_carries_the_accounts_uuid()
    {
        var http = new FakeDddiceHttp().On("GET", "activate/1234AB",
            (200, """{"data":{"code":"1234AB","token":"acct-token","user":{"username":"Allen","uuid":"uuid-a"}}}"""));
        using var rest = new DddiceRest(http);

        var poll = await rest.PollActivationAsync(new DddiceActivationCode("1234AB", "s3cret", DateTimeOffset.MaxValue), default);

        Assert.Equal(new DddiceActivationPoll("acct-token", "Allen", AccountA), poll);
    }

    [Fact]
    public async Task Guests_are_unchanged_and_never_use_or_fill_the_room_memory()
    {
        var rooms = new InMemoryDddiceRoomMemory();
        var http = new FakeDddiceHttp().On("POST", "user", (201, """{"data":"guest-token"}""")).On("POST", "room", (201, """{"data":{"slug":"guest-room"}}"""));
        using var rest = new DddiceRest(http);
        var source = new DddiceSessionSource(rest, new DddiceConnection(new MemoryAccountStore(), rooms));

        var session = await source.GetAsync(default);

        Assert.Equal(new DddiceSession("guest-token", "guest-room", "dddice-bees", false), session);
        Assert.Equal(0, http.Count("GET", "user"));
        Assert.DoesNotContain(http.Requests, r => r.Path.StartsWith("room/"));
    }
}
