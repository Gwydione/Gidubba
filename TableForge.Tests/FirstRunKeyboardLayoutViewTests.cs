using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>The whole V1 workflow from a brand-new installation, driven through the real window like a person would.</summary>
[Collection("UI")]
public class FirstRunViewTests
{
    [Fact]
    public void First_launch_to_a_saved_rolled_and_found_again_table_through_the_real_views()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(9);                       // an empty database: nothing exists yet

            // ---- what a new user sees ----------------------------------------------------------
            var empty = ui.One<TextBlock>(t => t.Name == "EmptyStateBlock");
            Assert.True(empty.IsVisible);
            Assert.StartsWith("Welcome to Gidubba.", empty.Text);
            var nameBox = ui.One<TextBox>(t => t.Name == "NewCollectionBox");
            Assert.Same(nameBox, ui.Focused);                      // the cursor is already where the first thing to do is
            Assert.False(ui.One<Button>(b => b.Content as string == "Paste Table…").IsEnabled);

            // ---- create the first collection with the keyboard ----------------------------------
            nameBox.Text = "Solo Play";
            ui.Press(nameBox, Key.Enter, until: () => ui.Main.SelectedCollection is not null);
            Assert.Equal("Solo Play", ui.Main.SelectedCollection?.Name);
            var pasteButton = ui.One<Button>(b => b.Name == "PasteTableButton");
            Assert.True(pasteButton.IsEnabled);                    // Paste Table is available immediately
            Assert.Same(pasteButton, ui.Focused);                  // ...and is the natural next step
            Assert.Contains("no tables yet", empty.Text);

            // ---- paste, interpret, review --------------------------------------------------------
            ui.Click("Paste Table…");
            var pasteBox = ui.One<TextBox>(t => t.Name == "PasteSourceBox");
            Assert.Same(pasteBox, ui.Focused);                     // ready to paste
            pasteBox.Text = Fixtures.RandomStartingGear;
            ui.Click("Interpret");

            Assert.Same(ui.One<TextBox>(t => t.Name == "NameBox"), ui.Focused);
            Assert.Equal("Random Starting Gear", ui.One<TextBox>(t => t.Name == "NameBox").Text);
            Assert.Equal(8, ui.One<ItemsControl>(i => i.Name == "RowsList").Items.Count);
            Assert.Contains(ui.All<TextBlock>(), t => t.Name == "NotesText" && t.IsVisible && t.Text.Contains("Supplies")); // the joined line is flagged

            // ---- save, roll, manual roll -----------------------------------------------------------
            ui.Click("Save Table");
            Assert.Same(ui.One<Button>(b => b.Name == "RollButton"), ui.Focused);   // Enter/Space now rolls
            Assert.Equal("Saved \"Random Starting Gear\".", ui.Main.Status);
            ui.Click("Roll");
            Assert.Contains(ui.Texts(), t => t.Text == "Rolled 9");
            Assert.Contains(ui.Texts(), t => t.Text == "D20 Construction Supplies" && t.FontSize == 26);

            var manual = ui.One<TextBox>(t => t.Name == "ManualBox");
            manual.Text = "1";
            ui.Press(manual, Key.Enter, until: () => ui.Texts().Any(t => t.Text == "Rolled 1"));   // Enter resolves the typed roll
            Assert.Contains(ui.Texts(), t => t.Text == "Rolled 1");
            Assert.Contains(ui.Texts(), t => t.Text == "Backpack" && t.FontSize == 26);

            // ---- leave, then find the table again ------------------------------------------------
            ui.Click("Paste Table…");
            ui.Click("Cancel");
            Assert.Null(ui.Main.Current);
            Assert.StartsWith("Choose a table on the left", empty.Text);

            var filter = ui.One<TextBox>(t => t.Name == "TableFilterBox");
            filter.Text = "starting";
            ui.Press(filter, Key.Enter, until: () => ui.Main.Current is RollViewModel);   // Enter opens the first match
            Assert.Equal("Random Starting Gear", ((RollViewModel)ui.Main.Current!).Title);

            Assert.Equal(["Random Starting Gear"], ui.Main.RecentTables.Select(t => t.Name).ToArray());
            Assert.Equal(2, ui.Main.RecentRolls.Count);            // the random roll and the manual one
            Assert.Equal(["1", "9"], ui.Main.RecentRolls.Select(r => r.RollDisplay).ToArray());

            ui.Press(filter, Key.Escape, until: () => filter.Text == "");   // Escape clears the search
            Assert.Equal("", filter.Text);
        });
    }
}

