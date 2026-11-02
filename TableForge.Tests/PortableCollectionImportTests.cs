using System.IO;
using Microsoft.Data.Sqlite;
using TableForge.Data;
using TableForge.Domain;
using TableForge.Portable;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>Import is all or nothing, always a new Collection, named without colliding; and Export refuses what it cannot carry.</summary>
public class PortableCollectionImportTests
{
    /// <summary>A source database with "Mythic GME" exported to a file, and a target database to import it into through the app.</summary>
    private sealed class World : IDisposable
    {
        public TempDatabase SourceTemp { get; } = new();
        public TempDatabase Temp { get; } = new();
        public TempFile File { get; } = new();
        public AppDatabase Db { get; }
        public List<string> Asked { get; } = [];
        public List<string> Shown { get; } = [];
        public bool Answer { get; set; } = true;
        public MainViewModel Main { get; private set; } = null!;

        public World(Action<AppDatabase>? seed = null)
        {
            using (var source = SourceTemp.Open())
                System.IO.File.WriteAllBytes(File.Path, ExportBytes(source, SeedMythic(source)));
            Db = Temp.Open();
            seed?.Invoke(Db);
            Start();
        }

        public MainViewModel Start() => Main = new MainViewModel(Db, new FixedDice(1), (_, _) => true,
            chooseImportFile: () => File.Path, confirmImport: m => { Asked.Add(m); return Answer; }, showMessage: Shown.Add);

        public void Import() => Main.ImportCollectionCommand.Execute(null);

        public void Dispose()
        {
            SourceTemp.Dispose();
            Temp.Dispose();
            File.Dispose();
        }
    }

    // ---- through the app --------------------------------------------------------------------------------------------------

    [Fact]
    public void Import_asks_first_with_the_name_counts_and_the_promise_then_selects_the_new_collection()
    {
        using var w = new World(db => SeedOther(db));
        w.Import();

        var message = Assert.Single(w.Asked);
        Assert.Contains("\"Mythic GME\"", message);
        Assert.Contains("3 folders, 12 tables.", message);
        Assert.Contains("This will create a new Collection. Existing Collections will not be changed.", message);

        Assert.Equal("Mythic GME", w.Main.SelectedCollection!.Name);
        Assert.Equal(["Mythic GME", "Other Game"], w.Main.Collections.Select(c => c.Name).ToArray()); // the database's order
        Assert.Equal(12, w.Main.Tables.Count);
        Assert.Equal("Imported \"Mythic GME\" (12 tables).", w.Main.Status);
        Assert.Empty(w.Shown);
    }

    [Fact]
    public void Cancelling_the_confirmation_changes_nothing()
    {
        using var w = new World(db => SeedOther(db));
        var before = Snapshot(w.Temp.Path);
        w.Answer = false;

        w.Import();

        Assert.Single(w.Asked);
        Assert.Equal(before, Snapshot(w.Temp.Path));
        Assert.Equal("Other Game", w.Main.SelectedCollection!.Name);
    }

    [Fact]
    public void Cancelling_the_file_choice_does_nothing()
    {
        using var w = new World();
        var main = new MainViewModel(w.Db, new FixedDice(1), (_, _) => true, chooseImportFile: () => null, confirmImport: m => { w.Asked.Add(m); return true; });
        main.ImportCollectionCommand.Execute(null);
        Assert.Empty(w.Asked);
        Assert.Empty(w.Db.GetCollections());
    }

    [Fact]
    public void Import_leaves_an_unsaved_paste_alone_as_choosing_a_collection_does()
    {
        using var w = new World(db => SeedOther(db));
        w.Main.PasteTableCommand.Execute(null);
        var paste = Assert.IsType<PasteViewModel>(w.Main.Current);
        paste.SourceText = "d6 Draft 1-6 x";

        w.Import();

        Assert.Same(paste, w.Main.Current);
        Assert.Equal("d6 Draft 1-6 x", paste.SourceText);
        Assert.Equal("Mythic GME", w.Main.SelectedCollection!.Name);
    }

