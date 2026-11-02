using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using TableForge.Domain;
using TableForge.Portable;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// RC26: table descriptions in .tfcollection files. A file is written in the lowest format version that carries all of its
/// content: no descriptions → version 1, exactly the RC25 shape; any description → version 2, which RC25 refuses as a newer format
/// instead of importing it and silently dropping the descriptions. This version reads both, and keeps a description exactly.
/// </summary>
public class PortableCollectionDescriptionTests
{
    private const string Multiline = "Roll when entering a new region\nor when the weather changes.\n\n  Indented note, kept.";
    private const string Unicode = "Apply +1 if the NPC is “already” favourable 🙂 — 雨の日には。";

    /// <summary>The table properties RC25 (format version 1) writes and knows.</summary>
    private static readonly string[] Rc25TableProperties = ["id", "name", "folder", "dice", "clampToRange", "resultSets"];

    /// <summary>A small valid RC25 (version 1) file, as RC25 itself writes one: no description anywhere.</summary>
    private const string Rc25File =
        """
        {
          "format": "TableForgeCollection",
          "formatVersion": 1,
          "generator": "TableForge 1.0.0-rc25",
          "collection": { "name": "Old Game" },
          "folders": [ { "id": "f1", "name": "Npcs" } ],
          "tables": [
            { "id": "t1", "name": "Mood", "folder": "f1", "dice": "d6", "clampToRange": false, "resultSets": [
              { "name": "", "entries": [ { "min": 1, "max": 3, "text": "Calm", "link": { "table": "t2" } }, { "min": 4, "max": 6, "text": "Angry" } ] } ] },
            { "id": "t2", "name": "Fate", "dice": "d20", "clampToRange": true, "resultSets": [
              { "name": "", "entries": [ { "min": 1, "max": 10, "text": "Bad" }, { "min": 11, "max": 20, "text": "Good" } ] } ] }
          ]
        }
        """;

    private static JsonNode Json(byte[] bytes) => JsonNode.Parse(bytes)!;
    private static byte[] Bytes(JsonNode doc) => Encoding.UTF8.GetBytes(doc.ToJsonString());

    private static void SetDescription(Data.AppDatabase db, Collection c, string table, string description)
    {
        var t = db.LoadTable(db.GetTableSummaries(c.Id).First(s => s.Name == table).Id)!;
        t.Description = description;
        db.SaveTable(t);
    }

    private static PortableReadResult Refused(JsonNode doc, PortableReadFailure expected, string problem)
    {
        var read = PortableCollectionReader.Read(Bytes(doc));
        Assert.False(read.Succeeded);
        Assert.Equal(expected, read.Failure);
        Assert.Contains(read.Problems, p => p.Contains(problem, StringComparison.Ordinal));
        Assert.Contains("Nothing was", read.Summary);
        return read;
    }

    // ---- which version is written ------------------------------------------------------------------------------------------

    [Fact]
    public void A_collection_with_no_descriptions_is_written_as_version_1_in_the_rc25_shape()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var file = Json(ExportBytes(db, SeedMythic(db)));

