using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Result-set editing, link authoring, table selection, editing/renaming and deleting, through the view models.</summary>
public class ReviewEditingTests
{
    private sealed class World : IDisposable
    {
        public TempDatabase Temp { get; } = new();
        public AppDatabase Db { get; }
        public Collection Collection { get; }
        public FixedDice Dice { get; } = new(6);
        public List<string> ConfirmPrompts { get; } = [];
        public bool ConfirmAnswer { get; set; } = true;
        public MainViewModel Main { get; private set; } = null!;

        public World()
        {
            Db = Temp.Open();
            Collection = Db.CreateCollection("Dungeon");
        }

        /// <summary>Creates the main view model after seeding, as at application start.</summary>
        public MainViewModel Start()
        {
            Main = new MainViewModel(Db, Dice, (message, _) => { ConfirmPrompts.Add(message); return ConfirmAnswer; });
            return Main;
        }

        public TableSummary Summary(string name) => Main.Tables.Single(t => t.Name == name);

        public ReviewViewModel Edit(string name)
        {
            Main.SelectedTable = Summary(name);
            Main.EditTableCommand.Execute(null);
            return Assert.IsType<ReviewViewModel>(Main.Current);
        }

        public void Dispose() => Temp.Dispose();
    }

    private static ResultSetEditorViewModel Set(ReviewViewModel r, string name) => r.ResultSets.Single(s => s.Name == name);

    // ---- multiple result sets ---------------------------------------------------------------

    [Fact]
    public void Saved_multi_set_table_opens_for_editing_with_every_set_and_can_be_switched()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();

        var review = w.Edit("Room Features");

        Assert.Equal("Edit table", review.HeadingTitle);
        Assert.Equal("Room Features", review.TableName);
        Assert.Equal("d100", review.DiceText);
        Assert.Contains("No original text", review.SourceText);
        Assert.Equal(["Ambient", "Noise", "General Feature"], review.ResultSets.Select(s => s.Name).ToArray());
        Assert.Same(review.ResultSets[0], review.SelectedResultSet);
        Assert.Equal(4, review.Rows.Count);

        review.SelectedResultSet = review.ResultSets[1];
        Assert.Equal(5, review.Rows.Count);
        Assert.Equal(("72–00", "Low chanting"), (review.Rows[4].RangeText, review.Rows[4].Text));

        review.SelectedResultSet = review.ResultSets[2];
        Assert.Equal(["Cracked stone walls", "Broken furniture", "Grated floors reveal dozens of people below", "Carved pillars"],
            review.Rows.Select(r => r.Text).ToArray());

