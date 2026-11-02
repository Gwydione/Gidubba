using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// Hands out the given final values in order, with a chosen <see cref="IDiceProvider.MaxAttemptsPerResult"/>. It can run something
/// as each throw is asked for, and leave one throw in the air until it is cancelled.
/// </summary>
internal sealed class ScriptedDice(params int[] values) : IDiceProvider
{
    private int _next;

    public List<string> Requested { get; } = [];
    public int MaxAttemptsPerResult { get; set; } = UniqueRoller.DefaultMaxAttempts;

    /// <summary>Called with the 1-based call number as each throw is asked for.</summary>
    public Action<int>? OnThrow { get; set; }

    /// <summary>1-based call that stays in the air until cancelled; 0 for never.</summary>
    public int PendOnCall { get; set; }

    public Task<int> RollAsync(DiceExpression expression, CancellationToken cancellationToken)
    {
        Requested.Add(expression.ToString());
        OnThrow?.Invoke(Requested.Count);
        if (Requested.Count == PendOnCall)
        {
            var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return pending.Task;
        }
        return Task.FromResult(values[_next++ % values.Length]);
    }
}

/// <summary>
/// Unique Results, rolling with the built-in dice (Checkpoint 2): rejection sampling through <see cref="RollViewModel"/> and
/// <see cref="UniqueRoller"/> — the original dice on every throw, the same modifier and Clamp, exhaustion before rolling, rows used up
/// only once a result is accepted (and before the next roll of a batch), Stop, the attempt limit, and Recent Rolls.
/// </summary>
public class UniqueRollingTests
{
    private const long TableId = 1;

    private static TableEntry Row(int min, int max, string text, bool unique = true) =>
        new() { Min = min, Max = max, Text = text, IsUnique = unique, RowKey = Guid.NewGuid() };

    private static ResultSet Set(string name, params TableEntry[] rows) => new() { Name = name, Entries = [.. rows] };

    private static RollableTable Table(string dice, params ResultSet[] sets) =>
        new() { Id = TableId, Name = "Encounters", Dice = DiceExpression.Parse(dice), ResultSets = [.. sets] };

    private static RollableTable Table(string dice, params TableEntry[] rows) => Table(dice, Set("", rows));

    /// <summary>Goblin 1-2, Ogre 3-4, Dragon 5-6: every row Unique.</summary>
    private static RollableTable Encounters() => Table("d6", Row(1, 2, "Goblin"), Row(3, 4, "Ogre"), Row(5, 6, "Dragon"));

    private static TableEntry Entry(RollableTable table, string text) => table.ResultSets.SelectMany(s => s.Entries).Single(e => e.Text == text);

    private sealed class Rig(RollableTable table, IDiceProvider dice, UsedResultsStore store)
    {
        public RollableTable Table { get; } = table;
        public List<RollSnapshot> History { get; } = [];
        public UsedResultsStore Store { get; } = store;
        public RollViewModel Session { get; set; } = null!;
        public IDiceProvider Dice { get; } = dice;

        public void MarkUsed(params string[] texts) => Store.MarkUsed(TableId, texts.Select(t => Entry(Table, t).RowKey).ToList());

        public string[] Used() => Table.ResultSets.SelectMany(s => s.Entries).Where(e => Store.Get(TableId).Contains(e.RowKey)).Select(e => e.Text).ToArray();

        public string[] Shown() => Session.Results.Select(l => l.Text).ToArray();

        public void Roll() => Session.RollCommand.Execute(null);
    }

    private static Rig Open(RollableTable table, IDiceProvider dice, UsedResultsStore? store = null)
    {
        var rig = new Rig(table, dice, store ?? UsedResultsStore.InMemory());
        rig.Session = new RollViewModel(table, dice, rolled: rig.History.Add, usedResults: rig.Store);
        return rig;
    }

    private static (Rig Rig, ScriptedDice Dice) Open(RollableTable table, params int[] values)
    {
        var dice = new ScriptedDice(values);
        return (Open(table, dice), dice);
    }

