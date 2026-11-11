using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Controls;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.Streaming;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>A temporary data folder, deleted afterwards.</summary>
internal sealed class OverlayFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tableforge-overlay-{Guid.NewGuid():N}");
    public OverlayFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

/// <summary>RC27 Streaming Overlay, phase 4: streaming-overlay.json.</summary>
public class OverlaySettingsStoreTests : IDisposable
{
    private readonly OverlayFolder _folder = new();
    private OverlaySettingsStore Store => new(_folder.Path);
    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_missing_file_gives_the_defaults_off_on_port_41285()
    {
        Assert.Equal(new OverlaySettings(Enabled: false, Port: 41285, ShowTableName: true, ShowRollValue: true), Store.Load());
        Assert.False(File.Exists(Store.FilePath));                                          // loading never writes
        Assert.Equal(_folder.File("streaming-overlay.json"), Store.FilePath);
    }

    [Fact]
    public void Saved_settings_have_the_approved_shape_and_round_trip()
    {
        Store.Save(OverlaySettings.Default);
        using (var doc = JsonDocument.Parse(File.ReadAllText(Store.FilePath)))
        {
            var r = doc.RootElement;
            Assert.Equal(["format", "enabled", "port", "showTableName", "showRollValue"], r.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal((1, false, 41285, true, true), (r.GetProperty("format").GetInt32(), r.GetProperty("enabled").GetBoolean(),
                r.GetProperty("port").GetInt32(), r.GetProperty("showTableName").GetBoolean(), r.GetProperty("showRollValue").GetBoolean()));
        }

        var changed = new OverlaySettings(true, 43000, false, true);
        Store.Save(changed);
        Assert.Equal(changed, Store.Load());
        Assert.Equal(["streaming-overlay.json"], Directory.GetFiles(_folder.Path).Select(f => System.IO.Path.GetFileName(f)!).ToArray());   // no temp file left
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"format":2,"enabled":true,"port":43000}""")]
    [InlineData("""{"enabled":true,"port":43000}""")]
    [InlineData("\u0000\u0001garbage")]
    public void An_unreadable_or_unknown_file_gives_the_defaults(string content)
    {
        File.WriteAllText(Store.FilePath, content);
        Assert.Equal(OverlaySettings.Default, Store.Load());
    }

    [Theory]
    [InlineData("""{"format":1,"enabled":true,"port":80,"showTableName":false,"showRollValue":false}""", true, 41285, false, false)]
    [InlineData("""{"format":1,"enabled":"yes","port":43000,"showTableName":1,"showRollValue":false}""", false, 43000, true, false)]
    [InlineData("""{"format":1,"enabled":true,"port":"43000"}""", true, 41285, true, true)]
    [InlineData("""{"format":1,"enabled":true,"port":70000}""", true, 41285, true, true)]
    [InlineData("""{"format":1,"enabled":true,"port":43000.5}""", true, 41285, true, true)]
    public void A_single_bad_value_falls_back_to_its_own_default(string content, bool enabled, int port, bool tableName, bool rollValue)
    {
        File.WriteAllText(Store.FilePath, content);
        Assert.Equal(new OverlaySettings(enabled, port, tableName, rollValue), Store.Load());
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(0)]
    public void Invalid_ports_are_never_saved(int port)
    {
        Assert.False(OverlaySettings.IsValidPort(port));
        Assert.Throws<ArgumentOutOfRangeException>(() => Store.Save(OverlaySettings.Default with { Port = port }));
        Assert.False(File.Exists(Store.FilePath));
    }

    [Fact]
    public void The_settings_live_beside_the_database_wherever_tableforge_data_dir_puts_it()
    {
        var folder = Data.AppDatabase.ResolveDataFolder(_folder.Path);
        Assert.Equal(System.IO.Path.Combine(folder, "streaming-overlay.json"), new OverlaySettingsStore(folder).FilePath);
    }
}

/// <summary>RC27 Streaming Overlay, phase 4: turning the overlay on and off, its port, and what it shows.</summary>
public class StreamingOverlayControllerTests : IDisposable
{
    private readonly OverlayFolder _folder = new();
    private readonly OverlayPublisher _publisher = new();
    private readonly int _port = RawHttp.FreePort();
    private readonly List<StreamingOverlayController> _controllers = [];

    public void Dispose()
    {
        foreach (var c in _controllers) c.Dispose();
        _folder.Dispose();
    }

    private StreamingOverlayController Controller(OverlaySettings? saved = null)
    {
        new OverlaySettingsStore(_folder.Path).Save(saved ?? OverlaySettings.Default with { Port = _port });
        var controller = new StreamingOverlayController(_publisher, _folder.Path);
        _controllers.Add(controller);
        controller.Start();
        return controller;
    }

    private OverlaySettings Saved => new OverlaySettingsStore(_folder.Path).Load();
    private string Page => File.ReadAllText(_folder.File("streaming-overlay.html"));

    private static async Task<bool> Answers(int port)
    {
        try { return (await RawHttp.Send(port, RawHttp.Get(port))).Status == 200; }
        catch (SocketException) { return false; }
    }

    [Fact]
    public async Task Off_by_default_nothing_listens_and_no_page_is_written()
    {
        var c = Controller();
        Assert.Equal(OverlayStatus.Disabled, c.Status);
        Assert.False(c.IsRunning);
        Assert.False(await Answers(_port));
        Assert.False(File.Exists(_folder.File("streaming-overlay.html")));
        Assert.Contains("Off", c.StatusText);
    }

    [Fact]
    public async Task Enabling_starts_the_overlay_writes_the_page_and_is_remembered()
    {
        var c = Controller();
        c.SetEnabled(true);

        Assert.Equal(OverlayStatus.Running, c.Status);
        Assert.Equal($"Running on port {_port}.", c.StatusText);
        Assert.True(await Answers(_port));
        Assert.Contains($"http://127.0.0.1:{_port}/state", Page);
        Assert.True(Saved.Enabled);
        Assert.Equal(c.LocalFilePath, _folder.File("streaming-overlay.html"));
        Assert.Equal($"http://127.0.0.1:{_port}/", c.BrowserUrl);

        c.SetEnabled(false);
        Assert.Equal(OverlayStatus.Disabled, c.Status);
        Assert.False(await Answers(_port));
        Assert.False(Saved.Enabled);
    }

    [Fact]
    public async Task An_overlay_left_on_starts_with_TableForge_and_stops_at_exit()
    {
        var c = Controller(OverlaySettings.Default with { Enabled = true, Port = _port });
        Assert.Equal(OverlayStatus.Running, c.Status);
        Assert.True(await Answers(_port));

        c.Dispose();
        Assert.False(await Answers(_port));
    }

    [Fact]
    public async Task A_port_in_use_is_reported_and_never_swapped_for_another_and_a_new_port_fixes_it()
    {
        var squatter = new TcpListener(IPAddress.Loopback, _port);
        squatter.Start();
        try
        {
            var c = Controller();
            c.SetEnabled(true);
            Assert.Equal(OverlayStatus.PortUnavailable, c.Status);
            Assert.Contains($"Port {_port} is already in use", c.StatusText);
            Assert.Contains("Choose a different port", c.StatusText);
            Assert.False(c.IsRunning);
            Assert.Equal(_port, Saved.Port);                                                  // nothing changed by itself
            Assert.True(Saved.Enabled);

            var other = RawHttp.FreePort();
            Assert.True(c.SetPort(other));
            Assert.Equal(OverlayStatus.Running, c.Status);
            Assert.Contains("refresh the Browser Source once", c.StatusText);
            Assert.True(await Answers(other));
            Assert.Equal(other, Saved.Port);
            Assert.Contains($"http://127.0.0.1:{other}/state", Page);
            Assert.DoesNotContain($":{_port}/", Page);
        }
        finally { squatter.Stop(); }
    }

    [Fact]
    public async Task Applying_the_same_port_again_retries_once_it_is_free()
    {
        var squatter = new TcpListener(IPAddress.Loopback, _port);
        squatter.Start();
        var c = Controller();
        c.SetEnabled(true);
        Assert.Equal(OverlayStatus.PortUnavailable, c.Status);
        squatter.Stop();

        Assert.True(c.SetPort(_port));
        Assert.Equal(OverlayStatus.Running, c.Status);
        Assert.DoesNotContain("refresh", c.StatusText);                                      // the port did not change
        Assert.True(await Answers(_port));
    }

    [Fact]
    public async Task Changing_the_port_while_running_moves_the_overlay_and_rewrites_the_page()
    {
        var c = Controller();
        c.SetEnabled(true);
        var other = RawHttp.FreePort();
        Assert.True(c.SetPort(other));
        Assert.False(await Answers(_port));
        Assert.True(await Answers(other));
        Assert.Contains($"http://127.0.0.1:{other}/state", Page);
        Assert.Equal($"http://127.0.0.1:{other}/", c.BrowserUrl);
    }

    [Fact]
    public void Changing_the_port_while_off_saves_it_and_rewrites_the_page_without_listening()
    {
        var c = Controller();
        var other = RawHttp.FreePort();
        Assert.True(c.SetPort(other));
        Assert.Equal(OverlayStatus.Disabled, c.Status);
        Assert.False(c.IsRunning);
        Assert.Equal(other, Saved.Port);
        Assert.Contains($"http://127.0.0.1:{other}/state", Page);
        Assert.Contains("refresh the Browser Source once", c.StatusText);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void An_invalid_port_changes_nothing(int port)
    {
        var c = Controller();
        Assert.False(c.SetPort(port));
        Assert.Equal(_port, c.Settings.Port);
        Assert.Equal(_port, Saved.Port);
    }

    [Fact]
    public void Show_table_name_and_roll_value_apply_live_and_are_remembered()
    {
        var c = Controller();
        c.SetShowTableName(false);
        Assert.False(_publisher.State.ShowTableName);
        Assert.False(Saved.ShowTableName);
        c.SetShowRollValue(false);
        Assert.False(_publisher.State.ShowRollValue);
        Assert.False(Saved.ShowRollValue);

        var next = new OverlayPublisher();                                                    // TableForge started again
        new StreamingOverlayController(next, _folder.Path).Also(n => _controllers.Add(n)).Start();
        Assert.False(next.State.ShowTableName);
        Assert.False(next.State.ShowRollValue);
    }

    [Fact]
    public async Task Show_test_result_and_clear_change_only_the_overlay()
    {
        var c = Controller();
        c.SetEnabled(true);
        var settings = File.ReadAllText(new OverlaySettingsStore(_folder.Path).FilePath);

        c.ShowTest();
        var shown = await RawHttp.Send(_port, RawHttp.Get(_port));
        Assert.Contains("Your streaming overlay is working.", shown.Text);
        c.Clear();
        Assert.True(_publisher.State.IsEmpty);
        Assert.Contains("\"empty\":true", (await RawHttp.Send(_port, RawHttp.Get(_port))).Text);
        Assert.Equal(settings, File.ReadAllText(new OverlaySettingsStore(_folder.Path).FilePath));
    }

    [Fact]
    public void Settings_that_cannot_be_saved_still_apply_for_this_run_and_the_status_says_so()
    {
        var c = Controller();
        File.Delete(new OverlaySettingsStore(_folder.Path).FilePath);
        Directory.CreateDirectory(new OverlaySettingsStore(_folder.Path).FilePath);            // a folder where the file must go
        c.SetShowRollValue(false);
        Assert.False(c.Settings.ShowRollValue);
        Assert.False(_publisher.State.ShowRollValue);
        Assert.Contains("could not be saved", c.StatusText);
    }
}

/// <summary>RC27 Streaming Overlay, phase 4: the Streaming Overlay dialog's view model and window.</summary>
public class StreamingOverlayDialogTests : IDisposable
{
    private readonly OverlayFolder _folder = new();
    private readonly int _port = RawHttp.FreePort();
    private readonly StreamingOverlayController _controller;
    private readonly List<string> _copied = [];
    private readonly List<string> _opened = [];

    public StreamingOverlayDialogTests()
    {
        new OverlaySettingsStore(_folder.Path).Save(OverlaySettings.Default with { Port = _port });
        _controller = new StreamingOverlayController(new OverlayPublisher(), _folder.Path);
        _controller.Start();
    }

    public void Dispose() { _controller.Dispose(); _folder.Dispose(); }

    private StreamingOverlayViewModel Vm(DddiceConnection? dddice = null) => new(_controller, dddice, _copied.Add, _opened.Add);

    [Fact]
    public void Copy_gives_the_local_file_path_and_makes_sure_the_file_exists_and_the_url()
    {
        var vm = Vm();
        vm.CopyLocalFileCommand.Execute(null);
        Assert.Equal(_folder.File("streaming-overlay.html"), _copied.Single());
        Assert.True(File.Exists(_copied.Single()));
        Assert.Equal("Local file path copied.", vm.CopyMessage);

        vm.CopyUrlCommand.Execute(null);
        Assert.Equal($"http://127.0.0.1:{_port}/", _copied[1]);
    }

    [Fact]
    public void Enable_and_the_two_switches_go_through_to_the_overlay()
    {
        var vm = Vm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.IsEnabled = true;
        Assert.True(_controller.IsRunning);
        Assert.Contains(nameof(vm.StatusText), raised);
        Assert.Equal($"Running on port {_port}.", vm.StatusText);
        vm.ShowTableName = false;
        Assert.False(_controller.Publisher.State.ShowTableName);
        vm.IsEnabled = false;
        Assert.False(_controller.IsRunning);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("80")]
    [InlineData("70000")]
    [InlineData("-5")]
    [InlineData("")]
    public void A_port_that_is_not_1024_to_65535_is_explained_and_not_used(string text)
    {
        var vm = Vm();
        vm.PortText = text;
        vm.ApplyPortCommand.Execute(null);
        Assert.Equal("Enter a port from 1024 to 65535.", vm.PortError);
        Assert.Equal(_port, _controller.Settings.Port);
        vm.PortText = "5";
        Assert.False(vm.HasPortError);                                                          // typing clears the message
    }

    [Fact]
    public void A_valid_port_is_applied_and_the_url_follows_it()
    {
        var vm = Vm();
        var other = RawHttp.FreePort();
        vm.PortText = $" {other} ";
        vm.ApplyPortCommand.Execute(null);
        Assert.Equal(other, _controller.Settings.Port);
        Assert.Equal($"http://127.0.0.1:{other}/", vm.BrowserUrl);
        Assert.Equal(other.ToString(), vm.PortText);
    }

    [Fact]
    public void Test_and_clear_buttons_reach_the_overlay()
    {
        var vm = Vm();
        vm.ShowTestCommand.Execute(null);
        Assert.Equal("Gidubba Overlay Test", _controller.Publisher.State.Result!.TableName);
        vm.ClearCommand.Execute(null);
        Assert.True(_controller.Publisher.State.IsEmpty);
    }

    [Fact]
    public void Without_a_connected_dddice_account_there_is_no_room_to_open()
    {
        foreach (var dddice in new DddiceConnection?[] { null, new DddiceConnection(new MemoryAccountStore()) })
        {
            var vm = Vm(dddice);
            Assert.False(vm.ShowDddiceRoom);
            Assert.False(vm.OpenDddiceRoomCommand.CanExecute(null));
            Assert.Contains("needs a connected dddice account", vm.DddiceHelp);
            Assert.False(vm.HasDddiceNotice);
        }
    }

    [Fact]
    public void Open_my_dddice_room_opens_the_accounts_saved_room_in_the_browser()
    {
        var dddice = new DddiceConnection(MemoryAccountStore.With(new DddiceAccount("secret-token", "Allen", "JXV7rd3", "my-blue", "My Blue Dice")));
        var vm = Vm(dddice);
        Assert.True(vm.ShowDddiceRoom);
        Assert.Contains("Streaming tools", vm.DddiceHelp);
        vm.OpenDddiceRoomCommand.Execute(null);
        Assert.Equal(["https://dddice.com/room/JXV7rd3"], _opened);
        Assert.DoesNotContain(_opened, u => u.Contains("secret-token") || u.Contains("key="));   // never a token or a streaming key
    }

    [Fact]
    public void An_account_without_a_room_yet_or_an_expired_one_offers_no_room()
    {
        Assert.False(Vm(new DddiceConnection(MemoryAccountStore.With(new DddiceAccount("t", "A", null, "x", "X")))).ShowDddiceRoom);
        Assert.False(Vm(new DddiceConnection(new MemoryAccountStore(new DddiceAccountLoad(DddiceAccountFileState.Unreadable)))).ShowDddiceRoom);
    }

    [Fact]
    public void The_dialog_stops_listening_when_it_closes()
    {
        var vm = Vm();
        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;
        vm.Detach();
        _controller.SetShowRollValue(false);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void The_main_window_offers_streaming_overlay_only_when_it_is_wired_in()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var opened = 0;
        var main = new MainViewModel(db, new FixedDice(1), showStreamingOverlay: () => opened++);
        Assert.True(main.HasStreamingOverlay);
        main.StreamingOverlayCommand.Execute(null);
        Assert.Equal(1, opened);

        var bare = new MainViewModel(db, new FixedDice(1));
        Assert.False(bare.HasStreamingOverlay);
        Assert.False(bare.StreamingOverlayCommand.CanExecute(null));
    }

    [Fact]
    public void The_window_shows_every_control()
    {
        Sta.Run(() =>
        {
            var vm = Vm();
            var window = new StreamingOverlayWindow { DataContext = vm, Left = -20000, Top = -20000, ShowActivated = false };
            window.Show();
            try
            {
                var names = ViewTests.FindAll<System.Windows.FrameworkElement>(window).Select(e => e.Name).Where(n => n.Length > 0).ToHashSet();
                foreach (var name in new[] { "EnableBox", "OverlayStatusText", "LocalFileBox", "CopyLocalFileButton", "BrowserUrlBox", "CopyUrlButton",
                             "PortBox", "ApplyPortButton", "ShowTableNameBox", "ShowRollValueBox", "ShowTestButton", "ClearOverlayButton", "OpenDddiceRoomButton" })
                    Assert.Contains(name, names);
                var local = ViewTests.FindAll<TextBox>(window).Single(t => t.Name == "LocalFileBox");
                Assert.Equal(_folder.File("streaming-overlay.html"), local.Text);
                Assert.True(local.IsReadOnly);
                Assert.Equal("Streaming Overlay", window.Title);
            }
            finally { window.Close(); }
        });
    }
}

/// <summary>RC27 Streaming Overlay, phase 4: telling the person when dddice replaced the account's room.</summary>
public class DddiceRoomChangeTests
{
    private static readonly DddiceAccount Saved = new("acct-token", "Allen", "room-1", "my-blue", "My Blue Dice", "acct-1");

    private static FakeDddiceHttp Api(int savedRoomStatus) => new FakeDddiceHttp()
        .On("GET", "dice-box", (200, DiceBoxJson.Page(null, DiceBoxJson.Bees, DiceBoxJson.Blue, DiceBoxJson.Letter)))
        .On("GET", "room/room-1", (savedRoomStatus, "{}"))
        .On("GET", "room/room-2", (200, "{}"))
        .On("POST", "room", (201, """{"data":{"slug":"room-2"}}"""));

    [Fact]
    public async Task A_saved_room_that_still_works_is_no_change()
    {
        using var rest = new DddiceRest(Api(200));
        var connection = new DddiceConnection(MemoryAccountStore.With(Saved));
        await connection.PrepareSessionAsync(rest, default);
        Assert.False(connection.RoomChanged);
        Assert.Equal("https://dddice.com/room/room-1", connection.RoomUrl);
    }

    [Fact]
    public async Task The_first_room_an_account_ever_gets_is_no_change()
    {
        using var rest = new DddiceRest(Api(200));
        var connection = new DddiceConnection(MemoryAccountStore.With(Saved with { RoomSlug = null }));
        Assert.Null(connection.RoomUrl);
        await connection.PrepareSessionAsync(rest, default);
        Assert.False(connection.RoomChanged);
        Assert.Equal("https://dddice.com/room/room-2", connection.RoomUrl);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task A_saved_room_dddice_no_longer_allows_is_replaced_and_reported_once(int status)
    {
        using var rest = new DddiceRest(Api(status));
        var connection = new DddiceConnection(MemoryAccountStore.With(Saved));
        var changes = new List<DddiceChange>();
        connection.Changed += (_, c) => changes.Add(c);

        await connection.PrepareSessionAsync(rest, default);
        Assert.True(connection.RoomChanged);
        Assert.Equal("https://dddice.com/room/room-2", connection.RoomUrl);
        Assert.Equal([DddiceChange.Status], changes);                                         // only what is shown; the dice page is not restarted

        await connection.PrepareSessionAsync(rest, default);                                  // room-2 is fine now
        Assert.True(connection.RoomChanged);                                                   // the notice stays for this run
        Assert.Single(changes);

        connection.Disconnect();
        Assert.False(connection.RoomChanged);
    }

    [Fact]
    public async Task The_dice_panel_and_the_dialog_both_say_the_room_changed()
    {
        using var rest = new DddiceRest(Api(404));
        var connection = new DddiceConnection(MemoryAccountStore.With(Saved));
        var dice = new DiceProviderViewModel(new FixedDice(1), new SessionOnlyDddice(rest, connection), connection: connection);
        dice.Select(DiceProviderKind.Dddice);
        await Wait.Until(() => dice.State == DiceProviderState.Ready, "dddice to be ready");

        Assert.Equal("dddice is ready (theme: My Blue Dice). dddice room changed. Your OBS dice source may need to be updated.", dice.Message);

        using var folder = new OverlayFolder();
        using var controller = new StreamingOverlayController(new OverlayPublisher(), folder.Path);
        var vm = new StreamingOverlayViewModel(controller, connection, _ => { }, _ => { });
        Assert.Equal("dddice room changed. Your OBS dice source may need to be updated.", vm.DddiceNotice);
        Assert.True(vm.HasDddiceNotice);
    }

    [Fact]
    public async Task A_guest_never_sees_a_room_notice()
    {
        var http = new FakeDddiceHttp().On("POST", "user", (201, """{"data":"guest-token"}""")).On("POST", "room", (201, """{"data":{"slug":"guest-room"}}"""));
        using var rest = new DddiceRest(http);
        var connection = new DddiceConnection(new MemoryAccountStore());
        var dice = new DiceProviderViewModel(new FixedDice(1), new SessionOnlyDddice(rest, connection), connection: connection);
        dice.Select(DiceProviderKind.Dddice);
        await Wait.Until(() => dice.State == DiceProviderState.Ready, "dddice to be ready");
        Assert.Equal("dddice is ready (guest mode: no account needed).", dice.Message);
        Assert.False(connection.RoomChanged);
        Assert.Null(connection.RoomUrl);
    }

    /// <summary>The real session step of dddice preparation (guest or account), with no dice page.</summary>
    private sealed class SessionOnlyDddice(DddiceRest rest, DddiceConnection connection) : IPreparableDiceProvider
    {
        private readonly DddiceSessionSource _sessions = new(rest, connection);
        public async Task PrepareAsync(CancellationToken cancellationToken) => await _sessions.GetAsync(cancellationToken);
        public Task<int> RollAsync(DiceExpression expression, CancellationToken cancellationToken) => Task.FromResult(1);
    }
}
