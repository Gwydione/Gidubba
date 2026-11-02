using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using TableForge.Domain;
using TableForge.Portable;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// A .tfcollection file is untrusted data: every way it can be wrong is refused, with a reason, BEFORE the database is touched —
/// and nothing is ever trimmed, repaired or quietly dropped to make it fit.
/// </summary>
public class PortableCollectionValidationTests
{
    /// <summary>A small valid file: two folders, a d20 with formatting and a link, a d66 and a d100, every table referenced by id.</summary>
    private const string Valid =
        """
        {
          "format": "TableForgeCollection",
          "formatVersion": 1,
          "collection": { "name": "Test Game" },
          "folders": [ { "id": "f1", "name": "Npcs" }, { "id": "f2", "name": "Places" } ],
          "tables": [
            { "id": "t1", "name": "Plain", "folder": "f1", "dice": "d20", "clampToRange": false, "resultSets": [
              { "name": "", "entries": [
                { "min": 1, "max": 10, "text": "Bold start", "styles": [[0, 4, "b"]] },
                { "min": 11, "max": 20, "text": "Go on", "link": { "table": "t2" } } ] } ] },
            { "id": "t2", "name": "D66", "dice": "d66", "clampToRange": false, "resultSets": [
              { "name": "", "entries": [ { "min": 11, "max": 66, "text": "Anything" } ] } ] },
            { "id": "t3", "name": "Hundred", "dice": "d100", "clampToRange": false, "resultSets": [
              { "name": "", "entries": [ { "min": 1, "max": 99, "display": "01–99", "text": "No" }, { "min": 100, "max": 100, "display": "00", "text": "Yes" } ] } ] }
          ]
        }
        """;

    private static JsonNode Doc() => JsonNode.Parse(Valid)!;
    private static JsonNode Table(JsonNode doc, int t) => doc["tables"]![t]!;
    private static JsonNode Row(JsonNode doc, int t, int e) => doc["tables"]![t]!["resultSets"]![0]!["entries"]![e]!;
    private static byte[] Bytes(JsonNode doc) => Encoding.UTF8.GetBytes(doc.ToJsonString());

    private static PortableReadResult Refused(byte[] bytes, PortableReadFailure expected, string? problem = null)
    {
        var read = PortableCollectionReader.Read(bytes);
        Assert.False(read.Succeeded);
        Assert.Equal(expected, read.Failure);
        if (problem is not null) Assert.Contains(read.Problems, p => p.Contains(problem, StringComparison.Ordinal));
        Assert.Contains("Nothing was", read.Summary);
        return read;
    }

    private static PortableReadResult Refused(Action<JsonNode> change, PortableReadFailure expected, string? problem = null)
    {
        var doc = Doc();
        change(doc);
        return Refused(Bytes(doc), expected, problem);
    }

    [Fact]
    public void The_valid_file_is_accepted_exactly_as_written()
    {
        var c = Read(Encoding.UTF8.GetBytes(Valid));
        Assert.Equal("Test Game", c.Name);
        Assert.Equal(["Npcs", "Places"], c.Folders.ToArray());
        Assert.Equal(0, c.Tables[0].FolderIndex);
        Assert.Null(c.Tables[1].FolderIndex);
        Assert.Equal(1, c.Tables[0].Table.ResultSets[0].Entries[1].LinkedTableId); // the position of t2
        Assert.Equal(DiceExpression.D66, c.Tables[1].Table.Dice);
        Assert.Equal("B 0+4", c.Tables[0].Table.ResultSets[0].Entries[0].Styles.ToString());
        Assert.Equal((100, 100, "00"), (c.Tables[2].Table.ResultSets[0].Entries[1].Min, c.Tables[2].Table.ResultSets[0].Entries[1].Max,
            c.Tables[2].Table.ResultSets[0].Entries[1].DisplayRange));
    }

    // ---- 1. the file ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Malformed_json_is_not_a_collection_file() =>
        Refused(Encoding.UTF8.GetBytes("{ \"format\": \"TableForgeCollection\", "), PortableReadFailure.NotACollectionFile, "not valid UTF-8 JSON");

