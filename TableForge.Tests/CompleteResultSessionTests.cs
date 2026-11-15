using TableForge.Dice;
using TableForge.Domain;
using TableForge.Results;
using TableForge.Streaming;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// Gidubba 1.6 Complete Roll Results on the Roll screen (<see cref="RollViewModel.CompleteResult"/>): the trail metadata it is
/// built from, how it follows rolls, links, auto-follow, Return and inline dice, Copy Complete Result — and that nothing else
/// (Recent Rolls, the overlay, the trail itself) changes because of it.
/// </summary>
public class CompleteResultSessionTests
{
    /// <summary>Linked d6 tables, one row per face ("A 1" … "A 6"), unless a test changes them.</summary>
    private sealed class World
    {
        public Dictionary<long, RollableTable> Tables { get; } = [];

        public RollableTable Add(string name, bool manual = false, int sets = 1, bool clamp = false, int rows = 6)
        {
            var table = new RollableTable
            {
                Id = Tables.Count + 1,
                Name = name,
                Dice = DiceExpression.Parse("d6"),
                IsManualSelection = manual,
                ClampResultsToRange = clamp,
                ResultSets = Enumerable.Range(0, sets).Select(s => new ResultSet
                {
                    Name = sets > 1 ? $"Set {s + 1}" : "",
                    SortOrder = s,
                    Entries = Enumerable.Range(1, rows).Select(v => new TableEntry { Min = v, Max = v, Text = $"{name} {v}", SortOrder = v }).ToList(),
                }).ToList(),
            };
            Tables[table.Id] = table;
            return table;
        }

        public void Link(RollableTable from, int value, RollableTable to, int set = 0) =>
            from.ResultSets[set].Entries.Single(e => e.Min == value).LinkedTableId = to.Id;

        public void Text(RollableTable table, int value, string text, int set = 0) =>
            table.ResultSets[set].Entries.Single(e => e.Min == value).Text = text;

        public RollableTable? Load(long id) => Tables.GetValueOrDefault(id);
    }

    private sealed record Rig(RollViewModel Session, List<RollSnapshot> History, List<string> Copied, OverlayPublisher Overlay);

    private static Rig Open(World world, RollableTable start, IDiceProvider dice, bool autoFollow = false)
    {
        var history = new List<RollSnapshot>();
        var copied = new List<string>();
        var overlay = new OverlayPublisher();
        var session = new RollViewModel(start, dice, world.Load, rolled: history.Add, copyText: copied.Add, overlay: overlay,
            autoFollow: new AutoFollowSetting(autoFollow));
        return new Rig(session, history, copied, overlay);
    }

    private static string[] Texts(RollViewModel s) => s.CompleteResult.Pieces.SelectMany(p => p.Lines).Select(l => l.Text).ToArray();

    private static string[] Captions(RollViewModel s) => s.CompleteResult.Pieces.Select(p => p.Caption).ToArray();

    private static void Follow(RollViewModel s, int line = 0) => s.Current.Outcomes[^1].Lines[line].FollowCommand!.Execute(null);

    /// <summary>A → B → C: a 1 on A links to B, a 1 on B links to C.</summary>
    private static (World World, RollableTable A, RollableTable B, RollableTable C) Chain()
    {
        var w = new World();
        var a = w.Add("A");
        var b = w.Add("B");
        var c = w.Add("C");
        w.Link(a, 1, b);
        w.Link(b, 1, c);
        return (w, a, b, c);
    }

    // ---- metadata on the trail -----------------------------------------------------------------------------------------

    [Fact]
    public void Every_action_is_numbered_and_the_results_of_one_multi_roll_action_share_it()
    {
        var w = new World();
        var a = w.Add("A");
        var s = Open(w, a, new SequenceDice(2, 3, 4, 5)).Session;

        s.RollCommand.Execute(null);
        s.RollCount = 3;
        s.RollCommand.Execute(null);

        var outcomes = s.Current.Outcomes;
        Assert.Equal([1, 2, 2, 2], outcomes.Select(o => o.ActionNumber));
        Assert.Equal([0, 1, 2, 3], outcomes.Select(o => o.BatchPosition));
        Assert.Equal(["2", "3", "4", "5"], outcomes.Select(o => o.RollText));
        Assert.All(outcomes, o => Assert.False(o.Entered));
    }