    // ---- accepted and rejected rolls -------------------------------------------------------------------------------------

    [Fact]
    public void An_accepted_first_throw_is_shown_used_up_and_recorded_with_no_rerolls()
    {
        var (rig, dice) = Open(Encounters(), 3);
        rig.Roll();

        Assert.Equal(["Ogre"], rig.Shown());
        Assert.Equal(["Ogre"], rig.Used());
        Assert.Equal(0, Assert.Single(rig.Session.LatestRolls).RerolledCount);
        var record = Assert.Single(rig.History);
        Assert.Equal((3, "Ogre", 0), (record.RollValue, record.ResultText, record.RerolledCount));
        Assert.Equal(["d6"], dice.Requested);
        Assert.Equal("", rig.Session.Message);
    }

    [Fact]
    public void A_throw_on_a_used_row_is_rerolled_with_the_original_dice_and_only_the_accepted_result_is_shown_and_recorded()
    {
        var (rig, dice) = Open(Encounters(), 6, 5, 2);
        rig.MarkUsed("Dragon");
        rig.Roll();

        Assert.Equal(["d6", "d6", "d6"], dice.Requested);
        var outcome = Assert.Single(rig.Session.Current.Outcomes);                  // the rejected 6 and 5 are never shown
        Assert.Equal(("Rolled 2", 2), (outcome.Display, outcome.RerolledCount));
        Assert.Equal(["Goblin"], rig.Shown());
        Assert.Equal(["Goblin", "Dragon"], rig.Used());
        var record = Assert.Single(rig.History);
        Assert.Equal((2, "Goblin", 2), (record.RollValue, record.ResultText, record.RerolledCount));
        Assert.True(rig.Session.ResultSets[0].Entries[0].IsMatched);                // only the accepted row is highlighted
        Assert.False(rig.Session.ResultSets[0].Entries[2].IsMatched);
    }

    [Fact]
    public void Repeatable_rows_are_never_rerolled_or_used_up()
    {
        var (rig, dice) = Open(Table("d6", Row(1, 3, "Hermit"), Row(4, 6, "Nobody", unique: false)), 5, 5);
        rig.Roll();
        rig.Roll();

        Assert.Equal(["d6", "d6"], dice.Requested);
        Assert.Equal([0, 0], rig.History.Select(h => h.RerolledCount).ToArray());
        Assert.Empty(rig.Used());
    }

    [Fact]
    public void A_gap_is_the_no_match_it_always_was_and_uses_nothing_up()
    {
        var rows = new[] { Row(1, 3, "Hermit"), Row(4, 6, "Witch") };
        var (rig, dice) = Open(Table("d8", rows), 7);
        rig.MarkUsed("Hermit", "Witch");
        rig.Roll();

        Assert.Equal(["d8"], dice.Requested);                                       // not exhausted: 7 and 8 can still be rolled
        Assert.Equal(["No entry covers 7."], rig.Shown());
        Assert.Equal(0, Assert.Single(rig.History).RerolledCount);
    }

    [Fact]
    public void A_throw_is_rejected_when_any_of_several_overlapping_rows_is_used()
    {
        var (rig, _) = Open(Table("d6", Row(1, 6, "Corridor", unique: false), Row(3, 4, "Treasure")), 3, 1);
        rig.MarkUsed("Treasure");
        rig.Roll();

        Assert.Equal(["Corridor"], rig.Shown());
        Assert.Equal(1, Assert.Single(rig.History).RerolledCount);
    }

    [Fact]
    public void With_several_result_sets_a_used_row_in_any_set_rejects_the_whole_roll_and_an_accepted_one_uses_up_every_set()
    {
        var table = Table("d6",
            Set("Who", Row(1, 3, "Bandits", unique: false), Row(4, 6, "Knights", unique: false)),
            Set("Where", Row(1, 3, "Ford"), Row(4, 6, "Bridge")));
        var (rig, _) = Open(table, 5, 2);
        rig.MarkUsed("Bridge");
        rig.Roll();

        Assert.Equal(["Bandits", "Ford"], rig.Shown());                             // one value for both sets: never Knights with Ford
        Assert.Equal(["Ford", "Bridge"], rig.Used());
        Assert.Equal(1, Assert.Single(rig.History).RerolledCount);

        var both = Table("d6", Set("A", Row(1, 3, "A low"), Row(4, 6, "A high")), Set("B", Row(1, 6, "B any")));
        var (other, _) = Open(both, 2);
        other.Roll();
        Assert.Equal(["A low", "B any"], other.Used());
    }

