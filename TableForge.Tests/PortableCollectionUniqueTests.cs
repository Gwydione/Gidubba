using System.Text;
using System.Text.Json.Nodes;
using TableForge.Data;
using TableForge.Domain;
using TableForge.Export.Pum;
using TableForge.Portable;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// .tfcollection format version 3 (Checkpoint 6): a row's Unique setting travels as "unique": true, in the lowest version that
/// carries the content (so only a Collection with a Unique row is version 3). Which rows have been used is local play state: it is
/// never written, never read, and an imported Collection's Unique rows all start available, whatever the source's state. Versions
/// 1 and 2 still read exactly as before (every row repeatable), and the external exports ignore the setting.
/// </summary>
public class PortableCollectionUniqueTests
{
    private static JsonNode Json(byte[] bytes) => JsonNode.Parse(bytes)!;
    private static byte[] Bytes(JsonNode doc) => Encoding.UTF8.GetBytes(doc.ToJsonString());

    private static TableEntry Entry(RollableTable t, string text) => t.ResultSets.SelectMany(s => s.Entries).Single(e => e.Text == text);

    private static RollableTable Load(AppDatabase db, long collectionId, string name) =>
        db.LoadTable(db.GetTableSummaries(collectionId).Single(t => t.Name == name).Id)!;

    private static void MakeUnique(AppDatabase db, RollableTable table, params string[] texts)
    {
        var fresh = db.LoadTable(table.Id)!;
        foreach (var text in texts) Entry(fresh, text).IsUnique = true;
        db.SaveTable(fresh);
    }

    /// <summary>
    /// Dungeon: Monsters (in Wilds; Goblin and Ogre Unique, Goblin links to Hoard, Ogre bold, Rats repeatable), Hoard (Gold Unique),
    /// and Paired, an aligned two-set table whose sets are Unique on different rows.
    /// </summary>
    private static Collection Seed(AppDatabase db, string name = "Dungeon")
    {
        var c = db.CreateCollection(name);
        var wilds = db.CreateFolder(c.Id, "Wilds");
        var hoard = Save(db, c.Id, "Hoard", "d6", null, Set("", E(1, 3, "Gold"), E(4, 6, "Gems")));
        var monsters = Save(db, c.Id, "Monsters", "d6", wilds.Id,
            Set("", E(1, 2, "Goblin", link: hoard.Id), E(3, 4, "Ogre", styles: Runs(4, (0, 4, TextStyle.Bold))), E(5, 6, "Rats")));
        var paired = Save(db, c.Id, "Paired", "d6", null,
            Set("Who", E(1, 3, "Bandits"), E(4, 6, "Knights")), Set("Where", E(1, 3, "Ford"), E(4, 6, "Bridge")));
        MakeUnique(db, hoard, "Gold");
        MakeUnique(db, monsters, "Goblin", "Ogre");
        MakeUnique(db, paired, "Knights", "Ford");
        return c;
    }

    /// <summary>Every row of every table, as "Table/Set/Text" with its Unique setting, in the Collection's own order.</summary>
    private static string[] UniqueMap(AppDatabase db, long collectionId) =>
        db.GetTableSummaries(collectionId).Select(s => db.LoadTable(s.Id)!)
            .SelectMany(t => t.ResultSets.SelectMany(s => s.Entries.Select(e => $"{t.Name}/{s.Name}/{e.Text}={(e.IsUnique ? "U" : "-")}")))
            .ToArray();

    // ---- writing ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_collection_with_a_unique_row_is_version_3_and_only_unique_rows_say_so()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var file = Json(ExportBytes(db, Seed(db)));

