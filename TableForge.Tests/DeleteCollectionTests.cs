using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>Delete Collection (RC25): everything in it goes, atomically, and nothing outside it is left broken.</summary>
public class DeleteCollectionTests
{
    private sealed class World : IDisposable
    {
        public TempDatabase Temp { get; } = new();
        public AppDatabase Db { get; }
        public bool Answer { get; set; } = true;
        public List<string> Confirmations { get; } = [];
        public MainViewModel Main { get; private set; } = null!;

        public World() => Db = Temp.Open();

        public MainViewModel Start() => Main = new MainViewModel(Db, new FixedDice(1), (m, _) => { Confirmations.Add(m); return Answer; });

        public void Select(string name) => Main.SelectedCollection = Main.Collections.Single(c => c.Name == name);

        public void Dispose() => Temp.Dispose();
    }

    private static long Rows(World w, string sql, long collectionId) => Count(w.Temp.Path, sql.Replace("$c", collectionId.ToString()));

    private static void AssertNothingLeft(World w, long collectionId)
    {
        Assert.Equal(0, Rows(w, "SELECT COUNT(*) FROM Collections WHERE Id = $c", collectionId));
        Assert.Equal(0, Rows(w, "SELECT COUNT(*) FROM Folders WHERE CollectionId = $c", collectionId));
        Assert.Equal(0, Rows(w, "SELECT COUNT(*) FROM Tables WHERE CollectionId = $c", collectionId));
        Assert.Equal(0, Count(w.Temp.Path, "SELECT COUNT(*) FROM ResultSets WHERE TableId NOT IN (SELECT Id FROM Tables)"));
        Assert.Equal(0, Count(w.Temp.Path, "SELECT COUNT(*) FROM Entries WHERE ResultSetId NOT IN (SELECT Id FROM ResultSets)"));
        using var raw = Raw(w.Temp.Path);
        using var check = raw.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check";
        using var reader = check.ExecuteReader();
        Assert.False(reader.Read(), "the database has a broken reference");
    }

