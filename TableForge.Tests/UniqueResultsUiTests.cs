using System.Windows;
using System.Windows.Controls;
using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;
using TableForge.Views;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// The Unique Results UI (Checkpoint 5), through the view models: the Unique setting in the table editor (per row, Mark All Unique,
/// Clear Unique), the Roll screen's Unique / Used tags and summary, Reset Used Results… (confirmed, current table only, the setting
/// untouched, a failure never shown as success), and the "Rerolled 2×" notes on a result and in Recent Rolls.
/// </summary>
public class UniqueResultsUiTests
{
    // ---- the editor ------------------------------------------------------------------------------------------------------

    private sealed class Db : IDisposable
    {
        public TempDatabase Temp { get; } = new();
        public AppDatabase Database { get; private set; }
        public Collection Collection { get; }
        public List<string> Confirmations { get; } = [];
        public List<string> ConfirmationTitles { get; } = [];
        public bool Answer { get; set; } = true;
        public MainViewModel Main { get; private set; }

        public Db(params Func<long, RollableTable>[] tables)
        {
            Database = Temp.Open();
            Collection = Database.CreateCollection("Dungeon");
            foreach (var table in tables) Database.SaveTable(table(Collection.Id));
            Main = NewMain();
        }

        private MainViewModel NewMain() => new(Database, new FixedDice(1), (m, title) => { Confirmations.Add(m); ConfirmationTitles.Add(title); return Answer; });

        public void Restart()
        {
            Database.Dispose();
            Database = Temp.Open();
            Main = NewMain();
        }

        public ReviewViewModel Edit(string name)
        {
            Main.SelectedTable = Main.Tables.Single(t => t.Name == name);
            Main.EditTableCommand.Execute(null);
            return Assert.IsType<ReviewViewModel>(Main.Current);
        }

        public RollViewModel Open(string name)
        {
            Main.SelectedTable = Main.Tables.Single(t => t.Name == name);
            return Assert.IsType<RollViewModel>(Main.Current);
        }

        public RollableTable Stored(string name) =>
            Database.LoadTable(Database.GetTableSummaries(Collection.Id).Single(t => t.Name == name).Id)!;

        public void Dispose()
        {
            Database.Dispose();
            Temp.Dispose();
        }
    }

    private static RollableTable Monsters(long c) => Save(c, "Monsters", "d6", Set("", E(1, 2, "Goblin"), E(3, 4, "Ogre"), E(5, 6, "Rats")));

    private static RollableTable Save(long c, string name, string dice, params ResultSet[] sets) =>
        new() { CollectionId = c, Name = name, Dice = DiceExpression.Parse(dice), ResultSets = [.. sets] };

    private static RollableTable Paired(long c) => Save(c, "Paired", "d6",
        Set("Who", E(1, 3, "Bandits"), E(4, 6, "Knights")), Set("Where", E(1, 3, "Ford"), E(4, 6, "Bridge")));

    [Fact]
    public void Rows_are_marked_and_unmarked_Unique_one_by_one_and_the_setting_is_saved_and_reloaded()
    {
        using var db = new Db(Monsters);
        var edit = db.Edit("Monsters");
        Assert.All(edit.SelectedResultSet.Rows, r => Assert.False(r.IsUnique));

        edit.SelectedResultSet.Rows[0].IsUnique = true;
        edit.SelectedResultSet.Rows[1].IsUnique = true;
        edit.SelectedResultSet.Rows[1].IsUnique = false;
        edit.SaveCommand.Execute(null);

        Assert.Equal([true, false, false], db.Stored("Monsters").ResultSets[0].Entries.Select(e => e.IsUnique).ToArray());
        db.Restart();
        Assert.Equal([true, false, false], db.Edit("Monsters").SelectedResultSet.Rows.Select(r => r.IsUnique).ToArray());
    }

