using System.Text;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.Portable;
using TableForge.Streaming;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// RC27 Streaming Overlay, phase 2: the Roll screen publishes every resolved result — Built-in, dddice and Manual Entry alike —
/// through <see cref="RollViewModel"/>'s one resolution path, and a successful inline roll updates it only while its result is current.
/// </summary>
public class StreamingOverlayWiringTests
{
    private static readonly (int, int, string)[] D20Rows = [(1, 9, "Low"), (10, 13, "Mid"), (14, 20, "High")];

    private static (RollViewModel Session, OverlayPublisher Overlay, List<RollSnapshot> Rolled) Open(RollableTable table, IDiceProvider dice)
    {
        var overlay = new OverlayPublisher();
        var rolled = new List<RollSnapshot>();
        return (new RollViewModel(table, dice, rolled: rolled.Add, overlay: overlay), overlay, rolled);
    }

    private static RollableTable D20(string name = "Omens", params (int, int, string)[] rows) =>
        Fixtures.Table(DiceExpression.Parse("d20"), rows.Length == 0 ? D20Rows : rows).Also(t => t.Name = name);

    private static string Text(OverlayPublisher p) => string.Join(" | ", p.State.Result!.Lines.Select(l => (l.Heading.Length > 0 ? l.Heading + ": " : "") + l.Text));
    private static string Json(OverlayPublisher p) => Encoding.UTF8.GetString(p.StateJson);

    [Fact]
    public void A_built_in_roll_publishes_the_table_name_roll_and_result()
    {
        var (session, overlay, _) = Open(D20(), new FixedDice(15));
        Assert.True(overlay.State.IsEmpty);

        session.RollCommand.Execute(null);

        Assert.Equal(("Omens", "15", "High"), (overlay.State.Result!.TableName, overlay.State.Result.RollValue, Text(overlay)));
        Assert.Equal(overlay.State.Generation, session.LatestRolls.Single().OverlayToken!.Value.Generation);
    }

    [Fact]
    public async Task A_dddice_roll_publishes_only_once_the_dice_settle()
    {
        var roller = new FakeDddiceRoller();
        var (session, overlay, _) = Open(D20(), new DddiceDiceProvider(roller));

        var roll = session.RollAsync();
        await Wait.Until(() => roller.IsRollPending, "the dice to be thrown");
        Assert.True(overlay.State.IsEmpty);                                                // nothing while the dice are in the air

        roller.Settle(("d20", 11));
        await roll;
        Assert.Equal(("11", "Mid"), (overlay.State.Result!.RollValue, Text(overlay)));
    }

    [Fact]
    public void Manual_entry_publishes_like_any_resolved_roll()
    {
        var (session, overlay, _) = Open(D20(), new FixedDice(1));
        session.ManualRollText = "17";
        session.ResolveManualCommand.Execute(null);
        Assert.Equal(("17", "High"), (overlay.State.Result!.RollValue, Text(overlay)));

        session.ManualRollText = "not a number";
        var version = overlay.State.Version;
        session.ResolveManualCommand.Execute(null);                                        // refused: nothing resolved, nothing published
        Assert.Equal(version, overlay.State.Version);
    }

    [Fact]
    public void A_situational_modifier_shows_only_the_final_calculated_roll()
    {
        var (session, overlay, _) = Open(D20(), new FixedDice(11));
        session.ModifierText = "+3";
        session.RollCommand.Execute(null);

        Assert.Equal("11 +3 situational", session.RollBreakdown);                          // the Roll screen keeps its breakdown
        Assert.Equal(("14", "High"), (overlay.State.Result!.RollValue, Text(overlay)));
        Assert.DoesNotContain("situational", Json(overlay));
        Assert.DoesNotContain("+3", Json(overlay));
    }

    [Fact]
    public void A_clamped_roll_shows_the_calculated_value_and_the_resolved_result_without_clamp_mechanics()
    {
        var table = D20().Also(t => t.ClampResultsToRange = true);
        var (session, overlay, rolled) = Open(table, new FixedDice(20));
        session.ModifierText = "+5";
        session.RollCommand.Execute(null);

        Assert.Equal("Resolved as 20 (clamped)", session.RollClampNote);
        Assert.Equal(("25", "High"), (overlay.State.Result!.RollValue, Text(overlay)));
        Assert.DoesNotContain("clamp", Json(overlay), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"20\"", Json(overlay));
        Assert.Equal(20, rolled[0].ClampedValue);
    }