    // ---- the database ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Deleting_removes_the_collection_its_folders_tables_result_sets_and_entries_including_links_self_links_and_cycles()
    {
        using var w = new World();
        var mythic = SeedMythic(w.Db);
        var other = SeedOther(w.Db);
        var otherBefore = Describe(w.Db, other.Id);

        Assert.True(w.Db.DeleteCollection(mythic.Id));

        AssertNothingLeft(w, mythic.Id);
        Assert.Equal(otherBefore, Describe(w.Db, other.Id));
        Assert.Equal(["Other Game"], w.Db.GetCollections().Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Deleting_a_collection_that_does_not_exist_changes_nothing()
    {
        using var w = new World();
        SeedOther(w.Db);
        var before = Snapshot(w.Temp.Path);
        Assert.False(w.Db.DeleteCollection(9999));
        Assert.Equal(before, Snapshot(w.Temp.Path));
    }

    [Fact]
    public void Recent_rolls_from_its_tables_stay_as_readable_records_without_a_table()
    {
        using var w = new World();
        var mythic = SeedMythic(w.Db);
        var other = SeedOther(w.Db);
        var weather = w.Db.GetTableSummaries(mythic.Id).Single(t => t.Name == "Weather");
        var towns = w.Db.GetTableSummaries(other.Id).Single(t => t.Name == "Towns");
        w.Db.AddRollHistory(new RollSnapshot(weather.Id, "Weather", "d20", 5, "Clear skies"));
        w.Db.AddRollHistory(new RollSnapshot(towns.Id, "Towns", "d8", 6, "Bridge"));

        w.Db.DeleteCollection(mythic.Id);

        var history = w.Db.GetRollHistory();
        Assert.Equal(2, history.Count);
        var weatherRoll = history.Single(h => h.TableName == "Weather");
        Assert.Null(weatherRoll.TableId);
        Assert.Equal("Clear skies", weatherRoll.ResultText);
        Assert.Equal(towns.Id, history.Single(h => h.TableName == "Towns").TableId);
    }

    [Fact]
    public void A_damaged_link_into_the_deleted_collection_becomes_an_unresolved_name()
    {
        using var w = new World();
        var alpha = w.Db.CreateCollection("Alpha");
        var beta = w.Db.CreateCollection("Beta");
        var target = Save(w.Db, alpha.Id, "Alpha Table", "d6", null, Set("", E(1, 6, "a")));
        var source = Save(w.Db, beta.Id, "Beta Table", "d6", null, Set("", E(1, 6, "to alpha", link: target.Id)));
        Assert.Equal(1, w.Db.CountLinksIntoCollection(alpha.Id));
        Assert.Equal(0, w.Db.CountLinksIntoCollection(beta.Id));

        w.Db.DeleteCollection(alpha.Id);

        var entry = w.Db.LoadTable(source.Id)!.ResultSets[0].Entries[0];
        Assert.Null(entry.LinkedTableId);
        Assert.Equal("Alpha Table", entry.UnresolvedLinkName);
        AssertNothingLeft(w, alpha.Id);
    }

    [Fact]
    public void A_damaged_link_out_of_the_deleted_collection_leaves_its_destination_alone()
    {
        using var w = new World();
        var alpha = w.Db.CreateCollection("Alpha");
        var beta = w.Db.CreateCollection("Beta");
        var target = Save(w.Db, beta.Id, "Beta Table", "d6", null, Set("", E(1, 6, "b")));
        Save(w.Db, alpha.Id, "Alpha Table", "d6", null, Set("", E(1, 6, "to beta", link: target.Id)));
        var betaBefore = Describe(w.Db, beta.Id);

        w.Db.DeleteCollection(alpha.Id);

        Assert.Equal(betaBefore, Describe(w.Db, beta.Id));
        AssertNothingLeft(w, alpha.Id);
    }

    [Fact]
    public void A_failure_part_way_through_deleting_changes_nothing()
    {
        using var w = new World();
        var mythic = SeedMythic(w.Db);
        SeedOther(w.Db);
        // Test-only: SQLite refuses the very last step, after the tables and folders have already been deleted in the transaction.
        Execute(w.Temp.Path, "CREATE TRIGGER fail_delete BEFORE DELETE ON Collections BEGIN SELECT RAISE(ABORT, 'forced failure'); END;");
        var before = Snapshot(w.Temp.Path);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Db.DeleteCollection(mythic.Id));

        Assert.Equal(before, Snapshot(w.Temp.Path));
    }

    // ---- through the app -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Delete_asks_with_the_name_and_table_count_and_says_it_cannot_be_undone()
    {
        using var w = new World();
        SeedMythic(w.Db, "Mythic GME (2)");
        w.Start();
        w.Answer = false;

        w.Main.DeleteCollectionCommand.Execute(null);

        var message = Assert.Single(w.Confirmations);
        Assert.StartsWith("Delete the collection \"Mythic GME (2)\" and all 12 tables in it?", message);
        Assert.Contains("Its folders are deleted too.", message);
        Assert.EndsWith("This cannot be undone.", message);
        Assert.DoesNotContain("link to its tables", message);
    }

    [Fact]
    public void Declining_deletes_nothing()
    {
        using var w = new World();
        SeedMythic(w.Db);
        w.Start();
        var before = Snapshot(w.Temp.Path);
        w.Answer = false;

        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.Equal(before, Snapshot(w.Temp.Path));
        Assert.Single(w.Main.Collections);
        Assert.Equal(12, w.Main.Tables.Count);
    }

    [Fact]
    public void The_confirmation_counts_one_table_and_an_empty_collection_plainly()
    {
        using var w = new World();
        var one = w.Db.CreateCollection("One");
        Save(w.Db, one.Id, "Only", "d6", null, Set("", E(1, 6, "x")));
        w.Db.CreateCollection("Zero");
        w.Start();
        w.Answer = false;

        w.Select("One");
        w.Main.DeleteCollectionCommand.Execute(null);
        w.Select("Zero");
        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.StartsWith("Delete the collection \"One\" and the 1 table in it?", w.Confirmations[0]);
        Assert.StartsWith("Delete the collection \"Zero\"? It has no tables.", w.Confirmations[1]);
    }

    [Fact]
    public void The_confirmation_warns_about_links_from_other_collections()
    {
        using var w = new World();
        var alpha = w.Db.CreateCollection("Alpha");
        var beta = w.Db.CreateCollection("Beta");
        var target = Save(w.Db, alpha.Id, "Alpha Table", "d6", null, Set("", E(1, 6, "a")));
        Save(w.Db, beta.Id, "Beta Table", "d6", null, Set("", E(1, 6, "to alpha", link: target.Id)));
        w.Start();
        w.Answer = false;
        w.Select("Alpha");

        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.Contains("1 entry in other collections link to its tables. They will keep the table names as unresolved links.", w.Confirmations[0]);
    }

    [Fact]
    public void After_deleting_the_neighbouring_collection_is_selected_and_the_lists_refresh()
    {
        using var w = new World();
        SeedOther(w.Db, "A Game");
        SeedMythic(w.Db, "B Game");
        SeedOther(w.Db, "C Game");
        w.Start();
        w.Select("B Game");
        w.Main.TableFilter = "";
        w.Main.OpenFirstMatchCommand.Execute(null);
        Assert.NotNull(w.Main.Current);

        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.Equal(["A Game", "C Game"], w.Main.Collections.Select(c => c.Name).ToArray());
        Assert.Equal("C Game", w.Main.SelectedCollection!.Name);
        Assert.Equal(["Names", "Towns"], w.Main.Tables.Select(t => t.Name).ToArray());
        Assert.Null(w.Main.Current);
        Assert.Equal("Deleted the collection \"B Game\".", w.Main.Status);
    }

    [Fact]
    public void Deleting_another_collection_leaves_an_open_edit_of_this_one_alone()
    {
        using var w = new World();
        SeedOther(w.Db, "Keep");
        SeedMythic(w.Db, "Remove");
        w.Start();
        w.Select("Keep");
        w.Main.HighlightedTable = w.Main.Tables.Single(t => t.Name == "Towns");
        w.Main.EditTableCommand.Execute(null);
        var review = Assert.IsType<ReviewViewModel>(w.Main.Current);

        w.Select("Remove");
        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.Same(review, w.Main.Current);
        Assert.Equal(["Keep"], w.Main.Collections.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Recent_rolls_from_the_deleted_tables_can_no_longer_be_opened()
    {
        using var w = new World();
        var mythic = SeedMythic(w.Db);
        SeedOther(w.Db);
        w.Start();
        w.Select("Mythic GME");
        w.Main.OpenFirstMatchCommand.Execute(null);
        var roll = Assert.IsType<RollViewModel>(w.Main.Current);
        roll.RollCommand.Execute(null);
        Assert.False(Assert.Single(w.Main.RecentRolls).IsDeleted);

        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.True(Assert.Single(w.Main.RecentRolls).IsDeleted);
        Assert.False(w.Main.OpenRecentRollCommand.CanExecute(w.Main.RecentRolls[0]));
        Assert.Empty(w.Main.RecentTables);
        AssertNothingLeft(w, mythic.Id);
    }

    [Fact]
    public void Deleting_the_last_collection_leaves_a_valid_empty_app()
    {
        using var w = new World();
        SeedMythic(w.Db);
        w.Start();

        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.Empty(w.Main.Collections);
        Assert.Null(w.Main.SelectedCollection);
        Assert.Empty(w.Main.Tables);
        Assert.Empty(w.Main.RecentTables);
        Assert.StartsWith("Welcome to Gidubba.", w.Main.EmptyStateText);
        Assert.Equal("Deleted the collection \"Mythic GME\". Create a collection to begin.", w.Main.Status);
        Assert.False(w.Main.DeleteCollectionCommand.CanExecute(null));
        Assert.False(w.Main.ExportCollectionCommand.CanExecute(null));
        Assert.False(w.Main.PasteTableCommand.CanExecute(null));

        // And the app carries on normally from there.
        w.Main.NewCollectionName = "Fresh";
        w.Main.CreateCollectionCommand.Execute(null);
        Assert.Equal("Fresh", w.Main.SelectedCollection!.Name);
    }

    [Fact]
    public void An_imported_collection_can_be_deleted_again_leaving_the_library_as_it_was()
    {
        using var w = new World();
        SeedOther(w.Db);
        using var sourceTemp = new TempDatabase();
        byte[] file;
        using (var source = sourceTemp.Open()) file = ExportBytes(source, SeedMythic(source));
        var before = Snapshot(w.Temp.Path);

        var imported = w.Db.ImportCollection(Read(file));
        w.Db.DeleteCollection(imported.Id);

        // Only SQLite's id counters remember it: ids are never reused.
        Assert.Equal(Strip(before), Strip(Snapshot(w.Temp.Path)));

        static string Strip(string snapshot) => snapshot[..snapshot.IndexOf("[sqlite_sequence]", StringComparison.Ordinal)];
    }
}
