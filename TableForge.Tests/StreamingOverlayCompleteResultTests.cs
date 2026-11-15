using System.Text;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.Streaming;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>
/// Gidubba 1.6.0 "Overlay shows: Complete result", end to end through the Roll screen: what is published, and when. Latest Roll
/// (the default) stays exactly as before; Complete result publishes the growing chain as each result arrives — never when a roll
/// merely starts, on Back, or when the panel opens or closes — and keeps Clear, Test and the stale-update protection.
/// </summary>
public class StreamingOverlayCompleteResultTests
{
    /// <summary>Linked d6 tables, one row per face ("A 1" … "A 6"), unless a test changes them.</summary>
    private sealed class World
    {
        public Dictionary<long, RollableTable> Tables { get; } = [];

        public RollableTable Add(string name)
        {
            var table = new RollableTable
            {
                Id = Tables.Count + 1,
                Name = name,
                Dice = DiceExpression.Parse("d6"),
                ResultSets = [new ResultSet { Entries = Enumerable.Range(1, 6).Select(v => new TableEntry { Min = v, Max = v, Text = $"{name} {v}", SortOrder = v }).ToList() }],
            };
            Tables[table.Id] = table;
            return table;
        }

        public void Link(RollableTable from, int value, RollableTable to) => from.ResultSets[0].Entries.Single(e => e.Min == value).LinkedTableId = to.Id;
        public void Text(RollableTable table, int value, string text) => table.ResultSets[0].Entries.Single(e => e.Min == value).Text = text;
        public RollableTable? Load(long id) => Tables.GetValueOrDefault(id);
    }

    private sealed class Rig
    {
        public required RollViewModel Session { get; init; }
        public required OverlayPublisher Overlay { get; init; }

        /// <summary>Every state the overlay was given, as the page shows it, in order.</summary>
        public List<string> Published { get; } = [];
    }

    private static Rig Open(World world, RollableTable start, IDiceProvider dice, OverlayDisplayMode mode, bool autoFollow = false)
    {
        var overlay = new OverlayPublisher(instanceId: "test");
        overlay.SetDisplayMode(mode);
        var rig = new Rig
        {
            Overlay = overlay,
            Session = new RollViewModel(start, dice, world.Load, overlay: overlay, autoFollow: new AutoFollowSetting(autoFollow)),
        };
        overlay.Changed += (_, _) => rig.Published.Add(Shown(overlay));
        return rig;
    }

    /// <summary>The overlay as the page shows it: "Table / roll — line | heading: line | …", or "(empty)".</summary>
    private static string Shown(OverlayPublisher p)
    {
        var s = p.State;
        if (s.Result is not { } r) return "(empty)";
        var lines = r.Lines.Select(l => OverlayPublisher.Shown(l, s.ShowTableName, s.ShowRollValue)).Select(l => l.Heading.Length > 0 ? $"{l.Heading}: {l.Text}" : l.Text);
        return $"{r.TableName} / {r.RollValue} — {string.Join(" | ", lines)}";
    }

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

    private static void Follow(RollViewModel s) => s.Current.Outcomes[^1].Lines[0].FollowCommand!.Execute(null);

    // ---- Latest roll: unchanged ---------------------------------------------------------------------------------------------