/// <summary>Keyboard behavior of the primary controls.</summary>
[Collection("UI")]
public class KeyboardViewTests
{
    [Fact]
    public void Arrowing_through_the_table_list_only_highlights_and_Enter_opens()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            var list = ui.One<ListBox>(l => l.Name == "TablesList");

            list.SelectedItem = list.Items[0];                     // an arrow key moves the selection like this
            list.SelectedItem = list.Items[1];
            ui.Layout();
            Assert.Null(ui.Main.Current);                          // nothing opened while browsing
            Assert.Empty(ui.Main.RecentTables);

            ui.Press(list, Key.Enter, until: () => ui.Main.Current is RollViewModel);
            Assert.Equal(((TableSummary)list.SelectedItem).Name, ((RollViewModel)ui.Main.Current!).Title);
            Assert.Same(ui.One<Button>(b => b.Name == "RollButton"), ui.Focused);
            Assert.Single(ui.Main.RecentTables);
        });
    }

    [Fact]
    public void The_search_box_is_where_the_cursor_starts_when_collections_exist()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));

            Assert.Same(ui.One<TextBox>(t => t.Name == "TableFilterBox"), ui.Focused);
        });
    }

    [Fact]
    public void Escape_closes_the_paste_rows_area_and_returns_focus_to_its_button()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(1, (db, c) => db.SaveTable(Fixtures.RoomFeatures(c.Id)));
            ui.SelectTable("Room Features");
            ui.Click("Edit table");
            var box = ui.One<TextBox>(t => t.Name == "PasteRowsBox");
            Assert.False(box.IsVisible);

            ui.Click("Paste rows into this set…");
            Assert.True(box.IsVisible);
            Assert.Same(box, ui.Focused);                          // typing/pasting can start immediately

            ui.Press(box, Key.Escape, until: () => !box.IsVisible);

            Assert.False(box.IsVisible);
            Assert.Same(ui.One<Button>(b => b.Name == "PasteRowsToggle"), ui.Focused);
            Assert.False(((ReviewViewModel)ui.Main.Current!).IsPasteRowsOpen);
        });
    }

    [Fact]
    public void Ctrl_Enter_is_bound_to_interpret_and_to_adding_pasted_rows()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(1, (db, c) => db.SaveTable(Fixtures.RoomFeatures(c.Id)));
            ui.Click("Paste Table…");
            var pasteBox = ui.One<TextBox>(t => t.Name == "PasteSourceBox");
            var interpret = pasteBox.InputBindings.OfType<KeyBinding>().Single();
            Assert.Equal((Key.Return, ModifierKeys.Control), (interpret.Key, interpret.Modifiers));
            Assert.Same(((PasteViewModel)ui.Main.Current!).InterpretCommand, interpret.Command);
            ui.Click("Cancel");

            ui.SelectTable("Room Features");
            ui.Click("Edit table");
            var panel = ui.One<Border>(b => b.Child is StackPanel && b.InputBindings.Count == 2);
            var keys = panel.InputBindings.OfType<KeyBinding>().ToList();
            Assert.Contains(keys, k => k.Key == Key.Escape && k.Modifiers == ModifierKeys.None);
            Assert.Contains(keys, k => k.Key == Key.Return && k.Modifiers == ModifierKeys.Control
                                       && ReferenceEquals(k.Command, ((ReviewViewModel)ui.Main.Current!).AppendPastedRowsCommand));
        });
    }

    [Fact]
    public void Delete_row_buttons_are_not_tab_stops_so_tab_walks_across_each_row()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(1, (db, c) => db.SaveTable(Fixtures.RoomFeatures(c.Id)));
            ui.SelectTable("Room Features");
            ui.Click("Edit table");
            var rows = ui.One<ItemsControl>(i => i.Name == "RowsList");

            var deleteButtons = ui.All<Button>().Where(b => b.Content as string == "✕" && rows.IsAncestorOf(b)).ToList();
            Assert.Equal(4, deleteButtons.Count);
            Assert.All(deleteButtons, b => Assert.False(b.IsTabStop));

            // Tab order inside a row: range → text → link chooser → Unique checkbox → next row's range.
            var firstRow = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(0);
            var boxes = ViewTests.FindAll<TextBox>(firstRow).ToList();
            var combo = ViewTests.FindAll<ComboBox>(firstRow).Single();
            var unique = ViewTests.FindAll<CheckBox>(firstRow).Single(b => b.Name == "UniqueBox");
            var nextRange = ViewTests.FindAll<TextBox>((ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(1)).First();

            boxes[0].Focus();
            Assert.Same(boxes[0], ui.Focused);
            boxes[0].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(boxes[1], ui.Focused);
            boxes[1].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(combo, ui.Focused);
            combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(unique, ui.Focused);
            unique.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.Same(nextRange, ui.Focused);
        });
    }

    [Fact]
    public void Following_a_link_moves_focus_to_Roll_and_a_manual_roll_keeps_the_cursor_in_the_box()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            ui.SelectTable("Scavenging");
            ui.Click("Roll");

            var manual = ui.One<TextBox>(t => t.Name == "ManualBox");
            manual.Focus();
            manual.Text = "9";
            ui.Press(manual, Key.Enter, until: () => ui.Texts().Any(t => t.Text == "Rolled 9"));
            Assert.Same(manual, ui.Focused);                       // ready for the next number without touching the mouse

            ui.Click("Open Scavenged Items");
            Assert.Same(ui.One<Button>(b => b.Name == "RollButton"), ui.Focused);
        });
    }
}

