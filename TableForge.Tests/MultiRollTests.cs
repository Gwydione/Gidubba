using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Hands out the given final values in order; can be told to fail on one particular call.</summary>
internal sealed class SequenceDice(params int[] values) : IDiceProvider
{
    private int _next;

    public List<string> Requested { get; } = [];

    /// <summary>1-based call that throws instead of rolling; 0 for never.</summary>
    public int FailOnCall { get; set; }

    public Task<int> RollAsync(DiceExpression expression, CancellationToken cancellationToken)
    {
        Requested.Add(expression.ToString());
        if (Requested.Count == FailOnCall) return Task.FromException<int>(new DddiceException("dddice is temporarily unavailable. Try again shortly."));
        return Task.FromResult(values[_next++ % values.Length]);
    }
}

/// <summary>Multi-roll through <see cref="RollViewModel"/>: N independent rolls, each through the ordinary single-roll pipeline.</summary>
public class MultiRollTests
{
    private static readonly (int, int, string)[] D20Rows = [(1, 5, "Ruined bridge"), (6, 10, "Abandoned chapel"), (11, 15, "Watchtower"), (16, 20, "Standing stones")];

    private static (RollViewModel Session, SequenceDice Dice, List<RollSnapshot> History) Open(RollableTable table, params int[] values)
    {
        var dice = new SequenceDice(values);
        var history = new List<RollSnapshot>();
        return (new RollViewModel(table, dice, rolled: history.Add), dice, history);
    }

    private static RollableTable D20() => Fixtures.Table(DiceExpression.Parse("d20"), D20Rows);

    private static string[] Texts(RollViewModel s) => s.LatestRolls.Select(o => Assert.Single(o.Lines).Text).ToArray();

    [Fact]
    public void Count_one_is_the_ordinary_single_roll()
    {
        var (session, dice, history) = Open(D20(), 7);
        Assert.Equal((1, "Roll", true), (session.RollCount, session.RollButtonLabel, session.IsRollCountEditable));
        Assert.Equal(Enumerable.Range(1, 10), session.RollCountOptions);

        session.RollCommand.Execute(null);

        var outcome = Assert.Single(session.LatestRolls);
        Assert.Equal(("Rolled 7", "", false), (outcome.Display, outcome.BatchLabel, outcome.HasBatchLabel));
        Assert.Equal("Abandoned chapel", Assert.Single(session.Results).Text);
        Assert.Equal("Rolled 7", session.RollDisplay);
        Assert.Single(dice.Requested);
        Assert.Single(history);
        Assert.Equal("", session.Message);
    }