    [Fact]
    public void A_d66_table_rerolls_among_its_own_legal_results()
    {
        var (rig, dice) = Open(Table("d66", Row(11, 36, "Slip"), Row(41, 66, "Fall")), 22, 45);
        rig.MarkUsed("Slip");
        rig.Roll();

        Assert.Equal(["d66", "d66"], dice.Requested);
        Assert.Equal(["Fall"], rig.Shown());
    }

    // ---- exhaustion ------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_exhausted_table_is_not_rolled_at_all_and_keeps_its_modifier()
    {
        // Open-ended ends, so that with the +1 every value the roll can reach is still a row, not a gap.
        var (rig, dice) = Open(Table("d6", Row(RangeBounds.OpenBelow, 2, "Goblin"), Row(3, 4, "Ogre"), Row(5, RangeBounds.OpenAbove, "Dragon")), 1);
        rig.MarkUsed("Goblin", "Ogre", "Dragon");
        rig.Session.ModifierText = "1";
        rig.Roll();

        Assert.Empty(dice.Requested);
        Assert.Equal(UniqueResults.ExhaustedMessage, rig.Session.Message);
        Assert.Equal("All results used — Reset to roll again.", rig.Session.Message);
        Assert.Empty(rig.Session.Current.Outcomes);
        Assert.Empty(rig.History);
        Assert.Equal("1", rig.Session.ModifierText);
        Assert.False(rig.Session.IsRolling);
        Assert.True(rig.Session.RollCommand.CanExecute(null));
    }

    [Fact]
    public void Rolling_continues_while_a_repeatable_row_is_left_even_when_every_unique_row_is_used()
    {
        var (rig, dice) = Open(Table("d6", Row(1, 3, "Hermit"), Row(4, 6, "Nobody", unique: false)), 2, 2, 6);
        rig.MarkUsed("Hermit");
        rig.Roll();

        Assert.Equal(["Nobody"], rig.Shown());
        Assert.Equal(3, dice.Requested.Count);
    }

    // ---- situational modifier and Clamp ------------------------------------------------------------------------------------

    private static RollableTable Depths() =>
        Table("d6", Row(1, 4, "Shallow"), Row(5, 6, "Deep"), Row(7, 10, "Abyss"));

    [Fact]
    public void The_situational_modifier_is_added_to_every_throw_and_used_up_by_the_accepted_one()
    {
        var (rig, dice) = Open(Depths(), 4, 1);
        rig.MarkUsed("Deep");
        rig.Session.ModifierText = "2";
        rig.Roll();

        Assert.Equal(["d6", "d6"], dice.Requested);                                 // 4 + 2 = 6 is Deep, used; 1 + 2 = 3 is accepted
        var outcome = Assert.Single(rig.Session.LatestRolls);
        Assert.Equal(("Rolled 3", "1 +2 situational"), (outcome.Display, outcome.Breakdown));
        Assert.Equal(["Shallow"], rig.Shown());
        Assert.Equal("0", rig.Session.ModifierText);
        var record = Assert.Single(rig.History);
        Assert.Equal((3, 2, 1), (record.RollValue, record.SituationalModifier, record.RerolledCount));
    }

    [Fact]
    public void Exhaustion_is_judged_with_the_modifier_the_roll_would_use()
    {
        var (rig, dice) = Open(Depths(), 1);
        rig.MarkUsed("Deep", "Abyss");

        rig.Session.ModifierText = "4";                                            // 5-10: every value used
        rig.Roll();
        Assert.Empty(dice.Requested);
        Assert.Equal(UniqueResults.ExhaustedMessage, rig.Session.Message);
        Assert.Equal("4", rig.Session.ModifierText);

        rig.Session.ModifierText = "0";                                            // 1-6 still reaches Shallow
        rig.Roll();
        Assert.Equal(["Shallow"], rig.Shown());
    }

