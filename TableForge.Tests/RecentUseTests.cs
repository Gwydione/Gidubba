using Microsoft.Data.Sqlite;
using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Recent Rolls and Recent Tables through the view models, on a real database.</summary>
public class RecentUseTests
{
    private sealed class World : IDisposable
    {
        public TempDatabase Temp { get; } = new();
        public AppDatabase Db { get; }
        public Collection Collection { get; }
        public FixedDice Dice { get; } = new(6);
        public bool ConfirmAnswer { get; set; } = true;
        public MainViewModel Main { get; private set; } = null!;

        public World()
        {
            Db = Temp.Open();
            Collection = Db.CreateCollection("Dungeon");
        }

        public MainViewModel Start() => Main = new MainViewModel(Db, Dice, (_, _) => ConfirmAnswer);

        public TableSummary Summary(string name) => Main.Tables.Single(t => t.Name == name);

        public RollViewModel Open(string name)
        {
            Main.SelectedTable = Summary(name);
            return Assert.IsType<RollViewModel>(Main.Current);
        }

        public string[] Recent() => Main.RecentTables.Select(t => t.Name).ToArray();

        public void Dispose() => Temp.Dispose();
    }

    private static void SaveTables(World w, int count) =>
        Enumerable.Range(1, count).ToList().ForEach(i => w.Db.SaveTable(Fixtures.ScavengedItems(w.Collection.Id).Also(t => t.Name = $"Table {i}")));

    // ---- what creates history -----------------------------------------------------------------

    [Fact]
    public void A_built_in_roll_creates_exactly_one_history_snapshot()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenging");
        Assert.Empty(w.Main.RecentRolls);                       // opening a table is not a roll

        roll.RollCommand.Execute(null);

