using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// RC24: bold/italic on the Review/Edit screen, through the view models — Bold, Italic, Clear Formatting on a selection,
/// keeping formatting through typing and every cleanup (and their Undo), the preview, and saving. Also the aligned view
/// keeping its rows (and so the editor being typed in) across ordinary edits.
/// </summary>
public class RichTextReviewTests
{
    private sealed class World : IDisposable
    {
        public TempDatabase Temp { get; } = new();
        public AppDatabase Db { get; }
        public Collection Collection { get; }
        public MainViewModel Main { get; private set; } = null!;
        public List<string> Copied { get; } = [];

        public World()
        {
            Db = Temp.Open();
            Collection = Db.CreateCollection("Dungeon");
        }

        public MainViewModel Start() => Main = new MainViewModel(Db, new FixedDice(1), (_, _) => true, copyText: Copied.Add);

        public ReviewViewModel Edit(string name)
        {
            Main.SelectedTable = Main.Tables.Single(t => t.Name == name);
            Main.EditTableCommand.Execute(null);
            return Assert.IsType<ReviewViewModel>(Main.Current);
        }

        public RollableTable Load(string name) => Db.LoadTable(Db.GetTableSummaries(Collection.Id).Single(t => t.Name == name).Id)!;

        public void Dispose() => Temp.Dispose();
    }

    private static RollableTable Plain(long collectionId, string name, params string[] texts) => new()
    {
        CollectionId = collectionId,
        Name = name,
        Dice = DiceExpression.Parse($"d{Math.Max(2, texts.Length)}"),
        ResultSets = [new ResultSet { Entries = texts.Select((t, i) => new TableEntry { Min = i + 1, Max = i + 1, Text = t }).ToList() }],
    };

    private static string Md(EntryRowViewModel row) => TextStylesTests.ToMd(row.Text, row.Styles);

    private static void Select(ReviewViewModel review, EntryRowViewModel row, string part, FormattingAction action) =>
        review.ApplyFormatting(row, row.Text.IndexOf(part, StringComparison.Ordinal), part.Length, action);

    // ---- Bold, Italic, Clear -----------------------------------------------------------------------------------------