    [Fact]
    public void Latest_roll_still_shows_each_linked_result_alone()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 1, 3), OverlayDisplayMode.LatestRoll, autoFollow: true);

        rig.Session.RollCommand.Execute(null);

        Assert.Equal(["A / 1 — A 1", "B / 1 — B 1", "C / 3 — C 3"], rig.Published);
    }

    [Fact]
    public void Latest_roll_shows_only_the_last_result_of_a_multi_roll_as_before()
    {
        var w = new World();
        var a = w.Add("A");
        var rig = Open(w, a, new SequenceDice(2, 5, 3), OverlayDisplayMode.LatestRoll);
        rig.Session.RollCount = 3;

        rig.Session.RollCommand.Execute(null);

        Assert.Equal("A / 3 — A 3", Shown(rig.Overlay));
    }

    // ---- Complete result: what is published ----------------------------------------------------------------------------------

    [Fact]
    public void Auto_follow_publishes_the_growing_chain_as_each_linked_result_arrives()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 1, 3), OverlayDisplayMode.CompleteResult, autoFollow: true);

        rig.Session.RollCommand.Execute(null);

        Assert.Equal([
            "A / 1 — A 1",                                   // the first result alone, exactly as Latest roll shows it
            "A / 1 — A 1 | B: 1 — B 1",
            "A / 1 — A 1 | B: 1 — B 1 | C: 3 — C 3",
        ], rig.Published);
    }

    [Fact]
    public void Opening_a_link_by_hand_publishes_nothing_until_its_result()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 4), OverlayDisplayMode.CompleteResult);

        rig.Session.RollCommand.Execute(null);
        Follow(rig.Session);                                 // "→ B — not rolled yet" in the app; nothing on the overlay
        Assert.Equal(["A / 1 — A 1"], rig.Published);

        rig.Session.RollCommand.Execute(null);
        Assert.Equal(["A / 1 — A 1", "A / 1 — A 1 | B: 4 — B 4"], rig.Published);
    }

    [Fact]
    public void Rolling_a_linked_table_again_republishes_the_chain_with_its_new_result()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 4, 6), OverlayDisplayMode.CompleteResult);
        rig.Session.RollCommand.Execute(null);
        Follow(rig.Session);
        rig.Session.RollCommand.Execute(null);

        rig.Session.RollCommand.Execute(null);

        Assert.Equal("A / 1 — A 1 | B: 6 — B 6", Shown(rig.Overlay));
    }

    [Fact]
    public void An_ordinary_single_roll_is_published_exactly_as_Latest_roll_publishes_it()
    {
        var w = new World();
        var a = w.Add("A");
        w.Text(a, 4, "Gain 1d4 Armor.");
        var latest = Open(w, a, new SequenceDice(4), OverlayDisplayMode.LatestRoll);
        var complete = Open(w, a, new SequenceDice(4), OverlayDisplayMode.CompleteResult);

        latest.Session.RollCommand.Execute(null);
        complete.Session.RollCommand.Execute(null);

        Assert.Equal(Encoding.UTF8.GetString(latest.Overlay.StateJson), Encoding.UTF8.GetString(complete.Overlay.StateJson));
        Assert.Equal("A / 4 — Gain 1d4 Armor.", Shown(complete.Overlay));
    }

    [Fact]
    public void A_multi_roll_shows_every_result()
    {
        var w = new World();
        var a = w.Add("A");
        var rig = Open(w, a, new SequenceDice(2, 5, 3), OverlayDisplayMode.CompleteResult);
        rig.Session.RollCount = 3;

        rig.Session.RollCommand.Execute(null);

        Assert.Equal(["A / 2 — A 2", "A / 2 — A 2 | 5 — A 5", "A / 2 — A 2 | 5 — A 5 | 3 — A 3"], rig.Published);
    }

    [Fact]
    public void Show_table_name_and_Show_roll_value_still_apply_live_to_a_chain_on_the_overlay()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 4), OverlayDisplayMode.CompleteResult, autoFollow: true);
        rig.Session.RollCommand.Execute(null);

        rig.Overlay.SetPresentation(showTableName: false, showRollValue: false);
        Assert.Contains("\"heading\":\"\"", Encoding.UTF8.GetString(rig.Overlay.StateJson));
        Assert.Equal("A / 1 — A 1 | B 4", Shown(rig.Overlay));
        rig.Overlay.SetPresentation(showTableName: true, showRollValue: false);
        Assert.Equal("A / 1 — A 1 | B: B 4", Shown(rig.Overlay));
    }

    // ---- Complete result: when nothing is published ------------------------------------------------------------------------

    [Fact]
    public void Back_keeps_the_previous_chain_on_the_overlay_until_the_next_result()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 1, 3, 5), OverlayDisplayMode.CompleteResult, autoFollow: true);
        rig.Session.RollCommand.Execute(null);
        var count = rig.Published.Count;

        rig.Session.ReturnToOriginalCommand.Execute(null);
        Assert.True(rig.Session.CompleteResult.IsPrevious);
        Assert.Equal(count, rig.Published.Count);
        Assert.Equal("A / 1 — A 1 | B: 1 — B 1 | C: 3 — C 3", Shown(rig.Overlay));

        rig.Session.RollCommand.Execute(null);               // a single roll on the original table: shown on its own
        Assert.Equal("A / 5 — A 5", Shown(rig.Overlay));
    }

    [Fact]
    public void Opening_or_closing_the_panel_never_publishes_and_a_closed_panel_still_publishes_the_chain()
    {
        var (w, a, _, _) = Chain();
        var rig = Open(w, a, new SequenceDice(1, 4), OverlayDisplayMode.CompleteResult);
        rig.Session.RollCommand.Execute(null);

        rig.Session.IsCompleteResultExpanded = false;
        rig.Session.IsCompleteResultExpanded = true;
        rig.Session.IsCompleteResultExpanded = false;
        Assert.Equal(["A / 1 — A 1"], rig.Published);

        Follow(rig.Session);
        rig.Session.RollCommand.Execute(null);
        Assert.Equal("A / 1 — A 1 | B: 4 — B 4", Shown(rig.Overlay));
    }

    [Fact]
    public async Task With_dddice_nothing_is_published_while_the_dice_are_in_the_air_and_each_result_once_they_settle()
    {
        var (w, a, _, _) = Chain();
        var roller = new FakeDddiceRoller();
        var rig = Open(w, a, new DddiceDiceProvider(roller), OverlayDisplayMode.CompleteResult, autoFollow: true);

        var roll = rig.Session.RollAsync();
        await Wait.Until(() => roller.IsRollPending, "A's dice to be thrown");
        Assert.Empty(rig.Published);

        roller.Settle(("d6", 1));
        await Wait.Until(() => rig.Published.Count == 1 && roller.IsRollPending, "A's result, then B's dice");
        Assert.Equal(["A / 1 — A 1"], rig.Published);         // B is rolling: "rolling…" in the app, nothing new on the overlay

        roller.Settle(("d6", 1));
        await Wait.Until(() => rig.Published.Count == 2 && roller.IsRollPending, "B's result, then C's dice");
        roller.Settle(("d6", 2));
        await roll;

        Assert.Equal(["A / 1 — A 1", "A / 1 — A 1 | B: 1 — B 1", "A / 1 — A 1 | B: 1 — B 1 | C: 2 — C 2"], rig.Published);
    }

    [Fact]
    public async Task Stop_mid_chain_leaves_the_chain_so_far()
    {
        var (w, a, _, _) = Chain();
        var roller = new FakeDddiceRoller();
        var rig = Open(w, a, new DddiceDiceProvider(roller), OverlayDisplayMode.CompleteResult, autoFollow: true);
        var roll = rig.Session.RollAsync();
        await Wait.Until(() => roller.IsRollPending, "A's dice");
        roller.Settle(("d6", 1));
        await Wait.Until(() => rig.Published.Count == 1 && roller.IsRollPending, "B's dice");

        rig.Session.StopCommand.Execute(null);
        await roll;

        Assert.Equal(["A / 1 — A 1"], rig.Published);
        Assert.Equal("→ B — not rolled (auto-follow stopped)", rig.Session.CompleteResultPendingText);
    }

    // ---- Clear, Test, inline dice and stale results ---------------------------------------------------------------------------

    [Fact]
    public void An_inline_roll_on_the_latest_result_updates_the_chain_in_place()
    {
        var (w, a, _, c) = Chain();
        w.Text(c, 3, "Gain 1d4 Armor.");
        var rig = Open(w, a, new SequenceDice(1, 1, 3, 2), OverlayDisplayMode.CompleteResult, autoFollow: true);
        rig.Session.RollCommand.Execute(null);
        var generation = rig.Overlay.State.Generation;

        rig.Session.Current.Outcomes[^1].Lines[0].InlineActions.Single().RollCommand.Execute(null);

        Assert.Equal("A / 1 — A 1 | B: 1 — B 1 | C: 3 — Gain 2 Armor.", Shown(rig.Overlay));
        Assert.Equal(generation, rig.Overlay.State.Generation);   // the same result, updated: not a new one
    }

    [Fact]
    public void Clear_and_Show_Test_Result_are_never_undone_by_Back_the_panel_or_an_inline_roll()
    {
        var (w, a, _, c) = Chain();
        w.Text(c, 3, "Gain 1d4 Armor.");
        var rig = Open(w, a, new SequenceDice(1, 1, 3, 2, 2), OverlayDisplayMode.CompleteResult, autoFollow: true);
        rig.Session.RollCommand.Execute(null);
        var inline = rig.Session.Current.Outcomes[^1].Lines[0].InlineActions.Single();

        rig.Overlay.Clear();
        inline.RollCommand.Execute(null);
        rig.Session.IsCompleteResultExpanded = false;
        Assert.Equal("(empty)", Shown(rig.Overlay));

        rig.Overlay.ShowTest();
        inline.RollCommand.Execute(null);
        rig.Session.ReturnToOriginalCommand.Execute(null);
        Assert.Equal("Gidubba Overlay Test / 12 — Your streaming overlay is working.", Shown(rig.Overlay));
    }

    [Fact]
    public void A_newer_result_from_elsewhere_is_never_replaced_by_an_older_chain()
    {
        var (w, a, _, c) = Chain();
        w.Text(c, 3, "Gain 1d4 Armor.");
        var rig = Open(w, a, new SequenceDice(1, 1, 3, 2), OverlayDisplayMode.CompleteResult, autoFollow: true);
        rig.Session.RollCommand.Execute(null);
        var other = new RollViewModel(w.Add("Other"), new SequenceDice(6), w.Load, overlay: rig.Overlay);
        other.RollCommand.Execute(null);                      // another Roll screen (another table) publishes after it

        rig.Session.Current.Outcomes[^1].Lines[0].InlineActions.Single().RollCommand.Execute(null);

        Assert.Equal("Other / 6 — Other 6", Shown(rig.Overlay));
    }

    // ---- switching "Overlay shows" -------------------------------------------------------------------------------------------

    [Fact]
    public void Switching_mode_publishes_nothing_keeps_what_is_shown_and_applies_from_the_next_result()
    {
        var (w, a, _, c) = Chain();
        w.Text(c, 3, "Gain 1d4 Armor.");
        var rig = Open(w, a, new SequenceDice(1, 1, 3, 2, 1, 4), OverlayDisplayMode.CompleteResult, autoFollow: true);
        rig.Session.RollCommand.Execute(null);
        var count = rig.Published.Count;

        rig.Overlay.SetDisplayMode(OverlayDisplayMode.LatestRoll);
        Assert.Equal(count, rig.Published.Count);

        // The chain on the overlay stays a chain when its latest result's inline dice are rolled …
        rig.Session.Current.Outcomes[^1].Lines[0].InlineActions.Single().RollCommand.Execute(null);
        Assert.Equal("A / 1 — A 1 | B: 1 — B 1 | C: 3 — Gain 2 Armor.", Shown(rig.Overlay));

        // … and the next result is shown the new way.
        rig.Session.ReturnToOriginalCommand.Execute(null);
        rig.Session.RollCommand.Execute(null);
        Assert.Equal("B / 4 — B 4", Shown(rig.Overlay));   // auto-follow: A 1 → B 4; Latest roll shows B's result alone
    }

    [Fact]
    public void A_result_published_alone_stays_alone_when_the_mode_changes_before_its_inline_roll()
    {
        var (w, a, _, c) = Chain();
        w.Text(c, 3, "Gain 1d4 Armor.");
        var rig = Open(w, a, new SequenceDice(1, 1, 3, 2), OverlayDisplayMode.LatestRoll, autoFollow: true);
        rig.Session.RollCommand.Execute(null);

        rig.Overlay.SetDisplayMode(OverlayDisplayMode.CompleteResult);
        rig.Session.Current.Outcomes[^1].Lines[0].InlineActions.Single().RollCommand.Execute(null);

        Assert.Equal("C / 3 — Gain 2 Armor.", Shown(rig.Overlay));
    }
}
