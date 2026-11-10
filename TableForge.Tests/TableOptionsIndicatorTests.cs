using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// 1.5: the Roll screen's read-only table options line — "Clamp: On / Off / Not available" by what Clamp actually does on the
/// current table, and "Manual selection: On / Off" — with a tooltip and help text saying they are changed in Edit table.
/// </summary>
public class TableOptionsIndicatorTests
{
    private const string Hint = "These are table settings. To change them, use Edit table in the table list.";

    /// <summary>In-memory tables: every face of the dice is its own row, "Name 1" … "Name N".</summary>
    private sealed class World
    {
        private readonly Dictionary<long, RollableTable> _all = [];

        public RollableTable Add(string name, string dice = "d6", bool clamp = false, bool manual = false)
        {
            var parsed = DiceExpression.Parse(dice);
            var values = parsed.IsD66
                ? Enumerable.Range(1, 6).SelectMany(t => Enumerable.Range(1, 6).Select(o => t * 10 + o)).ToList()
                : Enumerable.Range(parsed.Min, parsed.Max - parsed.Min + 1).ToList();
            var table = new RollableTable
            {
                Id = _all.Count + 1,
                Name = name,
                Dice = parsed,
                ClampResultsToRange = clamp,
                IsManualSelection = manual,
                ResultSets = [new ResultSet { Entries = values.Select(v => new TableEntry { Min = v, Max = v, Text = $"{name} {v}", SortOrder = v }).ToList() }],
            };
            _all[table.Id] = table;
            return table;
        }

        public void Link(RollableTable from, int value, RollableTable to) =>
            from.ResultSets[0].Entries.Single(e => e.Min == value).LinkedTableId = to.Id;

        public RollableTable? Load(long id) => _all.GetValueOrDefault(id);
    }

    private static RollViewModel Open(World w, RollableTable table, IDiceProvider? dice = null, bool autoFollow = false) =>
        new(table, dice ?? new SequenceDice(1), w.Load, autoFollow: new AutoFollowSetting(autoFollow));

    private static (string Clamp, string Manual, string Tip) Options(RollViewModel s) => (s.ClampStatus, s.ManualSelectionStatus, s.TableOptionsToolTip);

    // ---- what each table shows ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_table_that_can_clamp_shows_its_own_Clamp_and_Manual_selection_options()
    {
        var w = new World();
        Assert.Equal(("Clamp: On", "Manual selection: Off", Hint), Options(Open(w, w.Add("A", clamp: true))));
        Assert.Equal(("Clamp: Off", "Manual selection: Off", Hint), Options(Open(w, w.Add("B"))));
        Assert.Equal(("Clamp: Off", "Manual selection: On", Hint), Options(Open(w, w.Add("C", manual: true))));
        Assert.Equal(("Clamp: On", "Manual selection: On", Hint), Options(Open(w, w.Add("D", "2d6", clamp: true, manual: true))));
    }

    [Fact]
    public void Clamp_is_Not_available_on_a_d66_table_with_the_reason_even_when_its_stored_option_is_on()
    {
        var w = new World();
        foreach (var stored in new[] { false, true })
        {
            var s = Open(w, w.Add($"Hexes {stored}", "d66", clamp: stored));
            Assert.Equal("Clamp: Not available", s.ClampStatus);
            Assert.Equal($"{Hint}\n{TableClamp.D66Message}", s.TableOptionsToolTip);
            Assert.Equal("Manual selection: Off", s.ManualSelectionStatus);
        }
    }

    [Fact]
    public void Clamp_is_Not_available_when_result_sets_have_different_ranges_or_there_are_no_rows()
    {
        var w = new World();
        var mismatched = w.Add("Obstacle", clamp: true);
        mismatched.ResultSets.Add(new ResultSet { Name = "Other", SortOrder = 1, Entries = [new TableEntry { Min = 1, Max = 4, Text = "Low" }] });
        var s = Open(w, mismatched);
        Assert.Equal("Clamp: Not available", s.ClampStatus);
        Assert.Equal($"{Hint}\n{TableClamp.IncompatibleMessage}", s.TableOptionsToolTip);

        var empty = w.Add("Empty", clamp: true);
        empty.ResultSets[0].Entries.Clear();
        s = Open(w, empty);
        Assert.Equal("Clamp: Not available", s.ClampStatus);
        Assert.Equal($"{Hint}\n{TableClamp.NoRowsMessage}", s.TableOptionsToolTip);
    }

    // ---- the line follows the current table ----------------------------------------------------------------------------------