    [Fact]
    public void Bold_and_italic_on_selections_are_saved_and_come_back_when_the_table_is_edited_again()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "Boons", "The creature gains +2 Armor until the next dawn.", "Nothing."));
        w.Start();

        var review = w.Edit("Boons");
        var row = review.Rows[0];
        Assert.False(row.HasFormatting);
        Select(review, row, "+2 Armor", FormattingAction.Bold);
        Assert.Equal("Made the selection bold.", review.FormatMessage);
        Select(review, row, "next dawn", FormattingAction.Italic);
        Assert.Equal("Made the selection italic.", review.FormatMessage);
        Assert.True(row.HasFormatting);
        Assert.Equal("The creature gains **+2 Armor** until the *next dawn*.", Md(row));
        Assert.Equal("The creature gains +2 Armor until the next dawn.", row.Text); // the text itself never changes
        review.SaveCommand.Execute(null);

        var entry = w.Load("Boons").ResultSets[0].Entries[0];
        Assert.Equal("The creature gains **+2 Armor** until the *next dawn*.", TextStylesTests.ToMd(entry.Text, entry.Styles));

        var again = w.Edit("Boons");
        Assert.True(again.Rows[0].HasFormatting);
        Assert.False(again.Rows[1].HasFormatting);
        Assert.Equal(["The creature gains ", "+2 Armor", " until the ", "next dawn", "."], again.Rows[0].FormattedSegments.Select(s => s.Text).ToArray());
    }

    [Fact]
    public void The_same_command_again_takes_the_style_off_and_bold_italic_is_both()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "gains +2 Armor now", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];

        Select(review, row, "+2 Armor", FormattingAction.Bold);
        Select(review, row, "+2 Armor", FormattingAction.Italic);
        Assert.Equal("gains ***+2 Armor*** now", Md(row));
        Select(review, row, "+2 Armor", FormattingAction.Bold);
        Assert.Equal("Removed bold from the selection.", review.FormatMessage);
        Assert.Equal("gains *+2 Armor* now", Md(row));
        Select(review, row, "+2 Armor", FormattingAction.Italic);
        Assert.Equal("Removed italic from the selection.", review.FormatMessage);
        Assert.False(row.HasFormatting);
    }

    [Fact]
    public void Clear_formatting_removes_both_styles_from_the_selection_only()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "one two three", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        review.ApplyFormatting(row, 0, row.Text.Length, FormattingAction.Bold);
        Select(review, row, "three", FormattingAction.Italic);

        Select(review, row, "two three", FormattingAction.Clear);
        Assert.Equal("Removed bold and italic from the selection.", review.FormatMessage);
        Assert.Equal("**one **two three", Md(row));

        Select(review, row, "two", FormattingAction.Clear);
        Assert.Equal("The selection has no bold or italic.", review.FormatMessage);
    }

    [Fact]
    public void Formatting_needs_a_selection_in_a_result_text()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "text", "x"));
        w.Start();
        var review = w.Edit("T");

        review.ApplyFormatting(null, 0, 0, FormattingAction.Bold);
        Assert.Equal("Select some result text first, then choose Bold, Italic or Clear Formatting.", review.FormatMessage);
        review.ApplyFormatting(review.Rows[0], 2, 0, FormattingAction.Italic);
        Assert.Equal("Select some result text first, then choose Bold, Italic or Clear Formatting.", review.FormatMessage);
        review.ApplyFormatting(review.Rows[0], 2, 50, FormattingAction.Bold);
        Assert.False(review.Rows[0].HasFormatting);
    }

    [Fact]
    public void Formatting_is_presentation_only_so_it_does_not_revalidate_or_rebuild_anything()
    {
        using var w = new World();
        w.Db.SaveTable(new RollableTable
        {
            CollectionId = w.Collection.Id, Name = "Aligned", Dice = DiceExpression.Parse("d2"),
            ResultSets =
            [
                new ResultSet { Name = "A", Entries = [new() { Min = 1, Max = 1, Text = "Easy going" }, new() { Min = 2, Max = 2, Text = "Hard" }] },
                new ResultSet { Name = "B", Entries = [new() { Min = 1, Max = 1, Text = "+10" }, new() { Min = 2, Max = 2, Text = "-10" }] },
            ],
        });
        w.Start();
        var review = w.Edit("Aligned");
        Assert.True(review.IsAligned);
        review.CopyTableTextCommand.Execute(null);
        var rows = review.AlignedRows.ToList();

        Select(review, review.AlignedRows[0].Cells[0], "Easy", FormattingAction.Bold);

        Assert.Equal(rows, review.AlignedRows);                       // the same row objects: nothing was rebuilt
        Assert.Equal("Table text copied.", review.CopyMessage);       // a full refresh would have cleared this
        Assert.True(review.CanSave);
        Assert.Equal("**Easy** going", Md(review.ResultSets[0].Rows[0]));
    }

    // ---- typing -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Typing_keeps_formatting_on_its_characters_and_does_not_extend_it_at_a_boundary()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "gains +2 Armor now", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        Select(review, row, "+2 Armor", FormattingAction.Bold);

        row.Text = "gains +2 Heavy Armor now";                          // inside the run
        Assert.Equal("gains **+2 Heavy Armor** now", Md(row));
        row.Text = "gains +2 Heavy Armors now";                         // at its edge
        Assert.Equal("gains **+2 Heavy Armor**s now", Md(row));
        row.Text = "You gains +2 Heavy Armors now";                     // elsewhere
        Assert.Equal("You gains **+2 Heavy Armor**s now", Md(row));
        row.Text = "You gains +2 Hea now";                              // deleting across the run's end
        Assert.Equal("You gains **+2 Hea** now", Md(row));
    }

    [Fact]
    public void The_editors_exact_change_decides_which_side_of_a_boundary_a_repeated_character_went()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "abb", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        review.ApplyFormatting(row, 1, 2, FormattingAction.Bold);  // a**bb**

        row.Text = "abbb";                                         // the text box binding: old and new text only
        Assert.Equal("a**bb**b", Md(row));                         // the comparison's best guess
        Assert.True(row.RefineLastEdit(1, 0, 1));                  // the text box: "b" was typed at 1
        Assert.Equal("ab**bb**", Md(row));
        Assert.False(row.RefineLastEdit(1, 0, 1));                 // used once
    }

    [Fact]
    public void A_whole_text_replacement_is_never_taken_as_the_exact_edit()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "gains +2 Armor now", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        Select(review, row, "+2 Armor", FormattingAction.Bold);

        row.Text = "gains +2 Armor now!";
        // What a hidden copy of this row's editor reports when the binding hands it the new text: everything replaced.
        Assert.False(row.RefineLastEdit(0, "gains +2 Armor now".Length, "gains +2 Armor now!".Length));
        Assert.Equal("gains **+2 Armor** now!", Md(row));
        Assert.False(row.RefineLastEdit(5, 0, 1));                  // and nothing that does not describe the edit
        Assert.True(row.RefineLastEdit(18, 0, 1));
        Assert.Equal("gains **+2 Armor** now!", Md(row));
    }

    // ---- cleanups and Undo ------------------------------------------------------------------------------------------

    [Fact]
    public void Join_keeps_both_rows_formatting_and_Undo_restores_both_rows_exactly()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "You find a damaged chest", "Start"));
        w.Start();
        var review = w.Edit("T");
        var first = review.Rows[0];
        var second = review.Rows[1];
        Select(review, first, "damaged", FormattingAction.Bold);
        second.RangeText = "";
        second.Text = "containing old coins.";
        Select(review, second, "old coins", FormattingAction.Italic);
        review.SelectedRow = second;

        review.JoinWithPreviousRowCommand.Execute(null);

        Assert.Single(review.Rows);
        Assert.Equal("You find a **damaged** chest containing *old coins*.", Md(first));
        review.UndoCleanupCommand.Execute(null);
        Assert.Equal(2, review.Rows.Count);
        Assert.Equal("You find a **damaged** chest", Md(review.Rows[0]));
        Assert.Equal("containing *old coins*.", Md(review.Rows[1]));
    }

    [Fact]
    public void Dehyphenate_keeps_formatting_and_Undo_restores_it_exactly()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "A magnifi- cent sword", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        Select(review, row, "magnifi- cent", FormattingAction.Bold);
        review.SelectedRow = row;

        review.DehyphenateSelectedCommand.Execute(null);
        Assert.Equal("A **magnificent** sword", Md(row));
        review.UndoCleanupCommand.Execute(null);
        Assert.Equal("A **magnifi- cent** sword", Md(row));
    }

    [Fact]
    public void Normalize_keeps_formatting_through_ligatures_and_spaces_and_Undo_restores_it_exactly()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "The ﬁne sword   of ﬂame", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        Select(review, row, "ﬁne sword", FormattingAction.Bold);
        Select(review, row, "ﬂame", FormattingAction.Italic);
        var before = row.Styles;

        review.NormalizeTextCommand.Execute(null);
        Assert.Equal("The **fine sword** of *flame*", Md(row));
        review.UndoCleanupCommand.Execute(null);
        Assert.Equal("The ﬁne sword   of ﬂame", row.Text);
        Assert.Equal(before, row.Styles);
    }

    [Fact]
    public void Remove_empty_rows_and_its_Undo_leave_formatted_rows_alone()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "keep this", "x"));
        w.Start();
        var review = w.Edit("T");
        Select(review, review.Rows[0], "this", FormattingAction.Bold);
        review.SelectedResultSet.AddRowCommand.Execute(null);
        review.Rows[^1].RangeText = "";

        review.RemoveEmptyRowsCommand.Execute(null);
        review.UndoCleanupCommand.Execute(null);
        Assert.Equal("keep **this**", Md(review.Rows[0]));
    }

    [Fact]
    public void Save_trims_the_text_and_keeps_the_formatting_on_the_same_words()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "gains +2 Armor now", "x"));
        w.Start();
        var review = w.Edit("T");
        var row = review.Rows[0];
        Select(review, row, "+2 Armor", FormattingAction.Bold);
        row.Text = "    gains +2 Armor now";   // typed spaces, one edit at a time, as a text box reports them
        row.Text = "    gains +2 Armor now   ";
        review.SaveCommand.Execute(null);

        var entry = w.Load("T").ResultSets[0].Entries[0];
        Assert.Equal("gains **+2 Armor** now", TextStylesTests.ToMd(entry.Text, entry.Styles));
    }

    [Fact]
    public void Pasted_rows_are_plain_and_leave_formatted_rows_as_they_were()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "keep this", "x"));
        w.Start();
        var review = w.Edit("T");
        Select(review, review.Rows[0], "keep", FormattingAction.Italic);

        review.PasteRowsText = "3 New **row**\n4 Another";
        review.AppendPastedRowsCommand.Execute(null);

        Assert.Equal("*keep* this", Md(review.Rows[0]));
        Assert.Equal("New **row**", review.Rows[2].Text);              // pasted text is never read as formatting
        Assert.False(review.Rows[2].HasFormatting);
        Assert.False(review.Rows[3].HasFormatting);
    }

    [Fact]
    public void Copy_Table_Text_on_the_Review_screen_is_plain_text()
    {
        using var w = new World();
        w.Db.SaveTable(Plain(w.Collection.Id, "T", "gains +2 Armor now", "x"));
        w.Start();
        var review = w.Edit("T");
        Select(review, review.Rows[0], "+2 Armor", FormattingAction.Bold);

        review.CopyTableTextCommand.Execute(null);
        Assert.Equal("T\r\nd2\r\n\r\n1\tgains +2 Armor now\r\n2\tx", Assert.Single(w.Copied));
    }

    // ---- the aligned view keeps its rows ----------------------------------------------------------------------------

    private static RollableTable Difficulty(long collectionId) => new()
    {
        CollectionId = collectionId, Name = "Difficulty", Dice = DiceExpression.Parse("d3"),
        ResultSets =
        [
            new ResultSet { Name = "Difficulty", Entries = [new() { Min = 1, Max = 1, Text = "Easy" }, new() { Min = 2, Max = 2, Text = "Normal" }, new() { Min = 3, Max = 3, Text = "Hard" }] },
            new ResultSet { Name = "Modifier", Entries = [new() { Min = 1, Max = 1, Text = "+10" }, new() { Min = 2, Max = 2, Text = "+0" }, new() { Min = 3, Max = 3, Text = "-10" }] },
        ],
    };

    [Fact]
    public void Typing_in_the_aligned_view_keeps_the_same_aligned_rows()
    {
        using var w = new World();
        w.Db.SaveTable(Difficulty(w.Collection.Id));
        w.Start();
        var review = w.Edit("Difficulty");
        var rows = review.AlignedRows.ToList();

        review.AlignedRows[0].Cells[0].Text = "Easy!";
        review.AlignedRows[1].Cells[1].Text = "+1";
        review.AlignedRows[2].RangeText = "3";

        Assert.True(review.IsAligned);
        Assert.Equal(rows, review.AlignedRows);  // before RC24 every keystroke replaced them all, and the editor lost focus
        Assert.Equal("Easy!", review.ResultSets[0].Rows[0].Text);
    }

    [Fact]
    public void The_aligned_rows_are_rebuilt_when_the_rows_change_and_follow_range_changes_made_elsewhere()
    {
        using var w = new World();
        w.Db.SaveTable(Difficulty(w.Collection.Id));
        w.Start();
        var review = w.Edit("Difficulty");
        var rows = review.AlignedRows.ToList();

        review.AddAlignedRowCommand.Execute(null);
        Assert.Equal(4, review.AlignedRows.Count);
        Assert.NotSame(rows[0], review.AlignedRows[0]);

        var first = review.AlignedRows[0];
        var raised = new List<string?>();
        first.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        first.RangeText = " 1 ";                                                // typed in the aligned row's own range box
        Assert.Same(first, review.AlignedRows[0]);                              // every column changed, then one refresh: kept
        Assert.Equal([" 1 ", " 1 "], review.ResultSets.Select(s => s.Rows[0].RangeText).ToArray());

        review.NormalizeTextCommand.Execute(null);                              // changed underneath the aligned row
        Assert.Equal("1", review.AlignedRows[0].RangeText);
        if (ReferenceEquals(first, review.AlignedRows[0])) Assert.Contains(nameof(AlignedRowViewModel.RangeText), raised);

        review.ResultSets[1].Rows[0].RangeText = "9";                          // no longer aligned
        Assert.False(review.IsAligned);
        Assert.Empty(review.AlignedRows);
    }

    [Fact]
    public void Formatting_an_aligned_cell_formats_that_result_set_only()
    {
        using var w = new World();
        w.Db.SaveTable(Difficulty(w.Collection.Id));
        w.Start();
        var review = w.Edit("Difficulty");

        Select(review, review.AlignedRows[2].Cells[1], "-10", FormattingAction.Bold);
        review.SaveCommand.Execute(null);

        var table = w.Load("Difficulty");
        Assert.True(table.ResultSets[0].Entries.All(e => e.Styles.IsEmpty));
        Assert.Equal("**-10**", TextStylesTests.ToMd(table.ResultSets[1].Entries[2].Text, table.ResultSets[1].Entries[2].Styles));
    }
}