        var item = Assert.Single(w.Main.RecentRolls).Item;
        Assert.Equal(("Scavenging", "2d6", 6, "1x Scavenged Item"), (item.TableName, item.DiceText, item.RollValue, item.ResultText));
        Assert.Equal(w.Summary("Scavenging").Id, item.TableId);
        Assert.Single(w.Db.GetRollHistory());
    }

    [Fact]
    public void A_legal_manual_roll_creates_one_and_invalid_input_creates_none()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenging");

        foreach (var bad in new[] { "", "abc", "1", "13", "-4", "6.5", "6-7" })
        {
            roll.ManualRollText = bad;
            roll.ResolveManualCommand.Execute(null);
        }
        Assert.Empty(w.Db.GetRollHistory());
        Assert.Empty(w.Main.RecentRolls);

        roll.ManualRollText = "9";
        roll.ResolveManualCommand.Execute(null);

        var item = Assert.Single(w.Db.GetRollHistory());
        Assert.Equal((9, "2x Scavenged Items"), (item.RollValue, item.ResultText));
    }

    [Fact]
    public void Following_a_link_creates_no_history_until_the_child_is_rolled_and_each_roll_is_its_own_flat_record()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenging");
        roll.RollCommand.Execute(null);                          // parent roll: 1 record
        roll.Results[0].FollowCommand!.Execute(null);

        Assert.Single(w.Db.GetRollHistory());                    // following added nothing

        w.Dice.Value = 12;
        roll.RollCommand.Execute(null);
        roll.RollCommand.Execute(null);                          // two rolls on the child stay two ordinary records

        Assert.Equal(
            [("Scavenged Items", 12), ("Scavenged Items", 12), ("Scavenging", 6)],
            w.Main.RecentRolls.Select(r => (r.TableName, r.Item.RollValue)).ToArray());
    }

    [Fact]
    public void A_multi_result_set_roll_snapshots_every_output()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Dice.Value = 68;
        w.Start();

        w.Open("Room Features").RollCommand.Execute(null);

        var item = Assert.Single(w.Main.RecentRolls).Item;
        Assert.Equal("Ambient: Smell of burning flesh\nNoise: Hissing\nGeneral Feature: Grated floors reveal dozens of people below", item.ResultText);
        Assert.Equal("Room Features\nd100 → 68\n\nAmbient: Smell of burning flesh\nNoise: Hissing\nGeneral Feature: Grated floors reveal dozens of people below", item.FullText);
        Assert.Equal("68", Assert.Single(w.Main.RecentRolls).RollDisplay);
    }

    [Fact]
    public void A_roll_of_100_on_a_d100_shows_as_00_in_recent_rolls()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Dice.Value = 100;
        w.Start();

        w.Open("Room Features").RollCommand.Execute(null);

        var recent = Assert.Single(w.Main.RecentRolls);
        Assert.Equal(("00", 100), (recent.RollDisplay, recent.Item.RollValue));
    }

    [Fact]
    public void A_no_match_snapshot_keeps_what_the_user_was_shown()
    {
        using var w = new World();
        var table = Fixtures.RoomFeatures(w.Collection.Id);
        table.ResultSets[1].Entries.RemoveAll(e => e.Text == "Hissing");
        w.Db.SaveTable(table);
        w.Dice.Value = 68;
        w.Start();

        w.Open("Room Features").RollCommand.Execute(null);

        Assert.Equal("Ambient: Smell of burning flesh\nNoise: No entry covers 68.\nGeneral Feature: Grated floors reveal dozens of people below",
            Assert.Single(w.Main.RecentRolls).Item.ResultText);
    }

    [Fact]
    public void An_ambiguous_snapshot_keeps_the_ambiguity_that_was_shown()
    {
        using var w = new World();
        var table = Fixtures.RoomFeatures(w.Collection.Id);
        table.ResultSets[0].Entries.Add(new TableEntry { Min = 60, Max = 68, Text = "Rotting damp" });
        w.Db.SaveTable(table);
        w.Dice.Value = 68;
        w.Start();

        var session = w.Open("Room Features");
        session.RollCommand.Execute(null);

        var stored = Assert.Single(w.Main.RecentRolls).Item.ResultText;
        Assert.Equal("Ambient: Ambiguous: \"Smell of burning flesh\" (66–70) and \"Rotting damp\" (60–68) both cover 68.", stored.Split('\n')[0]);
        Assert.Equal(session.Results[0].Text, stored.Split('\n')[0]["Ambient: ".Length..]);   // exactly what the roll screen showed
    }

    // ---- snapshots stay put -------------------------------------------------------------------

    [Fact]
    public void Editing_or_renaming_the_table_later_never_changes_recent_rolls()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Dice.Value = 12;
        w.Start();
        w.Open("Scavenged Items").RollCommand.Execute(null);         // "D4 rations"
        var before = Assert.Single(w.Main.RecentRolls).Item;

        w.Main.SelectedTable = w.Summary("Scavenged Items");
        w.Main.EditTableCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;
        review.TableName = "Salvage";
        review.Rows[2].Text = "Three days of food";
        review.SaveCommand.Execute(null);

        var after = Assert.Single(w.Main.RecentRolls).Item;
        Assert.Equal(before, after);
        Assert.Equal(("Scavenged Items", "D4 rations"), (after.TableName, after.ResultText));
        Assert.Equal(w.Summary("Salvage").Id, after.TableId);          // still attached to the same table...
        Assert.Contains("Salvage", w.Recent());                        // ...whose current name shows in Recent Tables
        Assert.Equal(after, w.Db.GetRollHistory().Single());           // and the stored row itself is unchanged
    }

    [Fact]
    public void Deleting_the_table_keeps_the_snapshot_readable_but_not_openable()
    {
        using var w = new World();
        var (items, _) = Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Dice.Value = 12;
        w.Start();
        w.Open("Scavenged Items").RollCommand.Execute(null);
        var entry = Assert.Single(w.Main.RecentRolls);
        Assert.True(w.Main.OpenRecentRollCommand.CanExecute(entry));

        w.Main.SelectedTable = w.Summary("Scavenged Items");
        w.Main.DeleteTableCommand.Execute(null);

        var kept = Assert.Single(w.Main.RecentRolls);
        Assert.True(kept.IsDeleted);
        Assert.Equal(("Scavenged Items", "D4 rations", "12"), (kept.TableName, kept.Item.ResultText, kept.RollDisplay));
        Assert.Contains("deleted", kept.FullText);
        Assert.False(w.Main.OpenRecentRollCommand.CanExecute(kept));
        Assert.Null(w.Db.GetRollHistory().Single().TableId);
        Assert.Null(w.Db.LoadTable(items.Id));
    }

    // ---- retention and opening ----------------------------------------------------------------

    [Fact]
    public void History_never_exceeds_ten_rolls()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenged Items");

        for (var i = 1; i <= 15; i++)
        {
            w.Dice.Value = i;
            roll.RollCommand.Execute(null);
            Assert.True(w.Main.RecentRolls.Count <= 10);
        }

        Assert.Equal(10, w.Main.RecentRolls.Count);
        Assert.Equal(10, w.Db.GetRollHistory(100).Count);
        Assert.Equal(15, w.Main.RecentRolls[0].Item.RollValue);      // newest first
        Assert.Equal(6, w.Main.RecentRolls[^1].Item.RollValue);      // and the oldest five have aged out
    }

    [Fact]
    public void Clicking_a_recent_roll_reopens_the_table_fresh_without_restoring_any_trail()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenging");
        roll.RollCommand.Execute(null);
        roll.Results[0].FollowCommand!.Execute(null);
        roll.RollCommand.Execute(null);
        Assert.Equal(2, roll.Steps.Count);

        var parentEntry = w.Main.RecentRolls.Single(r => r.TableName == "Scavenging");
        w.Main.OpenRecentRollCommand.Execute(parentEntry);

        var reopened = Assert.IsType<RollViewModel>(w.Main.Current);
        Assert.NotSame(roll, reopened);
        Assert.Equal("Scavenging", reopened.Title);
        Assert.Single(reopened.Steps);                               // no linked trail restored
        Assert.Empty(reopened.Current.Outcomes);                     // and no historical result restored
        Assert.Equal(2, w.Main.RecentRolls.Count);                   // opening rolled nothing
    }

    [Fact]
    public void A_recent_roll_from_another_collection_switches_to_that_collection_and_opens_its_table()
    {
        using var w = new World();
        var other = w.Db.CreateCollection("Wilds");
        w.Db.SaveTable(Fixtures.ScavengedItems(other.Id));
        w.Start();
        w.Main.SelectedCollection = w.Main.Collections.Single(c => c.Name == "Wilds");
        w.Open("Scavenged Items").RollCommand.Execute(null);
        w.Main.SelectedCollection = w.Main.Collections.Single(c => c.Name == "Dungeon");
        Assert.Empty(w.Main.Tables);

        w.Main.OpenRecentRollCommand.Execute(w.Main.RecentRolls.Single());

        Assert.Equal("Wilds", w.Main.SelectedCollection!.Name);
        Assert.Equal("Scavenged Items", ((RollViewModel)w.Main.Current!).Title);
        Assert.Equal("Scavenged Items", w.Main.SelectedTable!.Name);
    }

    [Fact]
    public void A_failure_to_write_history_never_stops_the_roll()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenging");
        using (var raw = new SqliteConnection($"Data Source={w.Temp.Path};Pooling=False"))
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER NoHistory BEFORE INSERT ON RollHistory BEGIN SELECT RAISE(ABORT, 'history unavailable'); END;";
            cmd.ExecuteNonQuery();
        }

        roll.RollCommand.Execute(null);

        Assert.Equal("1x Scavenged Item", Assert.Single(roll.Results).Text);   // the user still gets their result
        Assert.Contains("Could not record the roll", w.Main.Status);
        Assert.Empty(w.Main.RecentRolls);
    }

    // ---- recent tables ------------------------------------------------------------------------

    [Fact]
    public void Tables_become_recent_when_opened_and_listing_or_searching_alone_does_not_count()
    {
        using var w = new World();
        SaveTables(w, 3);
        w.Start();
        Assert.Empty(w.Main.RecentTables);
        Assert.False(w.Main.HasRecentTables);

        w.Main.TableFilter = "Table";                                   // the search shows all three, opening none
        Assert.Equal(3, w.Main.Tables.Count);
        Assert.Empty(w.Main.RecentTables);

        w.Open("Table 2");

        Assert.Equal(["Table 2"], w.Recent());
        Assert.True(w.Main.HasRecentTables);
    }

    [Fact]
    public void Reopening_a_table_moves_it_to_the_front_without_duplicating_it()
    {
        using var w = new World();
        SaveTables(w, 3);
        w.Start();

        w.Open("Table 1");
        w.Open("Table 2");
        w.Open("Table 3");
        w.Open("Table 1");

        Assert.Equal(["Table 1", "Table 3", "Table 2"], w.Recent());
    }

    [Fact]
    public void Recent_tables_are_limited_to_five()
    {
        using var w = new World();
        SaveTables(w, 7);
        w.Start();

        for (var i = 1; i <= 7; i++) w.Open($"Table {i}");

        Assert.Equal(["Table 7", "Table 6", "Table 5", "Table 4", "Table 3"], w.Recent());
    }

    [Fact]
    public void Recent_tables_belong_to_their_collection()
    {
        using var w = new World();
        SaveTables(w, 2);
        var other = w.Db.CreateCollection("Wilds");
        w.Db.SaveTable(Fixtures.ScavengedItems(other.Id).Also(t => t.Name = "Wild Table"));
        w.Start();
        w.Open("Table 1");

        w.Main.SelectedCollection = w.Main.Collections.Single(c => c.Name == "Wilds");
        Assert.Empty(w.Main.RecentTables);                              // A's recents do not appear in B
        w.Open("Wild Table");
        Assert.Equal(["Wild Table"], w.Recent());

        w.Main.SelectedCollection = w.Main.Collections.Single(c => c.Name == "Dungeon");
        Assert.Equal(["Table 1"], w.Recent());
    }

    [Fact]
    public void A_renamed_table_shows_its_new_name_in_recent_tables()
    {
        using var w = new World();
        SaveTables(w, 2);
        w.Start();
        w.Open("Table 1");
        w.Open("Table 2");

        w.Main.SelectedTable = w.Summary("Table 1");
        w.Main.EditTableCommand.Execute(null);
        ((ReviewViewModel)w.Main.Current!).TableName = "Renamed";
        ((ReviewViewModel)w.Main.Current!).SaveCommand.Execute(null);

        Assert.Equal(["Renamed", "Table 2"], w.Recent());              // saving it is use (it opens for rolling), under the new name
    }

    [Fact]
    public void Deleting_a_table_removes_it_from_recent_tables()
    {
        using var w = new World();
        SaveTables(w, 3);
        w.Start();
        w.Open("Table 1");
        w.Open("Table 2");
        w.Open("Table 3");

        w.Main.SelectedTable = w.Summary("Table 2");
        w.Main.DeleteTableCommand.Execute(null);

        Assert.Equal(["Table 3", "Table 1"], w.Recent());
    }

    [Fact]
    public void Following_a_link_makes_the_destination_recent_and_the_recent_list_reopens_tables_fresh()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();
        var roll = w.Open("Scavenging");
        roll.RollCommand.Execute(null);
        roll.Results[0].FollowCommand!.Execute(null);

        Assert.Equal(["Scavenged Items", "Scavenging"], w.Recent());

        w.Main.OpenRecentTableCommand.Execute(w.Main.RecentTables[1]);   // back to Scavenging, from the recent list

        var reopened = Assert.IsType<RollViewModel>(w.Main.Current);
        Assert.Equal("Scavenging", reopened.Title);
        Assert.Single(reopened.Steps);
        Assert.Empty(reopened.Current.Outcomes);
        Assert.Equal(["Scavenging", "Scavenged Items"], w.Recent());
    }

    [Fact]
    public void Saving_a_new_table_and_rolling_it_puts_it_at_the_front_of_recents_and_history()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.ScavengedItems(w.Collection.Id));
        var main = w.Start();
        w.Open("Scavenged Items");

        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "d6 Loot\n1-6 Coin";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        ((ReviewViewModel)main.Current!).SaveCommand.Execute(null);
        ((RollViewModel)main.Current!).RollCommand.Execute(null);

        Assert.Equal(["Loot", "Scavenged Items"], w.Recent());
        Assert.Equal("Loot", main.RecentRolls[0].TableName);
    }
}
