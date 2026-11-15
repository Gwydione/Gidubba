using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;
using TableForge.Views;

namespace TableForge.Tests;

/// <summary>
/// Gidubba 1.6 Complete Roll Results in the real Roll screen: the panel above the trail, Copy Complete Result, collapse,
/// its bounded height in small windows, scrolling and keyboard order — and that an ordinary roll looks exactly as before.
/// </summary>
[Collection("UI")]
public class CompleteResultViewTests
{
    private static Border Panel(UiHarness ui) => ui.One<Border>(b => b.Name == "CompleteResultPanel");
    private static ScrollViewer Body(UiHarness ui) => ui.One<ScrollViewer>(s => s.Name == "CompleteResultScroller");
    private static ScrollViewer Trail(UiHarness ui) => ui.One<ScrollViewer>(s => s.Name == "TrailScroller");
    private static Expander Expander(UiHarness ui) => ui.One<Expander>(e => e.Name == "CompleteResultExpander");
    private static RollViewModel Session(UiHarness ui) => (RollViewModel)ui.Main.Current!;

    private static string[] Shown(UiHarness ui, string name) =>
        ui.Texts().Where(t => t.Name == name).Select(t => t.Text).ToArray();

    private static Rect BoundsIn(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    /// <summary>Saves a chain of <paramref name="count"/> d20 tables ("Step 1" → "Step 2" → …), each one long result linking on.</summary>
    private static void SeedChain(AppDatabase db, Collection c, int count, string text = "")
    {
        long? next = null;
        for (var i = count; i >= 1; i--)
        {
            var table = Fixtures.Table(DiceExpression.Parse("d20"), (1, 20, text.Length > 0 ? $"Step {i}: {text}" : $"Result of step {i}"));
            table.Name = $"Step {i}";
            table.CollectionId = c.Id;
            table.ResultSets[0].Entries[0].LinkedTableId = next;
            next = db.SaveTable(table).Id;
        }
    }

    private const string Long = "A long result written by the table's author, which goes on for a while so that it wraps over several lines " +
        "in the panel and in the trail alike, as real tables often do.";

    // ---- an ordinary roll is unchanged ----------------------------------------------------------------------------------

    [Fact]
    public void An_ordinary_roll_shows_no_panel_and_the_trail_starts_at_the_top_of_the_screen_as_before()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(68, (db, c) => db.SaveTable(Fixtures.RoomFeatures(c.Id)));
            ui.SelectTable("Room Features");
            ui.Click("Roll");

            Assert.Equal(Visibility.Collapsed, Panel(ui).Visibility);
            Assert.Equal(0, Panel(ui).ActualHeight);
            var view = ui.One<RollView>(_ => true);
            Assert.Equal(0, BoundsIn(Trail(ui), view).Top, 1);
            Assert.False(ui.HasVisibleButton("Copy Complete Result"));
        });
    }

    // ---- the panel follows the chain ------------------------------------------------------------------------------------

    [Fact]
    public void Following_a_link_shows_the_panel_with_captions_and_a_pending_table_then_the_roll_replaces_it()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            Assert.Equal(Visibility.Collapsed, Panel(ui).Visibility);

            ui.Click("Open Scavenged Items");
            Assert.True(Panel(ui).IsVisible);
            Assert.Equal(["Complete Result"], Shown(ui, "CompleteResultHeadingText"));
            Assert.Equal(["Scavenging · 6"], Shown(ui, "CompleteResultCaption"));
            Assert.Equal(["1x Scavenged Item"], Shown(ui, "CompleteResultLineText"));
            Assert.Equal(["→ Scavenged Items — not rolled yet"], Shown(ui, "CompleteResultPendingText"));
            Assert.Equal(18, ui.Texts().Single(t => t.Name == "CompleteResultLineText").FontSize);

            ui.Dice.Value = 13;
            ui.Click("Roll");
            Assert.Equal(["Scavenging · 6", "Scavenged Items · 13"], Shown(ui, "CompleteResultCaption"));
            Assert.Equal(["1x Scavenged Item", "Waterskin"], Shown(ui, "CompleteResultLineText"));
            Assert.Empty(Shown(ui, "CompleteResultPendingText"));

            // The trail below is exactly what it always was: each roll, its own size, the parent's link a plain note.
            Assert.Equal(["1x Scavenged Item", "Waterskin"], ui.Texts().Where(t => t.FontSize == 26).Select(t => t.Text));
            Assert.Contains(ui.Texts(), t => t.Text == "→ Scavenged Items");
        });
    }

    [Fact]
    public void Several_results_of_one_action_on_one_table_show_the_panel()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(13, (db, c) => db.SaveTable(Fixtures.ScavengedItems(c.Id)));
            ui.SelectTable("Scavenged Items");
            Session(ui).RollCount = 2;
            ui.Click("Roll 2 Times");

            Assert.True(Panel(ui).IsVisible);
            Assert.Equal(["Scavenged Items · roll 1 · 13", "Scavenged Items · roll 2 · 13"], Shown(ui, "CompleteResultCaption"));
        });
    }

    [Fact]
    public void Back_to_the_original_table_keeps_the_previous_chain_and_says_so()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            ui.Click("Roll");
            ui.Click("← Back to Scavenging");

            Assert.Equal(["Complete Result · previous roll"], Shown(ui, "CompleteResultHeadingText"));
            Assert.Equal(2, Shown(ui, "CompleteResultLineText").Length);

            ui.Dice.Value = 3;
            ui.Click("Roll");
            Assert.False(Panel(ui).IsVisible); // one roll of a new chain: nothing to assemble
        });
    }

    [Fact]
    public void Problems_and_unavailable_links_use_the_screens_own_colours()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) =>
            {
                var (items, _) = Fixtures.SeedScavenging(db, c.Id);
                items.ResultSets[0].Entries.RemoveAll(e => e.Min == 20); // no row covers 20
                db.SaveTable(items);
            });
            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            ui.Dice.Value = 20;
            ui.Click("Roll");

            var problem = ui.Texts().Single(t => t.Name == "CompleteResultLineText" && t.Text == "No entry covers 20.");
            Assert.Equal(Color.FromRgb(0xA0, 0, 0), ((SolidColorBrush)problem.Foreground).Color);
        });
    }

    // ---- Copy Complete Result ------------------------------------------------------------------------------------------

    [Fact]
    public void Copy_Complete_Result_copies_the_text_and_says_so_beside_the_button_not_in_the_Export_message()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            ui.Dice.Value = 13;
            ui.Click("Roll");

            ui.Click("Copy Complete Result");

            Assert.Equal("1x Scavenged Item\r\nWaterskin", ui.Copied.Last());
            Assert.Equal(["Complete result copied."], Shown(ui, "CompleteResultCopyMessageText"));
            Assert.Empty(Shown(ui, "CopyMessageText"));

            ui.Click("Roll");
            Assert.Empty(Shown(ui, "CompleteResultCopyMessageText")); // a new result: the message no longer applies
        });
    }

    // ---- collapse --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Collapsed_the_panel_is_only_its_header_for_this_Roll_screen_and_a_newly_opened_table_starts_open()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            var open = Panel(ui).ActualHeight;

            var toggle = (IExpandCollapseProvider)new ExpanderAutomationPeer(Expander(ui)).GetPattern(PatternInterface.ExpandCollapse);
            toggle.Collapse();
            ui.Layout();

            Assert.False(Session(ui).IsCompleteResultExpanded);
            Assert.False(Body(ui).IsVisible);
            Assert.True(Panel(ui).ActualHeight < open);
            Assert.True(Panel(ui).ActualHeight <= 44, $"collapsed, the panel is {Panel(ui).ActualHeight:0} px");
            Assert.True(ui.HasVisibleButton("Copy Complete Result")); // still copyable while collapsed

            ui.Click("Roll"); // stays collapsed while on this screen
            Assert.False(Body(ui).IsVisible);

            ui.SelectTable("Scavenging"); // a new Roll screen
            ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            Assert.True(Session(ui).IsCompleteResultExpanded);
            Assert.True(Body(ui).IsVisible);
        });
    }

    // ---- small windows, scrolling ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1000, 640, true)]
    [InlineData(1000, 640, false)]
    [InlineData(1366, 768, true)]
    [InlineData(1920, 1080, false)]
    public void A_long_chain_scrolls_inside_a_bounded_panel_and_the_Roll_controls_stay_in_view(int width, int height, bool recentRolls)
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(7, (db, c) => SeedChain(db, c, 6, Long));
            ui.Window.Width = width;
            ui.Window.Height = height;
            ui.Main.AutoFollow.IsOn = true;
            ui.Main.IsRecentRollsExpanded = recentRolls;
            ui.Layout();
            ui.SelectTable("Step 1");
            ui.Click("Roll");
            ui.Layout();

            var view = ui.One<RollView>(_ => true);
            var body = Body(ui);
            Assert.Equal(6, Shown(ui, "CompleteResultLineText").Length);
            Assert.True(body.ActualHeight <= Math.Min(RollView.CompleteResultMaxBody, view.ActualHeight * RollView.CompleteResultShare) + 1
                        || body.ActualHeight <= RollView.CompleteResultMinBody + 1,
                $"the body is {body.ActualHeight:0} px of a {view.ActualHeight:0} px screen");
            Assert.True(body.ExtentHeight > body.ViewportHeight, "six long results overflow the panel, which scrolls on its own");
            Assert.Equal(Visibility.Visible, body.ComputedVerticalScrollBarVisibility);
            Assert.Equal(body.ScrollableHeight, body.VerticalOffset, 1); // the newest part is in view

            // The Roll controls: whole, inside the trail's view, and (with Recent Rolls open) above it.
            var trail = Trail(ui);
            var controls = ui.One<StackPanel>(s => s.Name == "RollControls");
            Assert.True(trail.ViewportHeight >= controls.ActualHeight, $"the trail shows {trail.ViewportHeight:0} px; the controls need {controls.ActualHeight:0}");
            var viewport = BoundsIn(trail, ui.Window);
            var roll = BoundsIn(ui.One<Button>(b => b.Name == "RollButton"), ui.Window);
            Assert.True(roll.Top >= viewport.Top - 1 && roll.Bottom <= viewport.Bottom + 1, $"Roll ({roll}) must be inside the trail's view ({viewport})");
            if (recentRolls)
                Assert.True(roll.Bottom <= BoundsIn(ui.One<Expander>(e => e.Name == "RecentRollsPanel"), ui.Window).Top);

            // Every link note, pending line and caption wraps: nothing scrolls sideways.
            Assert.True(body.ExtentWidth <= body.ViewportWidth + 1);
        });
    }

    [Fact]
    public void Opening_another_table_starts_the_panel_and_the_trail_at_their_tops()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(7, (db, c) => SeedChain(db, c, 6, Long));
            ui.Window.Height = 640;
            ui.Main.AutoFollow.IsOn = true;
            ui.Layout();
            ui.SelectTable("Step 1");
            ui.Click("Roll");
            Assert.True(Body(ui).VerticalOffset > 0);

            ui.SelectTable("Step 2");
            ui.Click("Roll");
            ui.Layout();
            Assert.Equal(5, Shown(ui, "CompleteResultLineText").Length);
            Assert.Equal(Body(ui).ScrollableHeight, Body(ui).VerticalOffset, 1); // a fresh chain, newest part in view
        });
    }

    // ---- keyboard --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Focus_still_goes_to_Roll_after_a_link_and_Tab_reaches_the_panel_toggle_then_Copy()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            ui.Click("Open Scavenged Items");
            ui.Layout();

            var rollButton = ui.One<Button>(b => b.Name == "RollButton");
            Assert.Same(rollButton, ui.Focused);

            var header = ui.All<System.Windows.Controls.Primitives.ToggleButton>().Single(t => Expander(ui).IsAncestorOf(t));
            Assert.True(header.Focusable);
            header.Focus();
            ui.Layout();
            Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            ui.Layout();
            Assert.Same(ui.One<Button>(b => b.Name == "CopyCompleteResultButton"), Keyboard.FocusedElement);
            Assert.Equal("Complete Result", AutomationProperties.GetName(Expander(ui)));
        });
    }

    [Fact]
    public void With_the_toggle_or_Copy_focused_Page_and_Home_End_keys_scroll_the_panel_without_changing_the_Tab_order()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(7, (db, c) => SeedChain(db, c, 6, Long));
            ui.Window.Height = 640;
            ui.Main.AutoFollow.IsOn = true;
            ui.Layout();
            ui.SelectTable("Step 1");
            ui.Click("Roll");
            var body = Body(ui);
            Assert.True(body.ScrollableHeight > 0);

            var header = ui.All<System.Windows.Controls.Primitives.ToggleButton>().Single(t => Expander(ui).IsAncestorOf(t));
            ui.Press(header, Key.Home, until: () => body.VerticalOffset == 0);
            Assert.Equal(0, body.VerticalOffset);
            ui.Press(header, Key.PageDown, until: () => body.VerticalOffset > 0);
            Assert.True(body.VerticalOffset > 0);
            var copy = ui.One<Button>(b => b.Name == "CopyCompleteResultButton");
            ui.Press(copy, Key.End, until: () => Math.Abs(body.VerticalOffset - body.ScrollableHeight) < 1);
            Assert.Equal(body.ScrollableHeight, body.VerticalOffset, 1);
            ui.Press(copy, Key.PageUp, until: () => body.VerticalOffset < body.ScrollableHeight - 1);
            Assert.True(body.VerticalOffset < body.ScrollableHeight);

            Assert.False(body.Focusable);                    // still not a Tab stop of its own
            Assert.Empty(ui.Copied);                         // the keys never press Copy
            Assert.True(Session(ui).IsCompleteResultExpanded); // nor toggle the panel
            Assert.Contains("Page Up", AutomationProperties.GetHelpText(Expander(ui)));

            ((IExpandCollapseProvider)new ExpanderAutomationPeer(Expander(ui)).GetPattern(PatternInterface.ExpandCollapse)).Collapse();
            ui.Layout();
            var trailBefore = Trail(ui).VerticalOffset;
            ui.Press(header, Key.Home);
            Assert.Equal(trailBefore, Trail(ui).VerticalOffset); // collapsed: the keys do nothing here
        });
    }
}