    [Fact]
    public void Clamp_applies_to_every_throw_and_the_accepted_one_is_recorded_as_clamped()
    {
        var table = Table("d20", Row(3, 10, "Low"), Row(11, 18, "High"));
        table.ClampResultsToRange = true;
        var (rig, _) = Open(table, 1, 20);
        rig.MarkUsed("Low");
        rig.Roll();                                                                 // 1 is looked up as 3 (Low, used); 20 as 18

        var outcome = Assert.Single(rig.Session.LatestRolls);
        Assert.Equal(("Rolled 20", "Resolved as 18 (clamped)", 1), (outcome.Display, outcome.ClampNote, outcome.RerolledCount));
        Assert.Equal(["High"], rig.Shown());
        var record = Assert.Single(rig.History);
        Assert.Equal((20, (int?)18, 1), (record.RollValue, record.ClampedValue, record.RerolledCount));
        Assert.Equal(["Low", "High"], rig.Used());
    }

    // ---- multi-roll batches ------------------------------------------------------------------------------------------------

    [Fact]
    public void In_a_batch_each_accepted_result_is_used_up_before_the_next_roll()
    {
        var (rig, dice) = Open(Encounters(), 1, 2, 3, 4, 5);
        rig.Session.RollCount = 3;
        rig.Roll();

        Assert.Equal(["Goblin", "Ogre", "Dragon"], rig.Session.LatestRolls.Select(o => Assert.Single(o.Lines).Text).ToArray());
        Assert.Equal([0, 1, 1], rig.Session.LatestRolls.Select(o => o.RerolledCount).ToArray()); // 2 was Goblin again; 4 was Ogre
        Assert.Equal(5, dice.Requested.Count);
        Assert.Equal([1, 3, 5], rig.History.Select(h => h.RollValue).ToArray());
        Assert.Equal([0, 1, 1], rig.History.Select(h => h.RerolledCount).ToArray());
        Assert.Equal(["Goblin", "Ogre", "Dragon"], rig.Used());
        Assert.Equal("", rig.Session.Message);
    }

    [Fact]
    public void A_batch_that_uses_up_the_table_stops_there_and_keeps_what_it_rolled()
    {
        var (rig, dice) = Open(Table("d6", Row(1, 3, "Hermit"), Row(4, 6, "Witch")), 2, 5);
        rig.Session.RollCount = 3;
        rig.Roll();

        Assert.Equal(["Hermit", "Witch"], rig.Session.LatestRolls.Select(o => Assert.Single(o.Lines).Text).ToArray());
        Assert.Equal(2, dice.Requested.Count);                                      // the third roll was never thrown
        Assert.Equal(2, rig.History.Count);
        Assert.Equal("All results used — Reset to roll again. Stopped at roll 3 of 3: rolls 1–2 are shown; the rest were not rolled.",
            rig.Session.Message);
        Assert.False(rig.Session.IsRolling);
    }

    [Fact]
    public void In_a_batch_only_the_first_result_uses_the_modifier_rerolls_included()
    {
        var (rig, _) = Open(Table("d6", Row(1, 4, "Shallow", unique: false), Row(5, 6, "Deep"), Row(7, 10, "Abyss")), 4, 1, 4);
        rig.MarkUsed("Deep");
        rig.Session.ModifierText = "2";
        rig.Session.RollCount = 2;
        rig.Roll();

        Assert.Equal([(3, 2, 1), (4, 0, 0)], rig.History.Select(h => (h.RollValue, h.SituationalModifier, h.RerolledCount)).ToArray());
    }

    // ---- the attempt limit -----------------------------------------------------------------------------------------------