    [Fact]
    public void Mark_All_Unique_and_Clear_Unique_act_on_the_selected_result_set_or_every_set_when_aligned()
    {
        using var db = new Db(Monsters, Paired);
        var edit = db.Edit("Monsters");
        Assert.True(edit.MarkAllUniqueCommand.CanExecute(null));
        Assert.False(edit.ClearUniqueCommand.CanExecute(null));                     // nothing is Unique yet
        edit.MarkAllUniqueCommand.Execute(null);
        Assert.All(edit.SelectedResultSet.Rows, r => Assert.True(r.IsUnique));
        Assert.False(edit.MarkAllUniqueCommand.CanExecute(null));
        edit.ClearUniqueCommand.Execute(null);
        Assert.All(edit.SelectedResultSet.Rows, r => Assert.False(r.IsUnique));
        edit.CancelCommand.Execute(null);

        var aligned = db.Edit("Paired");
        Assert.True(aligned.IsAligned);
        aligned.MarkAllUniqueCommand.Execute(null);
        Assert.All(aligned.ResultSets.SelectMany(s => s.Rows), r => Assert.True(r.IsUnique));
        aligned.AlignedRows[0].Cells[0].IsUnique = false;                           // each cell is its own row: Bandits repeatable
        aligned.SaveCommand.Execute(null);
        var stored = db.Stored("Paired");
        Assert.Equal([false, true], stored.ResultSets[0].Entries.Select(e => e.IsUnique).ToArray());
        Assert.Equal([true, true], stored.ResultSets[1].Entries.Select(e => e.IsUnique).ToArray());
    }

    [Fact]
    public void Editing_text_and_ranges_of_a_Unique_row_keeps_it_Unique_and_keeps_its_used_state()
    {
        using var db = new Db(Monsters);
        var edit = db.Edit("Monsters");
        edit.SelectedResultSet.Rows[0].IsUnique = true;
        edit.SaveCommand.Execute(null);
        var roll = db.Open("Monsters");
        roll.ManualRollText = "1";
        roll.ResolveManualCommand.Execute(null);

        var again = db.Edit("Monsters");
        again.SelectedResultSet.Rows[0].Text = "Goblin war band";
        again.SelectedResultSet.Rows[0].RangeText = "1";
        again.SelectedResultSet.Rows[1].RangeText = "2-4";
        again.SaveCommand.Execute(null);

        var reopened = db.Open("Monsters");
        Assert.Equal(["Used", "", ""], reopened.ResultSets[0].Entries.Select(e => e.UniqueTag).ToArray());
        Assert.Equal("Goblin war band", reopened.ResultSets[0].Entries[0].Text);
    }

    // ---- the Roll screen: tags, summary and reset ------------------------------------------------------------------------

    private static Func<long, RollableTable> UniqueMonsters => c => Monsters(c).Also(t =>
    {
        t.ResultSets[0].Entries[0].IsUnique = true;                                 // Goblin and Ogre Unique, Rats repeatable
        t.ResultSets[0].Entries[1].IsUnique = true;
    });

    private static string[] Tags(RollViewModel s) => s.ResultSets.SelectMany(r => r.Entries).Select(e => e.UniqueTag).ToArray();

    [Fact]
    public void The_listing_tags_available_and_used_Unique_rows_in_words_and_never_hides_or_reorders_them()
    {
        using var db = new Db(UniqueMonsters);
        var roll = db.Open("Monsters");
        Assert.True(roll.HasUniqueRows);
        Assert.Equal(["Unique", "Unique", ""], Tags(roll));
        Assert.Equal("Unique results: 0 of 2 used", roll.UsedResultsSummary);
        Assert.False(roll.ResetUsedResultsCommand.CanExecute(null));                // nothing to reset

        roll.RollCommand.Execute(null);                                              // FixedDice 1 → Goblin

        Assert.Equal(["Goblin", "Ogre", "Rats"], roll.ResultSets[0].Entries.Select(e => e.Text).ToArray());
        Assert.Equal(["Used", "Unique", ""], Tags(roll));
        Assert.True(roll.ResultSets[0].Entries[0].IsUsed);
        Assert.True(roll.ResultSets[0].Entries[0].IsMatched);                       // the result just rolled stays highlighted
        Assert.Equal("Unique results: 1 of 2 used", roll.UsedResultsSummary);
        Assert.True(roll.ResetUsedResultsCommand.CanExecute(null));
    }