    [Fact]
    public void No_match_publishes_and_replaces_the_previous_result()
    {
        var (session, overlay, _) = Open(D20("Gaps", (1, 10, "Low")), new SequenceDice(4, 15));
        session.RollCommand.Execute(null);
        Assert.Equal("Low", Text(overlay));

        session.RollCommand.Execute(null);
        Assert.Equal(("15", "No entry covers 15."), (overlay.State.Result!.RollValue, Text(overlay)));
    }

    [Fact]
    public void Several_rolls_at_once_each_publish_and_the_last_one_wins()
    {
        var (session, overlay, _) = Open(D20(), new SequenceDice(3, 11, 18));
        session.RollCount = 3;
        session.RollCommand.Execute(null);

        Assert.Equal(("18", "High"), (overlay.State.Result!.RollValue, Text(overlay)));
        var tokens = session.LatestRolls.Select(o => o.OverlayToken!.Value.Generation).ToArray();
        Assert.Equal(3, tokens.Distinct().Count());                                        // each roll has its own token
        Assert.Equal(overlay.State.Generation, tokens[^1]);
    }

    [Fact]
    public void D100_and_d66_values_read_as_the_dice_show_them()
    {
        var (d100, overlay100, _) = Open(Fixtures.Table(DiceExpression.Parse("d100"), (1, 99, "Most"), (100, 100, "Doubles zero")), new FixedDice(100));
        d100.RollCommand.Execute(null);
        Assert.Equal(("00", "Doubles zero"), (overlay100.State.Result!.RollValue, Text(overlay100)));

        var (d66, overlay66, _) = Open(Fixtures.Table(DiceExpression.Parse("d66"), (11, 66, "Anything")), new FixedDice(35));
        d66.RollCommand.Execute(null);
        Assert.Equal("35", overlay66.State.Result!.RollValue);
    }

    [Fact]
    public void Every_result_set_is_its_own_line_headed_by_its_name()
    {
        var table = new RollableTable
        {
            Name = "Weather", Dice = DiceExpression.Parse("d6"),
            ResultSets = [Set("Sky", E(1, 6, "Storm")), Set("", E(1, 6, "Mud underfoot"))],
        };
        var (session, overlay, _) = Open(table, new FixedDice(4));
        session.RollCommand.Execute(null);
        Assert.Equal([("Sky", "Storm"), ("", "Mud underfoot")], overlay.State.Result!.Lines.Select(l => (l.Heading, l.Text)).ToArray());
    }

    [Fact]
    public void Bold_and_italic_reach_the_overlay()
    {
        const string text = "The creature gains +2 Armor until dawn.";
        var styles = Runs(text.Length, (19, 8, TextStyle.Bold), (34, 4, TextStyle.Italic));
        var table = new RollableTable { Name = "Boons", Dice = DiceExpression.Parse("d6"), ResultSets = [Set("", E(1, 6, text, styles: styles))] };
        var (session, overlay, _) = Open(table, new FixedDice(2));
        session.RollCommand.Execute(null);

        var segments = overlay.State.Result!.Lines[0].Segments;
        Assert.Equal(text, string.Concat(segments.Select(s => s.Text)));
        Assert.Contains(new OverlaySegment("+2 Armor", Bold: true), segments);
        Assert.Contains(new OverlaySegment("dawn", Italic: true), segments);
    }

    [Fact]
    public void An_inline_roll_updates_the_current_result_in_context()
    {
        var table = D20("Crypt", (1, 20, "You encounter 2d6 Skeletons guarding the gate."));
        var (session, overlay, rolled) = Open(table, new SequenceDice(12, 7));
        session.RollCommand.Execute(null);
        Assert.Equal("You encounter 2d6 Skeletons guarding the gate.", Text(overlay));
        var generation = overlay.State.Generation;

        Assert.Single(session.Results[0].InlineActions).RollCommand!.Execute(null);

        Assert.Equal("You encounter 7 Skeletons guarding the gate.", Text(overlay));
        Assert.Equal(("Crypt", "12", generation), (overlay.State.Result!.TableName, overlay.State.Result.RollValue, overlay.State.Generation));
        Assert.DoesNotContain("2d6", Json(overlay));
        Assert.DoesNotContain("Resolved", Json(overlay));
        Assert.Single(rolled);                                                              // an inline roll is never history
    }

