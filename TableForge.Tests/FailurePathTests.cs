using Microsoft.Data.Sqlite;
using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Primary failure paths fail visibly and safely: nothing is guessed, no data is lost, and play is not blocked.</summary>
public class FailurePathTests
{
    private static void Exec(string dbPath, string sql)
    {
        using var raw = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static MainViewModel NewMain(TempDatabase temp, out AppDatabase db, out Collection collection, int roll = 6)
    {
        db = temp.Open();
        collection = db.CreateCollection("Dungeon");
        return new MainViewModel(db, new FixedDice(roll), (_, _) => true);
    }

    // ---- save ---------------------------------------------------------------------------------

    [Fact]
    public void A_failed_save_says_so_keeps_every_edit_on_screen_and_stores_nothing()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out var db, out _);
        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = Fixtures.RandomStartingGear;
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)main.Current!;
        review.TableName = "My corrected name";
        review.Rows[0].Text = "Backpack (corrected)";
        Exec(temp.Path, "CREATE TRIGGER NoSave BEFORE INSERT ON Entries BEGIN SELECT RAISE(ABORT, 'disk said no'); END;");

        review.SaveCommand.Execute(null);                      // must not throw

        Assert.StartsWith("Could not save:", review.SaveError);
        Assert.Contains("Your edits are still here", review.SaveError);
        Assert.Same(review, main.Current);                     // still on the Review screen
        Assert.Equal(("My corrected name", "Backpack (corrected)"), (review.TableName, review.Rows[0].Text));
        Assert.True(review.CanSave);
        Assert.Empty(db.GetTableSummaries(main.SelectedCollection!.Id));   // the half-written table was rolled back

        Exec(temp.Path, "DROP TRIGGER NoSave");
        review.SaveCommand.Execute(null);                      // and once the cause is gone, the same screen saves fine
        Assert.IsType<RollViewModel>(main.Current);
        Assert.Equal("My corrected name", Assert.Single(main.Tables).Name);
    }

    [Fact]
    public void A_table_that_cannot_be_built_is_never_sent_to_the_database_and_says_why()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out var db, out _);
        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "d6 Loot\n1-3 Coin\n4-6 Gem";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)main.Current!;

        review.DiceText = "d6*2";                             // an unsupported dice expression
        Assert.False(review.SaveCommand.CanExecute(null));
        Assert.Contains(review.Blockers, b => b.Contains("d6*2") && b.Contains("not supported") && b.Contains("2d6+1"));
        review.Rows[0].RangeText = "banana";
        Assert.Contains(review.Blockers, b => b.Contains("Row 1"));

        review.SaveCommand.Execute(null);                      // even forced, nothing is written
        Assert.Empty(db.GetTableSummaries(main.SelectedCollection!.Id));
    }

    // ---- other database operations ------------------------------------------------------------

    [Fact]
    public void A_failed_delete_is_reported_rolled_back_and_leaves_the_app_as_it_was()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out var db, out var collection);
        var (items, scavenging) = Fixtures.SeedScavenging(db, collection.Id);
        main = new MainViewModel(db, new FixedDice(6), (_, _) => true);
        main.SelectedTable = main.Tables.Single(t => t.Name == "Scavenged Items");
        var openBefore = main.Current;
        Exec(temp.Path, "CREATE TRIGGER NoDelete BEFORE DELETE ON Tables BEGIN SELECT RAISE(ABORT, 'delete blocked'); END;");

        main.DeleteTableCommand.Execute(null);                 // must not throw

        Assert.StartsWith("Could not delete the table:", main.Status);
        Assert.Same(openBefore, main.Current);                 // nothing was closed or reset
        Assert.NotNull(db.LoadTable(items.Id));
        Assert.Equal(items.Id, db.LoadTable(scavenging.Id)!.ResultSets[0].Entries[1].LinkedTableId); // links not half-converted
        Assert.Equal(2, main.Tables.Count);
    }

    [Fact]
    public void Commands_that_hit_a_broken_database_report_in_the_status_bar_instead_of_crashing()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out var db, out var collection);
        db.SaveTable(Fixtures.ScavengedItems(collection.Id));
        main = new MainViewModel(db, new FixedDice(6), (_, _) => true);
        var summary = main.Tables[0];
        db.Dispose();                                          // the database goes away underneath the running app

        main.OpenRecentTableCommand.Execute(summary);
        Assert.StartsWith("Could not open the table:", main.Status);

        main.NewCollectionName = "New one";
        main.CreateCollectionCommand.Execute(null);
        Assert.StartsWith("Could not create the collection:", main.Status);

        main.HighlightedTable = summary;
        main.EditTableCommand.Execute(null);
        Assert.StartsWith("Could not open the table for editing:", main.Status);
    }

    [Fact]
    public void A_history_write_failure_never_blocks_the_roll()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out var db, out var collection);
        Fixtures.SeedScavenging(db, collection.Id);
        main = new MainViewModel(db, new FixedDice(9), (_, _) => true);
        main.SelectedTable = main.Tables.Single(t => t.Name == "Scavenging");
        var roll = (RollViewModel)main.Current!;
        Exec(temp.Path, "CREATE TRIGGER NoHistory BEFORE INSERT ON RollHistory BEGIN SELECT RAISE(ABORT, 'no history'); END;");

        roll.RollCommand.Execute(null);
        roll.ManualRollText = "6";
        roll.ResolveManualCommand.Execute(null);

        Assert.Equal("1x Scavenged Item", Assert.Single(roll.Results).Text);   // the second roll worked too
        Assert.Equal(2, roll.Current.Outcomes.Count);
        Assert.Contains("Could not record the roll", main.Status);
        Assert.Empty(db.GetRollHistory());
    }

    // ---- play-time surprises ------------------------------------------------------------------

    [Fact]
    public void An_unreadable_link_destination_is_shown_as_unavailable_and_the_roll_still_happens()
    {
        var table = Fixtures.Scavenging(1, itemsTableId: 42);
        var session = new RollViewModel(table, new FixedDice(6), loadTable: _ => throw new InvalidOperationException("db went away"));

        session.RollCommand.Execute(null);

        var line = Assert.Single(session.Results);
        Assert.Equal("1x Scavenged Item", line.Text);
        Assert.Equal(LinkState.Missing, line.Link);
        Assert.Null(line.FollowCommand);
    }

    [Fact]
    public void Illegal_manual_rolls_of_every_kind_are_refused_without_a_result_or_a_history_record()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out var db, out var collection);
        db.SaveTable(Fixtures.ScavengedItems(collection.Id));
        main = new MainViewModel(db, new FixedDice(6), (_, _) => true);
        main.SelectedTable = main.Tables[0];
        var roll = (RollViewModel)main.Current!;

        foreach (var bad in new[] { "", " ", "0", "21", "-1", "1.5", "1e1", "twenty", "５", "1 2", "9999999999999" })
        {
            roll.ManualRollText = bad;
            roll.ResolveManualCommand.Execute(null);
            Assert.Equal("Enter a whole number from 1 to 20.", roll.Message);
        }

        Assert.Empty(roll.Current.Outcomes);
        Assert.Empty(db.GetRollHistory());
    }

    [Fact]
    public void A_pasted_table_with_nothing_understandable_cannot_be_saved_and_explains_itself()
    {
        using var temp = new TempDatabase();
        var main = NewMain(temp, out _, out _);
        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "Lorem ipsum dolor sit amet,\nconsectetur adipiscing elit.";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)main.Current!;

        Assert.False(review.CanSave);
        Assert.NotEmpty(review.Blockers);
        Assert.Contains(review.TableNotes, n => n.Contains("No numbered rows"));
    }
}