    [Fact]
    public void A_table_without_unique_rows_shows_no_tags_summary_or_reset()
    {
        using var db = new Db(Monsters);
        var roll = db.Open("Monsters");
        roll.RollCommand.Execute(null);

        Assert.False(roll.HasUniqueRows);
        Assert.All(roll.ResultSets[0].Entries, e => Assert.False(e.HasUniqueTag));
        Assert.False(roll.ResetUsedResultsCommand.CanExecute(null));
        Assert.Empty(db.Confirmations);
    }

    [Fact]
    public void Reset_asks_first_then_clears_only_this_tables_used_results_keeping_the_Unique_setting_and_lasts_after_a_restart()
    {
        using var db = new Db(UniqueMonsters, c => UniqueMonsters(c).Also(t => t.Name = "Other"));
        db.Open("Other").RollCommand.Execute(null);                                 // Other's Goblin used too
        var roll = db.Open("Monsters");
        roll.RollCommand.Execute(null);

        db.Answer = false;
        roll.ResetUsedResultsCommand.Execute(null);
        Assert.Equal("Reset the used results for \"Monsters\"?\n\n1 used Unique result will be available to roll again. Which rows are Unique does not change.",
            db.Confirmations.Single());
        Assert.Equal("Reset Used Results", db.ConfirmationTitles.Single());              // titled for what it does, not "Delete"
        Assert.Equal(["Used", "Unique", ""], Tags(roll));                             // declined: nothing changed

        db.Answer = true;
        roll.ResetUsedResultsCommand.Execute(null);
        Assert.Equal(["Unique", "Unique", ""], Tags(roll));
        Assert.Equal("Unique results: 0 of 2 used", roll.UsedResultsSummary);
        Assert.False(roll.ResetUsedResultsCommand.CanExecute(null));
        Assert.Equal("", roll.Message);

        db.Restart();
        Assert.Equal(["Unique", "Unique", ""], Tags(db.Open("Monsters")));
        Assert.Equal(["Used", "Unique", ""], Tags(db.Open("Other")));                 // the other table keeps its used results
        Assert.Equal([true, true, false], db.Stored("Monsters").ResultSets[0].Entries.Select(e => e.IsUnique).ToArray());
    }

    [Fact]
    public void A_reset_that_fails_says_so_and_still_shows_the_results_as_used()
    {
        var table = UniqueMonsters(1).Also(t => { t.Id = 1; foreach (var e in t.ResultSets[0].Entries) e.RowKey = Guid.NewGuid(); });
        var inner = UsedResultsStore.InMemory();
        var store = new UsedResultsStore(inner.Get, inner.MarkUsed, _ => throw new InvalidOperationException("disk full"));
        var roll = new RollViewModel(table, new FixedDice(1), usedResults: store, confirm: (_, _) => true);
        roll.RollCommand.Execute(null);

        roll.ResetUsedResultsCommand.Execute(null);

        Assert.Equal("The used results could not be reset: disk full", roll.Message);
        Assert.Equal(["Used", "Unique", ""], Tags(roll));
        Assert.Equal("Unique results: 1 of 2 used", roll.UsedResultsSummary);
        Assert.True(roll.ResetUsedResultsCommand.CanExecute(null));
    }

    [Fact]
    public void Without_a_confirmation_nothing_is_ever_reset()
    {
        var table = UniqueMonsters(1).Also(t => { t.Id = 1; foreach (var e in t.ResultSets[0].Entries) e.RowKey = Guid.NewGuid(); });
        var roll = new RollViewModel(table, new FixedDice(1));
        roll.RollCommand.Execute(null);
        roll.ResetUsedResultsCommand.Execute(null);
        Assert.Equal(["Used", "Unique", ""], Tags(roll));
    }

    [Fact]
    public void Used_results_that_cannot_be_read_are_not_shown_as_available()
    {
        var table = UniqueMonsters(1).Also(t => { t.Id = 1; foreach (var e in t.ResultSets[0].Entries) e.RowKey = Guid.NewGuid(); });
        var store = new UsedResultsStore(_ => throw new InvalidOperationException("disk full"), (_, _) => { });
        var roll = new RollViewModel(table, new FixedDice(1), usedResults: store);

        Assert.Equal("Unique results: which have been used could not be read.", roll.UsedResultsSummary);
        Assert.False(roll.ResetUsedResultsCommand.CanExecute(null));
    }

