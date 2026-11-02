using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TableForge.Data;
using TableForge.Domain;
using TableForge.Export;
using TableForge.Export.Pum;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// Export to PUM Companion from the app: the database read into a request, the review before saving, the Save dialog, and a
/// file written in full or not at all, for a whole Collection and for the table a Roll screen shows. It only ever reads.
/// </summary>
public class PumExportFlowTests
{
    private static readonly DateTime Stamp = new(2026, 10, 24, 12, 0, 0);

    private static TableEntry E(int min, int max, string text) => new() { Min = min, Max = max, Text = text };

    /// <summary>
    /// "Dungeon": Blank (every result empty), Encounters (links to Weather and to a table in another Collection) and Weather (in
    /// the Caves folder, two result sets). Everything is seeded before the shell starts, as it would be on launch.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly TempDatabase _temp = new();
        public AppDatabase Db { get; }
        public Collection Collection { get; }
        public RollableTable Weather { get; }
        public RollableTable Encounters { get; }
        public RollableTable Blank { get; }
        public MainViewModel Main { get; private set; } = null!;
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"tableforge-pum-{Guid.NewGuid():N}");

        public List<PumExportReviewViewModel> Reviews { get; } = [];
        public bool SaveReview { get; set; } = true;
        public Action<PumExportReviewViewModel>? DuringReview { get; set; }
        public List<SaveFileRequest> SaveRequests { get; } = [];
        public string? SavePath { get; set; }
        public List<string> Messages { get; } = [];
        public List<string> Copied { get; } = [];

        public World(bool emptyCollection = false, bool onlyBlank = false)
        {
            Directory.CreateDirectory(Folder);
            Db = _temp.Open();
            Collection = Db.CreateCollection("Dungeon");
            var other = Db.CreateCollection("Other");
            var elsewhere = Db.SaveTable(new RollableTable { CollectionId = other.Id, Name = "Elsewhere", Dice = DiceExpression.Parse("d2"), ResultSets = [new ResultSet { Entries = [E(1, 2, "Far")] }] });
            var caves = Db.CreateFolder(Collection.Id, "Caves");
            Weather = Blank = Encounters = null!;
            if (emptyCollection) return;

            Blank = Db.SaveTable(new RollableTable { CollectionId = Collection.Id, Name = "Blank", Dice = DiceExpression.Parse("d6"), ResultSets = [new ResultSet { Entries = [E(1, 3, ""), E(4, 6, " ")] }] });
            if (onlyBlank) return;
            Weather = Db.SaveTable(new RollableTable
            {
                CollectionId = Collection.Id, Name = "Weather", FolderId = caves.Id, Dice = DiceExpression.Parse("d6"),
                ResultSets =
                [
                    new ResultSet { Name = "Sky", Entries = [E(1, 3, "Rain"), E(4, 6, "Sun")] },
                    new ResultSet { Name = "Wind", Entries = [E(1, 6, "Calm")] },
                ],
            });
            Encounters = Db.SaveTable(new RollableTable
            {
                CollectionId = Collection.Id, Name = "Encounters", Dice = DiceExpression.Parse("d6"),
                ResultSets = [new ResultSet { Entries = [E(1, 3, "Wolves").Also(e => e.LinkedTableId = Weather.Id), E(4, 5, "Bandits").Also(e => e.LinkedTableId = elsewhere.Id), E(6, 6, "Nothing")] }],
            });
        }

        public MainViewModel Start(bool withReview = true) => Main = new MainViewModel(Db, new FixedDice(3), (_, _) => true,
            copyText: Copied.Add, showMessage: Messages.Add, now: () => Stamp,
            reviewPumExport: withReview ? review => { Reviews.Add(review); DuringReview?.Invoke(review); return SaveReview; } : null,
            choosePumFile: request => { SaveRequests.Add(request); return SavePath; });

        public RollViewModel Open(string name)
        {
            Main.SelectedTable = Main.Tables.Single(t => t.Name == name);
            return Assert.IsType<RollViewModel>(Main.Current);
        }

        /// <summary>Everything the export could conceivably change: every table as saved, Recent Tables and Recent Rolls.</summary>
        public string Snapshot() => string.Join("\n", Db.GetTableSummaries(Collection.Id).Select(s => Db.LoadTable(s.Id)!)
                .Select(t => $"{t.Name}|{t.UpdatedUtc:O}|{t.FolderId}|{t.Dice}|{TableTextExporter.Export(t, t.ResultSets[0], TableTextSeparator.Tab)}"
                    + string.Concat(t.ResultSets.SelectMany(r => r.Entries).Select(e => $"|{e.LinkedTableId}"))))
            + "\nrecent:" + string.Join(",", Db.GetRecentTables(Collection.Id).Select(t => t.Name))
            + "\nrolls:" + Db.GetRollHistory().Count;

        public string[] FilesInFolder() => Directory.GetFiles(Folder).Select(Path.GetFileName).ToArray()!;

        public void Dispose()
        {
            _temp.Dispose();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    /// <summary>The bundle's table files (not tables.json), by name, parsed.</summary>
    private static Dictionary<string, JsonElement> Tables(byte[] bundle)
    {
        Assert.Equal("PUM_ARCHIVE"u8.ToArray(), bundle[..11]);
        using var zip = new ZipArchive(new MemoryStream(bundle, 11, bundle.Length - 11), ZipArchiveMode.Read);
        return zip.Entries.Where(e => e.FullName != "tables.json").ToDictionary(e => e.FullName.Split('/')[0], e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
        });
    }

    private static List<JsonElement> Metadata(byte[] bundle)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle, 11, bundle.Length - 11), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("tables.json")!.Open(), Encoding.UTF8);
        return JsonDocument.Parse(reader.ReadToEnd()).RootElement.GetProperty("tables").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static string Row(JsonElement table, string key) => table.GetProperty("entries").GetProperty(key).GetString()!;

    // ---- Collection export --------------------------------------------------------------------------------------------

    [Fact]
    public void A_collection_is_reviewed_then_saved_as_exactly_the_engine_bundle()
    {
        using var w = new World();
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        Assert.True(w.Main.ExportPumCommand.CanExecute(null));

        w.Main.ExportPumCommand.Execute(null);

        var review = Assert.Single(w.Reviews);
        Assert.Equal("Export \"Dungeon\" to PUM Companion", review.Heading);
        Assert.Equal("2 tables will be exported. 1 table will be skipped.", review.Summary);
        Assert.Equal(["Skipped: Blank: Every result is blank, so the table would only show empty results in PUM.",
            "Warning: Encounters: The row \"4–5\" links to \"Elsewhere\", which isn't in this export."], review.Important);
        Assert.True(review.CanSave);
        Assert.Equal("Cancel", review.CancelLabel);
        Assert.Equal(PumExporter.ReimportWarning, review.ReimportWarning);
        Assert.Equal(review.Report.ToText(), review.ReportText);

        var request = Assert.Single(w.SaveRequests);
        Assert.Equal(("Export to PUM Companion", "Dungeon.pumb"), (request.Title, request.SuggestedName));
        var saved = File.ReadAllBytes(w.SavePath);
        Assert.Equal(PumExporter.Export(PumExportSource.ForCollection(w.Db, w.Collection), Stamp).Bundle, saved);
        Assert.Equal(["out.pumb"], w.FilesInFolder());
        Assert.Equal("Exported 2 of 3 tables from \"Dungeon\" to out.pumb (1 skipped).", w.Main.Status);
        Assert.Empty(w.Messages);

        var tables = Tables(saved);
        Assert.Equal(["Encounters", "Weather"], tables.Keys.Order());
        Assert.Equal("Wolves\n→ Weather", Row(tables["Encounters"], "3"));
        Assert.Equal("Bandits\n→ Elsewhere (not included in this export)", Row(tables["Encounters"], "5"));
        Assert.Equal("Sky: Rain\nWind: Calm", Row(tables["Weather"], "3"));
        Assert.Equal(["Dungeon", "Dungeon - Caves"], Metadata(saved).Select(m => m.GetProperty("classification").GetString()));
        Assert.All(Metadata(saved), m => Assert.Equal("", m.GetProperty("description").GetString()));
    }

    [Fact]
    public void A_collection_with_nothing_skipped_says_so_simply()
    {
        using var w = new World();
        w.Db.DeleteTable(w.Blank.Id);
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "Dungeon.pumb");
        w.Main.ExportPumCommand.Execute(null);
        Assert.Equal("2 tables will be exported.", w.Reviews[0].Summary);
        Assert.Equal("Exported 2 tables from \"Dungeon\" to Dungeon.pumb.", w.Main.Status);
    }

    [Fact]
    public void Cancelling_the_review_asks_nothing_more_and_writes_nothing()
    {
        using var w = new World();
        w.Start();
        w.SaveReview = false;
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        w.Main.ExportPumCommand.Execute(null);
        Assert.Single(w.Reviews);
        Assert.Empty(w.SaveRequests);
        Assert.Empty(w.FilesInFolder());
        Assert.Equal("", w.Main.Status);
    }

    [Fact]
    public void Cancelling_the_Save_dialog_writes_nothing()
    {
        using var w = new World();
        w.Start();
        w.SavePath = null;
        w.Main.ExportPumCommand.Execute(null);
        Assert.Single(w.SaveRequests);
        Assert.Empty(w.FilesInFolder());
        Assert.Equal("", w.Main.Status);
    }

    [Fact]
    public void A_collection_with_nothing_exportable_is_reviewed_with_only_Close_and_no_file_is_asked_for()
    {
        using var w = new World(onlyBlank: true);
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        w.Main.ExportPumCommand.Execute(null);   // the review answers "save", but there is nothing to save
        var review = Assert.Single(w.Reviews);
        Assert.False(review.CanSave);
        Assert.Equal("Close", review.CancelLabel);
        Assert.Equal("Nothing can be exported, so no file will be saved. 1 table was skipped.", review.Summary);
        Assert.Empty(w.SaveRequests);
        Assert.Empty(w.FilesInFolder());
    }

    [Fact]
    public void A_collection_with_no_tables_says_so_without_a_review()
    {
        using var w = new World(emptyCollection: true);
        w.Start();
        w.Main.ExportPumCommand.Execute(null);
        Assert.Empty(w.Reviews);
        Assert.Equal("\"Dungeon\" has no tables to export.", w.Main.Status);
    }

    [Fact]
    public void Exporting_only_reads_whether_saved_or_cancelled()
    {
        using var w = new World();
        w.Start();
        var before = w.Snapshot();
        w.SaveReview = false;
        w.Main.ExportPumCommand.Execute(null);
        w.SaveReview = true;
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        w.Main.ExportPumCommand.Execute(null);
        var roll = w.Open("Encounters");
        var opened = w.Snapshot();   // opening a table is what puts it in Recent Tables
        roll.SavePumBundleCommand.Execute(null);
        Assert.Equal(before, w.Snapshot().Replace("recent:Encounters", "recent:"));
        Assert.Equal(opened, w.Snapshot());
    }

    [Fact]
    public void Without_a_review_PUM_export_is_unavailable()
    {
        using var w = new World();
        w.Start(withReview: false);
        Assert.False(w.Main.ExportPumCommand.CanExecute(null));
        Assert.False(w.Open("Weather").SavePumBundleCommand.CanExecute(null));
    }

    // ---- Native nested tables ---------------------------------------------------------------------------------------------

    [Fact]
    public void Rolling_linked_tables_is_offered_off_and_turning_it_on_saves_the_nested_bundle_while_only_reading()
    {
        using var w = new World();
        w.Start();
        var before = w.Snapshot();
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        var off = PumExporter.Export(PumExportSource.ForCollection(w.Db, w.Collection), Stamp);
        w.DuringReview = review =>
        {
            Assert.True(review.CanRollLinkedTables);
            Assert.False(review.RollLinkedTables);
            Assert.False(review.ShowNameClashWarning);
            Assert.Equal("", review.LinkSummary);
            review.RollLinkedTables = true;
        };

        w.Main.ExportPumCommand.Execute(null);

        var review = Assert.Single(w.Reviews);
        Assert.Equal("1 of 2 links will roll their table in PUM. 1 stays text (see the report).", review.LinkSummary);
        Assert.True(review.ShowNameClashWarning);
        Assert.Equal(PumExporter.NameClashWarning, review.NameClashWarning);
        Assert.Equal(review.Result!.Report.ToText(), review.ReportText);
        Assert.Equal("2 tables will be exported. 1 table will be skipped.", review.Summary);

        var saved = File.ReadAllBytes(w.SavePath);
        Assert.Equal(review.Result.Bundle, saved);
        Assert.NotEqual(off.Bundle, saved);
        var tables = Tables(saved);
        Assert.Equal("Wolves\n→ Weather: {{Weather}}", Row(tables["Encounters"], "3"));
        Assert.Equal("Bandits\n→ Elsewhere (not included in this export)", Row(tables["Encounters"], "5"));
        Assert.Equal("Exported 2 of 3 tables from \"Dungeon\" to out.pumb (1 skipped).", w.Main.Status);
        Assert.Equal(before, w.Snapshot());
    }

    [Fact]
    public void Turning_it_off_again_saves_exactly_the_bundle_there_was_before()
    {
        using var w = new World();
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        var off = PumExporter.Export(PumExportSource.ForCollection(w.Db, w.Collection), Stamp);
        w.DuringReview = review =>
        {
            review.RollLinkedTables = true;
            review.RollLinkedTables = false;
            Assert.Equal("", review.LinkSummary);
            Assert.False(review.ShowNameClashWarning);
            Assert.Equal(off.Report.ToText(), review.ReportText);
        };

        w.Main.ExportPumCommand.Execute(null);

        Assert.Equal(off.Bundle, File.ReadAllBytes(w.SavePath));
    }

    [Fact]
    public void A_link_to_a_name_another_collection_uses_stays_text_and_the_review_warns_about_it()
    {
        using var w = new World();
        var other = w.Db.GetCollections().Single(c => c.Name == "Other");
        w.Db.SaveTable(new RollableTable { CollectionId = other.Id, Name = "WEATHER", Dice = DiceExpression.Parse("d2"), ResultSets = [new ResultSet { Entries = [E(1, 2, "Hail")] }] });
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "out.pumb");
        w.DuringReview = review => review.RollLinkedTables = true;

        w.Main.ExportPumCommand.Execute(null);

        var review = Assert.Single(w.Reviews);
        Assert.Equal("None of the 2 links can roll in PUM: they stay text (see the report).", review.LinkSummary);
        Assert.Contains("Warning: Encounters: The row \"1–3\" links to \"Weather\", but \"Other\" also has a table with that name, so it stays text: "
            + "PUM finds tables by name alone (in every category, ignoring case), so in PUM they would be one table.", review.Important);
        Assert.Equal("Wolves\n→ Weather", Row(Tables(File.ReadAllBytes(w.SavePath))["Encounters"], "3"));
    }

    [Fact]
    public void A_single_table_export_offers_no_nested_tables()
    {
        using var w = new World();
        w.Start();
        w.SaveReview = false;
        w.Open("Encounters").SavePumBundleCommand.Execute(null);

        var review = Assert.Single(w.Reviews);
        Assert.False(review.CanRollLinkedTables);
        review.RollLinkedTables = true;
        Assert.False(review.RollLinkedTables);
        Assert.Null(review.Result);
    }

    [Fact]
    public void The_request_names_every_other_collections_tables_as_they_would_be_exported()
    {
        using var w = new World();
        var other = w.Db.GetCollections().Single(c => c.Name == "Other");
        foreach (var name in new[] { "Loot", "loot", "Wind/Rain" })
            w.Db.SaveTable(new RollableTable { CollectionId = other.Id, Name = name, Dice = DiceExpression.Parse("d2"), ResultSets = [new ResultSet { Entries = [E(1, 2, "x")] }] });

        var names = PumExportSource.ForCollection(w.Db, w.Collection).OtherCollectionTableNames!;
        Assert.Equal(["Elsewhere", "Loot", "Wind - Rain", "loot (2)"], names.Keys.Order(StringComparer.Ordinal));
        Assert.All(names.Values, c => Assert.Equal("Other", c));
        Assert.True(names.ContainsKey("LOOT (2)"));   // PUM ignores case, and so does the check
        Assert.False(names.ContainsKey("Weather"));   // this Collection's own tables are not "elsewhere"
    }

    // ---- Single table -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_single_table_is_reviewed_and_saved_with_its_own_category_and_links_marked_not_included()
    {
        using var w = new World();
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "enc.pumb");
        var roll = w.Open("Encounters");
        roll.SavePumBundleCommand.Execute(null);

        var review = Assert.Single(w.Reviews);
        Assert.Equal("Export \"Encounters\" to PUM Companion", review.Heading);
        Assert.Equal("1 table will be exported.", review.Summary);
        Assert.Equal("Encounters.pumb", w.SaveRequests[0].SuggestedName);
        Assert.Equal("PUM bundle saved to enc.pumb.", roll.CopyMessage);

        var saved = File.ReadAllBytes(w.SavePath);
        var table = Assert.Single(Tables(saved)).Value;
        Assert.Equal("Wolves\n→ Weather (not included in this export)", Row(table, "3"));
        Assert.Equal("Bandits\n→ Elsewhere (not included in this export)", Row(table, "5"));
        Assert.Equal("", w.Main.Status);   // the Roll screen's own line says it
    }

    [Fact]
    public void A_single_table_is_the_same_PUM_table_as_in_the_collection_export()
    {
        using var w = new World();
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "weather.pumb");
        var roll = w.Open("Weather");
        roll.CopyResultSetIndex = 1;   // the result-set choice is for the other formats: PUM gets every set
        roll.SavePumBundleCommand.Execute(null);

        var single = Assert.Single(Metadata(File.ReadAllBytes(w.SavePath)));
        var collection = Metadata(PumExporter.Export(PumExportSource.ForCollection(w.Db, w.Collection), Stamp).Bundle!)
            .Single(m => m.GetProperty("name").GetString() == "Weather");
        Assert.Equal(collection.GetRawText(), single.GetRawText());   // same id, name and category: a re-export replaces it
        Assert.Equal("Sky: Sun\nWind: Calm", Row(Tables(File.ReadAllBytes(w.SavePath))["Weather"], "6"));
    }

    [Fact]
    public void A_single_table_that_cannot_be_exported_says_why_without_a_review()
    {
        using var w = new World();
        w.Start();
        var roll = w.Open("Blank");
        roll.SavePumBundleCommand.Execute(null);
        Assert.Empty(w.Reviews);
        Assert.Empty(w.SaveRequests);
        Assert.Equal("This table cannot be exported to PUM Companion: Every result is blank, so the table would only show empty results in PUM.", roll.CopyMessage);
    }

    [Fact]
    public void Cancelling_a_single_table_export_clears_the_message_and_writes_nothing()
    {
        using var w = new World();
        w.Start();
        var roll = w.Open("Blank");
        roll.SavePumBundleCommand.Execute(null);
        Assert.NotEqual("", roll.CopyMessage);
        roll = w.Open("Weather");
        w.SaveReview = false;
        roll.SavePumBundleCommand.Execute(null);
        Assert.Equal("", roll.CopyMessage);
        Assert.Empty(w.FilesInFolder());
    }

    // ---- Saving -------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_failed_save_is_reported_and_leaves_nothing_behind()
    {
        using var w = new World();
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "missing folder", "out.pumb");
        w.Main.ExportPumCommand.Execute(null);
        Assert.StartsWith("The PUM bundle could not be saved: ", w.Main.Status);
        Assert.StartsWith("The PUM bundle could not be saved: ", Assert.Single(w.Messages));
        Assert.Empty(w.FilesInFolder());

        var roll = w.Open("Weather");
        roll.SavePumBundleCommand.Execute(null);
        Assert.StartsWith("The PUM bundle could not be saved: ", roll.CopyMessage);
    }

    [Fact]
    public void A_file_that_cannot_be_replaced_is_left_exactly_as_it_was_with_no_partial_file()
    {
        using var w = new World();
        w.Start();
        w.SavePath = Path.Combine(w.Folder, "Dungeon.pumb");
        File.WriteAllText(w.SavePath, "the earlier bundle");
        using (new FileStream(w.SavePath, FileMode.Open, FileAccess.Read, FileShare.None))   // another program has it open
            w.Main.ExportPumCommand.Execute(null);
        Assert.StartsWith("The PUM bundle could not be saved: ", w.Main.Status);
        Assert.Equal("the earlier bundle", File.ReadAllText(w.SavePath));
        Assert.Equal(["Dungeon.pumb"], w.FilesInFolder());

        w.Main.ExportPumCommand.Execute(null);   // once it is free, the bundle replaces it
        Assert.Equal(PumExporter.Export(PumExportSource.ForCollection(w.Db, w.Collection), Stamp).Bundle, File.ReadAllBytes(w.SavePath));
        Assert.Equal(["Dungeon.pumb"], w.FilesInFolder());
    }

    [Fact]
    public void Writing_whole_replaces_a_file_and_never_leaves_a_temporary_one()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tableforge-pum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "a.pumb");
            MainViewModel.WriteWhole(path, [1, 2, 3]);
            MainViewModel.WriteWhole(path, [4, 5]);
            Assert.Equal([4, 5], File.ReadAllBytes(path));
            Assert.Equal(["a.pumb"], Directory.GetFiles(folder).Select(Path.GetFileName));
            Assert.ThrowsAny<IOException>(() => MainViewModel.WriteWhole(Path.Combine(folder, "nope", "b.pumb"), [1]));
            Assert.Equal(["a.pumb"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- The database request ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_request_keys_tables_by_database_id_with_folder_names_and_names_links_outside_the_export()
    {
        using var w = new World();
        var request = PumExportSource.ForCollection(w.Db, w.Collection);
        Assert.Equal("Dungeon", request.CollectionName);
        Assert.Equal([(w.Blank.Id, "Blank", null), (w.Encounters.Id, "Encounters", null), (w.Weather.Id, "Weather", "Caves")],
            request.Tables.Select(t => (t.Key, t.Table.Name, t.FolderName)));
        Assert.Equal(["Elsewhere"], request.OtherTableNames!.Values);

        var single = PumExportSource.ForTable(w.Db, w.Encounters);
        Assert.Equal(w.Encounters.Id, Assert.Single(single.Tables).Key);
        Assert.Equal(["Elsewhere", "Weather"], single.OtherTableNames!.Values.Order());
    }

    [Fact]
    public void A_link_to_a_deleted_table_names_it_as_an_unresolved_link_as_TableForge_does()
    {
        using var w = new World();
        w.Db.DeleteTable(w.Weather.Id);
        var pum = PumExporter.Export(PumExportSource.ForTable(w.Db, w.Db.LoadTable(w.Encounters.Id)!), Stamp);
        Assert.Equal("Wolves\n→ Weather (unresolved link)", PumEquivalence.PumLookup(Assert.Single(pum.Tables).Rows, 1).Text);
    }

    // ---- The review -------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_review_lists_at_most_eight_problems_and_Copy_Report_copies_the_whole_report()
    {
        var tables = Enumerable.Range(1, 10).Select(i => new PumSourceTable(i, new RollableTable
        {
            Name = $"Blank {i}", Dice = DiceExpression.Parse("d6"), ResultSets = [new ResultSet { Entries = [E(1, 6, "")] }],
        })).Append(new PumSourceTable(11, new RollableTable { Name = "Fine", Dice = DiceExpression.Parse("d6"), ResultSets = [new ResultSet { Entries = [E(1, 6, "x")] }] }));
        var report = PumExporter.Export(new("C", tables.ToList()), Stamp).Report;
        var copied = new List<string>();
        var review = new PumExportReviewViewModel("C", report, copied.Add);

        Assert.Equal("1 table will be exported. 10 tables will be skipped.", review.Summary);
        Assert.Equal(PumExportReviewViewModel.ImportantShown + 1, review.Important.Count);
        Assert.Equal("…and 2 more (see the full report below).", review.Important[^1]);

        review.CopyReportCommand.Execute(null);
        Assert.Equal([report.ToText()], copied);
        Assert.Contains("Blank 10: Every result is blank", copied[0]);
        Assert.Equal("Report copied.", review.CopyMessage);

        var busy = new PumExportReviewViewModel("C", report, _ => throw new System.Runtime.InteropServices.ExternalException("Clipboard busy"));
        busy.CopyReportCommand.Execute(null);
        Assert.Equal("The report could not be copied: Clipboard busy", busy.CopyMessage);
    }
}