    [Fact]
    public void Import_works_into_an_empty_library()
    {
        using var w = new World();
        Assert.Empty(w.Main.Collections);
        w.Import();
        Assert.Equal("Mythic GME", Assert.Single(w.Main.Collections).Name);
        Assert.Equal(w.Main.Collections[0], w.Main.SelectedCollection);
    }

    // ---- names --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Same_name_imports_get_the_lowest_free_suffix_and_are_independent_collections()
    {
        using var w = new World(db => db.CreateCollection("Mythic GME"));
        w.Import();
        Assert.Contains("A collection named \"Mythic GME\" already exists, so this one will be called \"Mythic GME (2)\".", w.Asked[^1]);
        Assert.Equal("Mythic GME (2)", w.Main.SelectedCollection!.Name);

        w.Import();
        Assert.Equal("Mythic GME (3)", w.Main.SelectedCollection!.Name);

        var collections = w.Db.GetCollections();
        var second = collections.Single(c => c.Name == "Mythic GME (2)");
        var third = collections.Single(c => c.Name == "Mythic GME (3)");
        Assert.Equal(Describe(w.Db, second.Id, includeName: false), Describe(w.Db, third.Id, includeName: false));
        Assert.Empty(w.Db.GetTableSummaries(second.Id).Select(t => t.Id).Intersect(w.Db.GetTableSummaries(third.Id).Select(t => t.Id)));

        // Independent: deleting one copy's table leaves the other copy's links working.
        var weatherInSecond = w.Db.GetTableSummaries(second.Id).Single(t => t.Name == "Weather").Id;
        w.Db.DeleteTable(weatherInSecond);
        var encounter = w.Db.LoadTable(w.Db.GetTableSummaries(third.Id).Single(t => t.Name == "Encounter").Id)!;
        Assert.Equal("Weather", w.Db.LoadTable(encounter.ResultSets[0].Entries[1].LinkedTableId!.Value)!.Name);
    }

    [Fact]
    public void Names_are_compared_ignoring_capitals_and_a_suffix_is_not_read_specially()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        db.CreateCollection("X");
        db.CreateCollection("X (2)");
        db.CreateCollection("x (3)");

