using System.Diagnostics;
using System.Windows.Controls;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// RC24 performance guard: a large table with formatting on every row must not be noticeably slower to open for editing,
/// to type into, to open for rolling or to show in the real window than the same table without formatting (the preview is
/// only created for formatted rows, and formatting is cheap Runs, not rich-text editors). Bounds are generous so the checks
/// stay reliable on a busy machine; each measurement is the best of a few runs.
/// </summary>
[Collection("UI")]
public class RichTextPerformanceTests
{
    private const int Rows = 1000;

    private const int UiRows = 300;

    private static RollableTable Large(long collectionId, string name, bool formatted, int rows = Rows) => new()
    {
        CollectionId = collectionId,
        Name = name,
        Dice = DiceExpression.Parse($"d{rows}"),
        ResultSets =
        [
            new ResultSet
            {
                Entries = Enumerable.Range(1, rows).Select(i =>
                {
                    var text = $"Row {i}: the creature gains +2 Armor until the next dawn, then rolls 1d4 more.";
                    var styles = formatted
                        ? TextStyles.FromRuns([new StyleRun(text.IndexOf("+2", StringComparison.Ordinal), 8, TextStyle.Bold),
                            new StyleRun(text.IndexOf("next", StringComparison.Ordinal), 9, TextStyle.Italic)], text.Length)
                        : TextStyles.Empty;
                    return new TableEntry { Min = i, Max = i, Text = text, Styles = styles };
                }).ToList(),
            },
        ],
    };

    private static double BestMs(int runs, Action action)
    {
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    [Fact]
    public void A_large_formatted_table_edits_and_rolls_about_as_fast_as_a_plain_one()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var c = db.CreateCollection("C");
        db.SaveTable(Large(c.Id, "Plain", formatted: false));
        db.SaveTable(Large(c.Id, "Formatted", formatted: true));
        var main = new MainViewModel(db, new FixedDice(500), (_, _) => true);

        (double Open, double Keystroke, double Roll) Measure(string name)
        {
            ReviewViewModel? review = null;
            var open = BestMs(3, () =>
            {
                main.SelectedTable = main.Tables.Single(t => t.Name == name);
                main.EditTableCommand.Execute(null);
                review = (ReviewViewModel)main.Current!;
            });
            var row = review!.Rows[Rows / 2];
            var n = 0;
            var keystroke = BestMs(10, () => row.Text += (n++ % 10).ToString());
            main.CancelEditCommand().Execute(null);
            var roll = BestMs(3, () =>
            {
                main.SelectedTable = main.Tables.Single(t => t.Name == name);
                main.OpenTableCommand.Execute(null);
                ((RollViewModel)main.Current!).RollCommand.Execute(null);
            });
            return (open, keystroke, roll);
        }

        Measure("Plain"); // warm up
        var plain = Measure("Plain");
        var formatted = Measure("Formatted");
        var report = $"plain {plain}, formatted {formatted}";

        Assert.True(formatted.Open <= plain.Open * 2 + 100, report);
        Assert.True(formatted.Keystroke <= plain.Keystroke * 2 + 20, report);
        Assert.True(formatted.Roll <= plain.Roll * 2 + 100, report);
        Assert.True(formatted.Keystroke < 250, report);   // typing into a 1000-row table stays interactive
    }

    [Fact]
    public void A_large_formatted_table_shows_in_the_real_window_about_as_fast_as_a_plain_one()
    {
        Sta.Run(() =>
        {
            // 300 rows: a large printed table (d100, d300). The Review screen does not virtualize its rows (it never has), so a
            // thousand rows is slow to show with or without formatting; that is what the view model check above covers.
            using var ui = new UiHarness(1, (db, c) =>
            {
                db.SaveTable(Large(c.Id, "Plain", formatted: false, rows: UiRows));
                db.SaveTable(Large(c.Id, "Formatted", formatted: true, rows: UiRows));
            });

            double Show(string name, string button) => BestMs(2, () =>
            {
                ui.SelectTable(name);
                ui.Click(button);
                ui.Layout();
                if (button == "Edit table") ui.Click("Cancel");
            });

            Show("Plain", "Edit table"); // warm up
            var plainEdit = Show("Plain", "Edit table");
            var formattedEdit = Show("Formatted", "Edit table");
            var plainRoll = Show("Plain", "Roll");
            var formattedRoll = Show("Formatted", "Roll");
            var report = $"edit plain {plainEdit:F0} ms, formatted {formattedEdit:F0} ms; roll plain {plainRoll:F0} ms, formatted {formattedRoll:F0} ms";

            // Measured on the development machine (300 rows, every one formatted): edit about 1.9 s plain / 2.7 s formatted,
            // roll about 0.17 s / 0.56 s — roughly 2.5 ms and 1.3 ms per formatted row, nothing for a plain one.
            Assert.True(formattedEdit <= plainEdit * 2 + 500, report);
            Assert.True(formattedRoll <= plainRoll * 2 + 500, report);
            Assert.True((formattedEdit - plainEdit) / UiRows < 8, report);   // milliseconds per formatted row
            Assert.True((formattedRoll - plainRoll) / UiRows < 5, report);

            // And the previews really exist only for formatted rows.
            ui.SelectTable("Formatted");
            ui.Click("Edit table");
            Assert.Equal(UiRows, ViewTests.FindAll<System.Windows.Controls.Border>(ui.One<ItemsControl>(i => i.Name == "RowsList")).Count(b => b.Name == "FormattedPreview"));
            ui.Click("Cancel");
            ui.SelectTable("Plain");
            ui.Click("Edit table");
            Assert.DoesNotContain(ViewTests.FindAll<System.Windows.Controls.Border>(ui.One<ItemsControl>(i => i.Name == "RowsList")), b => b.Name == "FormattedPreview");
        });
    }
}

file static class MainViewModelTestExtensions
{
    /// <summary>Leaves the Review screen without saving, as Cancel does.</summary>
    public static System.Windows.Input.ICommand CancelEditCommand(this MainViewModel main) => ((ReviewViewModel)main.Current!).CancelCommand;
}
