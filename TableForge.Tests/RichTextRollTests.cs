using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// RC24: formatted result text on the Roll screen — the result, the resolved inline-dice line, entry lists, linked steps —
/// while everything that reads result text as text (inline dice detection, Recent Rolls, every export) sees exactly the
/// plain text it saw before.
/// </summary>
public class RichTextRollTests
{
    private const TextStyle B = TextStyle.Bold;
    private const TextStyle I = TextStyle.Italic;
    private const TextStyle BI = TextStyle.Bold | TextStyle.Italic;

    private static RollableTable One(string marked, long? link = null, string name = "Test")
    {
        var (text, styles) = TextStylesTests.Md(marked);
        return new RollableTable
        {
            Id = 7, CollectionId = 1, Name = name, Dice = DiceExpression.Parse("d8"),
            ResultSets = [new ResultSet { Entries = [new TableEntry { Min = 1, Max = 8, Text = text, Styles = styles, LinkedTableId = link }] }],
        };
    }

    private static (string, TextStyle)[] Parts(IEnumerable<FormattedSegment> segments) => segments.Select(s => (s.Text, s.Style)).ToArray();

    private static RollableTable WithoutFormatting(RollableTable table)
    {
        var copy = RichTextPersistenceTests.Formatted(table.CollectionId);
        foreach (var e in copy.ResultSets.SelectMany(s => s.Entries)) e.Styles = TextStyles.Empty;
        return copy;
    }

    // ---- the rolled result --------------------------------------------------------------------------------------------

    [Fact]
    public void A_rolled_result_shows_its_formatting_and_its_text_stays_plain()
    {
        var session = new RollViewModel(One("The creature gains **+2 Armor** until the *next dawn*."), new FixedDice(1));
        session.RollCommand.Execute(null);

        var line = Assert.Single(session.Results);
        Assert.Equal("The creature gains +2 Armor until the next dawn.", line.Text);
        Assert.Equal([("The creature gains ", TextStyle.None), ("+2 Armor", B), (" until the ", TextStyle.None), ("next dawn", I), (".", TextStyle.None)],
            Parts(line.Segments));
        Assert.Equal(Parts(session.ResultSets[0].Entries[0].Segments), Parts(line.Segments)); // the entry list shows the same
    }

    [Fact]
    public void A_plain_result_is_one_plain_segment()
    {
        var session = new RollViewModel(One("Nothing happens."), new FixedDice(1));
        session.RollCommand.Execute(null);
        Assert.Equal([("Nothing happens.", TextStyle.None)], Parts(Assert.Single(session.Results).Segments));
    }

    [Fact]
    public void Problem_lines_are_never_formatted()
    {
        var table = One("**Bold** row");
        table.ResultSets[0].Entries.Add(new TableEntry { Min = 5, Max = 8, Text = "Other", Styles = TextStylesTests.Md("*Other*").Styles });
        table.ResultSets[0].Entries[0].Max = 6; // 5-6 overlap: ambiguous
        var session = new RollViewModel(table, new FixedDice(5));
        session.RollCommand.Execute(null);

        var line = Assert.Single(session.Results);
        Assert.True(line.IsProblem);
        Assert.Equal("Ambiguous: \"Bold row\" (1–6) and \"Other\" (5–8) both cover 5.", line.Text);
        Assert.True(line.Styles.IsEmpty);
    }

    // ---- inline dice ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Inline_dice_are_found_in_the_plain_text_exactly_as_without_formatting()
    {
        var marked = "You gain ***+1d4 Armor*** until *next dawn*, then **d6** more; *2d6+1* _ok_";
        var (text, _) = TextStylesTests.Md(marked);
        var formatted = new RollViewModel(One(marked), new FixedDice(1));
        var plain = new RollViewModel(One(text), new FixedDice(1));
        formatted.RollCommand.Execute(null);
        plain.RollCommand.Execute(null);

        Assert.Equal(plain.Results[0].InlineMatches, formatted.Results[0].InlineMatches);
        Assert.Equal(InlineDiceDetector.FindAll(text), formatted.Results[0].InlineMatches);
        Assert.Equal(["d4", "d6", "2d6+1"], formatted.Results[0].InlineActions.Select(a => a.DisplayExpression).ToArray());
    }

    [Fact]
    public void A_resolved_inline_roll_inside_formatted_text_keeps_the_formatting()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(One("You gain ***+1d4 Armor*** until *next dawn*."), dice);
        session.RollCommand.Execute(null);
        var line = Assert.Single(session.Results);

        dice.Value = 3;
        Assert.Single(line.InlineActions).RollCommand!.Execute(null);