    [Fact]
    public void Following_a_link_shows_the_destinations_own_used_results()
    {
        var w = new Dictionary<long, RollableTable>();
        var monsters = UniqueMonsters(1).Also(t => { t.Id = 2; foreach (var e in t.ResultSets[0].Entries) e.RowKey = Guid.NewGuid(); });
        var area = Save(1, "Area", "d6", Set("", E(1, 6, "Lair", link: monsters.Id))).Also(t => t.Id = 1);
        w[1] = area;
        w[2] = monsters;
        var store = UsedResultsStore.InMemory();
        store.MarkUsed(2, [monsters.ResultSets[0].Entries[1].RowKey]);
        var roll = new RollViewModel(area, new FixedDice(3), w.GetValueOrDefault, usedResults: store);
        Assert.False(roll.HasUniqueRows);

        roll.RollCommand.Execute(null);
        roll.LatestRolls[0].Lines[0].FollowCommand!.Execute(null);

        Assert.True(roll.HasUniqueRows);
        Assert.Equal(["Unique", "Used", ""], Tags(roll));
        Assert.Equal("Unique results: 1 of 2 used", roll.UsedResultsSummary);
    }

    [Fact]
    public void Reset_is_unavailable_while_a_roll_is_in_the_air()
    {
        var table = UniqueMonsters(1).Also(t => { t.Id = 1; foreach (var e in t.ResultSets[0].Entries) e.RowKey = Guid.NewGuid(); });
        var dice = new ScriptedDice(1, 3) { PendOnCall = 2 };
        var roll = new RollViewModel(table, dice, confirm: (_, _) => true);
        roll.RollCommand.Execute(null);
        roll.RollCommand.Execute(null);                                              // the second throw stays in the air
        Assert.True(roll.IsRolling);
        Assert.False(roll.ResetUsedResultsCommand.CanExecute(null));
        roll.CancelRoll();
    }

    // ---- reroll notes ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_result_shows_Rerolled_N_only_when_it_took_rerolls()
    {
        var table = UniqueMonsters(1).Also(t => { t.Id = 1; foreach (var e in t.ResultSets[0].Entries) e.RowKey = Guid.NewGuid(); });
        var roll = new RollViewModel(table, new ScriptedDice(1, 2, 1, 3));
        roll.RollCommand.Execute(null);                                              // Goblin first time
        var first = roll.Current.Outcomes.Last();
        Assert.False(first.HasRerollNote);
        Assert.Equal("", first.RerollNote);

        roll.RollCommand.Execute(null);                                              // 2 and 1 are Goblin (used), then 3: Ogre
        var second = roll.Current.Outcomes.Last();
        Assert.True(second.HasRerollNote);
        Assert.Equal("Rerolled 2×", second.RerollNote);
        Assert.Equal("Ogre", roll.Results.Single().Text);
    }

    [Fact]
    public void Recent_Rolls_shows_the_stored_reroll_count_ahead_of_the_result_and_in_the_full_snapshot()
    {
        var at = new DateTime(2026, 10, 27, 14, 32, 0, DateTimeKind.Utc);
        var rerolled = new RecentRollViewModel(new RollHistoryItem(1, 1, "Monsters", "d6", 3, "Ogre", at, RerolledCount: 2));
        var plain = new RecentRollViewModel(new RollHistoryItem(2, 1, "Monsters", "d6", 1, "Goblin", at));

        Assert.Equal($"{at.ToLocalTime():HH:mm} · Rerolled 2× · Ogre", rerolled.Detail);
        Assert.Contains("\nRerolled 2× (used Unique results came up)\n", rerolled.Item.FullText);
        Assert.Equal($"{at.ToLocalTime():HH:mm} · Goblin", plain.Detail);
        Assert.DoesNotContain("Rerolled", plain.Item.FullText);
    }