    [Fact]
    public void When_every_allowed_throw_lands_on_a_used_row_nothing_is_used_shown_or_recorded()
    {
        var (rig, dice) = Open(Encounters(), 6);
        rig.MarkUsed("Dragon");
        rig.Session.ModifierText = "0";
        rig.Roll();

        Assert.Equal(10, dice.Requested.Count);                                     // 10 throws in all, the first included
        Assert.Equal("No unused result came up in 10 rolls, so nothing was used. Roll again, or enter a result manually.", rig.Session.Message);
        Assert.Empty(rig.Session.Current.Outcomes);
        Assert.Empty(rig.History);
        Assert.Equal(["Dragon"], rig.Used());
    }

    [Fact]
    public void The_limit_is_the_providers_own()
    {
        Assert.Equal(10_000, new BuiltInDiceProvider().MaxAttemptsPerResult);
        Assert.Equal(10, ((IDiceProvider)new FixedDice(1)).MaxAttemptsPerResult);   // any other provider: the agreed 10

        var (rig, dice) = Open(Encounters(), 6);
        dice.MaxAttemptsPerResult = 3;
        rig.MarkUsed("Dragon");
        rig.Roll();
        Assert.Equal(3, dice.Requested.Count);
        Assert.Equal(RollViewModel.AttemptLimitMessage(3), rig.Session.Message);
    }

    [Fact]
    public void The_built_in_dice_give_up_after_10000_throws_without_changing_the_dice_or_picking_the_result()
    {
        // 100d6: only 100 (every die a 1) is still available — reachable, so not exhausted, but never going to come up.
        var table = Table("100d6", Row(100, 100, "All ones"), Row(101, 600, "Anything else"));
        var rig = Open(table, new BuiltInDiceProvider(new Random(12345)));
        rig.MarkUsed("Anything else");
        rig.Roll();

        Assert.Equal("No unused result came up in 10,000 rolls, so nothing was used. Roll again, or enter a result manually.", rig.Session.Message);
        Assert.Empty(rig.History);
        Assert.Equal(["Anything else"], rig.Used());
    }

    [Fact]
    public void The_built_in_dice_find_an_unlikely_remaining_result_without_changing_the_dice()
    {
        // 3d6 with only 3 left: one chance in 216 per throw.
        var table = Table("3d6", Row(3, 3, "Snake eyes and one"), Row(4, 18, "Everything else"));
        var rig = Open(table, new BuiltInDiceProvider(new Random(7)));
        rig.MarkUsed("Everything else");
        rig.Roll();

        Assert.Equal(["Snake eyes and one"], rig.Shown());
        var record = Assert.Single(rig.History);
        Assert.Equal(("3d6", 3), (record.DiceText, record.RollValue));
        Assert.True(record.RerolledCount > 0);
        Assert.Equal(UniqueResults.ExhaustedMessage, Roll(rig));                     // and now it is exhausted

        static string Roll(Rig r) { r.Roll(); return r.Session.Message; }
    }

    // ---- probability weighting -------------------------------------------------------------------------------------------

    [Fact]
    public void Accepted_rolls_keep_the_dice_s_own_weights_among_the_values_left()
    {
        // 2d6, one Unique row per total, with 7 and 8 used: the totals left must come up in the 2d6 proportions 1:2:3:4:5:3:2:1:...
        var rows = Enumerable.Range(2, 11).Select(v => Row(v, v, $"{v}")).ToArray();
        var table = Table("2d6", rows);
        var used = new HashSet<Guid> { rows[5].RowKey, rows[6].RowKey };            // 7 and 8
        var dice = new BuiltInDiceProvider(new Random(2026));

        const int samples = 50_000;
        var counts = new Dictionary<int, int>();
        var rerolls = 0;
        for (var i = 0; i < samples; i++)
        {
            var result = UniqueRoller.RollAsync(table, 0, used, UniqueRoller.BuiltInMaxAttempts,
                async () => await dice.RollAsync(table.Dice, CancellationToken.None), () => false).GetAwaiter().GetResult();
            Assert.Equal(UniqueRollStatus.Accepted, result.Status);
            counts[result.Roll] = counts.GetValueOrDefault(result.Roll) + 1;
            rerolls += result.RerolledCount;
        }

        Assert.False(counts.ContainsKey(7));
        Assert.False(counts.ContainsKey(8));
        int Ways(int total) => 6 - Math.Abs(total - 7);
        var remaining = Enumerable.Range(2, 11).Where(v => v is not (7 or 8)).ToList();
        var weight = remaining.Sum(Ways);                                            // 36 - 6 - 5 = 25
        var chiSquare = remaining.Sum(v =>
        {
            var expected = samples * (double)Ways(v) / weight;
            var observed = counts.GetValueOrDefault(v);
            return (observed - expected) * (observed - expected) / expected;
        });
        Assert.True(chiSquare < 27.88, $"chi-square {chiSquare:F2} with 8 degrees of freedom (p < 0.0005 would be 27.88)");

        // A throw is rejected 11 times in 36, so each accepted roll takes 11/25 rerolls on average.
        Assert.InRange(rerolls / (double)samples, 0.40, 0.48);
    }