    [Fact]
    public void Following_a_link_by_hand_and_Back_show_each_current_tables_options()
    {
        var w = new World();
        var a = w.Add("A", clamp: true);
        var b = w.Add("B", "d66", manual: true);
        w.Link(a, 1, b);
        var s = Open(w, a);
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.RollCommand.Execute(null);                                     // A 1 → B: a roll alone changes nothing here
        Assert.Equal(("Clamp: On", "Manual selection: Off", Hint), Options(s));
        Assert.DoesNotContain(nameof(RollViewModel.ClampStatus), raised);

        s.Results[0].FollowCommand!.Execute(null);
        Assert.Equal(("Clamp: Not available", "Manual selection: On", $"{Hint}\n{TableClamp.D66Message}"), Options(s));
        Assert.Contains(nameof(RollViewModel.ClampStatus), raised);
        Assert.Contains(nameof(RollViewModel.ManualSelectionStatus), raised);
        Assert.Contains(nameof(RollViewModel.TableOptionsToolTip), raised);

        raised.Clear();
        s.ReturnToOriginalCommand.Execute(null);
        Assert.Equal(("Clamp: On", "Manual selection: Off", Hint), Options(s));
        Assert.Contains(nameof(RollViewModel.ClampStatus), raised);
        Assert.Contains(nameof(RollViewModel.ManualSelectionStatus), raised);
        Assert.Contains(nameof(RollViewModel.TableOptionsToolTip), raised);
    }

    [Fact]
    public async Task Auto_follow_shows_the_options_of_the_table_it_ends_on()
    {
        var w = new World();
        var a = w.Add("A");
        var b = w.Add("B", "2d6", clamp: true);
        var c = w.Add("C", "d4", manual: true);
        w.Link(a, 1, b);
        w.Link(b, 2, c);
        var s = Open(w, a, new SequenceDice(1, 2), autoFollow: true);

        s.RollCommand.Execute(null);                                     // A 1 → B 2 → C (opened, not rolled)
        await s.RollTask;

        Assert.Equal(["A", "B", "C"], s.Steps.Select(st => st.Title).ToArray());
        Assert.Equal(("Clamp: Off", "Manual selection: On", Hint), Options(s));
    }

    [Fact]
    public void The_options_line_changes_no_roll()
    {
        var w = new World();
        var history = new List<RollSnapshot>();
        var a = w.Add("A", "d4", clamp: true);
        var s = new RollViewModel(a, new SequenceDice(3), w.Load, rolled: history.Add);
        _ = (s.ClampStatus, s.ManualSelectionStatus, s.TableOptionsToolTip);

        s.ModifierText = "+5";                                           // 8 on a d4: clamped to 4, exactly as before
        s.RollCommand.Execute(null);

        var roll = Assert.Single(history);
        Assert.Equal((8, 4), (roll.RollValue, roll.ClampedValue));
        Assert.True(a.ClampResultsToRange);
        Assert.False(a.IsManualSelection);
    }
}

/// <summary>The table options line on screen: placed with the Roll controls, muted, read-only, wrapping, and kept up to date.</summary>
[Collection("UI")]
public class TableOptionsIndicatorViewTests
{
    private const string Hint = "These are table settings. To change them, use Edit table in the table list.";