    [Fact]
    public void In_the_app_a_rerolled_result_reaches_Recent_Rolls_with_its_count()
    {
        using var db = new Db(UniqueMonsters);
        db.Open("Monsters").RollCommand.Execute(null);                              // Goblin
        db.Database.AddRollHistory(new RollSnapshot(db.Stored("Monsters").Id, "Monsters", "d6", 3, "Ogre", RerolledCount: 4));
        db.Restart();

        Assert.Equal([4, 0], db.Main.RecentRolls.Select(r => r.Item.RerolledCount).ToArray());
        Assert.Contains("Rerolled 4×", db.Main.RecentRolls[0].Detail);
        Assert.DoesNotContain("Rerolled", db.Main.RecentRolls[1].Detail);
        Assert.Equal(10, MainViewModelRecentRollLimit(db));
    }

    private static int MainViewModelRecentRollLimit(Db db)
    {
        var id = db.Stored("Monsters").Id;
        for (var i = 0; i < 12; i++) db.Database.AddRollHistory(new RollSnapshot(id, "Monsters", "d6", 5, "Rats", RerolledCount: 1));
        db.Restart();
        return db.Main.RecentRolls.Count;
    }
}

/// <summary>The Unique Results controls in the real windows: the editor's checkboxes (keyboard, accessibility, width), the Roll screen's tags and Reset.</summary>
[Collection("UI")]
public class UniqueResultsViewTests
{
    private static UiHarness Open(int roll = 1) => new(roll, (db, c) =>
    {
        db.SaveTable(new RollableTable
        {
            CollectionId = c.Id, Name = "Monsters", Dice = DiceExpression.Parse("d6"),
            ResultSets = [Set("", E(1, 2, "Goblin"), E(3, 4, "Ogre — a very long result that wraps across the row to see that the text keeps room"), E(5, 6, "Rats"))],
        });
        db.SaveTable(new RollableTable
        {
            CollectionId = c.Id, Name = "Paired", Dice = DiceExpression.Parse("d6"),
            ResultSets = [Set("Who", E(1, 3, "Bandits"), E(4, 6, "Knights")), Set("Where", E(1, 3, "Ford"), E(4, 6, "Bridge"))],
        });
    });

    private static List<CheckBox> UniqueBoxes(UiHarness ui) => ui.All<CheckBox>().Where(b => b.Name == "UniqueBox" && b.IsVisible).ToList();

    [Fact]
    public void Each_editor_row_has_an_accessible_Unique_checkbox_that_is_saved()
    {
        Sta.Run(() =>
        {
            using var ui = Open();
            ui.SelectTable("Monsters");
            ui.Click("Edit table");

            var boxes = UniqueBoxes(ui);
            Assert.Equal(3, boxes.Count);
            Assert.All(boxes, b => Assert.True(b.IsTabStop && b.Focusable));       // reached by Tab after each row's link
            Assert.Equal(["Unique result (row 1)", "Unique result (row 2)", "Unique result (row 3)"],
                boxes.Select(System.Windows.Automation.AutomationProperties.GetName).ToArray());
            Assert.Contains(ui.Texts(), t => t.Text == "Unique");                   // the column heading

            // Ctrl+U toggles the selected row's Unique: the row is selected by focus reaching its result text. (The chord itself
            // is not driven — Keyboard.Modifiers reads live OS state — so its binding is checked and its command run, as for Ctrl+J.)
            var view = ui.One<ReviewView>(_ => true);
            var binding = view.InputBindings.OfType<System.Windows.Input.KeyBinding>()
                .Single(k => k.Key == System.Windows.Input.Key.U && k.Modifiers == System.Windows.Input.ModifierKeys.Control);
            var edit = Assert.IsType<ReviewViewModel>(ui.Main.Current);
            Assert.Same(edit.ToggleUniqueSelectedCommand, binding.Command);
            var text = ui.All<TextBox>().First(b => b.Name == "ResultTextBox" && b.IsVisible);
            text.Focus();
            ui.Layout();
            binding.Command.Execute(null);
            ui.Layout();
            Assert.True(boxes[0].IsChecked);
            Assert.Same(text, ui.Focused);                                          // typing carries on where it was
            ui.Click("Mark All Unique");
            Assert.All(UniqueBoxes(ui), b => Assert.True(b.IsChecked));
            UniqueBoxes(ui)[2].IsChecked = false;
            ui.Layout();
            ui.Click("Save Table");

            ui.SelectTable("Monsters");
            ui.Click("Edit table");
            Assert.Equal([true, true, false], UniqueBoxes(ui).Select(b => b.IsChecked == true).ToArray());
        });
    }