    [Fact]
    public void An_inline_roll_keeps_the_formatting_of_the_expression_it_replaces()
    {
        const string text = "Gain 1d4 coins.";
        var table = new RollableTable { Name = "Loot", Dice = DiceExpression.Parse("d6"), ResultSets = [Set("", E(1, 6, text, styles: Runs(text.Length, (5, 3, TextStyle.Bold))))] };
        var (session, overlay, _) = Open(table, new SequenceDice(2, 3));
        session.RollCommand.Execute(null);
        session.Results[0].InlineActions[0].RollCommand!.Execute(null);

        Assert.Contains(new OverlaySegment("3", Bold: true), overlay.State.Result!.Lines[0].Segments);
        Assert.Equal("Gain 3 coins.", Text(overlay));
    }

    [Fact]
    public void An_inline_roll_on_an_older_result_never_replaces_the_current_one()
    {
        var table = D20("Crypt", (1, 10, "A: 2d6 bats"), (11, 20, "B: 1d4 rats"));
        var (session, overlay, _) = Open(table, new SequenceDice(5, 15, 9, 2));
        session.RollCount = 2;
        session.RollCommand.Execute(null);                                                  // A (5) then B (15)
        var (a, b) = (session.LatestRolls[0], session.LatestRolls[1]);
        Assert.Equal("B: 1d4 rats", Text(overlay));

        a.Lines[0].InlineActions[0].RollCommand!.Execute(null);                             // 9 bats, on A: the overlay keeps B
        Assert.Equal("A: 9 bats", a.Lines[0].ResolvedText);
        Assert.Equal("B: 1d4 rats", Text(overlay));

        b.Lines[0].InlineActions[0].RollCommand!.Execute(null);                             // 2 rats, on B: B updates in context
        Assert.Equal("B: 2 rats", Text(overlay));
    }

    [Fact]
    public void After_clear_or_test_an_inline_roll_cannot_bring_an_older_result_back()
    {
        var table = D20("Crypt", (1, 20, "You find 1d4 coins."));
        var (session, overlay, rolled) = Open(table, new SequenceDice(8, 3, 4));
        session.RollCommand.Execute(null);
        var action = session.Results[0].InlineActions[0];

        overlay.Clear();
        action.RollCommand!.Execute(null);
        Assert.True(overlay.State.IsEmpty);
        Assert.Equal("You find 3 coins.", session.Results[0].ResolvedText);                // the Roll screen still resolves it

        overlay.ShowTest();
        action.RollCommand!.Execute(null);
        Assert.Equal("Your streaming overlay is working.", Text(overlay));
        Assert.Single(rolled);                                                              // Clear and Test are never history
    }

    [Fact]
    public void Without_an_overlay_the_roll_screen_behaves_exactly_as_before()
    {
        var session = new RollViewModel(D20(), new FixedDice(15));
        session.RollCommand.Execute(null);
        Assert.Equal("Rolled 15", session.RollDisplay);
        Assert.Null(session.LatestRolls.Single().OverlayToken);
    }
}