        Assert.Equal("You gain +3 Armor until next dawn.", line.ResolvedText);
        Assert.Equal([("Resolved: ", TextStyle.None), ("You gain ", TextStyle.None), ("+3 Armor", BI), (" until ", TextStyle.None), ("next dawn", I), (".", TextStyle.None)],
            Parts(line.ResolvedSegments));
        Assert.Equal("You gain +1d4 Armor until next dawn.", line.Text);   // the source text never changes
        Assert.Equal(("+1d4 Armor", BI), Parts(line.Segments)[1]);
    }

    [Fact]
    public void Formatting_around_an_unformatted_inline_roll_is_kept_and_the_value_is_plain()
    {
        var dice = new FixedDice(4);
        var session = new RollViewModel(One("Gain 1d6 *gold* coins and **a map**."), dice);
        session.RollCommand.Execute(null);
        var line = Assert.Single(session.Results);
        line.InlineActions[0].RollCommand!.Execute(null);

        Assert.Equal([("Resolved: ", TextStyle.None), ("Gain 4 ", TextStyle.None), ("gold", I), (" coins and ", TextStyle.None), ("a map", B), (".", TextStyle.None)],
            Parts(line.ResolvedSegments));
    }

    [Fact]
    public void A_formatting_boundary_inside_a_dice_expression_gives_the_value_its_first_characters_style()
    {
        var dice = new FixedDice(2);
        var session = new RollViewModel(One("Take 1**d4** and **2**d6 damage"), dice);
        session.RollCommand.Execute(null);
        var line = Assert.Single(session.Results);
        Assert.Equal(["d4", "2d6"], line.InlineActions.Select(a => a.DisplayExpression).ToArray());
        foreach (var action in line.InlineActions) action.RollCommand!.Execute(null);

        Assert.Equal("Take 2 and 2 damage", line.ResolvedText);
        Assert.Equal([("Resolved: ", TextStyle.None), ("Take 2 and ", TextStyle.None), ("2", B), (" damage", TextStyle.None)], Parts(line.ResolvedSegments));
    }

    [Fact]
    public void An_expression_not_rolled_yet_keeps_its_own_formatting_in_the_resolved_line()
    {
        var dice = new FixedDice(5);
        var session = new RollViewModel(One("**d6** gold and *d8* silver"), dice);
        session.RollCommand.Execute(null);
        var line = Assert.Single(session.Results);
        line.InlineActions.Single(a => a.DisplayExpression == "d6").RollCommand!.Execute(null);

        Assert.Equal([("Resolved: ", TextStyle.None), ("5", B), (" gold and ", TextStyle.None), ("d8", I), (" silver", TextStyle.None)], Parts(line.ResolvedSegments));
    }

    [Fact]
    public void The_resolved_line_updates_and_is_kept_like_the_resolved_text()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(One("**+1d4** Armor"), dice);
        session.RollCommand.Execute(null);
        var line = Assert.Single(session.Results);
        var raised = new List<string?>();
        line.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        line.InlineActions[0].RollCommand!.Execute(null);
        Assert.Contains(nameof(ResultLineViewModel.ResolvedSegments), raised);

        session.RollCommand.Execute(null); // a new roll supersedes it: the resolved line stays, formatting and all (1.6)
        Assert.True(line.ShowResolved);
        Assert.Equal([("Resolved: ", TextStyle.None), ("+1", B), (" Armor", TextStyle.None)], Parts(line.ResolvedSegments));
        Assert.False(line.ShowInlineActions);
    }

    // ---- linked and aligned -----------------------------------------------------------------------------------------

    [Fact]
    public void A_linked_tables_results_show_their_formatting_after_following_the_link()
    {
        var target = One("The door is *locked* and **trapped**.", name: "Door");
        target.Id = 99;
        var source = One("Open the door", link: 99, name: "Hall");
        var session = new RollViewModel(source, new FixedDice(3), loadTable: id => id == 99 ? target : null);
        session.RollCommand.Execute(null);
        session.Results[0].FollowCommand!.Execute(null);

        Assert.Equal("Door", session.Title);
        Assert.Equal([("The door is ", TextStyle.None), ("locked", I), (" and ", TextStyle.None), ("trapped", B), (".", TextStyle.None)],
            Parts(session.ResultSets[0].Entries[0].Segments));
        session.RollCommand.Execute(null);
        Assert.Equal(("trapped", B), Parts(Assert.Single(session.Results).Segments)[3]);
    }

    [Fact]
    public void Aligned_roll_rows_show_each_cells_own_formatting()
    {
        var table = new RollableTable
        {
            CollectionId = 1, Name = "Difficulty", Dice = DiceExpression.Parse("d2"),
            ResultSets =
            [
                new ResultSet { Name = "Difficulty", Entries = [new() { Min = 1, Max = 1, Text = "Easy", Styles = TextStylesTests.Md("**Easy**").Styles }, new() { Min = 2, Max = 2, Text = "Hard" }] },
                new ResultSet { Name = "Modifier", Entries = [new() { Min = 1, Max = 1, Text = "+10", Styles = TextStylesTests.Md("*+10*").Styles }, new() { Min = 2, Max = 2, Text = "-10" }] },
            ],
        };
        var session = new RollViewModel(table, new FixedDice(1));
        Assert.True(session.IsAligned);
        Assert.Equal([("Easy", B)], Parts(session.AlignedRows[0].Cells[0].Segments));
        Assert.Equal([("+10", I)], Parts(session.AlignedRows[0].Cells[1].Segments));
        Assert.Equal([("Hard", TextStyle.None)], Parts(session.AlignedRows[1].Cells[0].Segments));
    }

    // ---- Recent Rolls stay plain ----------------------------------------------------------------------------------------

    [Fact]
    public void Recent_Rolls_record_exactly_the_plain_text_and_show_no_formatting()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        db.SaveTable(RichTextPersistenceTests.Formatted(collection.Id));
        var main = new MainViewModel(db, new FixedDice(1));
        main.SelectedTable = main.Tables.Single(t => t.Name == "Boons");
        main.OpenTableCommand.Execute(null);
        Assert.IsType<RollViewModel>(main.Current).RollCommand.Execute(null);

        var item = Assert.Single(main.RecentRolls).Item;
        Assert.Equal("The creature gains +2 Armor until the next dawn.", item.ResultText);
        Assert.Equal("Boons\nd6 → 1\n\nThe creature gains +2 Armor until the next dawn.", item.FullText);
        Assert.DoesNotContain("*", main.RecentRolls[0].Detail);
    }

    // ---- exports: byte-for-byte what the same table gives without formatting ------------------------------------------

    [Fact]
    public void Every_text_export_is_identical_with_and_without_formatting()
    {
        var formatted = RichTextPersistenceTests.Formatted(1);
        var plain = WithoutFormatting(formatted);
        Assert.False(formatted.ResultSets[0].Entries[0].Styles.IsEmpty);

        Assert.Equal(TableTextExporter.Export(plain, plain.ResultSets[0]), TableTextExporter.Export(formatted, formatted.ResultSets[0]));
        Assert.Equal(TableTextExporter.Export(plain, plain.ResultSets[0], TableTextSeparator.Space),
            TableTextExporter.Export(formatted, formatted.ResultSets[0], TableTextSeparator.Space));
        Assert.Equal(TableTextExporter.ExportRows(plain, plain.ResultSets[0]), TableTextExporter.ExportRows(formatted, formatted.ResultSets[0]));

        Assert.True(FoundryTableExporter.TryExport(formatted, formatted.ResultSets[0], out var foundry, out _));
        Assert.True(FoundryTableExporter.TryExport(plain, plain.ResultSets[0], out var foundryPlain, out _));
        Assert.Equal(foundryPlain!.Json, foundry!.Json);
        Assert.True(TablesPlusTableExporter.TryExport(formatted, formatted.ResultSets[0], out var tablesPlus, out _));
        Assert.True(TablesPlusTableExporter.TryExport(plain, plain.ResultSets[0], out var tablesPlusPlain, out _));
        Assert.Equal(tablesPlusPlain!.Json, tablesPlus!.Json);

        Assert.Equal("Boons\r\nd6\r\n\r\n1-2\tThe creature gains +2 Armor until the next dawn.\r\n3-4\tYou gain +1d4 Armor until next dawn.\r\n5-6\tNothing happens.",
            TableTextExporter.Export(formatted, formatted.ResultSets[0]));
        Assert.Contains("\"text\": \"The creature gains +2 Armor until the next dawn.\"", foundry.Json);
    }

    [Fact]
    public void Every_Roll_screen_export_copies_and_saves_the_same_bytes_with_and_without_formatting()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tableforge-richexport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            List<string> Run(RollableTable table, string tag)
            {
                var copied = new List<string>();
                var saves = 0;
                var session = new RollViewModel(table, new FixedDice(1), copyText: copied.Add,
                    chooseSaveFile: r => Path.Combine(folder, $"{tag}-{++saves}-{r.SuggestedName}"));
                foreach (var command in new[] { session.CopyTableTextCommand, session.CopyTableTextSpacesCommand, session.CopyForSojourCommand,
                             session.CopyFoundryJsonCommand, session.CopyTablesPlusJsonCommand })
                    command.Execute(null);
                session.SaveFoundryJsonCommand.Execute(null);
                session.SaveTablesPlusJsonCommand.Execute(null);
                return [.. copied, .. Directory.GetFiles(folder, $"{tag}-*").Order().Select(f => Convert.ToBase64String(File.ReadAllBytes(f)))];
            }

            var formatted = Run(RichTextPersistenceTests.Formatted(1), "f");
            var plain = Run(WithoutFormatting(RichTextPersistenceTests.Formatted(1)), "p");
            Assert.Equal(7, formatted.Count);
            Assert.Equal(plain, formatted);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