        Assert.Equal(1, (int?)file["formatVersion"]);
        Assert.Equal(["format", "formatVersion", "generator", "collection", "folders", "tables"], file.AsObject().Select(p => p.Key).ToArray());
        foreach (var table in file["tables"]!.AsArray())
            Assert.All(table!.AsObject().Select(p => p.Key), key => Assert.Contains(key, Rc25TableProperties));
        Assert.DoesNotContain("description", Text(ExportBytes(db, db.GetCollections().Single())));
    }

    [Fact]
    public void Any_description_makes_it_version_2_and_only_described_tables_carry_one()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = SeedMythic(db);
        SetDescription(db, c, "Weather", Multiline);

        var file = Json(ExportBytes(db, c));
        Assert.Equal(2, (int?)file["formatVersion"]);
        Assert.Equal(PortableFormat.DescriptionsVersion, (int?)file["formatVersion"]); // still 2: nothing here needs version 3
        var described = file["tables"]!.AsArray().Where(t => t!["description"] is not null).ToList();
        Assert.Equal(Multiline, (string?)Assert.Single(described)!["description"]);
        Assert.Equal("Weather", (string?)described[0]!["name"]);

        SetDescription(db, c, "Weather", "");                                                         // and back again
        Assert.Equal(1, (int?)Json(ExportBytes(db, c))["formatVersion"]);
    }

    // ---- round trips -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Multiline_and_unicode_descriptions_round_trip_exactly_through_a_clean_database()
    {
        using var tempA = new TempDatabase();
        using var tempB = new TempDatabase();
        var a = tempA.Open();
        var source = SeedMythic(a);
        SetDescription(a, source, "Weather", Multiline);
        SetDescription(a, source, "Encounter", Unicode);
        SetDescription(a, source, "Loot", new string('é', TableDescription.MaxLength));

        var file = ExportBytes(a, source);
        var b = tempB.Open();
        var imported = b.ImportCollection(Read(file));
        b.Dispose();
        b = tempB.Open();

        Assert.Equal(PortableFixtures.Describe(a, source.Id), PortableFixtures.Describe(b, imported.Id));
        var tables = b.GetTableSummaries(imported.Id).Select(s => b.LoadTable(s.Id)!).ToList();
        Assert.Equal(Multiline, tables.Single(t => t.Name == "Weather").Description);
        Assert.Equal(Unicode, tables.Single(t => t.Name == "Encounter").Description);
        Assert.Equal(tables.Count - 3, tables.Count(t => t.Description.Length == 0));

        Assert.Equal(Text(file), Text(ExportBytes(b, imported)));                                     // export → import → export: the same file
        Assert.Equal(file, ExportBytes(a, source));                                                    // and exporting twice: the same bytes
        Assert.DoesNotContain("2147483647", Text(file));                                               // open bounds are still null, never sentinels
        Assert.DoesNotContain("-2147483648", Text(file));
        Assert.DoesNotContain($"\"{source.Id}\"", Text(file));
    }

    [Fact]
    public void An_rc25_file_imports_with_every_description_empty()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = db.ImportCollection(Read(Encoding.UTF8.GetBytes(Rc25File)));

        var tables = db.GetTableSummaries(c.Id).Select(s => db.LoadTable(s.Id)!).ToList();
        Assert.Equal(["Fate", "Mood"], tables.Select(t => t.Name).ToArray());
        Assert.All(tables, t => Assert.Equal("", t.Description));
        Assert.True(tables[0].ClampResultsToRange);
        Assert.Equal(tables[0].Id, tables[1].ResultSets[0].Entries[0].LinkedTableId);
    }

    [Fact]
    public void A_valid_files_description_is_kept_exactly_without_review_normalization()
    {
        var doc = JsonNode.Parse(Rc25File)!;
        doc["formatVersion"] = 2;
        doc["tables"]![0]!["description"] = "  padded\r\nwith CRLF and trailing space  ";
        Assert.Equal("  padded\r\nwith CRLF and trailing space  ", Read(Bytes(doc)).Tables[0].Table.Description);
    }

    [Fact]
    public void A_description_in_a_hand_made_version_1_file_is_kept_rather_than_dropped()
    {
        var doc = JsonNode.Parse(Rc25File)!;
        doc["tables"]![1]!["description"] = "Roll at dawn.";
        Assert.Equal("Roll at dawn.", Read(Bytes(doc)).Tables[1].Table.Description);
    }

    [Fact]
    public void An_empty_description_property_reads_as_empty()
    {
        var doc = JsonNode.Parse(Rc25File)!;
        doc["formatVersion"] = 2;
        doc["tables"]![0]!["description"] = "";
        Assert.Equal("", Read(Bytes(doc)).Tables[0].Table.Description);
    }

    // ---- what RC25 does with a version 2 file -----------------------------------------------------------------------------

    [Fact]
    public void A_file_one_version_past_what_a_reader_knows_is_refused_as_newer_so_rc25_refuses_version_2()
    {
        // RC25 reads version 1 only, and refuses anything newer as NewerFormat before reading a single table — so a version 2
        // file never imports there with its descriptions dropped. The same rule, seen from this build, one version ahead:
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = SeedMythic(db);
        SetDescription(db, c, "Weather", "Roll at dawn.");
        var file = Json(ExportBytes(db, c));
        Assert.True((int)file["formatVersion"]! > 1);                                                 // beyond RC25's newest

        file["formatVersion"] = PortableFormat.NewestVersion + 1;
        var read = Refused(file, PortableReadFailure.NewerFormat, $"version {PortableFormat.NewestVersion + 1}");
        Assert.Contains("newer file format", read.Summary);
    }

    // ---- validation --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[\"Roll at dawn.\"]")]
    [InlineData("{\"text\":\"Roll at dawn.\"}")]
    public void A_description_that_is_not_text_is_refused(string value)
    {
        var doc = JsonNode.Parse(Rc25File)!;
        doc["formatVersion"] = 2;
        doc["tables"]![0]!.AsObject().Add("description", JsonNode.Parse(value));
        Refused(doc, PortableReadFailure.NotACollectionFile, "\"description\" must be text");
    }

    [Fact]
    public void A_description_over_2000_characters_is_refused_never_cut_short()
    {
        var doc = JsonNode.Parse(Rc25File)!;
        doc["formatVersion"] = 2;
        doc["tables"]![0]!["description"] = new string('x', TableDescription.MaxLength + 1);
        Refused(doc, PortableReadFailure.InvalidContent, "2,001 characters");

        doc["tables"]![0]!["description"] = string.Concat(Enumerable.Repeat("🙂", 1001));           // 2,002 UTF-16 characters
        Refused(doc, PortableReadFailure.InvalidContent, "the most Gidubba keeps is 2,000");

        doc["tables"]![0]!["description"] = new string('x', TableDescription.MaxLength);
        Assert.Equal(TableDescription.MaxLength, Read(Bytes(doc)).Tables[0].Table.Description.Length);
    }

    [Fact]
    public void A_broken_character_in_a_description_is_refused()
    {
        var text = Rc25File.Replace("\"formatVersion\": 1", "\"formatVersion\": 2")
            .Replace("\"name\": \"Mood\",", "\"name\": \"Mood\", \"description\": \"Bad \\ud800 note\",");
        var read = PortableCollectionReader.Read(Encoding.UTF8.GetBytes(text));
        Assert.False(read.Succeeded);
        Assert.Equal(PortableReadFailure.NotACollectionFile, read.Failure);
        Assert.Contains(read.Problems, p => p.Contains("not valid Unicode", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_utf8_inside_a_description_is_refused()
    {
        var doc = JsonNode.Parse(Rc25File)!;
        doc["formatVersion"] = 2;
        doc["tables"]![0]!["description"] = "Roll XY now";
        var bytes = Bytes(doc);
        var at = Encoding.UTF8.GetString(bytes).IndexOf("XY", StringComparison.Ordinal);
        bytes[at] = 0xFF;
        bytes[at + 1] = 0xFE;
        var read = PortableCollectionReader.Read(bytes);
        Assert.Equal(PortableReadFailure.NotACollectionFile, read.Failure);
    }

    [Fact]
    public void A_refused_file_changes_nothing_in_the_database()
    {
        using var temp = new TempDatabase();
        using var file = new TempFile();
        var db = temp.Open();
        SeedOther(db);
        var doc = JsonNode.Parse(Rc25File)!;
        doc["formatVersion"] = 2;
        doc["tables"]![1]!["description"] = new string('x', TableDescription.MaxLength + 1);
        File.WriteAllBytes(file.Path, Bytes(doc));
        var shown = new List<string>();
        var main = new MainViewModel(db, new FixedDice(1), (_, _) => true, chooseImportFile: () => file.Path, confirmImport: _ => true, showMessage: shown.Add);
        var before = Snapshot(temp.Path);

        main.ImportCollectionCommand.Execute(null);

        Assert.Equal(before, Snapshot(temp.Path));
        Assert.Contains(shown, m => m.Contains("Nothing was imported") && m.Contains("description"));
        Assert.Equal(["Other Game"], main.Collections.Select(c => c.Name).ToArray());
    }
}