        Assert.Equal(3, (int?)file["formatVersion"]);
        Assert.Equal(PortableFormat.NewestVersion, (int?)file["formatVersion"]);
        var rows = file["tables"]!.AsArray()
            .SelectMany(t => t!["resultSets"]!.AsArray().SelectMany(s => s!["entries"]!.AsArray().Select(e => (Set: (string?)s["name"], Row: e!))))
            .ToList();
        Assert.Equal(["Gold", "Goblin", "Ogre", "Knights", "Ford"],
            rows.Where(r => r.Row["unique"] is not null).Select(r => (string?)r.Row["text"]).ToArray());
        Assert.All(rows.Where(r => r.Row["unique"] is not null), r => Assert.True((bool)r.Row["unique"]!));
        // In the aligned table each set's own row carries its own setting.
        Assert.Equal([("Who", "Knights"), ("Where", "Ford")],
            rows.Where(r => r.Row["unique"] is not null && r.Set != "").Select(r => (r.Set!, (string)r.Row["text"]!)).ToArray());
    }

    [Fact]
    public void The_lowest_version_still_wins_without_unique_rows_and_unique_rows_outrank_descriptions()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var plain = SeedMythic(db);
        Assert.Equal(1, (int?)Json(ExportBytes(db, plain))["formatVersion"]);
        Assert.DoesNotContain("unique", Text(ExportBytes(db, plain)));

        var described = Load(db, plain.Id, "Weather");
        described.Description = "Roll each morning.";
        db.SaveTable(described);
        Assert.Equal(2, (int?)Json(ExportBytes(db, plain))["formatVersion"]);

        MakeUnique(db, described, described.ResultSets[0].Entries[0].Text);
        Assert.Equal(3, (int?)Json(ExportBytes(db, plain))["formatVersion"]);
    }

    [Fact]
    public void Used_results_never_reach_the_file_and_exporting_leaves_them_as_they_were()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = Seed(db);
        var monsters = Load(db, c.Id, "Monsters");
        db.MarkRowsUsed(monsters.Id, [Entry(monsters, "Goblin").RowKey]);
        db.AddRollHistory(new RollSnapshot(monsters.Id, "Monsters", "d6", 1, "Goblin", RerolledCount: 3));

        var text = Text(ExportBytes(db, c));

        foreach (var key in db.GetTableSummaries(c.Id).Select(s => db.LoadTable(s.Id)!).SelectMany(t => t.ResultSets.SelectMany(s => s.Entries)).Select(e => e.RowKey))
            Assert.DoesNotContain(key.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
        foreach (var word in new[] { "used", "Used", "rowKey", "rerolled", "Rerolled", "history" })
            Assert.DoesNotContain(word, text);
        Assert.Equal([Entry(monsters, "Goblin").RowKey], db.GetUsedRowKeys(monsters.Id));      // export changed nothing
        Assert.Equal(3, db.GetRollHistory().Single().RerolledCount);
    }

    [Fact]
    public void Exporting_a_version_3_file_says_it_needs_Gidubba_1_4_and_versions_1_and_2_say_nothing_new()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var plain = SeedMythic(db);
        var weather = Load(db, plain.Id, "Weather");
        weather.Description = "Roll each morning.";
        db.SaveTable(weather);
        Seed(db);
        using var file = new TempFile();
        var main = new MainViewModel(db, new FixedDice(1), chooseCollectionFile: _ => file.Path);

        main.SelectedCollection = main.Collections.Single(c => c.Name == "Mythic GME");    // version 2
        main.ExportCollectionCommand.Execute(null);
        Assert.Equal(2, (int?)Json(File.ReadAllBytes(file.Path))["formatVersion"]);
        Assert.Equal($"Exported \"Mythic GME\" to {Path.GetFileName(file.Path)}.", main.Status);

        main.SelectedCollection = main.Collections.Single(c => c.Name == "Dungeon");       // version 3
        main.ExportCollectionCommand.Execute(null);
        Assert.Equal(3, (int?)Json(File.ReadAllBytes(file.Path))["formatVersion"]);
        Assert.Equal($"Exported \"Dungeon\" to {Path.GetFileName(file.Path)}. It has Unique results, so importing it needs Gidubba 1.4 or later.",
            main.Status);
    }

    // ---- reading and importing -------------------------------------------------------------------------------------------

    [Fact]
    public void A_version_3_round_trip_keeps_every_unique_setting_link_and_format_and_re_exports_the_same_file()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = Seed(db);
        var bytes = ExportBytes(db, c);

        using var otherTemp = new TempDatabase();
        using var other = otherTemp.Open();
        var imported = other.ImportCollection(Read(bytes));

        Assert.Equal(UniqueMap(db, c.Id), UniqueMap(other, imported.Id));
        var monsters = Load(other, imported.Id, "Monsters");
        Assert.Equal(Load(other, imported.Id, "Hoard").Id, Entry(monsters, "Goblin").LinkedTableId);
        Assert.Equal(Runs(4, (0, 4, TextStyle.Bold)), Entry(monsters, "Ogre").Styles);
        Assert.Equal(Text(bytes), Text(ExportBytes(other, imported)));
    }

    [Fact]
    public void Imported_unique_rows_all_start_available_even_into_the_database_that_used_them()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = Seed(db);
        var monsters = Load(db, c.Id, "Monsters");
        var hoard = Load(db, c.Id, "Hoard");
        db.MarkRowsUsed(monsters.Id, [Entry(monsters, "Goblin").RowKey, Entry(monsters, "Ogre").RowKey]);
        db.MarkRowsUsed(hoard.Id, [Entry(hoard, "Gold").RowKey]);

        var copy = db.ImportCollection(Read(ExportBytes(db, c)));                    // into the very same database: "Dungeon (2)"

        Assert.NotEqual(c.Id, copy.Id);
        foreach (var summary in db.GetTableSummaries(copy.Id))
        {
            var table = db.LoadTable(summary.Id)!;
            Assert.Empty(db.GetUsedRowKeys(table.Id));
            var source = Load(db, c.Id, table.Name);
            var sourceKeys = source.ResultSets.SelectMany(s => s.Entries).Select(e => e.RowKey).ToHashSet();
            Assert.All(table.ResultSets.SelectMany(s => s.Entries), e => Assert.DoesNotContain(e.RowKey, sourceKeys));
        }
        Assert.Equal(2, db.GetUsedRowKeys(monsters.Id).Count);                      // the source keeps its own play state
        Assert.Single(db.GetUsedRowKeys(hoard.Id));

        // ...and on the Roll screen every imported Unique row is available.
        var main = new MainViewModel(db, new FixedDice(1));
        main.SelectedCollection = main.Collections.Single(x => x.Id == copy.Id);
        main.SelectedTable = main.Tables.Single(t => t.Name == "Monsters");
        var roll = Assert.IsType<RollViewModel>(main.Current);
        Assert.Equal(["Unique", "Unique", ""], roll.ResultSets[0].Entries.Select(e => e.UniqueTag).ToArray());
        Assert.Equal("Unique results: 0 of 2 used", roll.UsedResultsSummary);
    }

    [Fact]
    public void Extra_properties_in_a_file_can_never_bring_used_results_or_row_keys_with_them()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = Seed(db);
        var monsters = Load(db, c.Id, "Monsters");
        var goblinKey = Entry(monsters, "Goblin").RowKey;
        db.MarkRowsUsed(monsters.Id, [goblinKey]);

        var doc = Json(ExportBytes(db, c));
        var goblin = doc["tables"]!.AsArray().Single(t => (string?)t!["name"] == "Monsters")!["resultSets"]![0]!["entries"]![0]!;
        goblin["rowKey"] = goblinKey.ToString("D");                                  // a hand-edited file, trying to carry identity and state
        goblin["used"] = true;
        goblin["rerolledCount"] = 5;
        var copy = db.ImportCollection(Read(Bytes(doc)));

        var imported = Load(db, copy.Id, "Monsters");
        Assert.NotEqual(goblinKey, Entry(imported, "Goblin").RowKey);
        Assert.Empty(db.GetUsedRowKeys(imported.Id));
        Assert.True(Entry(imported, "Goblin").IsUnique);
    }

    /// <summary>A version 1 file as RC25 writes one, and a version 2 file with a description, a style and a self-link.</summary>
    private const string Version1File =
        """
        { "format": "TableForgeCollection", "formatVersion": 1, "collection": { "name": "Old Game" }, "folders": [ { "id": "f1", "name": "Npcs" } ],
          "tables": [
            { "id": "t1", "name": "Mood", "folder": "f1", "dice": "d6", "clampToRange": false, "resultSets": [
              { "name": "", "entries": [ { "min": 1, "max": 3, "text": "Calm", "link": { "table": "t2" } }, { "min": 4, "max": 6, "text": "Angry" } ] } ] },
            { "id": "t2", "name": "Fate", "dice": "d20", "clampToRange": true, "resultSets": [
              { "name": "", "entries": [ { "min": 1, "max": 10, "text": "Bad" }, { "min": 11, "max": 20, "text": "Good" } ] } ] } ] }
        """;

    private const string Version2File =
        """
        { "format": "TableForgeCollection", "formatVersion": 2, "collection": { "name": "Newer Game" }, "folders": [],
          "tables": [
            { "id": "t1", "name": "Weather", "dice": "d6", "clampToRange": false, "description": "Roll each morning.", "resultSets": [
              { "name": "Sky", "entries": [ { "min": 1, "max": 3, "text": "Clear", "styles": [[0, 5, "i"]] }, { "min": 4, "max": 6, "text": "Again", "link": { "table": "t1" } } ] },
              { "name": "Wind", "entries": [ { "min": 1, "max": 3, "text": "Calm" }, { "min": 4, "max": 6, "text": "Gale" } ] } ] } ] }
        """;

    [Fact]
    public void Version_1_and_2_files_import_as_before_with_every_row_repeatable()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();

        var v1 = db.ImportCollection(Read(Encoding.UTF8.GetBytes(Version1File)));
        Assert.All(UniqueMap(db, v1.Id), r => Assert.EndsWith("=-", r));
        Assert.Equal(Load(db, v1.Id, "Fate").Id, Entry(Load(db, v1.Id, "Mood"), "Calm").LinkedTableId);
        Assert.True(Load(db, v1.Id, "Fate").ClampResultsToRange);

        var v2 = db.ImportCollection(Read(Encoding.UTF8.GetBytes(Version2File)));
        var weather = Load(db, v2.Id, "Weather");
        Assert.All(UniqueMap(db, v2.Id), r => Assert.EndsWith("=-", r));
        Assert.Equal("Roll each morning.", weather.Description);
        Assert.Equal(["Sky", "Wind"], weather.ResultSets.Select(s => s.Name).ToArray());
        Assert.Equal(Runs(5, (0, 5, TextStyle.Italic)), Entry(weather, "Clear").Styles);
        Assert.Equal(weather.Id, Entry(weather, "Again").LinkedTableId);

        // Re-exported unchanged, they are still the version they were (nothing about them needs version 3).
        Assert.Equal(1, (int?)Json(ExportBytes(db, v1))["formatVersion"]);
        Assert.Equal(2, (int?)Json(ExportBytes(db, v2))["formatVersion"]);
    }

    [Fact]
    public void Unique_is_read_whatever_the_version_says_like_a_description_so_it_is_never_silently_dropped()
    {
        var doc = Json(Encoding.UTF8.GetBytes(Version1File));
        doc["tables"]![0]!["resultSets"]![0]!["entries"]![1]!["unique"] = true;
        var read = Read(Bytes(doc));
        Assert.True(read.Tables[0].Table.ResultSets[0].Entries[1].IsUnique);
        Assert.False(read.Tables[0].Table.ResultSets[0].Entries[0].IsUnique);
    }

    // ---- validation ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("\"true\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void A_unique_that_is_not_true_or_false_refuses_the_whole_file(string value)
    {
        var doc = Json(Encoding.UTF8.GetBytes(Version1File));
        doc["formatVersion"] = 3;
        doc["tables"]![0]!["resultSets"]![0]!["entries"]![0]!["unique"] = JsonNode.Parse(value);

        var read = PortableCollectionReader.Read(Bytes(doc));

        Assert.False(read.Succeeded);
        Assert.Equal(PortableReadFailure.NotACollectionFile, read.Failure);
        Assert.Contains("Table \"Mood\", row 1: unique must be true or false.", read.Problems);
        Assert.Contains("Nothing was imported", read.Summary);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("true", true)]
    public void Unique_false_or_null_is_a_repeatable_row(string value, bool expected)
    {
        var doc = Json(Encoding.UTF8.GetBytes(Version1File));
        doc["formatVersion"] = 3;
        doc["tables"]![0]!["resultSets"]![0]!["entries"]![0]!["unique"] = JsonNode.Parse(value);
        Assert.Equal(expected, Read(Bytes(doc)).Tables[0].Table.ResultSets[0].Entries[0].IsUnique);
    }

    [Fact]
    public void Versions_1_to_3_are_read_and_version_4_is_refused_as_newer()
    {
        foreach (var version in new[] { 1, 2, 3 })
        {
            var doc = Json(Encoding.UTF8.GetBytes(Version1File));
            doc["formatVersion"] = version;
            Assert.True(PortableCollectionReader.Read(Bytes(doc)).Succeeded, $"version {version}");
        }
        var newer = Json(Encoding.UTF8.GetBytes(Version1File));
        newer["formatVersion"] = 4;
        var read = PortableCollectionReader.Read(Bytes(newer));
        Assert.Equal(PortableReadFailure.NewerFormat, read.Failure);
        Assert.Contains("this version of Gidubba reads versions 1 to 3", Assert.Single(read.Problems));
    }

    [Fact]
    public void A_refused_file_imports_nothing_and_touches_no_used_results()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = Seed(db);
        var monsters = Load(db, c.Id, "Monsters");
        db.MarkRowsUsed(monsters.Id, [Entry(monsters, "Goblin").RowKey]);

        var doc = Json(ExportBytes(db, c));
        doc["tables"]![0]!["resultSets"]![0]!["entries"]![0]!["unique"] = "yes";
        var path = Path.Combine(Path.GetTempPath(), $"unique-refused-{Guid.NewGuid():N}.tfcollection");
        File.WriteAllBytes(path, Bytes(doc));
        try
        {
            var main = new MainViewModel(db, new FixedDice(1), (_, _) => true, chooseImportFile: () => path, confirmImport: _ => true);
            main.ImportCollectionCommand.Execute(null);
        }
        finally { File.Delete(path); }

        Assert.Single(db.GetCollections());
        Assert.Equal([Entry(monsters, "Goblin").RowKey], db.GetUsedRowKeys(monsters.Id));
    }

    // ---- the external exports ignore it ----------------------------------------------------------------------------------

    [Fact]
    public void Every_external_export_is_byte_for_byte_the_same_with_unique_rows_and_used_results()
    {
        RollableTable Make(bool unique) => new RollableTable
        {
            Id = 1, Name = "Monsters", Dice = DiceExpression.Parse("d6"),
            ResultSets = [Set("", E(1, 2, "Goblin"), E(3, 4, "Ogre", styles: Runs(4, (0, 4, TextStyle.Bold))), E(5, 6, "Rats"))],
        }.Also(t => { foreach (var e in t.ResultSets[0].Entries) { e.RowKey = Guid.NewGuid(); e.IsUnique = unique && e.Text != "Rats"; } });

        List<string> Copies(RollableTable table)
        {
            var copied = new List<string>();
            var store = UsedResultsStore.InMemory();
            if (table.ResultSets[0].Entries[0].IsUnique) store.MarkUsed(1, [table.ResultSets[0].Entries[0].RowKey]);
            var roll = new RollViewModel(table, new FixedDice(3), copyText: copied.Add, usedResults: store);
            roll.RollCommand.Execute(null);                                          // Ogre, used up on the Unique table
            roll.CopyTableTextCommand.Execute(null);
            roll.CopyTableTextSpacesCommand.Execute(null);
            roll.CopyForSojourCommand.Execute(null);
            roll.CopyFoundryJsonCommand.Execute(null);
            roll.CopyTablesPlusJsonCommand.Execute(null);
            var pum = PumExporter.Export(new PumExportRequest("Dungeon", [new PumSourceTable(1, table)]), new DateTime(2026, 10, 27, 12, 0, 0));
            copied.Add(Convert.ToBase64String(pum.Bundle!));
            return copied;
        }

        var unique = Copies(Make(unique: true));
        var plain = Copies(Make(unique: false));
        Assert.Equal(6, unique.Count);
        Assert.Equal(plain, unique);
        Assert.All(unique.Take(5), text => Assert.DoesNotContain("nique", text));
    }
}