        Assert.Equal("x (4)", db.UniqueCollectionName("x"));
        Assert.Equal("X (2) (2)", db.UniqueCollectionName("X (2)"));
        Assert.Equal("Y", db.UniqueCollectionName("Y"));
    }

    [Fact]
    public void The_imported_name_is_made_unique_inside_the_import_itself()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        db.CreateCollection("mythic gme");
        var portable = new PortableCollection("Mythic GME", [], []);
        Assert.Equal("Mythic GME (2)", db.ImportCollection(portable).Name);
        Assert.Equal("Mythic GME (3)", db.ImportCollection(portable).Name);
    }

    // ---- all or nothing ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_failure_part_way_through_the_import_leaves_the_database_exactly_as_it_was()
    {
        using var w = new World(db => SeedOther(db));
        // Test-only: make SQLite itself fail on one of the LAST rows the import writes (the second Rumors table is near the end).
        Execute(w.Temp.Path, "CREATE TRIGGER fail_import BEFORE INSERT ON Entries WHEN NEW.DisplayText = 'The bridge is out' BEGIN SELECT RAISE(ABORT, 'forced failure'); END;");
        var before = Snapshot(w.Temp.Path);

        w.Import();

        Assert.Equal(before, Snapshot(w.Temp.Path));
        Assert.Equal(["Other Game"], w.Db.GetCollections().Select(c => c.Name).ToArray());
        Assert.Equal(1, Count(w.Temp.Path, "SELECT COUNT(*) FROM Collections"));
        Assert.Equal(1, Count(w.Temp.Path, "SELECT COUNT(*) FROM Folders"));
        Assert.Equal(2, Count(w.Temp.Path, "SELECT COUNT(*) FROM Tables"));
        Assert.Equal("The collection could not be imported. Nothing was imported.", w.Main.Status);
        Assert.Contains("forced failure", Assert.Single(w.Shown));
        Assert.Equal(["Other Game"], w.Main.Collections.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void A_failure_inserting_the_tables_rolls_back_the_collection_and_folders_too()
    {
        using var w = new World();
        Execute(w.Temp.Path, "CREATE TRIGGER fail_import BEFORE INSERT ON Tables WHEN NEW.Name = 'Pong' BEGIN SELECT RAISE(ABORT, 'forced failure'); END;");
        var before = Snapshot(w.Temp.Path);

        var portable = PortableCollectionReader.ReadFile(w.File.Path).Collection!;
        Assert.Throws<SqliteException>(() => w.Db.ImportCollection(portable));

        Assert.Equal(before, Snapshot(w.Temp.Path));
        Assert.Equal(0, Count(w.Temp.Path, "SELECT COUNT(*) FROM Collections"));
        Assert.Equal(0, Count(w.Temp.Path, "SELECT COUNT(*) FROM Entries"));
    }

    [Fact]
    public void After_a_failed_import_the_same_file_imports_cleanly()
    {
        using var w = new World();
        Execute(w.Temp.Path, "CREATE TRIGGER fail_import BEFORE INSERT ON Entries WHEN NEW.DisplayText = 'Pong row' BEGIN SELECT RAISE(ABORT, 'forced failure'); END;");
        w.Import();
        Assert.Empty(w.Db.GetCollections());

        Execute(w.Temp.Path, "DROP TRIGGER fail_import;");
        w.Import();
        Assert.Equal("Mythic GME", Assert.Single(w.Db.GetCollections()).Name); // no "(2)": the failed attempt left nothing behind
    }

    [Fact]
    public void Import_leaves_no_broken_references_and_no_recent_use_or_history()
    {
        using var w = new World(db => SeedOther(db));
        w.Import();
        using (var raw = Raw(w.Temp.Path))
        using (var check = raw.CreateCommand())
        {
            check.CommandText = "PRAGMA foreign_key_check";
            using var reader = check.ExecuteReader();
            Assert.False(reader.Read());
        }
        var imported = w.Main.SelectedCollection!;
        Assert.Empty(w.Db.GetRecentTables(imported.Id));
        Assert.Empty(w.Db.GetRollHistory());
    }

    // ---- export, through the app --------------------------------------------------------------------------------------------

    [Fact]
    public void Export_writes_the_file_where_chosen_with_a_suggested_name()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        SeedMythic(db, "Mythic: GME?");
        using var file = new TempFile();
        SaveFileRequest? asked = null;
        var main = new MainViewModel(db, new FixedDice(1), chooseCollectionFile: r => { asked = r; return file.Path; });

        main.ExportCollectionCommand.Execute(null);

        Assert.Equal("Export Collection", asked!.Title);
        Assert.Equal("Mythic_ GME_.tfcollection", asked.SuggestedName);
        Assert.Equal(ExportBytes(db, main.SelectedCollection!), File.ReadAllBytes(file.Path));
        Assert.Equal($"Exported \"Mythic: GME?\" to {Path.GetFileName(file.Path)}.", main.Status);
    }

    [Fact]
    public void Cancelling_the_export_file_choice_writes_nothing()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        SeedOther(db);
        var main = new MainViewModel(db, new FixedDice(1), chooseCollectionFile: _ => null);
        main.ExportCollectionCommand.Execute(null);
        Assert.Equal("", main.Status);
    }

    [Fact]
    public void An_empty_collection_exports_and_imports()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var empty = db.CreateCollection("Empty");
        var imported = db.ImportCollection(Read(ExportBytes(db, empty)));
        Assert.Equal("Empty (2)", imported.Name);
        Assert.Empty(db.GetTableSummaries(imported.Id));
    }

    // ---- export refusals ----------------------------------------------------------------------------------------------------

    private static (TempDatabase Temp, AppDatabase Db, Collection A, RollableTable Source) LinkedAcross()
    {
        var temp = new TempDatabase();
        var db = temp.Open();
        var a = db.CreateCollection("Alpha");
        var b = db.CreateCollection("Beta");
        var target = Save(db, b.Id, "Beta Table", "d6", null, Set("", E(1, 6, "b")));
        // The UI cannot make this, but the schema allows it: a row in Alpha linking to a table in Beta.
        var source = Save(db, a.Id, "Alpha Table", "d6", null, Set("", E(1, 3, "ok"), E(4, 6, "to beta", link: target.Id)));
        return (temp, db, a, source);
    }

    [Fact]
    public void Export_refuses_a_link_to_a_table_in_another_collection_and_says_where()
    {
        var (temp, db, a, _) = LinkedAcross();
        using (temp)
        {
            var export = PortableCollectionExporter.Export(db, a);
            Assert.False(export.Succeeded);
            var problem = Assert.Single(export.Problems);
            Assert.Contains("Table \"Alpha Table\", row 2 (4–6)", problem);
            Assert.Contains("\"Beta Table\" in the collection \"Beta\"", problem);
        }
    }

    [Fact]
    public void Export_refuses_a_link_to_a_table_that_no_longer_exists()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = db.CreateCollection("C");
        var table = Save(db, c.Id, "Source", "d6", null, Set("", E(1, 6, "x")));
        Execute(temp.Path, "PRAGMA foreign_keys = OFF; UPDATE Entries SET LinkedTableId = 99999;"); // a damaged database

        var export = PortableCollectionExporter.Export(db, c);

        Assert.False(export.Succeeded);
        Assert.Contains("Table \"Source\", row 1 (1–6) links to a table that no longer exists", Assert.Single(export.Problems));
    }

    [Fact]
    public void Export_refuses_a_written_range_that_does_not_match_its_numbers_and_names_the_row()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = db.CreateCollection("C");
        Save(db, c.Id, "Odd", "d20", null, Set("", E(1, 20, "x", display: "1–5")));

        var export = PortableCollectionExporter.Export(db, c);

        Assert.False(export.Succeeded);
        Assert.Contains("Table \"Odd\", row 1: its written range \"1–5\" means 1–5, not the 1–20", Assert.Single(export.Problems));
    }

    [Fact]
    public void A_refused_export_asks_for_no_file_and_shows_why()
    {
        var (temp, db, _, _) = LinkedAcross();
        using (temp)
        {
            var asked = false;
            var shown = new List<string>();
            var main = new MainViewModel(db, new FixedDice(1), chooseCollectionFile: _ => { asked = true; return null; }, showMessage: shown.Add);
            main.SelectedCollection = main.Collections.Single(c => c.Name == "Alpha");

            main.ExportCollectionCommand.Execute(null);

            Assert.False(asked);
            Assert.Equal("\"Alpha\" can't be exported to a Collection file. Nothing was saved.", main.Status);
            Assert.Contains("in the collection \"Beta\"", Assert.Single(shown));
        }
    }

    [Fact]
    public void Export_refuses_a_row_with_both_a_link_and_an_unresolved_name_rather_than_dropping_one()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = db.CreateCollection("C");
        var target = Save(db, c.Id, "Target", "d6", null, Set("", E(1, 6, "x")));
        Save(db, c.Id, "Source", "d6", null, Set("", E(1, 6, "y", link: target.Id, unresolved: "Old Name"))); // TableForge never makes this

        var export = PortableCollectionExporter.Export(db, c);

        Assert.False(export.Succeeded);
        Assert.Contains("Table \"Source\", row 1 (1–6) has both a link and an unresolved link name", Assert.Single(export.Problems));
    }

    [Fact]
    public void The_other_collection_of_a_cross_link_still_exports()
    {
        var (temp, db, _, _) = LinkedAcross();
        using (temp)
            Assert.True(PortableCollectionExporter.Export(db, db.GetCollections().Single(c => c.Name == "Beta")).Succeeded);
    }

    [Fact]
    public void Export_and_delete_commands_need_a_selected_collection()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var main = new MainViewModel(db, new FixedDice(1));
        Assert.False(main.ExportCollectionCommand.CanExecute(null));
        Assert.False(main.DeleteCollectionCommand.CanExecute(null));
        Assert.True(main.ImportCollectionCommand.CanExecute(null));
    }
}