/// <summary>The window at realistic desktop sizes: everything stays reachable and readable, using ordinary scrolling.</summary>
[Collection("UI")]
public class LayoutViewTests
{
    private static Rect BoundsIn(FrameworkElement element, UIElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool InView(ScrollViewer scroller, FrameworkElement element)
    {
        var b = BoundsIn(element, scroller);
        return b.Top >= -1 && b.Bottom <= scroller.ViewportHeight + 1;
    }

    private static UiHarness Busy(int width, int height)
    {
        var ui = new UiHarness(6, (db, c) =>
        {
            Fixtures.SeedScavenging(db, c.Id);
            db.SaveTable(Fixtures.RoomFeatures(c.Id));
            for (var i = 1; i <= 8; i++)
                db.SaveTable(Fixtures.ScavengedItems(c.Id).Also(t => t.Name = $"A fairly long table name number {i} for testing"));
        });
        ui.Window.Width = width;
        ui.Window.Height = height;
        ui.Layout();
        return ui;
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1024, 768)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void The_left_pane_and_the_Recent_Rolls_panel_stay_usable_with_a_full_set_of_recent_tables_and_rolls(int width, int height)
    {
        Sta.Run(() =>
        {
            using var ui = Busy(width, height);
            foreach (var name in new[] { "Room Features", "Scavenging", "A fairly long table name number 1 for testing",
                                          "A fairly long table name number 2 for testing", "A fairly long table name number 3 for testing" })
            {
                ui.SelectTable(name);
                ui.Click("Roll");
            }
            for (var i = 0; i < 7; i++) ui.Click("Roll"); // ten recent rolls in all

            var scroller = ui.One<ScrollViewer>(s => s.Name == "LeftScroll");
            Assert.Equal(5, ui.Main.RecentTables.Count);                              // still tracked, no longer listed
            Assert.Equal(10, ui.Main.RecentRolls.Count);
            Assert.DoesNotContain(ui.All<Button>(), b => scroller.IsAncestorOf(b) && b.DataContext is RecentRollViewModel or TableSummary);
            Assert.True(InView(scroller, ui.One<Button>(b => b.Name == "PasteTableButton")));
            Assert.True(ui.One<ListBox>(l => l.Name == "TablesList").ActualHeight >= 90, "the table list must keep a usable height");

            // If everything does not fit, the pane scrolls; nothing is clipped without a way to reach it.
            if (scroller.ExtentHeight > scroller.ViewportHeight + 1)
                Assert.Equal(Visibility.Visible, scroller.ComputedVerticalScrollBarVisibility);
            scroller.ScrollToBottom();
            ui.Layout();
            Assert.True(InView(scroller, ui.One<Button>(b => b.Content as string == "Delete table")));

            // Long names are trimmed rather than pushing the pane wider.
            Assert.All(ui.All<TextBlock>().Where(t => scroller.IsAncestorOf(t) && t.Text.StartsWith("A fairly long") && t.IsVisible), t => Assert.Equal(TextTrimming.CharacterEllipsis, t.TextTrimming));
            Assert.True(scroller.ExtentWidth <= scroller.ViewportWidth + 1, "no horizontal scrolling in the left pane");

            // Recent Rolls open: its list takes at most 240 px and 40% of the main area, and the Roll button stays on screen above it.
            ui.Main.IsRecentRollsExpanded = true;
            ui.Layout();
            var main = ui.One<Grid>(g => g.Name == "MainArea");
            var rolls = ui.One<ScrollViewer>(s => s.Name == "RecentRollsScroller");
            Assert.True(rolls.ActualHeight <= Math.Min(240, main.ActualHeight * 0.4) + 1, $"Recent Rolls is {rolls.ActualHeight:0} px of {main.ActualHeight:0}");
            var panelTop = BoundsIn(ui.One<Expander>(e => e.Name == "RecentRollsPanel"), ui.Window).Top;
            var roll = BoundsIn(ui.One<Button>(b => b.Name == "RollButton"), ui.Window);
            Assert.True(roll.Top >= 0 && roll.Bottom <= panelTop, $"the Roll button ({roll}) must stay above Recent Rolls ({panelTop:0})");
            rolls.ScrollToBottom();                                                    // the oldest roll is reachable in its own scroll
            ui.Layout();
            var oldest = ui.All<Button>().Last(b => b.DataContext is RecentRollViewModel && b.IsVisible);
            Assert.True(InView(rolls, oldest));
            Assert.True(rolls.ExtentWidth <= rolls.ViewportWidth + 1, "long results wrap: no horizontal scrolling");
        });
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void The_review_screen_never_clips_its_controls_and_long_text_wraps(int width, int height)
    {
        Sta.Run(() =>
        {
            using var ui = Busy(width, height);
            ui.SelectTable("Room Features");
            ui.Click("Edit table");
            ui.Click("Paste rows into this set…");            // the tallest state of the screen
            var review = (ReviewViewModel)ui.Main.Current!;
            review.SelectedResultSet.Rows[0].Text = "A very long piece of result text that keeps going and going " + string.Concat(Enumerable.Repeat("so that it has to wrap onto another line to stay readable and readable and readable ", 8));
            ui.Layout();

            var clientWidth = ui.Window.ActualWidth;
            foreach (var name in new[] { "Add result set", "Delete result set", "Paste rows into this set…", "Mark All Unique", "Clear Unique", "Add row", "Save Table", "Cancel", "Add to end", "Close" })
            {
                var button = ui.One<Button>(b => b.IsVisible && b.Content as string == name);
                var b = BoundsIn(button, ui.Window);
                Assert.True(b.Left >= 0 && b.Right <= clientWidth + 1, $"'{name}' is clipped horizontally at {width}x{height}: {b}");
            }

            var save = BoundsIn(ui.One<Button>(b => b.Content as string == "Save Table"), ui.Window);
            Assert.True(save.Bottom <= ui.Window.ActualHeight, "the Save button must stay on screen");

            // Every row's ✕ (the rightmost control) is inside the window; the long text wrapped instead of overflowing.
            var rows = ui.One<ItemsControl>(i => i.Name == "RowsList");
            Assert.All(ui.All<Button>().Where(b => b.Content as string == "✕" && rows.IsAncestorOf(b)),
                b => Assert.True(BoundsIn(b, ui.Window).Right <= clientWidth + 1));
            var longBox = ui.All<TextBox>().First(t => rows.IsAncestorOf(t) && t.Text.StartsWith("A very long piece"));
            Assert.True(longBox.ActualHeight > 30, "long text should wrap onto more lines");
            Assert.True(BoundsIn(longBox, ui.Window).Right <= clientWidth + 1);

            // The source pane and the row editor are both present with a usable width.
            Assert.True(ui.One<TextBox>(t => t.Name == "SourceBox").ActualWidth >= 120);
            Assert.True(longBox.ActualWidth >= 150);
        });
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void The_roll_trail_and_the_roll_control_stay_reachable_and_long_results_wrap(int width, int height)
    {
        Sta.Run(() =>
        {
            using var ui = Busy(width, height);
            ui.SelectTable("Scavenging");
            for (var i = 0; i < 12; i++) ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            ui.Dice.Value = 12;
            ui.Click("Roll");

            var scroller = ui.One<ScrollViewer>(s => s.Name == "TrailScroller");
            var roll = ui.One<Button>(b => b.IsVisible && b.Content as string == "Roll");
            Assert.True(InView(scroller, roll), $"Roll must be reachable at {width}x{height}");
            Assert.True(scroller.ExtentHeight >= scroller.ViewportHeight);
            Assert.True(scroller.ExtentWidth <= scroller.ViewportWidth + 1, "no horizontal scrolling in the trail");

            // A long result wraps within the width instead of running off the edge.
            var longText = "An extremely long result that goes on and on " + string.Concat(Enumerable.Repeat("about the many things a party might find in a dusty old chest in the corner ", 8)).TrimEnd();
            ui.SelectTable("Room Features");
            ui.Click("Edit table");
            ((ReviewViewModel)ui.Main.Current!).SelectedResultSet.Rows[2].Text = longText; // covers roll 66-70
            ui.Click("Save Table");
            ui.Dice.Value = 68;
            ui.Click("Roll");
            var shown = ui.Texts().First(t => t.Text == longText && t.FontSize == 26);
            Assert.True(shown.ActualHeight > 40, "the long result should wrap over several lines");
            Assert.True(BoundsIn(shown, ui.Window).Right <= ui.Window.ActualWidth + 1);
        });
    }
}