    // ---- Stop ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_while_a_reroll_is_in_the_air_cancels_it_and_uses_nothing()
    {
        var (rig, dice) = Open(Encounters(), 5);
        dice.PendOnCall = 2;
        rig.MarkUsed("Dragon");
        rig.Session.ModifierText = "1";
        rig.Roll();                                                                 // 5 + 1 = 6 is Dragon, used: thrown again

        Assert.True(rig.Session.IsRolling);
        Assert.Equal(2, dice.Requested.Count);
        Assert.True(rig.Session.StopCommand.CanExecute(null));

        rig.Session.StopCommand.Execute(null);
        await rig.Session.RollTask;

        Assert.Equal("The roll was cancelled.", rig.Session.Message);
        Assert.Empty(rig.Session.Current.Outcomes);
        Assert.Empty(rig.History);
        Assert.Equal(["Dragon"], rig.Used());
        Assert.Equal("1", rig.Session.ModifierText);                               // kept for another try
        Assert.False(rig.Session.IsRolling);
        Assert.True(rig.Session.RollCommand.CanExecute(null));
    }

    [Fact]
    public void Stop_between_two_throws_ends_the_rerolls_even_when_every_throw_is_instant()
    {
        var (rig, dice) = Open(Encounters(), 5, 1);
        rig.MarkUsed("Dragon");
        dice.OnThrow = call => { if (call == 1) rig.Session.CancelRoll(); };        // pressed while the first (rejected) throw lands
        rig.Roll();

        Assert.Single(dice.Requested);
        Assert.Equal("The roll was cancelled.", rig.Session.Message);
        Assert.Empty(rig.History);
        Assert.Equal(["Dragon"], rig.Used());
    }

    [Fact]
    public void Stop_in_a_batch_keeps_the_results_already_accepted()
    {
        var (rig, dice) = Open(Encounters(), 1, 1, 3);
        rig.Session.RollCount = 3;
        dice.OnThrow = call => { if (call == 2) rig.Session.CancelRoll(); };        // roll 2's first throw is Goblin again
        rig.Roll();

        Assert.Equal(["Goblin"], rig.Session.LatestRolls.Select(o => Assert.Single(o.Lines).Text).ToArray());
        Assert.Equal("The roll was cancelled. Stopped at roll 2 of 3: rolls 1–1 are shown; the rest were not rolled.", rig.Session.Message);
        Assert.Equal(["Goblin"], rig.Used());
        Assert.Single(rig.History);
    }

    // ---- tables without Unique rows, and the store ---------------------------------------------------------------------

    private static UsedResultsStore Failing(bool onGet, bool onMark) => new(
        _ => onGet ? throw new InvalidOperationException("disk full") : new HashSet<Guid>(),
        (_, _) => { if (onMark) throw new InvalidOperationException("disk full"); });