    [Fact]
    public void At_the_minimum_window_size_the_result_text_keeps_its_room_and_every_checkbox_is_on_screen()
    {
        Sta.Run(() =>
        {
            using var ui = Open();
            ui.Window.Width = 1000;
            ui.Window.Height = 640;
            ui.Layout();
            ui.SelectTable("Monsters");
            ui.Click("Edit table");

            var text = ui.All<TextBox>().First(b => b.Name == "ResultTextBox" && b.IsVisible);
            // 181 px before the Unique column; the 24 px checkbox column is the only width it takes from the text.
            Assert.True(text.ActualWidth >= 150, $"result text is only {text.ActualWidth:0} px wide");
            var right = ui.Window.ActualWidth;
            foreach (var box in UniqueBoxes(ui))
            {
                var p = box.TransformToAncestor(ui.Window).Transform(new Point(box.ActualWidth, 0));
                Assert.True(p.X <= right, "a Unique checkbox is cut off");
            }

            ui.Click("Cancel");
            ui.SelectTable("Paired");
            ui.Click("Edit table");
            Assert.Equal(4, UniqueBoxes(ui).Count);                                  // one per cell in the aligned view
            Assert.All(UniqueBoxes(ui), b => Assert.Equal("Unique", b.Content));

            // Tab in an aligned cell: its link chooser, then its own Unique checkbox.
            var combo = ui.All<ComboBox>().First(c => c.IsVisible && c.Parent is Grid g && g.Children.OfType<CheckBox>().Any());
            var own = ((Grid)combo.Parent).Children.OfType<CheckBox>().Single();
            combo.Focus();
            ui.Layout();
            combo.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            Assert.Same(own, ui.Focused);
        });
    }

    [Fact]
    public void The_Roll_screen_tags_used_rows_and_Reset_Used_Results_clears_them()
    {
        Sta.Run(() =>
        {
            using var ui = Open(roll: 1);
            ui.SelectTable("Monsters");
            ui.Click("Edit table");
            ui.Click("Mark All Unique");
            ui.Click("Save Table");

            ui.SelectTable("Monsters");
            Assert.Contains(ui.Texts(), t => t.Text == "Unique results: 0 of 3 used");
            Assert.False(ui.One<Button>(b => b.Name == "ResetUsedResultsButton").IsEnabled);
            ui.Click("Roll");

            var tags = ui.Texts().Where(t => t.Name == "UniqueTagText").Select(t => t.Text).ToArray();
            Assert.Equal(["Used", "Unique", "Unique"], tags);
            Assert.Contains(ui.Texts(), t => t.Text == "Unique results: 1 of 3 used");
            Assert.Contains(ui.Texts(), t => t.Text == "Goblin");                       // how it looks: the Polish A tests below
            Assert.DoesNotContain(ui.Texts(), t => t.Name == "RerollNoteText");

            ui.Click("Reset Used Results…");                                         // the harness confirms every question
            Assert.Equal(["Unique", "Unique", "Unique"], ui.Texts().Where(t => t.Name == "UniqueTagText").Select(t => t.Text).ToArray());
            Assert.Contains(ui.Texts(), t => t.Text == "Unique results: 0 of 3 used");
        });
    }

    [Fact]
    public void Tables_without_unique_rows_show_no_Unique_UI_on_the_Roll_screen()
    {
        Sta.Run(() =>
        {
            using var ui = Open();
            ui.SelectTable("Monsters");
            ui.Click("Roll");
            Assert.DoesNotContain(ui.Texts(), t => t.Name is "UniqueTagText" or "UsedResultsSummaryText" or "RerollNoteText");
            Assert.False(ui.HasVisibleButton("Reset Used Results…"));
        });
    }