    [Fact]
    public void Three_rolls_are_three_independent_numbered_results_in_order()
    {
        var (session, dice, history) = Open(D20(), 7, 11, 4);
        session.RollCount = 3;
        Assert.Equal("Roll 3 Times", session.RollButtonLabel);

        session.RollCommand.Execute(null);

        Assert.Equal(["Roll 1", "Roll 2", "Roll 3"], session.LatestRolls.Select(o => o.BatchLabel).ToArray());
        Assert.Equal(["Rolled 7", "Rolled 11", "Rolled 4"], session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.Equal(["Abandoned chapel", "Watchtower", "Ruined bridge"], Texts(session));
        Assert.Equal(["d20", "d20", "d20"], dice.Requested);
        Assert.Equal([7, 11, 4], history.Select(h => h.RollValue).ToArray());          // one ordinary history record per roll, in order
        Assert.Equal(["Abandoned chapel", "Watchtower", "Ruined bridge"], history.Select(h => h.ResultText).ToArray());
        Assert.Equal(3, session.Current.Outcomes.Count);
        Assert.Equal("Rolled 4", session.RollDisplay);                                    // "the latest roll" is the last of them
        Assert.Equal(3, session.RollCount);                                               // the choice stays for this table
    }

    [Fact]
    public void Ten_rolls_make_ten_results()
    {
        var (session, dice, history) = Open(D20(), Enumerable.Range(1, 10).Select(i => i * 2).ToArray());
        session.RollCount = 10;

        session.RollCommand.Execute(null);

        Assert.Equal(10, session.LatestRolls.Count);
        Assert.Equal(10, dice.Requested.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"Roll {i}"), session.LatestRolls.Select(o => o.BatchLabel));
        Assert.Equal(10, history.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(11, 10)]
    [InlineData(-3, 1)]
    public void The_count_stays_between_one_and_ten(int asked, int kept)
    {
        var (session, _, _) = Open(D20(), 1);
        session.RollCount = asked;
        Assert.Equal(kept, session.RollCount);
    }

    [Fact]
    public void The_tables_own_modifier_applies_to_every_roll()
    {
        var (session, dice, history) = Open(Fixtures.Table(DiceExpression.Parse("2d6+1"), (3, 6, "Low"), (7, 9, "Middle"), (10, 13, "High")), 7, 12, 3);
        session.RollCount = 3;

        session.RollCommand.Execute(null);

        Assert.Equal(["2d6+1", "2d6+1", "2d6+1"], dice.Requested);
        Assert.Equal(["Rolled 7 (2d6+1)", "Rolled 12 (2d6+1)", "Rolled 3 (2d6+1)"], session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.All(history, h => Assert.Equal("2d6+1", h.DiceText));
    }

    [Fact]
    public void The_situational_modifier_is_used_by_the_first_roll_only_then_resets()
    {
        var (session, _, history) = Open(Fixtures.Table(DiceExpression.Parse("d6"), (1, 2, "Low"), (3, 4, "Mid"), (5, 8, "High")), 6, 3, 5);
        session.ModifierText = "+2";
        session.RollCount = 3;

        session.RollCommand.Execute(null);

        Assert.Equal(["Rolled 8", "Rolled 3", "Rolled 5"], session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.Equal(["6 +2 situational", "", ""], session.LatestRolls.Select(o => o.Breakdown).ToArray());
        Assert.Equal([2, 0, 0], history.Select(h => h.SituationalModifier).ToArray());
        Assert.Equal([8, 3, 5], history.Select(h => h.RollValue).ToArray());
        Assert.Equal("0", session.ModifierText);
    }

    [Fact]
    public void If_the_first_roll_fails_nothing_is_made_and_the_modifier_waits()
    {
        var (session, dice, history) = Open(D20(), 7, 11, 4);
        dice.FailOnCall = 1;
        session.ModifierText = "+2";
        session.RollCount = 3;

        session.RollCommand.Execute(null);

        Assert.Empty(session.LatestRolls);
        Assert.Empty(history);
        Assert.Single(dice.Requested);                                                    // stopped; no retry, no further rolls
        Assert.Equal("+2", session.ModifierText);
        Assert.Equal("dddice is temporarily unavailable. Try again shortly. None of the 3 rolls was made.", session.Message);
        Assert.False(session.IsRolling);
    }

    [Fact]
    public void A_failure_part_way_keeps_the_finished_results_and_stops()
    {
        var (session, dice, history) = Open(D20(), 7, 11, 4, 2, 9);
        dice.FailOnCall = 3;
        session.RollCount = 5;

        session.RollCommand.Execute(null);

        Assert.Equal(["Roll 1", "Roll 2"], session.LatestRolls.Select(o => o.BatchLabel).ToArray());
        Assert.Equal(["Abandoned chapel", "Watchtower"], Texts(session));
        Assert.Equal([7, 11], history.Select(h => h.RollValue).ToArray());               // recorded, and nothing made up for 3-5
        Assert.Equal(3, dice.Requested.Count);
        Assert.Equal("dddice is temporarily unavailable. Try again shortly. Stopped at roll 3 of 5: rolls 1–2 are shown; the rest were not rolled.", session.Message);

        dice.FailOnCall = 0;
        session.RollCount = 1;
        session.RollCommand.Execute(null);                                                 // a later roll clears the message as always
        Assert.Equal("", session.Message);
    }

    [Fact]
    public void Clamp_applies_to_each_result_on_its_own()
    {
        var table = ClampFixtures.Table("d6", true, ClampFixtures.D6Rows);
        var (session, _, history) = Open(table, 8, 4, -1);
        session.RollCount = 3;

        session.RollCommand.Execute(null);

        Assert.Equal(["Rolled 8", "Rolled 4", "Rolled -1"], session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.Equal(["Resolved as 6 (clamped)", "", "Resolved as 1 (clamped)"], session.LatestRolls.Select(o => o.ClampNote).ToArray());
        Assert.Equal(["Dangerous Encounter", "Four", "Calm"], Texts(session));
        Assert.Equal(new int?[] { 6, null, 1 }, history.Select(h => h.ClampedValue).ToArray());
    }

    [Fact]
    public void An_internal_gap_is_still_no_match_within_a_batch()
    {
        var (session, _, _) = Open(ClampFixtures.Table("d6", true, (1, 3, "Result A"), (5, 6, "Result B")), 4, 7);
        session.RollCount = 2;

        session.RollCommand.Execute(null);

        Assert.Equal(["No entry covers 4.", "Result B"], Texts(session));
        Assert.True(session.LatestRolls[0].Lines[0].IsProblem);
    }

    [Fact]
    public void d66_rolls_each_stay_one_legal_tens_and_ones_value()
    {
        var table = Fixtures.Table(DiceExpression.Parse("d66"), (11, 36, "Low"), (41, 66, "High"));
        var (session, dice, _) = Open(table, 24, 61, 13);
        session.RollCount = 3;

        session.RollCommand.Execute(null);

        Assert.Equal(["Rolled 24 (d66)", "Rolled 61 (d66)", "Rolled 13 (d66)"], session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.Equal(["Low", "High", "Low"], Texts(session));
        Assert.Equal(["d66", "d66", "d66"], dice.Requested);
    }

    [Fact]
    public void d100_keeps_its_00_display_and_numeric_100_lookup_for_each_roll()
    {
        var table = Fixtures.Table(DiceExpression.Parse("d100"), (1, 50, "Low"), (51, 100, "High"));
        var (session, _, history) = Open(table, 100, 5);
        session.RollCount = 2;

        session.RollCommand.Execute(null);

        Assert.Equal(["Rolled 00 (numeric 100)", "Rolled 5"], session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.Equal(["High", "Low"], Texts(session));
        Assert.Equal([100, 5], history.Select(h => h.RollValue).ToArray());
    }

    [Fact]
    public void Every_row_used_by_the_batch_is_highlighted()
    {
        var (session, _, _) = Open(D20(), 7, 11);
        session.RollCount = 2;
        session.RollCommand.Execute(null);

        Assert.Equal([false, true, true, false], session.ResultSets[0].Entries.Select(e => e.IsMatched).ToArray());

        session.RollCount = 1;
        session.RollCommand.Execute(null);                                                 // 7 again: a new action replaces the highlight
        Assert.Equal([false, true, false, false], session.ResultSets[0].Entries.Select(e => e.IsMatched).ToArray());
    }

    [Fact]
    public void Each_result_keeps_its_own_link_and_nothing_is_followed_automatically()
    {
        var village = Fixtures.Table(DiceExpression.Parse("d4"), (1, 4, "Farmers")).Also(t => { t.Id = 2; t.Name = "Settlement Details"; });
        var ruins = Fixtures.Table(DiceExpression.Parse("d4"), (1, 4, "Old fort")).Also(t => { t.Id = 3; t.Name = "Ruin Type"; });
        var sites = new RollableTable
        {
            Id = 1, Name = "Sites", Dice = DiceExpression.Parse("d6"),
            ResultSets = [new ResultSet { Entries = [
                new TableEntry { Min = 1, Max = 3, Text = "Village", LinkedTableId = 2 },
                new TableEntry { Min = 4, Max = 6, Text = "Ruins", LinkedTableId = 3 }] }],
        };
        var dice = new SequenceDice(2, 5, 1);
        var session = new RollViewModel(sites, dice, id => id == 2 ? village : id == 3 ? ruins : null);
        session.RollCount = 2;

        session.RollCommand.Execute(null);

        var (first, second) = (session.LatestRolls[0].Lines[0], session.LatestRolls[1].Lines[0]);
        Assert.Equal(("Open Settlement Details", true), (first.FollowLabel, first.ShowFollow));
        Assert.Equal(("Open Ruin Type", true), (second.FollowLabel, second.ShowFollow));
        Assert.Single(session.Steps);                                                       // nothing followed by itself

        first.FollowCommand!.Execute(null);                                                  // following the first result's own link
        Assert.Equal("Settlement Details", session.Title);
        Assert.False(second.ShowFollow);                                                     // the trail moved on: both become notes
        Assert.Equal(1, session.RollCount);                                                  // a followed table starts at one roll
    }

    [Fact]
    public void A_new_Roll_action_supersedes_the_previous_batchs_links()
    {
        var sites = new RollableTable
        {
            Id = 1, Name = "Sites", Dice = DiceExpression.Parse("d6"),
            ResultSets = [new ResultSet { Entries = [new TableEntry { Min = 1, Max = 6, Text = "Village", LinkedTableId = 2 }] }],
        };
        var session = new RollViewModel(sites, new SequenceDice(1, 2, 3), _ => Fixtures.Table(DiceExpression.Parse("d4"), (1, 4, "x")).Also(t => t.Id = 2));
        session.RollCount = 2;
        session.RollCommand.Execute(null);
        var previous = session.LatestRolls.ToList();

        session.RollCount = 1;
        session.RollCommand.Execute(null);

        Assert.All(previous, o => Assert.False(o.Lines[0].ShowFollow));
        Assert.True(Assert.Single(session.LatestRolls).Lines[0].ShowFollow);
    }

    [Fact]
    public void Each_result_has_its_own_inline_dice_and_none_are_rolled_automatically()
    {
        var (session, dice, _) = Open(Fixtures.Table(DiceExpression.Parse("d6"), (1, 6, "You gain +1d4 Armor")), 2, 5, 3, 1);
        session.RollCount = 2;
        session.RollCommand.Execute(null);
        Assert.Equal(2, dice.Requested.Count);                                                // the inline d4s were not rolled

        var (first, second) = (session.LatestRolls[0].Lines[0], session.LatestRolls[1].Lines[0]);
        Assert.True(first.ShowInlineActions && second.ShowInlineActions);

        first.InlineActions[0].RollCommand!.Execute(null);                                    // the next value (3)

        Assert.Equal(("You gain +3 Armor", true), (first.ResolvedText, first.ShowResolved));
        Assert.False(second.HasResolved);
        Assert.Equal("You gain +1d4 Armor", second.ResolvedText);

        session.RollCount = 1;
        session.RollCommand.Execute(null);                                                    // a new action: earlier inline results are kept (1.6)
        Assert.True(first.ShowResolved);
        Assert.Equal(3, first.InlineActions[0].LatestValue);
        Assert.False(first.ShowInlineActions);                                                // but only the current result can be rolled
    }

    [Fact]
    public void Manual_entry_is_always_one_result()
    {
        var (session, dice, history) = Open(D20(), 7);
        session.RollCount = 4;
        session.ManualRollText = "12";

        session.ResolveManualCommand.Execute(null);

        var outcome = Assert.Single(session.LatestRolls);
        Assert.Equal(("Rolled 12", ""), (outcome.Display, outcome.BatchLabel));
        Assert.Empty(dice.Requested);
        Assert.Single(history);
        Assert.Equal(4, session.RollCount);                                                   // untouched: it is for Roll only
    }
}

/// <summary>Multi-roll with the dddice provider: strictly one visual roll at a time, and the choice is locked meanwhile.</summary>
public class MultiRollDddiceTests
{
    private sealed class Rig
    {
        public FakeDddiceRoller Roller { get; } = new();
        public DiceProviderViewModel Providers { get; }
        public RollViewModel Session { get; }
        public List<RollSnapshot> History { get; } = [];

        public Rig(RollableTable table, DddiceConnection? connection = null)
        {
            Providers = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(Roller), connection: connection);
            Providers.Select(DiceProviderKind.Dddice);
            Wait.Until(() => Providers.State == DiceProviderState.Ready, "ready").GetAwaiter().GetResult();
            Session = new RollViewModel(table, Providers, rolled: History.Add, diceReady: () => Providers.CanRoll);
        }
    }

    [Fact]
    public async Task Each_visual_roll_settles_and_is_shown_before_the_next_is_thrown()
    {
        var rig = new Rig(Fixtures.Table(DiceExpression.Parse("2d6+1"), (3, 6, "Low"), (7, 9, "Middle"), (10, 13, "High")));
        rig.Session.RollCount = 3;

        rig.Session.RollCommand.Execute(null);

        Assert.Single(rig.Roller.Requests);                                                   // only the first throw
        Assert.Equal("Rolling 1 of 3…", rig.Session.RollStatus);
        Assert.False(rig.Session.IsRollCountEditable);
        Assert.False(rig.Session.RollCommand.CanExecute(null));
        rig.Session.RollCount = 7;                                                            // locked while rolling
        Assert.Equal(3, rig.Session.RollCount);
        Assert.Empty(rig.Session.LatestRolls);

        rig.Roller.Settle(FakeDddiceRoller.Twod6(1, 2));                                      // roll:finished for roll 1
        await Wait.Until(() => rig.Roller.Requests.Count == 2, "second throw");
        Assert.Equal(["Rolled 4 (2d6+1)"], rig.Session.LatestRolls.Select(o => o.Display).ToArray()); // shown before roll 2 settles
        Assert.Single(rig.History);
        Assert.Equal("Rolling 2 of 3…", rig.Session.RollStatus);

        rig.Roller.Settle(FakeDddiceRoller.Twod6(3, 4));
        await Wait.Until(() => rig.Roller.Requests.Count == 3, "third throw");
        rig.Roller.Settle(FakeDddiceRoller.Twod6(6, 6));
        await rig.Session.RollTask;

        Assert.Equal(["Rolled 4 (2d6+1)", "Rolled 8 (2d6+1)", "Rolled 13 (2d6+1)"], rig.Session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.All(rig.Roller.Requests, r => Assert.Equal(["d6", "d6"], r));                  // each throw is only that roll's dice
        Assert.Equal(3, rig.History.Count);
        Assert.True(rig.Session.IsRollCountEditable);
        Assert.Equal("", rig.Session.RollStatus);
    }

    [Fact]
    public async Task Repeated_d100_and_d66_use_the_usual_dddice_dice_each_time()
    {
        var percent = new Rig(Fixtures.Table(DiceExpression.Parse("d100"), (1, 50, "Low"), (51, 100, "High")));
        percent.Session.RollCount = 2;
        percent.Session.RollCommand.Execute(null);
        percent.Roller.Settle(("d10x", 9), ("d10", 10));                                       // 90 + 10 = 100
        await Wait.Until(() => percent.Roller.Requests.Count == 2, "second throw");
        percent.Roller.Settle(("d10x", 3), ("d10", 7));                                        // 37
        await percent.Session.RollTask;
        Assert.Equal(["Rolled 00 (numeric 100)", "Rolled 37"], percent.Session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.All(percent.Roller.Requests, r => Assert.Equal(["d10x", "d10"], r));

        var names = new Rig(Fixtures.Table(DiceExpression.Parse("d66"), (11, 36, "Low"), (41, 66, "High")));
        names.Session.RollCount = 2;
        names.Session.RollCommand.Execute(null);
        names.Roller.Settle(FakeDddiceRoller.Twod6(2, 4));
        await Wait.Until(() => names.Roller.Requests.Count == 2, "second throw");
        names.Roller.Settle(FakeDddiceRoller.Twod6(6, 1));
        await names.Session.RollTask;
        Assert.Equal(["Rolled 24 (d66)", "Rolled 61 (d66)"], names.Session.LatestRolls.Select(o => o.Display).ToArray());
    }

    [Fact]
    public async Task A_dddice_failure_part_way_stops_keeps_earlier_results_and_offers_the_usual_way_out()
    {
        var account = new DddiceConnection(MemoryAccountStore.With(new DddiceAccount("acct-token", "Allen", "room-1", "my-blue", "My Blue Dice")));
        var rig = new Rig(Fixtures.Table(DiceExpression.Parse("d20"), (1, 20, "Anything")), account);
        rig.Session.RollCount = 3;

        rig.Session.RollCommand.Execute(null);
        rig.Roller.Settle(("d20", 12));
        await Wait.Until(() => rig.Roller.Requests.Count == 2, "second throw");
        rig.Roller.FailRoll("The connection to dddice was lost during the roll.");
        await rig.Session.RollTask;

        Assert.Equal(["Rolled 12"], rig.Session.LatestRolls.Select(o => o.Display).ToArray());
        Assert.Single(rig.History);
        Assert.Equal(2, rig.Roller.Requests.Count);                                           // no third throw, no retry
        Assert.StartsWith("The connection to dddice was lost during the roll. Stopped at roll 2 of 3", rig.Session.Message);
        Assert.Equal(DiceProviderState.Failed, rig.Providers.State);                         // Try again / Use Built-in / Account…
        Assert.True(rig.Providers.ShowAccountOnFailure);
        Assert.Equal(DddiceConnectionState.Connected, account.State);                        // the account and theme are untouched
        Assert.Equal("Theme: My Blue Dice", rig.Providers.ThemeText);
    }
}

/// <summary>The Rolls choice through the real Roll screen.</summary>
[Collection("UI")]
public class MultiRollViewTests
{
    private static UiHarness Open(int roll) => new(roll, (db, c) =>
        db.SaveTable(Fixtures.Table(DiceExpression.Parse("d20"), (1, 10, "Abandoned chapel"), (11, 20, "Watchtower")).Also(t => { t.Name = "Sites"; t.CollectionId = c.Id; })));

    [Fact]
    public void The_Rolls_choice_sits_beside_Roll_and_several_rolls_show_numbered_separate_results()
    {
        Sta.Run(() =>
        {
            using var ui = Open(7);
            ui.SelectTable("Sites");

            var box = ui.One<ComboBox>(c => c.Name == "RollCountBox");
            Assert.True(box.IsVisible && box.IsEnabled);
            Assert.Equal(Enumerable.Range(1, 10), box.Items.Cast<int>());
            Assert.Equal(1, box.SelectedItem);
            var roll = ui.One<Button>(b => b.Name == "RollButton");
            Assert.Equal("Roll", roll.Content);
            var boxRight = box.TranslatePoint(new Point(box.ActualWidth, 0), roll).X;
            Assert.InRange(boxRight, -40, 0);                                                     // right next to Roll

            ui.Click("Roll");                                                                      // one roll: no numbering at all
            Assert.Contains(ui.Texts(), t => t.Text == "Rolled 7");
            Assert.DoesNotContain(ui.Texts(), t => t.Name == "BatchLabelText");

            box.SelectedItem = 3;
            ui.Layout();
            Assert.Equal("Roll 3 Times", roll.Content);
            ui.Click("Roll 3 Times");

            Assert.Equal(["Roll 1", "Roll 2", "Roll 3"], ui.Texts().Where(t => t.Name == "BatchLabelText").Select(t => t.Text).ToArray());
            Assert.Equal(4, ui.Texts().Count(t => t.Text == "Rolled 7"));                         // the earlier single roll, then three
            Assert.Equal(4, ui.Texts().Count(t => t.Text == "Abandoned chapel" && t.FontSize == 26));
            Assert.Equal(4, ui.Db.GetRollHistory().Count);
            Assert.Equal(3, box.SelectedItem);
        });
    }

    [Fact]
    public void The_Rolls_choice_is_locked_while_dice_are_rolling()
    {
        Sta.Run(() =>
        {
            using var temp = new TempDatabase();
            using var db = temp.Open();
            var c = db.CreateCollection("C");
            db.SaveTable(Fixtures.Table(DiceExpression.Parse("d20"), (1, 20, "Anything")).Also(t => { t.Name = "Sites"; t.CollectionId = c.Id; }));
            var roller = new FakeDddiceRoller();
            var providers = new DiceProviderViewModel(new FixedDice(4), new DddiceDiceProvider(roller), connection: new DddiceConnection(new MemoryAccountStore()), openAccount: () => { });
            var main = new MainViewModel(db, providers);
            var window = new MainWindow
            {
                DataContext = main, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
            };
            window.Show();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher)); // as for a real click
            try
            {
                void Layout() { window.Dispatcher.Invoke(DispatcherPriority.ContextIdle, () => { }); window.UpdateLayout(); }
                providers.Select(DiceProviderKind.Dddice);
                main.SelectedTable = main.Tables.Single();
                Layout();
                var session = Assert.IsType<RollViewModel>(main.Current);
                var box = ViewTests.FindAll<ComboBox>(window).Single(b => b.Name == "RollCountBox");
                box.SelectedItem = 2;
                Layout();

                session.RollCommand.Execute(null);
                Layout();
                Assert.False(box.IsEnabled);
                Assert.Contains(ViewTests.FindAll<TextBlock>(window), t => t.IsVisible && t.Text == "Rolling 1 of 2…");

                roller.Settle(("d20", 5));
                for (var i = 0; i < 200 && roller.Requests.Count < 2; i++) { Layout(); Thread.Sleep(5); }
                Layout();
                Assert.False(box.IsEnabled);
                Assert.Contains(ViewTests.FindAll<TextBlock>(window), t => t.IsVisible && t.Text == "Rolling 2 of 2…");

                roller.Settle(("d20", 9));
                for (var i = 0; i < 200 && session.IsRolling; i++) { Layout(); Thread.Sleep(5); }
                Layout();
                Assert.True(box.IsEnabled);

                // The Dice choice and RC14's Account… are still on screen at the default size, with results on the Roll screen.
                var pane = (ScrollViewer)window.FindName("LeftScroll");
                Assert.Equal(0, pane.VerticalOffset);
                foreach (var name in new[] { "BuiltInRadio", "DddiceRadio", "DddiceAccountButton" })
                {
                    var element = (FrameworkElement)window.FindName(name);
                    var top = element.TranslatePoint(new Point(0, 0), pane).Y;
                    Assert.True(element.IsVisible && top >= 0 && top + element.ActualHeight <= pane.ViewportHeight, $"{name} should be on screen");
                }
                Assert.Equal(["Roll 1", "Roll 2"], ViewTests.FindAll<TextBlock>(window).Where(t => t.IsVisible && t.Name == "BatchLabelText").Select(t => t.Text).ToArray());
            }
            finally { window.Close(); }
        });
    }
}
