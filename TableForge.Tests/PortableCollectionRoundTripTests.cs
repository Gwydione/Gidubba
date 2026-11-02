using System.Text.Json.Nodes;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.Portable;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// RC25 Portable Collections: Database A → Export Collection → a clean Database B → Import → the same Collection, compared by
/// meaning and never by database id; and the imported tables still roll, clamp, follow links, find inline dice, search and export.
/// </summary>
public class PortableCollectionRoundTripTests
{
    private sealed class RoundTrip : IDisposable
    {
        public TempDatabase TempA { get; } = new();
        public TempDatabase TempB { get; } = new();
        public Data.AppDatabase A { get; }
        public Data.AppDatabase B { get; private set; }
        public Collection Source { get; }
        public Collection Imported { get; private set; } = null!;
        public byte[] File { get; private set; } = [];

        public RoundTrip(Action<Data.AppDatabase>? seedB = null)
        {
            A = TempA.Open();
            SeedOther(A, "Unrelated A");
            Source = SeedMythic(A);
            B = TempB.Open();
            seedB?.Invoke(B);
        }

        public RoundTrip Run()
        {
            File = ExportBytes(A, Source);
            Imported = B.ImportCollection(Read(File));
            return this;
        }

        /// <summary>Closes and reopens Database B, so what is compared is what was actually stored.</summary>
        public void ReopenB()
        {
            B.Dispose();
            B = TempB.Open();
        }

        public RollableTable TableA(string name, int nth = 0) => Load(A, Source.Id, name, nth);
        public RollableTable TableB(string name, int nth = 0) => Load(B, Imported.Id, name, nth);

        private static RollableTable Load(Data.AppDatabase db, long collectionId, string name, int nth) =>
            db.LoadTable(db.GetTableSummaries(collectionId).Where(t => t.Name == name).ElementAt(nth).Id)!;

        public void Dispose()
        {
            TempA.Dispose();
            TempB.Dispose();
        }
    }

    // ---- semantic round trip -----------------------------------------------------------------------------------------

    [Fact]
    public void Export_then_import_into_a_clean_database_reproduces_the_collection_exactly()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();