/// <summary>
/// RC27 Streaming Overlay, phase 2: what must NOT change the overlay. The publisher is only ever called from
/// <see cref="RollViewModel"/>'s resolution (<c>Apply</c>) and successful-inline paths; these drive the real
/// <see cref="MainViewModel"/> through everything else a person does after a roll and check the overlay is untouched.
/// </summary>
public class StreamingOverlayNonPublishingTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly PortableFixtures.TempFile _file = new();
    private readonly AppDatabase _db;
    private readonly OverlayPublisher _overlay = new();
    private readonly MainViewModel _main;
    private readonly List<string> _copied = [];
    private OverlayState _before = null!;

    public StreamingOverlayNonPublishingTests()
    {
        _db = _temp.Open();
        var c = _db.CreateCollection("Alpha");
        var weatherFolder = _db.CreateFolder(c.Id, "Weather");
        var storm = Save(_db, c.Id, "Storm", "d6", weatherFolder.Id, Set("", E(1, 6, "Lightning")));
        Save(_db, c.Id, "Weather", "d20", weatherFolder.Id, Set("", E(1, 10, "Clear"), E(11, 20, "Storm brewing", link: storm.Id)));
        Save(_db, c.Id, "Calm", "d6", null, Set("", E(1, 6, "Nothing happens")));
        var other = _db.CreateCollection("Beta");
        Save(_db, other.Id, "Elsewhere", "d6", null, Set("", E(1, 6, "Far away")));

        _main = new MainViewModel(_db, new FixedDice(15), confirm: (_, _) => true, copyText: _copied.Add,
            chooseSaveFile: _ => null, chooseCollectionFile: _ => _file.Path, chooseImportFile: () => _file.Path,
            confirmImport: _ => true, showMessage: _ => { }, overlay: _overlay);
        Open("Weather");
        Roll.RollCommand.Execute(null);                                                      // 15: "Storm brewing", with its link
        _before = _overlay.State;
        Assert.Equal("Storm brewing", _before.Result!.Lines[0].Text);
    }

    private RollViewModel Roll => (RollViewModel)_main.Current!;
    private void Open(string name) { _main.HighlightedTable = _main.Tables.Single(t => t.Name == name); _main.OpenTableCommand.Execute(null); }
    private void AssertUnchanged(string action) => Assert.True(ReferenceEquals(_before, _overlay.State), $"{action} changed the overlay");

    public void Dispose() { _db.Dispose(); _temp.Dispose(); _file.Dispose(); }

    [Fact]
    public void Following_a_link_does_not_publish_but_rolling_the_linked_table_does()
    {
        Roll.Results[0].FollowCommand!.Execute(null);
        Assert.Equal("Storm", Roll.Title);
        AssertUnchanged("Following a link");

        Roll.ManualRollText = "3";                                                          // the test dice give 15; a d6 needs 1-6
        Roll.ResolveManualCommand.Execute(null);
        Assert.Equal(("Storm", "3", "Lightning"), (_overlay.State.Result!.TableName, _overlay.State.Result.RollValue, _overlay.State.Result.Lines[0].Text));
    }

    [Fact]
    public void Opening_other_tables_and_moving_around_collections_and_folders_does_not_publish()
    {
        Open("Calm");
        AssertUnchanged("Opening another table");
        _main.SelectedFolderNav = _main.FolderNav.First(f => f.Name == "Weather");
        _main.TableFilter = "Sto";
        _main.TableFilter = "";
        AssertUnchanged("Folder navigation and search");
        _main.SelectedCollection = _main.Collections.Single(c => c.Name == "Beta");
        Open("Elsewhere");
        _main.SelectedCollection = _main.Collections.Single(c => c.Name == "Alpha");
        AssertUnchanged("Changing collection");
        _main.OpenRecentTableCommand.Execute(_main.RecentTables.First());
        AssertUnchanged("Opening a recent table");
    }

    [Fact]
    public void Editing_a_table_cancelling_or_saving_does_not_publish()
    {
        _main.EditTableCommand.Execute(null);
        var review = Assert.IsType<ReviewViewModel>(_main.Current);
        review.CancelCommand.Execute(null);
        AssertUnchanged("Edit, then Cancel");

        _main.EditTableCommand.Execute(null);
        review = Assert.IsType<ReviewViewModel>(_main.Current);
        review.Description = "Roll at dawn.";
        review.SaveCommand.Execute(null);
        Assert.IsType<RollViewModel>(_main.Current);                                        // saving reopens the table, unrolled
        AssertUnchanged("Edit, then Save");
    }

    [Fact]
    public void Exports_imports_and_deletions_do_not_publish()
    {
        Roll.CopyTableTextCommand.Execute(null);
        Roll.CopyTableTextSpacesCommand.Execute(null);
        Roll.CopyForSojourCommand.Execute(null);
        Roll.CopyFoundryJsonCommand.Execute(null);
        Roll.CopyTablesPlusJsonCommand.Execute(null);
        Assert.Equal(5, _copied.Count);
        AssertUnchanged("The Roll screen's exports");

        _main.ExportCollectionCommand.Execute(null);
        Assert.True(File.Exists(_file.Path));
        _main.ImportCollectionCommand.Execute(null);
        Assert.Contains(_main.Collections, c => c.Name == "Alpha (2)");
        AssertUnchanged("Export Collection and Import Collection");

        Open("Calm");
        _main.DeleteTableCommand.Execute(null);
        _main.SelectedCollection = _main.Collections.Single(c => c.Name == "Alpha (2)");
        _main.DeleteCollectionCommand.Execute(null);
        Assert.DoesNotContain(_main.Collections, c => c.Name == "Alpha (2)");
        AssertUnchanged("Deleting a table and a collection");
    }

    [Fact]
    public void Clear_and_test_change_only_the_overlay_never_history()
    {
        var recent = _main.RecentRolls.Count;
        _overlay.ShowTest();
        _overlay.Clear();
        Assert.True(_overlay.State.IsEmpty);
        Assert.Equal(recent, _main.RecentRolls.Count);
        Assert.Equal(recent, _db.GetRollHistory().Count);
    }
}