    [Fact]
    public void Invalid_utf8_is_not_a_collection_file()
    {
        var bytes = Encoding.UTF8.GetBytes(Valid.Replace("Test Game", "Test XX"));
        var at = Array.IndexOf(bytes, (byte)'X');
        bytes[at] = 0xFF;
        bytes[at + 1] = 0xFE;
        Refused(bytes, PortableReadFailure.NotACollectionFile, "not valid UTF-8 JSON");
    }

    [Fact]
    public void An_escaped_half_surrogate_pair_is_refused()
    {
        var read = PortableCollectionReader.Read(Encoding.UTF8.GetBytes(Valid.Replace("\"Bold start\"", "\"Bold \\ud800 start\"")));
        Assert.False(read.Succeeded);
        Assert.Equal(PortableReadFailure.NotACollectionFile, read.Failure);
    }

    [Fact]
    public void A_repeated_property_makes_the_file_invalid_rather_than_ambiguous() =>
        Refused(Encoding.UTF8.GetBytes(Valid.Replace("\"name\": \"Test Game\"", "\"name\": \"Test Game\", \"name\": \"Other\"")),
            PortableReadFailure.NotACollectionFile);

    [Fact]
    public void A_utf8_byte_order_mark_is_accepted() =>
        Read([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Valid)]);

    [Fact]
    public void A_file_over_25_megabytes_is_refused_without_being_read()
    {
        using var file = new TempFile();
        using (var stream = File.Create(file.Path)) stream.SetLength(PortableFormat.MaxFileBytes + 1);
        var read = PortableCollectionReader.ReadFile(file.Path);
        Assert.Equal(PortableReadFailure.TooLarge, read.Failure);
        Assert.Contains("25 MB", read.Summary);
        Assert.Contains("Nothing was imported", read.Summary);
    }

    [Fact]
    public void A_missing_file_is_unreadable()
    {
        var read = PortableCollectionReader.ReadFile(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.tfcollection"));
        Assert.Equal(PortableReadFailure.Unreadable, read.Failure);
    }

    // ---- 2. the format ------------------------------------------------------------------------------------------------

    [Fact]
    public void Wrong_format_marker_is_refused() =>
        Refused(d => d["format"] = "SomethingElse", PortableReadFailure.NotACollectionFile, "TableForgeCollection");

    [Fact]
    public void A_newer_format_version_is_refused_as_newer()
    {
        var read = Refused(d => d["formatVersion"] = PortableFormat.NewestVersion + 1, PortableReadFailure.NewerFormat, $"version {PortableFormat.NewestVersion + 1}");
        Assert.Contains("newer file format", read.Summary);
        Assert.Contains("Nothing was changed", read.Summary);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void A_format_version_that_is_not_a_positive_whole_number_is_refused(string version) =>
        Refused(d => d["formatVersion"] = JsonNode.Parse(version), PortableReadFailure.NotACollectionFile);

    [Fact]
    public void Unknown_properties_are_ignored()
    {
        var doc = Doc();
        doc["publisher"] = "Someone";
        Table(doc, 0)["colour"] = "red";
        Row(doc, 0, 0)["note"] = new JsonObject { ["x"] = 1 };
        Assert.Equal("Test Game", Read(Bytes(doc)).Name);
    }

    [Fact]
    public void Missing_required_properties_are_refused()
    {
        Refused(d => d.AsObject().Remove("tables"), PortableReadFailure.NotACollectionFile, "\"tables\"");
        Refused(d => d.AsObject().Remove("folders"), PortableReadFailure.NotACollectionFile, "\"folders\"");
        Refused(d => d.AsObject().Remove("collection"), PortableReadFailure.NotACollectionFile, "\"collection\"");
        Refused(d => Table(d, 0).AsObject().Remove("dice"), PortableReadFailure.NotACollectionFile, "\"dice\"");
        Refused(d => Table(d, 0).AsObject().Remove("id"), PortableReadFailure.NotACollectionFile, "\"id\"");
        Refused(d => Row(d, 0, 0).AsObject().Remove("text"), PortableReadFailure.NotACollectionFile, "\"text\"");
        Refused(d => Row(d, 0, 0).AsObject().Remove("min"), PortableReadFailure.NotACollectionFile, "\"min\"");
        Refused(d => Row(d, 0, 0).AsObject().Remove("max"), PortableReadFailure.NotACollectionFile, "\"max\"");
    }

    [Fact]
    public void Wrongly_typed_known_properties_are_refused()
    {
        Refused(d => Table(d, 0)["clampToRange"] = "yes", PortableReadFailure.NotACollectionFile, "clampToRange");
        Refused(d => Row(d, 0, 0)["min"] = "1", PortableReadFailure.NotACollectionFile, "whole number");
        Refused(d => Row(d, 0, 0)["min"] = 1.5, PortableReadFailure.NotACollectionFile, "whole number");
        Refused(d => Row(d, 0, 0)["text"] = 5, PortableReadFailure.NotACollectionFile, "\"text\"");
        Refused(d => Table(d, 0)["folder"] = 1, PortableReadFailure.NotACollectionFile, "\"folder\"");
    }

    [Fact]
    public void Duplicate_file_local_ids_are_refused()
    {
        Refused(d => Table(d, 1)["id"] = "t1", PortableReadFailure.NotACollectionFile, "\"t1\" is used more than once");
        Refused(d => d["folders"]![1]!["id"] = "f1", PortableReadFailure.NotACollectionFile, "\"f1\" is used more than once");
    }

    // ---- 3. TableForge's rules ----------------------------------------------------------------------------------------

    [Fact]
    public void Blank_names_are_refused()
    {
        Refused(d => d["collection"]!["name"] = "  ", PortableReadFailure.InvalidContent, "The collection has no name");
        Refused(d => Table(d, 0)["name"] = "", PortableReadFailure.InvalidContent, "has no name");
        Refused(d => d["folders"]![0]!["name"] = " ", PortableReadFailure.InvalidContent, "has no name");
    }

    [Fact]
    public void Folder_names_differing_only_in_capitals_are_refused() =>
        Refused(d => d["folders"]![1]!["name"] = "NPCS", PortableReadFailure.InvalidContent, "two folders named");

    [Theory]
    [InlineData("d20+d6")]
    [InlineData("4d6kh3")]
    [InlineData("d66+1")]
    [InlineData("101d6")]
    [InlineData("d1")]
    public void Unsupported_dice_are_refused(string dice) =>
        Refused(d => Table(d, 0)["dice"] = dice, PortableReadFailure.InvalidContent, "does not support");

    [Fact]
    public void A_table_without_result_sets_or_a_result_set_without_rows_is_refused()
    {
        Refused(d => Table(d, 1)["resultSets"] = new JsonArray(), PortableReadFailure.InvalidContent, "has no result sets");
        Refused(d => Table(d, 1)["resultSets"]![0]!["entries"] = new JsonArray(), PortableReadFailure.InvalidContent, "has no rows");
    }

    [Fact]
    public void A_backwards_range_is_refused() =>
        Refused(d => { Row(d, 0, 0)["min"] = 10; Row(d, 0, 0)["max"] = 5; }, PortableReadFailure.InvalidContent, "runs backwards");

    [Theory]
    [InlineData(1_000_000)]
    [InlineData(-1_000_000)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void A_finite_bound_beyond_six_digits_is_refused_so_no_sentinel_can_be_smuggled_in(int bound) =>
        Refused(d => { Row(d, 0, 0)["min"] = Math.Min(bound, 1); Row(d, 0, 0)["max"] = Math.Max(bound, 1); }, PortableReadFailure.InvalidContent, "too large");

    [Fact]
    public void A_range_open_at_both_ends_is_refused() =>
        Refused(d => { Row(d, 0, 0)["min"] = null; Row(d, 0, 0)["max"] = null; Row(d, 0, 0)["display"] = "any"; },
            PortableReadFailure.InvalidContent, "open at both ends");

    [Fact]
    public void An_open_range_on_d66_is_refused() =>
        Refused(d => { Row(d, 1, 0)["max"] = null; Row(d, 1, 0)["display"] = "11+"; }, PortableReadFailure.InvalidContent, RangeText_OpenD66);

    private const string RangeText_OpenD66 = "Open-ended ranges are not available for d66 tables.";

    [Fact]
    public void An_open_range_must_keep_its_written_form() =>
        Refused(d => Row(d, 0, 1)["max"] = null, PortableReadFailure.InvalidContent, "written form");

    [Fact]
    public void Open_ranges_with_their_written_form_are_accepted()
    {
        var doc = Doc();
        Row(doc, 0, 0)["min"] = null; Row(doc, 0, 0)["max"] = 10; Row(doc, 0, 0)["display"] = "10 or less";
        Row(doc, 0, 1)["min"] = 11; Row(doc, 0, 1)["max"] = null; Row(doc, 0, 1)["display"] = "11+";
        var entries = Read(Bytes(doc)).Tables[0].Table.ResultSets[0].Entries;
        Assert.True(entries[0].IsOpenBelow);
        Assert.True(entries[1].IsOpenAbove);
    }

    [Theory]
    [InlineData("1–9")]      // different numbers
    [InlineData("10+")]      // open, but the file says finite
    [InlineData("10 or less")]
    public void A_written_range_that_means_something_else_is_refused(string display) =>
        Refused(d => Row(d, 0, 0)["display"] = display, PortableReadFailure.InvalidContent, "written range");

    [Theory]
    [InlineData("one to ten")]
    [InlineData("")]
    [InlineData("1234567")]
    public void A_written_range_TableForge_cannot_read_is_refused(string display) =>
        Refused(d => Row(d, 0, 0)["display"] = display, PortableReadFailure.InvalidContent, "is not a range Gidubba reads");

    [Fact]
    public void A_d100_written_range_is_read_with_the_tables_own_dice()
    {
        // "00" is 100 only on a d100: the same written range on a d20 is 0, which does not match 100.
        Refused(d => Table(d, 2)["dice"] = "d20", PortableReadFailure.InvalidContent, "written range \"00\"");
    }

    [Theory]
    [InlineData("""[[0, 4]]""", "must be [start, length, style]")]
    [InlineData("""[[0, 4, 1]]""", "must be [start, length, style]")]
    [InlineData("""[["0", 4, "b"]]""", "must be [start, length, style]")]
    [InlineData("""[[0, 4, "u"]]""", "unknown style")]
    [InlineData("""[[0, 4, "B"]]""", "unknown style")]
    [InlineData("""[[0, 0, "b"]]""", "is empty")]
    [InlineData("""[[0, -1, "b"]]""", "is empty")]
    [InlineData("""[[-1, 2, "b"]]""", "outside the text")]
    [InlineData("""[[5, 6, "b"]]""", "outside the text")]
    [InlineData("""[[0, 4, "b"], [2, 3, "i"]]""", "overlaps")]
    [InlineData("""[[5, 2, "b"], [0, 2, "i"]]""", "overlaps or comes before")]
    [InlineData("""{"v":1}""", "must be a list")]
    public void Malformed_formatting_is_refused_never_dropped(string runs, string problem)
    {
        var read = PortableCollectionReader.Read(Bytes(Doc().Also(d => Row(d, 0, 0)["styles"] = JsonNode.Parse(runs))));
        Assert.False(read.Succeeded);
        Assert.Contains(read.Problems, p => p.Contains(problem));
    }

    [Fact]
    public void A_formatting_run_that_splits_a_surrogate_pair_is_refused()
    {
        Refused(d => { Row(d, 0, 0)["text"] = "🐉 fire"; Row(d, 0, 0)["styles"] = JsonNode.Parse("[[1, 2, \"b\"]]"); },
            PortableReadFailure.InvalidContent, "splits a character");
        Refused(d => { Row(d, 0, 0)["text"] = "🐉 fire"; Row(d, 0, 0)["styles"] = JsonNode.Parse("[[0, 1, \"i\"]]"); },
            PortableReadFailure.InvalidContent, "splits a character");
    }

    [Fact]
    public void Formatting_whole_surrogate_pairs_and_every_style_is_accepted()
    {
        var doc = Doc();
        Row(doc, 0, 0)["text"] = "🐉 fire and ice";
        Row(doc, 0, 0)["styles"] = JsonNode.Parse("""[[0, 2, "b"], [3, 4, "i"], [12, 3, "bi"]]""");
        Assert.Equal("B 0+2, I 3+4, BI 12+3", Read(Bytes(doc)).Tables[0].Table.ResultSets[0].Entries[0].Styles.ToString());
    }

    [Fact]
    public void Clamp_is_refused_where_TableForge_could_not_clamp()
    {
        Refused(d => Table(d, 1)["clampToRange"] = true, PortableReadFailure.InvalidContent, TableClamp.D66Message);
        Refused(d =>
        {
            Table(d, 0)["clampToRange"] = true;
            Table(d, 0)["resultSets"]!.AsArray().Add(JsonNode.Parse("""{ "name": "Other", "entries": [ { "min": 1, "max": 5, "text": "x" } ] }"""));
        }, PortableReadFailure.InvalidContent, TableClamp.IncompatibleMessage);
    }

    [Fact]
    public void Clamp_is_accepted_where_TableForge_can_clamp()
    {
        var doc = Doc();
        Table(doc, 0)["clampToRange"] = true;
        Assert.True(Read(Bytes(doc)).Tables[0].Table.ClampResultsToRange);
    }

    [Fact]
    public void A_link_to_both_a_table_and_a_name_is_refused() =>
        Refused(d => Row(d, 0, 1)["link"]!["unresolved"] = "Elsewhere", PortableReadFailure.InvalidContent, "never both");

    [Fact]
    public void An_unresolved_link_needs_a_name()
    {
        Refused(d => Row(d, 0, 1)["link"] = JsonNode.Parse("""{ "unresolved": "  " }"""), PortableReadFailure.InvalidContent, "needs a name");
        Refused(d => Row(d, 0, 1)["link"] = new JsonObject(), PortableReadFailure.NotACollectionFile, "must name a table or an unresolved name");
    }

    // ---- 4. references --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_broken_folder_reference_is_an_invalid_relationship()
    {
        var read = Refused(d => Table(d, 0)["folder"] = "f9", PortableReadFailure.InvalidRelationship, "folder \"f9\"");
        Assert.Contains("invalid table relationship", read.Summary);
    }

    [Fact]
    public void A_broken_table_reference_is_an_invalid_relationship() =>
        Refused(d => Row(d, 0, 1)["link"]!["table"] = "t9", PortableReadFailure.InvalidRelationship, "table \"t9\"");

    [Fact]
    public void Self_links_and_cycles_are_valid()
    {
        var doc = Doc();
        Row(doc, 1, 0)["link"] = JsonNode.Parse("""{ "table": "t1" }"""); // t1 -> t2 -> t1
        Row(doc, 2, 0)["link"] = JsonNode.Parse("""{ "table": "t3" }"""); // t3 -> t3
        var c = Read(Bytes(doc));
        Assert.Equal(0, c.Tables[1].Table.ResultSets[0].Entries[0].LinkedTableId);
        Assert.Equal(2, c.Tables[2].Table.ResultSets[0].Entries[0].LinkedTableId);
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        var read = Refused(d =>
        {
            Table(d, 0)["dice"] = "d7x";
            Row(d, 1, 0)["min"] = 70;
            Row(d, 2, 0)["link"] = JsonNode.Parse("""{ "table": "nowhere" }""");
        }, PortableReadFailure.InvalidContent);
        Assert.True(read.Problems.Count >= 3, string.Join("\n", read.Problems));
    }

    [Fact]
    public void Names_and_text_are_kept_exactly_never_trimmed()
    {
        var doc = Doc();
        Row(doc, 0, 0)["text"] = "  spaced  ";
        Row(doc, 0, 0).AsObject().Remove("styles");
        Table(doc, 0)["resultSets"]![0]!["name"] = " Set ";
        var c = Read(Bytes(doc));
        Assert.Equal("  spaced  ", c.Tables[0].Table.ResultSets[0].Entries[0].Text);
        Assert.Equal(" Set ", c.Tables[0].Table.ResultSets[0].Name);
    }

    // ---- refused through the app: nothing is asked, nothing is changed -------------------------------------------------------

    public static TheoryData<string> BadFiles => new()
    {
        "{ broken",
        Valid.Replace("TableForgeCollection", "Nope"),
        Valid.Replace("\"formatVersion\": 1", "\"formatVersion\": 7"),
        Valid.Replace("\"d66\"", "\"d66+2\""),
        Valid.Replace("\"min\": 11, \"max\": 66", "\"min\": 66, \"max\": 11"),
        Valid.Replace("\"min\": 11, \"max\": 66", "\"min\": 11, \"max\": 6600000"),
        Valid.Replace("\"min\": 11, \"max\": 66", "\"min\": null, \"max\": null, \"display\": \"x\""),
        Valid.Replace("\"min\": 11, \"max\": 66", "\"min\": 11, \"max\": null, \"display\": \"11+\""),
        Valid.Replace("\"display\": \"01–99\"", "\"display\": \"01–98\""),
        Valid.Replace("[[0, 4, \"b\"]]", "[[0, 4]]"),
        Valid.Replace("[[0, 4, \"b\"]]", "[[0, 4, \"b\"], [3, 2, \"i\"]]"),
        Valid.Replace("\"Bold start\"", "\"🐉 start\"").Replace("[[0, 4, \"b\"]]", "[[1, 2, \"b\"]]"),
        Valid.Replace("\"folder\": \"f1\"", "\"folder\": \"f7\""),
        Valid.Replace("{ \"table\": \"t2\" }", "{ \"table\": \"t7\" }"),
        Valid.Replace("\"id\": \"t3\"", "\"id\": \"t2\""),
        Valid.Replace("\"Places\"", "\"npcs\""),
        Valid.Replace("\"dice\": \"d66\", \"clampToRange\": false", "\"dice\": \"d66\", \"clampToRange\": true"),
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public void A_refused_file_changes_nothing_and_asks_nothing(string content)
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        SeedOther(db);
        using var file = new TempFile();
        File.WriteAllText(file.Path, content, new UTF8Encoding(false));
        var asked = new List<string>();
        var shown = new List<string>();
        var main = new MainViewModel(db, new FixedDice(1), (_, _) => true, chooseImportFile: () => file.Path,
            confirmImport: m => { asked.Add(m); return true; }, showMessage: shown.Add);
        var before = Snapshot(temp.Path);

        main.ImportCollectionCommand.Execute(null);

        Assert.Equal(before, Snapshot(temp.Path));
        Assert.Empty(asked);
        Assert.Contains("Nothing was", main.Status);
        Assert.Single(shown);
        Assert.Single(main.Collections);
    }

    [Fact]
    public void A_file_over_the_size_limit_changes_nothing()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        SeedOther(db);
        using var file = new TempFile();
        using (var stream = File.Create(file.Path)) stream.SetLength(PortableFormat.MaxFileBytes + 1);
        var main = new MainViewModel(db, new FixedDice(1), (_, _) => true, chooseImportFile: () => file.Path, confirmImport: _ => true);
        var before = Snapshot(temp.Path);

        main.ImportCollectionCommand.Execute(null);

        Assert.Equal(before, Snapshot(temp.Path));
        Assert.Contains("too large", main.Status);
    }
}