    [Fact]
    public void A_rerolled_result_shows_its_note_under_the_roll()
    {
        Sta.Run(() =>
        {
            var table = new RollableTable
            {
                Id = 1, Name = "Monsters", Dice = DiceExpression.Parse("d6"),
                ResultSets = [new ResultSet { Entries = [
                    new TableEntry { Min = 1, Max = 3, Text = "Goblin", IsUnique = true, RowKey = Guid.NewGuid() },
                    new TableEntry { Min = 4, Max = 6, Text = "Ogre", IsUnique = true, RowKey = Guid.NewGuid() }] }],
            };
            var session = new RollViewModel(table, new ScriptedDice(1, 2, 3, 5));
            var window = new Window
            {
                Content = new RollView { DataContext = session }, Width = 900, Height = 700,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
            };
            window.Show();
            try
            {
                session.RollCommand.Execute(null);                                   // Goblin
                session.RollCommand.Execute(null);                                   // 2, 3 used, then 5: Ogre
                window.UpdateLayout();
                var notes = ViewTests.FindAll<TextBlock>(window).Where(t => t.Name == "RerollNoteText" && t.IsVisible).ToList();
                Assert.Equal("Rerolled 2×", Assert.Single(notes).Text);
            }
            finally { window.Close(); }
        });
    }

    // ---- UI Polish A: how used rows look ---------------------------------------------------------------------------------