    [Fact]
    public void A_table_without_unique_rows_rolls_exactly_as_before_and_never_touches_used_results()
    {
        var table = Fixtures.Table(DiceExpression.Parse("d20"), [(1, 10, "Low"), (11, 20, "High")]);
        var dice = new ScriptedDice(14);
        var rig = Open(table, dice, Failing(onGet: true, onMark: true));
        rig.Roll();

        Assert.Equal(["d20"], dice.Requested);
        Assert.Equal(["High"], rig.Shown());
        Assert.Equal(("Rolled 14", 0), (rig.Session.RollDisplay, Assert.Single(rig.History).RerolledCount));
        Assert.Equal("", rig.Session.Message);
    }

    [Fact]
    public void Used_results_that_cannot_be_read_stop_the_roll_before_anything_is_thrown()
    {
        var dice = new ScriptedDice(3);
        var rig = Open(Encounters(), dice, Failing(onGet: true, onMark: false));
        rig.Roll();

        Assert.Empty(dice.Requested);
        Assert.Equal("The used results could not be read: disk full", rig.Session.Message);
        Assert.Empty(rig.History);
    }

    [Fact]
    public void A_result_whose_use_cannot_be_recorded_is_not_shown_or_recorded()
    {
        var dice = new ScriptedDice(3);
        var rig = Open(Encounters(), dice, Failing(onGet: false, onMark: true));
        rig.Session.ModifierText = "1";
        rig.Roll();

        Assert.Equal("The result could not be recorded as used, so it was not shown: disk full", rig.Session.Message);
        Assert.Empty(rig.Session.Current.Outcomes);
        Assert.Empty(rig.History);
        Assert.Equal("1", rig.Session.ModifierText);
    }

    // ---- the app, with its database ------------------------------------------------------------------------------------

    [Fact]
    public void In_the_app_used_results_are_saved_and_still_used_after_a_restart_and_recent_rolls_keep_the_reroll_count()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var c = db.CreateCollection("Dungeon");
        var saved = db.SaveTable(new RollableTable
        {
            CollectionId = c.Id, Name = "Encounters", Dice = DiceExpression.Parse("d6"),
            ResultSets = [Set("", Row(1, 3, "Hermit"), Row(4, 6, "Witch"))],
        });
        var hermit = db.LoadTable(saved.Id)!.ResultSets[0].Entries[0].RowKey;

        var dice = new FixedDice(2);
        var main = new MainViewModel(db, dice, (_, _) => true);
        main.SelectedTable = main.Tables.Single(t => t.Name == "Encounters");
        var session = Assert.IsType<RollViewModel>(main.Current);
        session.RollCommand.Execute(null);
        Assert.Equal(["Hermit"], session.Results.Select(l => l.Text).ToArray());
        Assert.Equal([hermit], db.GetUsedRowKeys(saved.Id).ToArray());

        session.RollCommand.Execute(null);                                         // 2 is Hermit, used: ten throws, then it stops
        Assert.Equal(RollViewModel.AttemptLimitMessage(10), session.Message);
        Assert.Single(db.GetRollHistory());

        db.Dispose();                                                              // a restart
        var reopened = temp.Open();
        Assert.Equal([hermit], reopened.GetUsedRowKeys(saved.Id).ToArray());
        var restarted = new MainViewModel(reopened, dice, (_, _) => true);
        restarted.SelectedTable = restarted.Tables.Single(t => t.Name == "Encounters");
        var again = Assert.IsType<RollViewModel>(restarted.Current);
        again.RollCommand.Execute(null);
        Assert.Equal(RollViewModel.AttemptLimitMessage(10), again.Message);         // Hermit is still used
        dice.Value = 5;
        again.RollCommand.Execute(null);
        Assert.Equal(["Witch"], again.Results.Select(l => l.Text).ToArray());

        var history = reopened.GetRollHistory();
        Assert.Equal([("Witch", 0), ("Hermit", 0)], history.Select(h => (h.ResultText, h.RerolledCount)).ToArray());
        Assert.Equal(UniqueResults.ExhaustedMessage, Exhausted(again));

        static string Exhausted(RollViewModel s) { s.RollCommand.Execute(null); return s.Message; }
    }
}