    private static Rect BoundsIn(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    /// <summary>"Area" (d6, Clamp on) row 6 → "Hexes" (d66, stored Clamp on, Manual selection); "Plain" (d6, both off).</summary>
    private static UiHarness Open(int width = 1000, int height = 640)
    {
        var ui = new UiHarness(6, (db, c) =>
        {
            var hexes = db.SaveTable(Fixtures.Table(DiceExpression.Parse("d66"), (11, 66, "A hex")).Also(t =>
            {
                t.Name = "Hexes"; t.CollectionId = c.Id; t.ClampResultsToRange = true; t.IsManualSelection = true;
            }));
            db.SaveTable(Fixtures.Table(DiceExpression.Parse("d6"), (1, 5, "Nothing"), (6, 6, "Hexes ahead")).Also(t =>
            {
                t.Name = "Area"; t.CollectionId = c.Id; t.ClampResultsToRange = true;
                t.ResultSets[0].Entries[1].LinkedTableId = hexes.Id;
            }));
            db.SaveTable(Fixtures.Table(DiceExpression.Parse("d6"), (1, 6, "Calm")).Also(t => { t.Name = "Plain"; t.CollectionId = c.Id; }));
        });
        ui.Window.Width = width;
        ui.Window.Height = height;
        ui.Layout();
        return ui;
    }

    private static TextBlock Text(UiHarness ui, string name) => ui.Texts().Single(t => t.Name == name);

    private static (string, string) Shown(UiHarness ui) => (Text(ui, "ClampStatusText").Text, Text(ui, "ManualSelectionStatusText").Text);

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1920, 1080)]
    public void The_line_sits_with_the_Roll_controls_muted_and_inside_the_screen(int width, int height)
    {
        Sta.Run(() =>
        {
            using var ui = Open(width, height);
            ui.SelectTable("Area");
            Assert.Equal(("Clamp: On", "Manual selection: Off"), Shown(ui));

            var bar = ui.One<WrapPanel>(p => p.Name == "TableOptionsBar");
            var controls = ui.One<StackPanel>(p => p.Name == "RollControls");
            Assert.True(bar.IsVisible && controls.IsAncestorOf(bar));
            var roll = BoundsIn(ui.One<Button>(b => b.Name == "RollButton"), ui.Window);
            var line = BoundsIn(bar, ui.Window);
            Assert.True(line.Top >= roll.Bottom, "the line is beneath the Roll controls");
            var viewport = BoundsIn(ui.One<ScrollViewer>(s => s.Name == "TrailScroller"), ui.Window);
            Assert.True(line.Right <= viewport.Right, $"the line fits the Roll screen: {line} vs {viewport}");

            foreach (var name in new[] { "ClampStatusText", "ManualSelectionStatusText" })
            {
                var text = Text(ui, name);
                Assert.True(text.IsVisible);
                Assert.Equal(Color.FromRgb(0x66, 0x66, 0x66), ((SolidColorBrush)text.Foreground).Color);
                Assert.Equal(Hint, AutomationProperties.GetHelpText(text));
                Assert.InRange(BoundsIn(text, ui.Window).Height, 1, 24);           // one plain line each, no larger type
            }
            Assert.Equal(Hint, bar.ToolTip);
            Assert.All(bar.Children.Cast<UIElement>(), e => Assert.IsType<TextBlock>(e));  // read-only: text, nothing to click or type
        });
    }

    [Fact]
    public void In_a_narrow_space_the_two_options_wrap_onto_separate_lines()
    {
        Sta.Run(() =>
        {
            using var ui = Open();
            ui.SelectTable("Area");
            var bar = ui.One<WrapPanel>(p => p.Name == "TableOptionsBar");
            var one = Text(ui, "ClampStatusText");

            bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var wide = bar.DesiredSize;
            bar.Measure(new Size(wide.Width / 2, double.PositiveInfinity));
            Assert.True(bar.DesiredSize.Width <= wide.Width / 2 + 0.5, "the line never needs more than the width it has");
            Assert.True(bar.DesiredSize.Height >= 2 * one.DesiredSize.Height - 1, "the second option moves to its own line");
            bar.InvalidateMeasure();
            ui.Layout();
        });
    }

    [Fact]
    public void The_line_follows_the_current_table_through_links_Back_and_another_table()
    {
        Sta.Run(() =>
        {
            using var ui = Open();
            ui.SelectTable("Area");
            ui.Click("Roll");                                                     // 6 → "Hexes ahead"
            ui.Click("Open Hexes");
            Assert.Equal(("Clamp: Not available", "Manual selection: On"), Shown(ui));
            Assert.Equal($"{Hint}\n{TableClamp.D66Message}", ui.One<WrapPanel>(p => p.Name == "TableOptionsBar").ToolTip);
            Assert.Equal($"{Hint}\n{TableClamp.D66Message}", AutomationProperties.GetHelpText(Text(ui, "ClampStatusText")));

            ui.Click("← Back to Area");
            Assert.Equal(("Clamp: On", "Manual selection: Off"), Shown(ui));
            Assert.Equal(Hint, ui.One<WrapPanel>(p => p.Name == "TableOptionsBar").ToolTip);

            ui.SelectTable("Plain");
            Assert.Equal(("Clamp: Off", "Manual selection: Off"), Shown(ui));
        });
    }

    [Fact]
    public void A_change_saved_in_Edit_table_shows_on_the_Roll_screen()
    {
        Sta.Run(() =>
        {
            using var ui = Open();
            ui.SelectTable("Plain");
            Assert.Equal(("Clamp: Off", "Manual selection: Off"), Shown(ui));

            ui.Click("Edit table");
            ui.One<CheckBox>(c => c.Name == "ClampBox").IsChecked = true;
            ui.One<CheckBox>(c => c.Name == "ManualSelectionBox").IsChecked = true;
            ui.Layout();
            ui.Click("Save Table");

            ui.SelectTable("Plain");
            Assert.Equal(("Clamp: On", "Manual selection: On"), Shown(ui));
        });
    }
}
