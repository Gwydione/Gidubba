using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;

namespace TableForge.Tests;

/// <summary>
/// 1.5 visual polish: a row's Unique/Used tag sits right after its result text (under it when the text is long), in one-column and
/// aligned tables alike; and the selected table in the sidebar is clearly marked even when the list does not have keyboard focus.
/// </summary>
[Collection("UI")]
public class VisualPolishLayoutTests
{
    private const string LongText = "A very long result that keeps going across the whole width of the table listing so that it has to wrap " +
                                    "onto several lines, leaving no room at all for the tag beside its last line of words, even on the widest window " +
                                    "that TableForge is tested at, where a listing row is more than a thousand pixels wide and still runs out of room";

    private static Rect BoundsIn(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static TableEntry Row(int value, string text) => new() { Min = value, Max = value, Text = text, IsUnique = true, SortOrder = value };

    private static UiHarness Open(int width, int height, Action<AppDatabase, Collection> seed)
    {
        var ui = new UiHarness(1, seed);
        ui.Window.Width = width;
        ui.Window.Height = height;
        ui.Layout();
        return ui;
    }

    /// <summary>Each visible tag (its pill) with the result text it belongs to (the text before it in the same wrapping panel).</summary>
    private static List<(TextBlock Text, Border Tag)> Tags(UiHarness ui) =>
        ui.Texts().Where(t => t.Name == "UniqueTagText")
            .Select(label => (Border)VisualTreeHelper.GetParent(label))
            .Select(pill => ((TextBlock)((WrapPanel)VisualTreeHelper.GetParent(pill)).Children[0], pill))
            .ToList();

    private static string Label(Border pill) => ((TextBlock)pill.Child).Text;

    /// <summary>A short result's tag follows it on the same line, about 6 px on; a long result's tag goes under it, never outside the row.</summary>
    private static void AssertBesideOrUnder(UiHarness ui, string shortText, string longText, FrameworkElement? container = null)
    {
        var tags = Tags(ui);
        Assert.NotEmpty(tags);
        foreach (var (text, tag) in tags)
        {
            var t = BoundsIn(text, ui.Window);
            var g = BoundsIn(tag, ui.Window);
            var row = BoundsIn((FrameworkElement)VisualTreeHelper.GetParent(tag), ui.Window);
            Assert.True(g.Right <= row.Right + 1, $"the tag is outside its row: {g} vs {row}");
            if (container is not null) Assert.True(g.Right <= BoundsIn(container, ui.Window).Right + 1);
            if (text.Text == shortText)
            {
                Assert.InRange(g.Left - t.Right, 5, 9);                    // 6 px after the text, not at the far edge
                Assert.InRange(g.Top, t.Top, t.Bottom);                    // on the same line
            }
            else if (text.Text == longText)
            {
                Assert.True(g.Top >= t.Bottom - 1, $"a long result's tag goes under it: {g} vs {t}");
                Assert.InRange(g.Left - t.Left, 0, 10);
            }
        }
        Assert.Contains(tags, p => p.Text.Text == shortText);
        Assert.Contains(tags, p => p.Text.Text == longText);
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1920, 1080)]
    public void A_one_column_tables_tags_follow_their_result_text(int width, int height)
    {
        Sta.Run(() =>
        {
            using var ui = Open(width, height, (db, c) => db.SaveTable(new RollableTable
            {
                CollectionId = c.Id, Name = "Trinkets", Dice = DiceExpression.Parse("d2"),
                ResultSets = [new ResultSet { Entries = [Row(1, "Rusty nails"), Row(2, LongText)] }],
            }));
            ui.SelectTable("Trinkets");
            ui.Layout();
            Assert.Equal(["Unique", "Unique"], Tags(ui).Select(p => Label(p.Tag)).ToArray());
            AssertBesideOrUnder(ui, "Rusty nails", LongText);

            ui.Click("Roll");                                                         // row 1 is used and rolled: still beside it
            ui.Layout();
            Assert.Equal(["Used", "Unique"], Tags(ui).Select(p => Label(p.Tag)).ToArray());
            AssertBesideOrUnder(ui, "Rusty nails", LongText);

            // Each tag is a small rounded pill with a light fill: blue-gray for Unique, neutral gray for Used.
            var (used, unique) = (Tags(ui)[0].Tag, Tags(ui)[1].Tag);
            Assert.All([used, unique], pill => Assert.True(pill.CornerRadius.TopLeft >= 6 && pill.ActualHeight <= 20));
            Assert.Equal(Color.FromRgb(0xE8, 0xEE, 0xF6), ((SolidColorBrush)unique.Background).Color);
            Assert.Equal(Color.FromRgb(0xE2, 0xE2, 0xE2), ((SolidColorBrush)used.Background).Color);
            Assert.Equal(11, ((TextBlock)unique.Child).FontSize);
        });
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1920, 1080)]
    public void An_aligned_tables_tags_follow_their_cells_text_inside_their_own_column(int width, int height)
    {
        Sta.Run(() =>
        {
            using var ui = Open(width, height, (db, c) => db.SaveTable(new RollableTable
            {
                CollectionId = c.Id, Name = "Obstacle", Dice = DiceExpression.Parse("d2"),
                ResultSets =
                [
                    new ResultSet { Name = "Difficulty", Entries = [Row(1, "Low"), Row(2, LongText)] },
                    new ResultSet { Name = "Modifier", Entries = [Row(1, "Low"), Row(2, LongText)] },
                ],
            }));
            ui.SelectTable("Obstacle");
            ui.Layout();
            Assert.Equal(4, Tags(ui).Count);
            foreach (var cell in ui.All<Border>().Where(b => b.Name == "AlignedCell" && b.IsVisible))
                foreach (var (_, tag) in Tags(ui).Where(p => cell.IsAncestorOf(p.Tag)))
                    Assert.True(BoundsIn(tag, ui.Window).Right <= BoundsIn(cell, ui.Window).Right + 1, "each tag stays inside its own column");
            AssertBesideOrUnder(ui, "Low", LongText);
        });
    }

    [Fact]
    public void The_selected_table_is_clearly_marked_when_the_list_does_not_have_focus()
    {
        Sta.Run(() =>
        {
            const string longName = "A table with a very long name that cannot possibly fit in the sidebar";
            using var ui = Open(1000, 640, (db, c) =>
            {
                db.SaveTable(Fixtures.ScavengedItems(c.Id));
                db.SaveTable(Fixtures.ScavengedItems(c.Id).Also(t => t.Name = longName));
            });
            ui.SelectTable(longName);
            var list = ui.One<ListBox>(l => l.Name == "TablesList");
            Keyboard.Focus(ui.One<Button>(b => b.Name == "RollButton"));
            ui.Layout();
            Assert.False(list.IsKeyboardFocusWithin);

            Border Marker(string name) => ui.All<Border>().Single(b => b.Name == "TableRowMarker" && ((TableSummary)b.DataContext).Name == name);
            TextBlock Name(string name) => ui.All<TextBlock>().Single(t => t.Name == "TableRowName" && t.Text == name);

            var selected = Marker(longName);
            Assert.Equal(Color.FromRgb(0xA0, 0x7C, 0x14), ((SolidColorBrush)selected.BorderBrush).Color);  // the gold bar
            Assert.Equal(3, selected.BorderThickness.Left);
            Assert.Equal(Color.FromRgb(0xE3, 0xEA, 0xF5), ((SolidColorBrush)selected.Background).Color);
            Assert.Equal(FontWeights.SemiBold, Name(longName).FontWeight);
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(selected.DataContext);
            Assert.True(BoundsIn(selected, ui.Window).Width >= item.ActualWidth - 3, "the marker spans the row");

            var other = Marker("Scavenged Items");                                   // the rest are plain
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)other.BorderBrush).Color);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)other.Background).Color);
            Assert.Equal(FontWeights.Normal, Name("Scavenged Items").FontWeight);

            // A long name still ends in "…" inside the row, its dice still beside it.
            var name = Name(longName);
            name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(name.ActualWidth < name.DesiredSize.Width, "a long name is trimmed");
            Assert.True(BoundsIn(name, ui.Window).Right <= BoundsIn(item, ui.Window).Right);

            ui.SelectTable("Scavenged Items");                                       // the marker moves with the selection
            Keyboard.Focus(ui.One<Button>(b => b.Name == "RollButton"));
            ui.Layout();
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)Marker(longName).BorderBrush).Color);
            Assert.Equal(FontWeights.SemiBold, Name("Scavenged Items").FontWeight);
        });
    }
}