        Assert.Equal("Mythic GME", rt.Imported.Name);
        Assert.Equal(Describe(rt.A, rt.Source.Id), Describe(rt.B, rt.Imported.Id));
    }

    [Fact]
    public void The_fixture_really_holds_every_kind_of_content_it_is_meant_to_test()
    {
        using var rt = new RoundTrip().Run();
        var text = Describe(rt.B, rt.Imported.Id);

        Assert.Contains("folder Empty Folder", text);
        Assert.Contains("folder Unfiled", text);
        Assert.Contains("dice d66 (2,6,0,D66)", text);
        Assert.Contains("dice 2d6+1", text);
        foreach (var display in new[] { "01–07", "08", "96–00", "00", "-5 or less", "26+", "1 or less", "20 or more" })
            Assert.Contains($"display [{display}]", text);
        Assert.Contains("open..-5", text);
        Assert.Contains("26..open", text);
        Assert.Contains("-4..0", text);
        Assert.Contains("21..25", text);
        Assert.Contains("clamp True", text);
        Assert.Contains("styles [B 0+4, I 14+5]", text);
        Assert.Contains("styles [BI 0+9]", text);
        Assert.Contains("styles [B 2+2]", text);
        Assert.Contains("unresolved [Dragons]", text);
        Assert.Equal(2, rt.B.GetTableSummaries(rt.Imported.Id).Count(t => t.Name == "Rumors"));
    }

    [Fact]
    public void Links_self_links_and_cycles_point_at_the_new_tables_of_the_imported_collection()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();

        var chain = rt.TableB("Chain");
        Assert.Equal(chain.Id, chain.ResultSets[0].Entries[0].LinkedTableId);

        var ping = rt.TableB("Ping");
        var pong = rt.TableB("Pong");
        Assert.Equal(pong.Id, ping.ResultSets[0].Entries[0].LinkedTableId);
        Assert.Equal(ping.Id, pong.ResultSets[0].Entries[0].LinkedTableId);

        var merchants = rt.TableB("Encounter").ResultSets[0].Entries[1];
        Assert.Equal("Weather", rt.B.LoadTable(merchants.LinkedTableId!.Value)!.Name);
        Assert.Equal(rt.Imported.Id, rt.B.LoadTable(merchants.LinkedTableId!.Value)!.CollectionId);
    }

    [Fact]
    public void Only_the_exported_collection_travels()
    {
        using var rt = new RoundTrip().Run();
        Assert.Single(rt.B.GetCollections());
        Assert.DoesNotContain("Unrelated A", Text(rt.File));
        Assert.DoesNotContain("Towns", Text(rt.File));
        Assert.Equal(rt.A.GetTableSummaries(rt.Source.Id).Count, rt.B.GetTableSummaries(rt.Imported.Id).Count);
    }

    [Fact]
    public void Importing_never_changes_an_existing_unrelated_collection()
    {
        Collection other = null!;
        using var rt = new RoundTrip(b => other = SeedOther(b));
        var before = Describe(rt.B, other.Id);

        rt.Run();
        rt.ReopenB();

        Assert.Equal(before, Describe(rt.B, other.Id));
        Assert.Equal(2, rt.B.GetCollections().Count);
    }

    // ---- the file itself ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_file_is_utf8_json_without_a_byte_order_mark_and_names_its_own_format()
    {
        using var rt = new RoundTrip().Run();
        Assert.NotEqual(0xEF, rt.File[0]);
        var root = JsonNode.Parse(rt.File)!;
        Assert.Equal("TableForgeCollection", (string?)root["format"]);
        Assert.Equal(1, (int?)root["formatVersion"]);
        Assert.Equal("Mythic GME", (string?)root["collection"]!["name"]);
    }

    [Fact]
    public void Open_bounds_are_written_as_null_and_no_sentinel_or_database_id_ever_appears()
    {
        using var rt = new RoundTrip().Run();
        var text = Text(rt.File);
        Assert.DoesNotContain("2147483647", text);
        Assert.DoesNotContain("2147483648", text);

        var critical = JsonNode.Parse(rt.File)!["tables"]!.AsArray().Single(t => (string?)t!["name"] == "Critical")!;
        var first = critical["resultSets"]![0]!["entries"]![0]!;
        Assert.Null(first["min"]);
        Assert.True(first.AsObject().ContainsKey("min")); // present, and null: open below
        Assert.Equal(-5, (int?)first["max"]);
        Assert.Equal("-5 or less", (string?)first["display"]);

        // Identities are only f1, f2... and t1, t2...; none of the database's own ids is a property anywhere.
        foreach (var table in JsonNode.Parse(rt.File)!["tables"]!.AsArray())
        {
            Assert.Matches("^t[0-9]+$", (string?)table!["id"]);
            Assert.False(table.AsObject().ContainsKey("collectionId"));
        }
        Assert.DoesNotContain("Utc", text);
        Assert.DoesNotContain("lastUsed", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatting_is_written_as_semantic_runs_not_the_stored_string()
    {
        using var rt = new RoundTrip().Run();
        var weather = JsonNode.Parse(rt.File)!["tables"]!.AsArray().Single(t => (string?)t!["name"] == "Weather")!;
        var rows = weather["resultSets"]![0]!["entries"]!.AsArray();
        Assert.Equal("""[[0,4,"b"],[14,5,"i"]]""", rows[1]!["styles"]!.ToJsonString());
        Assert.Equal("""[[0,9,"bi"]]""", rows[3]!["styles"]!.ToJsonString());
        Assert.False(rows[0]!.AsObject().ContainsKey("styles")); // unformatted text has no runs at all
        Assert.DoesNotContain("\"v\":", Text(rt.File));
    }

    [Fact]
    public void Links_are_written_as_file_local_table_references()
    {
        using var rt = new RoundTrip().Run();
        var tables = JsonNode.Parse(rt.File)!["tables"]!.AsArray();
        var chain = tables.Single(t => (string?)t!["name"] == "Chain")!;
        Assert.Equal((string?)chain["id"], (string?)chain["resultSets"]![0]!["entries"]![0]!["link"]!["table"]);
        var encounter = tables.Single(t => (string?)t!["name"] == "Encounter")!;
        Assert.Equal("Dragons", (string?)encounter["resultSets"]![0]!["entries"]![2]!["link"]!["unresolved"]);
    }

    // ---- determinism ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Export_import_export_gives_byte_identical_files()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();
        Assert.Equal(Text(rt.File), Text(ExportBytes(rt.B, rt.Imported)));
    }

    [Fact]
    public void Exporting_twice_gives_the_same_bytes()
    {
        using var rt = new RoundTrip();
        Assert.Equal(ExportBytes(rt.A, rt.Source), ExportBytes(rt.A, rt.Source));
    }

    [Fact]
    public void A_renamed_import_differs_from_the_original_file_only_in_the_collection_name()
    {
        using var rt = new RoundTrip(b => b.CreateCollection("Mythic GME")).Run();
        Assert.Equal("Mythic GME (2)", rt.Imported.Name);
        var again = Text(ExportBytes(rt.B, rt.Imported));
        Assert.Equal(Text(rt.File), again.Replace("\"Mythic GME (2)\"", "\"Mythic GME\""));
    }

    // ---- the imported tables work ----------------------------------------------------------------------------------------

    [Fact]
    public void Every_value_resolves_to_the_same_rows_after_import()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();
        foreach (var summary in rt.A.GetTableSummaries(rt.Source.Id).Select((s, i) => (s, i)))
        {
            var a = rt.A.LoadTable(summary.s.Id)!;
            var b = rt.B.LoadTable(rt.B.GetTableSummaries(rt.Imported.Id)[summary.i].Id)!;
            for (var v = a.Dice.Min - 40; v <= a.Dice.Max + 40; v++)
                Assert.Equal(Resolve(a, v), Resolve(b, v));
        }

        static string Resolve(RollableTable t, int v) =>
            string.Join(" / ", TableResolver.Resolve(t, TableClamp.LookupValue(t, v)).Results.Select(r => $"{r.Status}:{string.Join(",", r.Matches.Select(m => m.Text))}"));
    }

    [Fact]
    public void Clamp_still_applies_to_the_imported_table()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();
        var morale = rt.TableB("Morale");
        Assert.True(morale.ClampResultsToRange);
        Assert.Equal(3, TableClamp.LookupValue(morale, 1));
        Assert.Equal(18, TableClamp.LookupValue(morale, 20));
    }

    [Fact]
    public void Inline_dice_are_found_in_the_imported_text_exactly_as_before()
    {
        using var rt = new RoundTrip().Run();
        var row = rt.TableB("Weather").ResultSets[0].Entries[1];
        Assert.Equal(["d4", "2d6"], InlineDiceDetector.Detect(row.Text).Select(d => d.ToString()).ToArray());
        Assert.Equal(rt.TableA("Weather").ResultSets[0].Entries[1].Styles, row.Styles);
    }

    [Fact]
    public void Imported_tables_can_be_found_by_search_and_opened_and_followed()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();
        var main = new MainViewModel(rt.B, new FixedDice(8), (_, _) => true);
        main.SelectedCollection = main.Collections.Single(c => c.Id == rt.Imported.Id);

        main.TableFilter = "rum";
        Assert.Equal(["Rumors", "Rumors"], main.Tables.Select(t => t.Name).ToArray());

        main.TableFilter = "Encounter";
        main.OpenFirstMatchCommand.Execute(null);
        var roll = Assert.IsType<RollViewModel>(main.Current);
        roll.RollCommand.Execute(null);
        var merchants = roll.Steps[0].Outcomes[0].Lines.Single(l => l.Text == "Merchants");
        Assert.True(merchants.FollowCommand!.CanExecute(null));
        merchants.FollowCommand.Execute(null);
        Assert.Equal("Weather", roll.Steps[^1].Table.Name);
    }

    [Fact]
    public void Existing_external_exports_of_imported_tables_are_unchanged()
    {
        using var rt = new RoundTrip().Run();
        rt.ReopenB();
        foreach (var summary in rt.A.GetTableSummaries(rt.Source.Id).Select((s, i) => (s, i)))
        {
            var a = rt.A.LoadTable(summary.s.Id)!;
            var b = rt.B.LoadTable(rt.B.GetTableSummaries(rt.Imported.Id)[summary.i].Id)!;
            for (var s = 0; s < a.ResultSets.Count; s++)
            {
                Assert.Equal(TableTextExporter.Export(a, a.ResultSets[s]), TableTextExporter.Export(b, b.ResultSets[s]));
                Assert.Equal(TableTextExporter.Export(a, a.ResultSets[s], TableTextSeparator.Space), TableTextExporter.Export(b, b.ResultSets[s], TableTextSeparator.Space));
                Assert.Equal(TableTextExporter.ExportRows(a, a.ResultSets[s]), TableTextExporter.ExportRows(b, b.ResultSets[s]));
                Assert.Equal(Foundry(a, s), Foundry(b, s));
                Assert.Equal(TablesPlus(a, s), TablesPlus(b, s));
            }
        }

        static string Foundry(RollableTable t, int s) =>
            FoundryTableExporter.TryExport(t, t.ResultSets[s], out var x, out var error) ? x!.Json + string.Join("", x.Warnings) : "refused: " + error;
        static string TablesPlus(RollableTable t, int s) =>
            TablesPlusTableExporter.TryExport(t, t.ResultSets[s], out var x, out var error) ? x!.Json + string.Join("", x.Warnings) : "refused: " + error;
    }
}