    [Fact]
    public void A_followed_step_remembers_the_line_it_was_opened_from_and_first_and_Return_steps_do_not()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new SequenceDice(1)).Session;
        s.RollCommand.Execute(null);
        var line = s.Current.Outcomes[0].Lines[0];
        Follow(s);

        Assert.Null(s.Steps[0].OpenedFrom);
        Assert.Same(line, s.Steps[1].OpenedFrom);
        s.ReturnToOriginalCommand.Execute(null);
        Assert.Null(s.Steps[2].OpenedFrom);
    }

    // ---- when it is shown ---------------------------------------------------------------------------------------------

    [Fact]
    public void An_ordinary_roll_has_no_Complete_Result_to_show_or_copy()
    {
        var w = new World();
        var a = w.Add("A", sets: 3);
        var s = Open(w, a, new FixedDice(4)).Session;
        Assert.False(s.CompleteResult.IsVisible);

        s.RollCommand.Execute(null);

        Assert.False(s.CompleteResult.IsVisible);
        Assert.False(s.CopyCompleteResultCommand.CanExecute(null));
    }

    [Fact]
    public void A_multi_roll_action_on_one_table_is_shown()
    {
        var w = new World();
        var a = w.Add("A");
        var s = Open(w, a, new SequenceDice(2, 5, 3)).Session;
        s.RollCount = 3;
        s.RollCommand.Execute(null);

        Assert.True(s.CompleteResult.IsVisible);
        Assert.Equal(["A 2", "A 5", "A 3"], Texts(s));
        Assert.Equal(["A · roll 1 · 2", "A · roll 2 · 5", "A · roll 3 · 3"], Captions(s));
    }

    // ---- manual links -------------------------------------------------------------------------------------------------

    [Fact]
    public void Open_shows_the_linked_table_as_pending_and_its_roll_is_appended()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new SequenceDice(1, 4)).Session;
        s.RollCommand.Execute(null);
        Assert.False(s.CompleteResult.IsVisible);

        Follow(s);
        Assert.True(s.CompleteResult.IsVisible);
        Assert.Equal(["A 1"], Texts(s));
        Assert.Equal("→ B — not rolled yet", s.CompleteResult.Pending!.Text);

        s.RollCommand.Execute(null);
        Assert.Null(s.CompleteResult.Pending);
        Assert.Equal(["A 1", "B 4"], Texts(s));
    }

    [Fact]
    public void Rolling_a_linked_table_again_replaces_its_part_and_the_trail_keeps_both_rolls()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new SequenceDice(1, 4, 6)).Session;
        s.RollCommand.Execute(null);
        Follow(s);
        s.RollCommand.Execute(null);
        s.RollCommand.Execute(null);

        Assert.Equal(["A 1", "B 6"], Texts(s));
        Assert.Equal(["B 4", "B 6"], s.Current.Outcomes.Select(o => o.Lines[0].Text)); // the trail is untouched
    }

    [Fact]
    public async Task A_pending_table_reads_rolling_while_its_dice_are_in_the_air_and_not_rolled_after_Stop()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new ScriptedDice(1, 4) { PendOnCall = 2 }).Session;
        s.RollCommand.Execute(null);
        Follow(s);

        s.RollCommand.Execute(null);
        Assert.Equal(CompleteResultPendingReason.Rolling, s.CompleteResult.Pending!.Reason);

        s.StopCommand.Execute(null);
        await s.RollTask;
        Assert.Equal(CompleteResultPendingReason.NotRolled, s.CompleteResult.Pending!.Reason);
    }

    // ---- auto-follow --------------------------------------------------------------------------------------------------

    [Fact]
    public void An_auto_follow_chain_is_assembled_in_resolution_order()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new SequenceDice(1, 1, 3), autoFollow: true).Session;
        s.RollCommand.Execute(null);

        Assert.Equal(["A 1", "B 1", "C 3"], Texts(s));
        Assert.Equal(["A · 1", "B · 1", "C · 3"], Captions(s));
        Assert.Null(s.CompleteResult.Pending);
        Assert.Equal("A 1\r\nB 1\r\nC 3", s.CompleteResult.ToPlainText("\r\n"));
    }

    [Fact]
    public async Task Auto_follow_to_a_Manual_selection_table_waits_for_a_choice_and_an_entered_result_continues_it()
    {
        var w = new World();
        var a = w.Add("A");
        var pick = w.Add("Pick", manual: true);
        var c = w.Add("C");
        w.Link(a, 1, pick);
        w.Link(pick, 2, c);
        var s = Open(w, a, new SequenceDice(1, 5), autoFollow: true).Session;

        s.RollCommand.Execute(null);
        Assert.Equal("→ Pick — choose a result", s.CompleteResult.Pending!.Text);

        s.ManualRollText = "2";
        s.ResolveManualCommand.Execute(null);
        await s.RollTask;

        Assert.Equal(["A 1", "Pick 2", "C 5"], Texts(s));
        Assert.Equal("Pick · 2 (entered)", Captions(s)[1]);
    }

    [Fact]
    public async Task Auto_follow_stopped_at_an_opened_table_says_so()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new ScriptedDice(1, 1) { PendOnCall = 2 }, autoFollow: true).Session;
        s.RollCommand.Execute(null);
        Assert.Equal(CompleteResultPendingReason.Rolling, s.CompleteResult.Pending!.Reason);

        s.StopCommand.Execute(null);
        await s.RollTask;
        Assert.Equal("→ B — not rolled (auto-follow stopped)", s.CompleteResult.Pending!.Text);
        Assert.Equal("A 1", s.CompleteResult.ToPlainText("\r\n"));
    }

    [Fact]
    public void A_result_with_several_links_notes_them_when_auto_follow_stops_there()
    {
        var w = new World();
        var a = w.Add("A", sets: 2);
        var b = w.Add("B");
        var c = w.Add("C");
        var start = w.Add("Start");
        w.Link(start, 1, a);
        w.Link(a, 1, b, set: 0);
        w.Link(a, 1, c, set: 1);
        var s = Open(w, start, new SequenceDice(1), autoFollow: true).Session;
        s.RollCommand.Execute(null);

        Assert.Equal(["", "→ B (not followed)", "→ C (not followed)"], s.CompleteResult.Pieces.SelectMany(p => p.Lines).Select(l => l.Note));
    }

    // ---- Return to the original table ---------------------------------------------------------------------------------

    [Fact]
    public void Return_keeps_the_finished_chain_until_the_next_result()
    {
        var (w, a, _, _) = Chain();
        var s = Open(w, a, new SequenceDice(1, 4, 6, 1, 2)).Session;
        s.RollCommand.Execute(null);
        Follow(s);
        s.RollCommand.Execute(null);
        s.ReturnToOriginalCommand.Execute(null);

        Assert.True(s.CompleteResult.IsPrevious);
        Assert.Equal(["A 1", "B 4"], Texts(s));

        s.RollCommand.Execute(null);
        Assert.False(s.CompleteResult.IsPrevious);
        Assert.Equal(["A 6"], Texts(s));
        Assert.False(s.CompleteResult.IsVisible);
    }

    // ---- clamp and modifiers ------------------------------------------------------------------------------------------

    [Fact]
    public void A_clamped_roll_says_what_it_was_resolved_as_and_a_modified_roll_shows_its_final_value()
    {
        var w = new World();
        var a = w.Add("A");
        var b = w.Add("B", clamp: true, rows: 4);
        w.Link(a, 1, b);
        var s = Open(w, a, new SequenceDice(1, 6, 3)).Session;
        s.RollCommand.Execute(null);
        Follow(s);
        s.RollCommand.Execute(null);
        Assert.Equal("B · 6 (resolved as 4)", Captions(s)[1]);
        Assert.Equal("B 4", Texts(s)[1]);

        s.ModifierText = "+1";
        s.RollCommand.Execute(null);
        Assert.Equal("B · 4", Captions(s)[1]); // 3 +1: the final value, as the roll shows it
    }

    // ---- inline dice --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_rolled_inline_value_is_kept_when_its_result_is_superseded_and_cannot_be_rolled_again()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("You gain +1d4 Armor"), dice);
        session.RollCommand.Execute(null);
        var first = session.Current.Outcomes[0].Lines[0];
        dice.Value = 3;
        first.InlineActions[0].RollCommand!.Execute(null);

        dice.Value = 2;
        session.RollCommand.Execute(null);
        var calls = dice.Calls;

        Assert.True(first.HasResolved);
        Assert.True(first.ShowResolved);
        Assert.Equal("You gain +3 Armor", first.ResolvedText);
        Assert.Equal("You gain +1d4 Armor", first.Text); // the stored text never changes
        Assert.False(first.ShowInlineActions);
        Assert.False(first.InlineActions[0].RollCommand!.CanExecute(null));
        first.InlineActions[0].RollCommand!.Execute(null); // even asked directly, a superseded result is never rerolled
        Assert.Equal(calls, dice.Calls);
        Assert.Equal(3, first.InlineActions[0].LatestValue);
    }

    [Fact]
    public void A_rolled_inline_value_carries_into_the_Complete_Result_after_following_and_an_unrolled_one_stays_as_written()
    {
        var w = new World();
        var a = w.Add("A");
        var b = w.Add("B");
        w.Text(a, 1, "You find 2d6 gold and d4 gems");
        w.Link(a, 1, b);
        var dice = new SequenceDice(1, 7, 5);
        var s = Open(w, a, dice).Session;
        s.RollCommand.Execute(null);
        var line = s.Current.Outcomes[0].Lines[0];
        line.InlineActions.Single(x => x.DisplayExpression == "2d6").RollCommand!.Execute(null);
        Assert.Equal("You find 7 gold and d4 gems", Texts(s)[0]);

        Follow(s);
        s.RollCommand.Execute(null);

        Assert.Equal(["You find 7 gold and d4 gems", "B 5"], Texts(s));
        Assert.Equal(["d6", "2d6", "d6"], dice.Requested); // nothing was rolled for d4
    }

    [Fact]
    public void Auto_follow_never_rolls_inline_dice_in_the_results_it_passes()
    {
        var (w, a, _, _) = Chain();
        w.Text(a, 1, "Gain 1d4 armor");
        var dice = new SequenceDice(1, 2);
        var s = Open(w, a, dice, autoFollow: true).Session;
        s.RollCommand.Execute(null);

        Assert.Equal(["Gain 1d4 armor", "B 2"], Texts(s));
        Assert.Equal(["d6", "d6"], dice.Requested);
    }

    // ---- Copy Complete Result -----------------------------------------------------------------------------------------

    [Fact]
    public void Copy_Complete_Result_copies_the_result_text_only_and_says_so_until_the_result_changes()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 1, 3), autoFollow: true);
        var s = rig.Session;
        s.RollCommand.Execute(null);

        Assert.True(s.CopyCompleteResultCommand.CanExecute(null));
        s.CopyCompleteResultCommand.Execute(null);

        Assert.Equal(["A 1\r\nB 1\r\nC 3"], rig.Copied);
        Assert.Equal("Complete result copied.", s.CompleteResultCopyMessage);
        Assert.Equal("", s.CopyMessage); // the Export message is its own

        s.ReturnToOriginalCommand.Execute(null);
        Assert.Equal("", s.CompleteResultCopyMessage);
    }

    [Fact]
    public void A_clipboard_failure_is_reported_beside_Copy()
    {
        var (w, a, _, _) = Chain();
        var s = new RollViewModel(a, new SequenceDice(1), w.Load,
            copyText: _ => throw new System.Runtime.InteropServices.ExternalException("The clipboard is busy."));
        s.RollCommand.Execute(null);
        Follow(s);

        s.CopyCompleteResultCommand.Execute(null);
        Assert.Equal("The complete result could not be copied: The clipboard is busy.", s.CompleteResultCopyMessage);
    }

    [Fact]
    public void No_Match_is_copied_with_its_table_name()
    {
        var w = new World();
        var a = w.Add("A");
        var b = w.Add("B", rows: 4);
        w.Link(a, 1, b);
        var rig = Open(w, a, new SequenceDice(1, 6));
        rig.Session.RollCommand.Execute(null);
        Follow(rig.Session);
        rig.Session.RollCommand.Execute(null);
        rig.Session.CopyCompleteResultCommand.Execute(null);

        Assert.Equal(["A 1\r\nB: No entry covers 6."], rig.Copied);
    }

    // ---- nothing else changes -----------------------------------------------------------------------------------------

    [Fact]
    public void Recent_Rolls_and_the_overlay_still_get_one_result_per_roll_exactly_as_before()
    {
        var (w, a, _, _) = Chain();
        w.Text(a, 1, "Gain 1d4 armor");
        var rig = Open(w, a, new SequenceDice(1, 1, 3), autoFollow: true);
        rig.Session.RollCommand.Execute(null);

        Assert.Equal(["Gain 1d4 armor", "B 1", "C 3"], rig.History.Select(h => h.ResultText));
        Assert.Equal(["A", "B", "C"], rig.History.Select(h => h.TableName));
        var shown = rig.Overlay.State.Result!;
        Assert.Equal("C", shown.TableName); // the overlay still shows the latest result only
        Assert.Equal(["C 3"], shown.Lines.Select(l => l.Text));
    }
}