    private static readonly System.Windows.Media.Color Used = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE);
    private static readonly System.Windows.Media.Color UsedText = System.Windows.Media.Color.FromRgb(0x5F, 0x5F, 0x5F);
    private static readonly System.Windows.Media.Color Match = System.Windows.Media.Color.FromRgb(0xDF, 0xF1, 0xE1);

    private static System.Windows.Media.Color? Fill(Border b) => (b.Background as System.Windows.Media.SolidColorBrush)?.Color;

    private static System.Windows.Media.Color? Ink(TextBlock t) => (t.Foreground as System.Windows.Media.SolidColorBrush)?.Color;

    /// <summary>Opens a RollView on <paramref name="table"/> off-screen, with these dice, and runs <paramref name="check"/>.</summary>
    private static void OnRollView(RollableTable table, int[] rolls, Action<RollViewModel, Window> check)
    {
        var session = new RollViewModel(table, new ScriptedDice(rolls));
        var window = new Window
        {
            Content = new RollView { DataContext = session }, Width = 1000, Height = 700,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
        };
        window.Show();
        try { check(session, window); }
        finally { window.Close(); }
    }

    /// <summary>The visible bordered row or cell (named <paramref name="name"/>) whose result text is <paramref name="text"/>.</summary>
    private static (Border Box, TextBlock Text) Shown(Window window, string name, string text)
    {
        window.UpdateLayout();
        var box = ViewTests.FindAll<Border>(window).Single(b => b.Name == name && b.IsVisible
            && ViewTests.FindAll<TextBlock>(b).Any(t => t.Text == text));
        return (box, ViewTests.FindAll<TextBlock>(box).Single(t => t.Text == text));
    }

    private static TableEntry Row(int min, int max, string text, bool unique) =>
        new() { Min = min, Max = max, Text = text, IsUnique = unique, RowKey = Guid.NewGuid() };

    [Fact]
    public void A_used_row_is_gray_with_muted_text_and_the_rolled_row_stays_green_with_normal_text()
    {
        Sta.Run(() =>
        {
            var table = new RollableTable
            {
                Id = 1, Name = "Monsters", Dice = DiceExpression.Parse("d6"),
                ResultSets = [new ResultSet { Entries = [Row(1, 2, "Goblin", true), Row(3, 4, "Ogre", true), Row(5, 6, "Rats", false)] }],
            };
            OnRollView(table, [1, 3], (session, window) =>
            {
                var normal = Ink(Shown(window, "EntryRow", "Rats").Text);
                Assert.NotEqual(UsedText, normal);

                // Nothing rolled: an available Unique row looks exactly like a repeatable one (only its tag differs).
                Assert.Null(Fill(Shown(window, "EntryRow", "Goblin").Box));
                Assert.Equal(normal, Ink(Shown(window, "EntryRow", "Goblin").Text));

                session.RollCommand.Execute(null);                                   // Goblin: rolled, and used at once
                var goblin = Shown(window, "EntryRow", "Goblin");
                Assert.Equal(Match, Fill(goblin.Box));                               // green wins over gray
                Assert.Equal(normal, Ink(goblin.Text));                              // normal text on the green

                session.RollCommand.Execute(null);                                   // Ogre
                goblin = Shown(window, "EntryRow", "Goblin");
                Assert.Equal(Used, Fill(goblin.Box));                                // used, not the latest roll: gray, whole row
                Assert.Equal(UsedText, Ink(goblin.Text));
                Assert.Equal(Match, Fill(Shown(window, "EntryRow", "Ogre").Box));
                Assert.Equal(normal, Ink(Shown(window, "EntryRow", "Ogre").Text));
                var rats = Shown(window, "EntryRow", "Rats");
                Assert.Null(Fill(rats.Box));                                         // repeatable: unchanged, no tag
                Assert.Equal(normal, Ink(rats.Text));
                Assert.Equal(["Used", "Used"], ViewTests.FindAll<TextBlock>(window)
                    .Where(t => t.Name == "UniqueTagText" && t.IsVisible).Select(t => t.Text).ToArray());
            });
        });
    }

    [Fact]
    public void In_an_aligned_table_each_cell_is_gray_on_its_own_and_never_over_the_rolled_rows_green()
    {
        Sta.Run(() =>
        {
            var table = new RollableTable
            {
                Id = 1, Name = "Paired", Dice = DiceExpression.Parse("d6"),
                ResultSets = [
                    new ResultSet { Name = "Who", Entries = [Row(1, 3, "Bandits", false), Row(4, 6, "Knights", true)] },
                    new ResultSet { Name = "Where", Entries = [Row(1, 3, "Ford", true), Row(4, 6, "Bridge", true)] }],
            };
            OnRollView(table, [1, 4], (session, window) =>
            {
                Assert.True(session.Current.IsAligned);
                session.RollCommand.Execute(null);                                   // row 1: Ford is used, but it is the rolled row
                Assert.Null(Fill(Shown(window, "AlignedCell", "Ford").Box));         // so the row's green shows through
                Assert.NotEqual(UsedText, Ink(Shown(window, "AlignedCell", "Ford").Text));

                session.RollCommand.Execute(null);                                   // row 2: Knights and Bridge used, and rolled
                Assert.Equal(Used, Fill(Shown(window, "AlignedCell", "Ford").Box));  // row 1 now: only its used cell is gray
                Assert.Equal(UsedText, Ink(Shown(window, "AlignedCell", "Ford").Text));
                Assert.Null(Fill(Shown(window, "AlignedCell", "Bandits").Box));      // the repeatable cell beside it is not
                Assert.NotEqual(UsedText, Ink(Shown(window, "AlignedCell", "Bandits").Text));
                Assert.Null(Fill(Shown(window, "AlignedCell", "Knights").Box));      // the rolled row: green, not gray
                Assert.Null(Fill(Shown(window, "AlignedCell", "Bridge").Box));
                Assert.NotEqual(UsedText, Ink(Shown(window, "AlignedCell", "Knights").Text));
                Assert.Equal(["Used", "Used", "Used"], ViewTests.FindAll<TextBlock>(window)   // Ford; Knights, Bridge (Bandits has none)
                    .Where(t => t.Name == "UniqueTagText" && t.IsVisible).Select(t => t.Text).ToArray());
            });
        });
    }

    /// <summary>WCAG 2 contrast ratio of two opaque colours.</summary>
    private static double Contrast(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        static double Channel(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double L(System.Windows.Media.Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    [Fact]
    public void Used_rows_stay_readable_and_distinct_from_the_rolled_highlight()
    {
        Sta.Run(() =>
        {
            var view = new RollView();
            System.Windows.Media.Color Brush(string key) => ((System.Windows.Media.SolidColorBrush)view.Resources[key]).Color;
            Assert.Equal(Used, Brush("UsedBrush"));
            Assert.Equal(UsedText, Brush("UsedText"));
            Assert.Equal(Match, Brush("MatchBrush"));
            Assert.True(Contrast(UsedText, Used) >= 4.5, $"muted text on gray is {Contrast(UsedText, Used):0.00}:1");
            Assert.True(Contrast(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66), Used) >= 4.5, "the Used tag on gray");
            Assert.True(Contrast(System.Windows.Media.Colors.Black, Match) >= 7, "normal text on the green");
            Assert.NotEqual(Used, Match);
        });
    }
}
