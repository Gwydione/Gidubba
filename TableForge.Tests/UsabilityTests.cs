using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Empty-state guidance and the keyboard-friendly table list, at view-model level.</summary>
public class UsabilityTests
{
    // ---- empty state --------------------------------------------------------------------------

    [Fact]
    public void The_empty_state_tells_a_new_user_what_to_do_next_at_each_stage()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var main = new MainViewModel(db, new FixedDice(1));

        // 1. Nothing yet: how to make a collection, and what comes after.
        Assert.StartsWith("Welcome to Gidubba.", main.EmptyStateText);
        Assert.Contains("naming a collection", main.EmptyStateText);
        Assert.Contains("press Enter", main.EmptyStateText);
        Assert.Contains("Paste Table…", main.EmptyStateText);

        // 2. A collection but no tables.
        main.NewCollectionName = "Solo";
        main.CreateCollectionCommand.Execute(null);
        Assert.Contains("no tables yet", main.EmptyStateText);
        Assert.Contains("Paste Table…", main.EmptyStateText);

        // 3. Tables exist.
        db.SaveTable(Fixtures.ScavengedItems(main.SelectedCollection!.Id));
        main.SelectedCollection = null;
        main.SelectedCollection = main.Collections[0];
        Assert.Equal("Choose a table on the left to roll it, or “Paste Table…” to add another.", main.EmptyStateText);
    }

    [Fact]
    public void Saving_the_first_table_updates_the_empty_state()
    {
        using var temp = new TempDatabase();
        var main = new MainViewModel(temp.Open(), new FixedDice(1));
        main.NewCollectionName = "Solo";
        main.CreateCollectionCommand.Execute(null);
        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).SourceText = "d6 Loot\n1-6 Coin";
        ((PasteViewModel)main.Current!).InterpretCommand.Execute(null);
        ((ReviewViewModel)main.Current!).SaveCommand.Execute(null);
        ((RollViewModel)main.Current!).RollCommand.Execute(null);

        main.PasteTableCommand.Execute(null);
        ((PasteViewModel)main.Current!).CancelCommand.Execute(null);   // back to the empty area, now with a table present

        Assert.Null(main.Current);
        Assert.StartsWith("Choose a table on the left", main.EmptyStateText);
    }

    [Fact]
    public void Creating_a_collection_announces_itself_so_focus_can_move_to_Paste_Table()
    {
        using var temp = new TempDatabase();
        var main = new MainViewModel(temp.Open(), new FixedDice(1));
        var announced = 0;
        main.CollectionCreated += (_, _) => announced++;

        main.NewCollectionName = "A";
        main.CreateCollectionCommand.Execute(null);

        Assert.Equal(1, announced);
    }

    [Fact]
    public void Enter_on_an_empty_collection_name_does_nothing()
    {
        using var temp = new TempDatabase();
        var main = new MainViewModel(temp.Open(), new FixedDice(1));

        main.NewCollectionName = "   ";
        Assert.False(main.CreateCollectionCommand.CanExecute(null));
        Assert.Empty(main.Collections);
    }

    // ---- accessible names -----------------------------------------------------------------------

    [Fact]
    public void List_items_have_readable_names_for_screen_readers_and_UI_Automation()
    {
        // These are what assistive technology reads for items that are displayed through a template.
        Assert.Equal("Dungeon", new Collection { Name = "Dungeon" }.ToString());
        Assert.Equal("Scavenged Items (d20)", new TableSummary(1, "Scavenged Items", DiceExpression.Parse("d20"), null, "Unfiled").ToString());
        Assert.Equal("Scavenged Items", new LinkChoice(LinkChoiceKind.Table, 7, "Scavenged Items").ToString());
        Assert.Equal("(no link)", new LinkChoice(LinkChoiceKind.None, null, "(no link)").ToString());

        var editor = new ResultSetEditorViewModel(new Import.ResultSetDraft { Name = "Noise" }, 2, () => { }, _ => { });
        Assert.Equal("Noise", editor.ToString());
        Assert.Equal("Result set 3", new ResultSetEditorViewModel(new Import.ResultSetDraft(), 3, () => { }, _ => { }).ToString());
    }

    // ---- highlighting versus opening ------------------------------------------------------------

    private static MainViewModel WithTables(TempDatabase temp, int count)
    {
        var db = temp.Open();
        var c = db.CreateCollection("C");
        for (var i = 1; i <= count; i++)
            db.SaveTable(Fixtures.ScavengedItems(c.Id).Also(t => t.Name = $"Table {i}"));
        return new MainViewModel(db, new FixedDice(6), (_, _) => true);
    }

    [Fact]
    public void Moving_the_highlight_through_the_list_opens_nothing_and_makes_nothing_recent()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 4);

        foreach (var t in main.Tables) main.HighlightedTable = t;   // what arrowing down the list does

        Assert.Null(main.Current);
        Assert.Empty(main.RecentTables);
        Assert.Null(main.SelectedTable);
        Assert.Equal("Table 4", main.HighlightedTable!.Name);
    }

    [Fact]
    public void Enter_or_a_click_opens_the_highlighted_table()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 3);
        main.HighlightedTable = main.Tables[1];

        Assert.True(main.OpenTableCommand.CanExecute(null));
        main.OpenTableCommand.Execute(null);

        Assert.Equal("Table 2", ((RollViewModel)main.Current!).Title);
        Assert.Equal("Table 2", main.SelectedTable!.Name);
        Assert.Equal(["Table 2"], main.RecentTables.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void Clicking_the_already_highlighted_table_reopens_it_fresh()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 2);
        main.SelectedTable = main.Tables[0];
        var first = (RollViewModel)main.Current!;
        first.RollCommand.Execute(null);

        main.OpenTableCommand.Execute(null);                   // click on the row that is already highlighted

        var second = (RollViewModel)main.Current!;
        Assert.NotSame(first, second);
        Assert.Empty(second.Current.Outcomes);
    }

    [Fact]
    public void Edit_and_Delete_act_on_the_highlighted_row()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 3);
        main.SelectedTable = main.Tables[0];                   // Table 1 is open...
        main.HighlightedTable = main.Tables[2];                // ...but the user has moved to Table 3

        main.EditTableCommand.Execute(null);

        Assert.Equal("Table 3", ((ReviewViewModel)main.Current!).TableName);
    }

    [Fact]
    public void Selecting_a_table_in_code_still_opens_it_and_moves_the_highlight_with_it()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 2);

        main.SelectedTable = main.Tables[1];

        Assert.IsType<RollViewModel>(main.Current);
        Assert.Same(main.Tables[1], main.HighlightedTable);
    }

    // ---- search box keys ----------------------------------------------------------------------

    [Fact]
    public void Enter_in_the_search_box_opens_the_first_match_and_says_so_when_there_is_none()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 3);

        main.TableFilter = "table 2";
        main.OpenFirstMatchCommand.Execute(null);
        Assert.Equal("Table 2", ((RollViewModel)main.Current!).Title);

        main.TableFilter = "zzz";
        main.OpenFirstMatchCommand.Execute(null);
        Assert.Equal("No table matches that search.", main.Status);
        Assert.Equal("Table 2", ((RollViewModel)main.Current!).Title);   // the open table is untouched
    }

    [Fact]
    public void Escape_in_the_search_box_clears_the_search()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 3);
        main.TableFilter = "Table 3";
        Assert.Single(main.Tables);

        main.ClearFilterCommand.Execute(null);

        Assert.Equal("", main.TableFilter);
        Assert.Equal(3, main.Tables.Count);
    }

    [Fact]
    public void The_first_match_is_opened_only_when_nothing_is_highlighted()
    {
        using var temp = new TempDatabase();
        var main = WithTables(temp, 3);
        main.HighlightedTable = main.Tables[2];

        main.OpenFirstMatchCommand.Execute(null);

        Assert.Equal("Table 3", ((RollViewModel)main.Current!).Title);     // the user's cursor row wins over "first in list"
    }
}