        Assert.Empty(review.ValidationNotes);
        Assert.True(review.CanSave);
    }

    [Fact]
    public void Validation_notes_are_per_result_set_and_never_leak_between_sets()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();
        var review = w.Edit("Room Features");
        var noise = Set(review, "Noise");

        noise.Rows[3].RangeText = "67-70"; // Hissing now stops at 70: Noise has a gap at 71

        Assert.Equal(["Noise: No row covers 71."], review.ValidationNotes.ToArray());
        Assert.True(noise.HasNotes);
        Assert.False(Set(review, "Ambient").HasNotes);
        Assert.False(Set(review, "General Feature").HasNotes);
        Assert.All(Set(review, "Ambient").Rows, r => Assert.False(r.HasNotes));

        // A different problem in another set is reported alongside, under its own name.
        Set(review, "Ambient").Rows[0].RangeText = "1-40";
        Assert.Contains("Ambient: Rows 1 and 2 both cover 31–40.", review.ValidationNotes);
        Assert.Contains("Noise: No row covers 71.", review.ValidationNotes);
        Assert.Equal(2, review.ValidationNotes.Count);
        Assert.True(review.CanSave); // facts, not blockers
    }

    [Fact]
    public void Add_result_set_starts_valid_and_can_be_named_edited_and_saved()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();
        var review = w.Edit("Room Features");

        review.AddResultSetCommand.Execute(null);

        Assert.Equal(4, review.ResultSets.Count);
        var added = review.SelectedResultSet;
        Assert.Same(review.ResultSets[3], added);
        Assert.Equal("Result set 4", added.DisplayName);
        Assert.Equal("1-100", Assert.Single(added.Rows).RangeText);
        Assert.Empty(review.ValidationNotes);

        added.Name = "Lighting";
        Assert.Equal("Lighting", added.DisplayName);
        added.Rows[0].Text = "Dim torchlight";
        added.AddRowCommand.Execute(null);                 // proposes the number after the highest used...
        Assert.Equal("101", added.Rows[1].RangeText);
        added.Rows[0].RangeText = "1-60";                  // ...then the user splits the range properly
        added.Rows[1].RangeText = "61-100";
        added.Rows[1].Text = "Pitch dark";
        Assert.Empty(review.ValidationNotes);

        review.SaveCommand.Execute(null);

        var saved = w.Db.LoadTable(w.Summary("Room Features").Id)!;
        Assert.Equal(["Ambient", "Noise", "General Feature", "Lighting"], saved.ResultSets.Select(s => s.Name).ToArray());
        Assert.Equal([(1, 60, "Dim torchlight"), (61, 100, "Pitch dark")], saved.ResultSets[3].Entries.Select(e => (e.Min, e.Max, e.Text)).ToArray());
        Assert.Equal(4, TableResolver.Resolve(saved, 68).Results.Count);
    }

    [Fact]
    public void Delete_result_set_renumbers_and_a_table_never_drops_to_zero_sets()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();
        var review = w.Edit("Room Features");
        review.AddResultSetCommand.Execute(null);
        review.AddResultSetCommand.Execute(null);
        Assert.Equal("Result set 5", review.SelectedResultSet.DisplayName);

        review.SelectedResultSet = review.ResultSets[1]; // Noise
        review.DeleteResultSetCommand.Execute(null);

        Assert.Equal(["Ambient", "General Feature", "", ""], review.ResultSets.Select(s => s.Name).ToArray());
        Assert.Equal(["Ambient", "General Feature", "Result set 3", "Result set 4"], review.ResultSets.Select(s => s.DisplayName).ToArray());
        Assert.Same(review.ResultSets[1], review.SelectedResultSet); // selection moves to a neighbour

        while (review.ResultSets.Count > 1)
        {
            Assert.True(review.DeleteResultSetCommand.CanExecute(null));
            review.DeleteResultSetCommand.Execute(null);
        }

        Assert.False(review.DeleteResultSetCommand.CanExecute(null));
        review.DeleteResultSetCommand.Execute(null); // does nothing rather than leave a table without sets
        Assert.Single(review.ResultSets);
        Assert.NotNull(review.SelectedResultSet);
        Assert.True(review.CanSave);
    }

    [Fact]
    public void Rows_can_be_added_and_deleted_within_the_selected_set_only()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();
        var review = w.Edit("Room Features");
        var ambient = Set(review, "Ambient");
        var noiseRowsBefore = Set(review, "Noise").Rows.Count;

        ambient.AddRowCommand.Execute(null);
        Assert.Equal(5, ambient.Rows.Count);
        // RC23: Ambient still covers every d100 roll, so a row beyond it reads as authored for modified rolls: information, not a warning.
        Assert.Empty(review.ValidationNotes);
        Assert.Contains("Ambient: Row 5 includes 101, outside the natural d100 range (1–00); only a modified roll reaches it.", review.InfoNotes);

        ambient.Rows[4].DeleteCommand.Execute(null);
        Assert.Equal(4, ambient.Rows.Count);
        Assert.Empty(review.ValidationNotes);

        ambient.Rows[0].DeleteCommand.Execute(null); // delete "01–30"
        Assert.Equal([1, 2, 3], ambient.Rows.Select(r => r.Number).ToArray());
        Assert.Equal("Ambient: No row covers 1–30.", review.ValidationNotes.Single());
        Assert.Equal(noiseRowsBefore, Set(review, "Noise").Rows.Count);
    }

    [Fact]
    public void A_set_with_no_rows_blocks_saving_but_blank_text_does_not()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();
        var review = w.Edit("Room Features");
        var ambient = Set(review, "Ambient");

        ambient.Rows[0].Text = "";                 // blank display text stays allowed
        Assert.True(review.CanSave);

        foreach (var row in ambient.Rows.ToList()) row.DeleteCommand.Execute(null);

        Assert.False(review.CanSave);
        Assert.Contains("Result set 'Ambient' has no entries.", review.Blockers);
        Assert.False(review.SaveCommand.CanExecute(null));
        Assert.Equal("Ambient: No row covers 1–00.", review.ValidationNotes.First());
    }

    [Fact]
    public void Editing_and_saving_an_unchanged_table_preserves_ids_ranges_display_forms_and_order()
    {
        using var w = new World();
        var original = w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        w.Start();

        w.Edit("Room Features").SaveCommand.Execute(null);

        var saved = w.Db.LoadTable(original.Id)!;
        Assert.Single(w.Db.GetTableSummaries(w.Collection.Id)); // updated in place, not duplicated
        Assert.Equal(original.ResultSets.Select(s => s.Name), saved.ResultSets.Select(s => s.Name));
        Assert.Equal(
            original.ResultSets.SelectMany(s => s.Entries).Select(e => (e.Min, e.Max, e.DisplayRange, e.Text)),
            saved.ResultSets.SelectMany(s => s.Entries).Select(e => (e.Min, e.Max, e.DisplayRange, e.Text)));
        Assert.IsType<RollViewModel>(w.Main.Current); // returns to rolling the saved table
    }

    // ---- link authoring ---------------------------------------------------------------------

    [Fact]
    public void Pasting_text_that_resembles_a_table_name_creates_no_link()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.ScavengedItems(w.Collection.Id));
        var main = w.Start();

        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "2d6 SCAVENGING\n2-5 Nothing useful\n6-8 1x Scavenged Item\n9-10 2x Scavenged Items\n11-12 Valuable find";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);

        var review = (ReviewViewModel)main.Current!;
        Assert.All(review.Rows, r => Assert.Equal(LinkChoiceKind.None, r.SelectedLink.Kind));
        Assert.Equal(["(no link)", "Unresolved name…", "Scavenged Items"], review.Rows[0].LinkChoices.Select(c => c.Label).ToArray());
    }

    [Fact]
    public void A_row_can_be_linked_to_a_table_in_the_same_collection_and_the_link_is_saved()
    {
        using var w = new World();
        var items = w.Db.SaveTable(Fixtures.ScavengedItems(w.Collection.Id));
        var otherCollection = w.Db.CreateCollection("Elsewhere");
        w.Db.SaveTable(Fixtures.RoomFeatures(otherCollection.Id));
        var main = w.Start();
        main.SelectedCollection = main.Collections.Single(c => c.Id == w.Collection.Id);

        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "2d6 SCAVENGING\n2-5 Nothing useful\n6-8 1x Scavenged Item\n9-10 2x Scavenged Items\n11-12 Valuable find";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)main.Current!;

        Assert.DoesNotContain(review.Rows[0].LinkChoices, c => c.Label == "Room Features"); // no cross-collection links offered

        var itemsChoice = review.Rows[1].LinkChoices.Single(c => c.TableId == items.Id);
        review.Rows[1].SelectedLink = itemsChoice;
        review.Rows[2].SelectedLink = itemsChoice;
        review.SaveCommand.Execute(null);

        var saved = w.Db.LoadTable(w.Summary("Scavenging").Id)!.ResultSets[0].Entries;
        Assert.Equal([null, items.Id, items.Id, null], saved.Select(e => e.LinkedTableId).ToArray());
        Assert.All(saved, e => Assert.Null(e.UnresolvedLinkName));
        Assert.Equal("2x Scavenged Items", saved[2].Text); // display text untouched

        // And it works at runtime: roll, follow, child not rolled.
        var roll = (RollViewModel)w.Main.Current!;
        roll.RollCommand.Execute(null);
        Assert.Equal(6, w.Dice.Value);
        Assert.Equal("Open Scavenged Items", Assert.Single(roll.Results).FollowLabel);
    }

    [Fact]
    public void An_unresolved_link_needs_a_name_and_saves_as_a_placeholder()
    {
        using var w = new World();
        var main = w.Start();
        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "d6 Loot\n1-3 Coin\n4-6 A gem";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)main.Current!;
        var row = review.Rows[1];

        row.SelectedLink = row.LinkChoices.Single(c => c.Kind == LinkChoiceKind.Unresolved);

        Assert.True(row.IsUnresolvedLink);
        Assert.False(review.CanSave);
        Assert.Contains(review.Blockers, b => b.Contains("unresolved link"));

        row.UnresolvedName = "Gem Types";
        Assert.True(review.CanSave);

        review.SaveCommand.Execute(null);
        var entries = w.Db.LoadTable(w.Summary("Loot").Id)!.ResultSets[0].Entries;
        Assert.Equal((null, "Gem Types"), (entries[1].LinkedTableId, entries[1].UnresolvedLinkName));
        Assert.Equal((null, null), (entries[0].LinkedTableId, entries[0].UnresolvedLinkName));
    }

    [Fact]
    public void An_existing_unresolved_link_is_shown_when_editing_and_can_be_cleared()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.Scavenging(w.Collection.Id, null, unresolvedName: "Scavenged Items"));
        w.Start();

        var review = w.Edit("Scavenging");

        var row = review.Rows[1];
        Assert.Equal(LinkChoiceKind.Unresolved, row.SelectedLink.Kind);
        Assert.Equal("Scavenged Items", row.UnresolvedName);
        Assert.True(review.CanSave);

        row.SelectedLink = row.LinkChoices.Single(c => c.Kind == LinkChoiceKind.None);
        Assert.False(row.IsUnresolvedLink);
        review.SaveCommand.Execute(null);

        var entries = w.Db.LoadTable(w.Summary("Scavenging").Id)!.ResultSets[0].Entries;
        Assert.Equal((null, null), (entries[1].LinkedTableId, entries[1].UnresolvedLinkName));
        Assert.Equal((null, "Scavenged Items"), (entries[2].LinkedTableId, entries[2].UnresolvedLinkName)); // untouched row keeps its placeholder
    }

    [Fact]
    public void A_link_can_later_be_resolved_by_editing_the_source_table()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.Scavenging(w.Collection.Id, null, unresolvedName: "Scavenged Items"));
        var items = w.Db.SaveTable(Fixtures.ScavengedItems(w.Collection.Id)); // the intended destination now exists
        w.Start();

        var review = w.Edit("Scavenging");
        foreach (var row in review.Rows.Where(r => r.IsUnresolvedLink))
            row.SelectedLink = row.LinkChoices.Single(c => c.TableId == items.Id);
        review.SaveCommand.Execute(null);

        var entries = w.Db.LoadTable(w.Summary("Scavenging").Id)!.ResultSets[0].Entries;
        Assert.Equal([null, items.Id, items.Id, null], entries.Select(e => e.LinkedTableId).ToArray());
        Assert.All(entries, e => Assert.Null(e.UnresolvedLinkName));
    }

    [Fact]
    public void Renaming_the_destination_through_the_editor_keeps_links_working_and_updates_the_shown_name()
    {
        using var w = new World();
        var (items, _) = Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();

        var review = w.Edit("Scavenged Items");
        review.TableName = "Salvage";
        review.SaveCommand.Execute(null);

        // The source table's editor lists the destination by its new name, still linked by id.
        var scavengingEditor = w.Edit("Scavenging");
        var row = scavengingEditor.Rows[1];
        Assert.Equal(items.Id, row.SelectedLink.TableId);
        Assert.Equal("Salvage", row.SelectedLink.Label);
        scavengingEditor.SaveCommand.Execute(null); // saving without touching links keeps them

        w.Main.SelectedTable = w.Summary("Scavenging");
        var roll = (RollViewModel)w.Main.Current!;
        roll.RollCommand.Execute(null);
        var line = Assert.Single(roll.Results);
        Assert.Equal(LinkState.Resolved, line.Link);
        Assert.Equal("Open Salvage", line.FollowLabel);
    }

    [Fact]
    public void Parser_issues_stay_with_their_row_when_earlier_rows_are_deleted()
    {
        using var w = new World();
        var main = w.Start();
        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = Fixtures.RandomStartingGear;
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)main.Current!;

        review.Rows[0].DeleteCommand.Execute(null);
        review.Rows[0].DeleteCommand.Execute(null);

        Assert.Equal(6, review.Rows.Count);
        Assert.Equal("D20 Construction Supplies", review.Rows[5].Text);
        Assert.True(review.Rows[5].HasWarning);
        Assert.Contains("Supplies", review.Rows[5].Notes);
        Assert.False(review.Rows[0].HasWarning);
        Assert.Equal([1, 2, 3, 4, 5, 6], review.Rows.Select(r => r.Number).ToArray());
    }

    // ---- selecting, filtering, deleting -----------------------------------------------------

    [Fact]
    public void Selecting_a_table_opens_it_directly_for_rolling()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        var main = w.Start();
        Assert.Null(main.Current);

        main.SelectedTable = w.Summary("Scavenging");

        var roll = Assert.IsType<RollViewModel>(main.Current);
        Assert.Equal("Scavenging", roll.Title);
        Assert.Empty(roll.Current.Outcomes);
    }

    [Fact]
    public void Table_filter_narrows_the_current_collections_list_and_keeps_the_selection()
    {
        using var w = new World();
        Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Db.SaveTable(Fixtures.RoomFeatures(w.Collection.Id));
        var other = w.Db.CreateCollection("Elsewhere");
        w.Db.SaveTable(Fixtures.ScavengedItems(other.Id).Also(t => t.Name = "Scavenged Hats"));
        var main = w.Start();
        Assert.Equal(["Room Features", "Scavenged Items", "Scavenging"], main.Tables.Select(t => t.Name).ToArray());

        main.TableFilter = "SCAV";
        Assert.Equal(["Scavenged Items", "Scavenging"], main.Tables.Select(t => t.Name).ToArray()); // case-insensitive, this collection only

        main.SelectedTable = w.Summary("Scavenging");
        var session = main.Current;
        main.TableFilter = "scavenging";
        Assert.Equal("Scavenging", main.SelectedTable!.Name);   // still selected
        Assert.Same(session, main.Current);                     // and filtering did not reopen it

        main.TableFilter = "room";
        Assert.Null(main.SelectedTable);                        // filtered out
        main.TableFilter = "";
        Assert.Equal(3, main.Tables.Count);
    }

    [Fact]
    public void Deleting_asks_first_and_relinks_dependents_as_unresolved_placeholders()
    {
        using var w = new World();
        var (items, scavenging) = Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        var main = w.Start();
        main.SelectedTable = w.Summary("Scavenged Items");

        w.ConfirmAnswer = false;
        main.DeleteTableCommand.Execute(null);
        Assert.NotNull(w.Db.LoadTable(items.Id));                    // declined: nothing happened
        Assert.Contains("2 entries in other tables link to it", w.ConfirmPrompts.Single());

        w.ConfirmAnswer = true;
        main.DeleteTableCommand.Execute(null);

        Assert.Null(w.Db.LoadTable(items.Id));
        Assert.Null(main.Current);
        Assert.Equal(["Scavenging"], main.Tables.Select(t => t.Name).ToArray());
        var entries = w.Db.LoadTable(scavenging.Id)!.ResultSets[0].Entries;
        Assert.Equal((null, "Scavenged Items"), (entries[1].LinkedTableId, entries[1].UnresolvedLinkName));

        // Rolling the orphaned source now shows the unresolved placeholder instead of a follow action.
        main.SelectedTable = w.Summary("Scavenging");
        var roll = (RollViewModel)main.Current!;
        roll.RollCommand.Execute(null);
        Assert.Equal(LinkState.Unresolved, Assert.Single(roll.Results).Link);
    }

    [Fact]
    public void Cancelling_an_edit_returns_to_the_table_without_changing_it()
    {
        using var w = new World();
        w.Db.SaveTable(Fixtures.ScavengedItems(w.Collection.Id));
        w.Start();

        var review = w.Edit("Scavenged Items");
        review.TableName = "Changed";
        review.CancelCommand.Execute(null);

        Assert.Equal("Scavenged Items", Assert.IsType<RollViewModel>(w.Main.Current).Title);
        Assert.Equal("Scavenged Items", w.Db.LoadTable(w.Summary("Scavenged Items").Id)!.Name);
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
