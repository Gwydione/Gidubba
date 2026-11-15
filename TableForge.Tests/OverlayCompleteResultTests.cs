using System.Text;
using System.Text.Json;
using TableForge.Domain;
using TableForge.Results;
using TableForge.Streaming;
using static TableForge.Results.CompleteResultStepKind;

namespace TableForge.Tests;

/// <summary>
/// Gidubba 1.6.0 "Overlay shows: Complete result": <see cref="OverlayCompleteResult"/> turns the shared builder's output into
/// the overlay's existing payload (the page and <c>/state</c>'s shape are unchanged), and trims a long chain to the first part,
/// one "…" line and the latest parts.
/// </summary>
public class OverlayCompleteResultTests
{
    private static CompleteResultLineInput L(string text, string heading = "", string linkTo = "") =>
        new(heading, text) { Link = linkTo.Length > 0 ? CompleteResultLink.Resolved : CompleteResultLink.None, LinkName = linkTo };

    private static CompleteResultRollInput R(int action, string roll, params CompleteResultLineInput[] lines) => new(action, roll, lines);

    private static CompleteResultStepInput First(string table, params CompleteResultRollInput[] rolls) => new(table, Original, rolls);

    private static CompleteResultStepInput Next(string table, params CompleteResultRollInput[] rolls) =>
        new(table, Linked, rolls) { OpenedFrom = new CompleteResultLineRef(0, 0) };

    private static CompleteResult Build(params CompleteResultStepInput[] steps) => CompleteResultBuilder.Build(new(steps));

    /// <summary>The 2D6 Dungeon example: L1SR 11 (two named result sets) → GOT1 2.</summary>
    private static CompleteResult Shrine() => Build(
        First("L1SR - Level 1 Small Rooms", R(1, "11", L("WALL SHRINE", "Room"),
            L("In the wall is a small shrine. It is dedicated to a god, roll on GOT1. Gain 1 FP if applied correctly.", "Description / Instructions", "GOT1 - God Table 1"))),
        Next("GOT1 - God Table 1", R(2, "2", L("Intuneric the Murk"))));

    /// <summary>Each line as the page shows it: "heading: text", with a part's roll value as "*4* — " (bold) starting its text.</summary>
    private static string[] Shown(OverlayResult result, bool tableName = true, bool rollValue = true) =>
        result.Lines.Select(l => OverlayPublisher.Shown(l, tableName, rollValue)).Select(Line).ToArray();

    private static string Line(OverlayLine l)
    {
        var text = string.Concat(l.Segments.Select((s, i) => i == 0 && s.Bold && l.Segments.Count > 1 && l.Segments[1].Text == OverlayPublisher.RollSeparator ? $"*{s.Text}*" : s.Text));
        return l.Heading.Length > 0 ? $"{l.Heading}: {text}" : text;
    }

    /// <summary>A chain of <paramref name="tables"/> linked tables, each result <paramref name="lines"/> lines of <paramref name="length"/> characters.</summary>
    private static CompleteResult LongChain(int tables, int lines = 1, int length = 10)
    {
        var steps = new List<CompleteResultStepInput>();
        for (var t = 0; t < tables; t++)
        {
            var text = Enumerable.Range(0, lines).Select(n => L($"T{t}.{n} ".PadRight(length, 'x'), lines > 1 ? $"S{n}" : "", t < tables - 1 && n == 0 ? $"T{t + 1}" : "")).ToArray();
            steps.Add(t == 0 ? First("T0", R(1, "1", text)) : Next($"T{t}", R(t + 1, $"{t + 1}", text)));
        }
        return Build([.. steps]);
    }

    // ---- what is shown ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_part_is_shown_as_Latest_roll_shows_it_and_later_parts_are_labelled_with_their_table_and_roll()
    {
        var result = OverlayCompleteResult.For(Shrine());

        Assert.Equal(("L1SR - Level 1 Small Rooms", "11"), (result.TableName, result.RollValue));   // the page's own table name and roll value
        Assert.Equal([
            "Room: WALL SHRINE",
            "Description / Instructions: In the wall is a small shrine. It is dedicated to a god, roll on GOT1. Gain 1 FP if applied correctly.",
            "GOT1 - God Table 1: *2* — Intuneric the Murk",
        ], Shown(result));
        Assert.False(result.Omitted);
        Assert.Null(result.Lines[0].Part);
        Assert.Null(result.Lines[1].Part);
        Assert.Equal(new OverlayPartLabel("GOT1 - God Table 1", "2"), result.Lines[2].Part);
    }

    [Fact]
    public void Show_table_name_and_Show_roll_value_decide_the_part_label_and_named_sets_keep_their_label()
    {
        var result = OverlayCompleteResult.For(Build(
            First("Area", R(1, "1", L("A ruined watchtower.", "", "Loot"))),
            Next("Loot", R(2, "4", L("A rusted key.", "Treasure"), L("Worth 5 sp.", "Value")))));

        Assert.Equal(["A ruined watchtower.", "Loot · Treasure: *4* — A rusted key.", "Value: Worth 5 sp."], Shown(result, true, true));
        Assert.Equal(["A ruined watchtower.", "Loot · Treasure: A rusted key.", "Value: Worth 5 sp."], Shown(result, true, false));
        Assert.Equal(["A ruined watchtower.", "Treasure: *4* — A rusted key.", "Value: Worth 5 sp."], Shown(result, false, true));
        Assert.Equal(["A ruined watchtower.", "Treasure: A rusted key.", "Value: Worth 5 sp."], Shown(result, false, false));
    }

    [Fact]
    public void Only_result_text_is_shown_never_captions_pending_lines_or_notes()
    {
        // Clamped, entered and "from roll" captions, a link not followed, and a pending table: none of it reaches the overlay.
        var complete = CompleteResultBuilder.Build(new([
            First("Area", new CompleteResultRollInput(1, "21", [L("Watchtower.", "", "Occupants")]) { ResolvedAs = 20 }),
            Next("Occupants", new CompleteResultRollInput(2, "3", [L("Bandits.", "", "Loot")]) { Entered = true }),
            Next("Loot"),
        ]));
        Assert.NotNull(complete.Pending);

        var json = Encoding.UTF8.GetString(OverlayPublisher.Serialize(new OverlayState("i", 1, 1, OverlayCompleteResult.For(complete), false, true, true)));

        Assert.Equal(["Watchtower.", "Occupants: *3* — Bandits."], Shown(OverlayCompleteResult.For(complete)));
        foreach (var never in new[] { "resolved as", "entered", "not rolled", "→", "Complete Result", "not followed" })
            Assert.DoesNotContain(never, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_multi_roll_shows_every_result_and_names_the_table_only_where_it_changes()
    {
        var weather = OverlayCompleteResult.For(Build(First("Weather",
            new CompleteResultRollInput(1, "2", [L("Drizzle.")]) { BatchPosition = 1 },
            new CompleteResultRollInput(1, "5", [L("Heavy rain.")]) { BatchPosition = 2 },
            new CompleteResultRollInput(1, "3", [L("Low fog.")]) { BatchPosition = 3 })));

        Assert.Equal(("Weather", "2"), (weather.TableName, weather.RollValue));
        Assert.Equal(["Drizzle.", "*5* — Heavy rain.", "*3* — Low fog."], Shown(weather));
        Assert.Equal(["Drizzle.", "Heavy rain.", "Low fog."], Shown(weather, true, false));
    }

    [Fact]
    public void Rolled_inline_dice_and_bold_and_italic_reach_the_overlay_as_the_Complete_Result_shows_them()
    {
        const string text = "Gain 1d4 Armor, maybe.";
        var styles = TextStyles.FromRuns([new StyleRun(0, 4, TextStyle.Bold), new StyleRun(9, 5, TextStyle.Italic)], text.Length);
        var line = new CompleteResultLineInput("", text)
        {
            Styles = styles,
            InlineMatches = Dice.InlineDiceDetector.FindAll(text),
            InlineValues = new Dictionary<DiceExpression, int> { [DiceExpression.Parse("1d4")] = 3 },
            Link = CompleteResultLink.Resolved,
            LinkName = "Loot",
        };
        var complete = Build(First("Area", R(1, "1", line)), Next("Loot", R(2, "6", L("Nothing."))));
        var result = OverlayCompleteResult.For(complete);

        Assert.Equal("Gain 3 Armor, maybe.", result.Lines[0].Text);
        Assert.Equal(complete.Pieces[0].Lines[0].Segments.Select(OverlaySegment.From), result.Lines[0].Segments);
        Assert.Contains(result.Lines[0].Segments, s => s.Bold);
        Assert.Contains(result.Lines[0].Segments, s => s.Italic);
    }

    [Fact]
    public void A_No_Match_message_is_shown_like_any_result()
    {
        var result = OverlayCompleteResult.For(Build(
            First("Area", R(1, "1", L("Watchtower.", "", "Occupants"))),
            Next("Occupants", R(2, "23", new CompleteResultLineInput("", "No entry covers 23.", IsProblem: true)))));
        Assert.Equal(["Watchtower.", "Occupants: *23* — No entry covers 23."], Shown(result));
    }

    [Fact]
    public void The_json_has_exactly_the_shape_existing_overlay_pages_read()
    {
        var json = OverlayPublisher.Serialize(new OverlayState("i", 3, 2, OverlayCompleteResult.For(Shrine()), false, true, true));
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal(["instance", "version", "empty", "showTableName", "showRollValue", "truncated", "tableName", "rollValue", "lines"],
            r.EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var line in r.GetProperty("lines").EnumerateArray())
            Assert.Equal(["heading", "segments"], line.EnumerateObject().Select(p => p.Name).ToArray());
        var part = r.GetProperty("lines")[2];
        Assert.Equal("GOT1 - God Table 1", part.GetProperty("heading").GetString());
        // The part's roll value starts its text, bold and followed by " — ", as the page shows the first line's roll value.
        var segments = part.GetProperty("segments").EnumerateArray().ToArray();
        Assert.Equal(("2", true), (segments[0].GetProperty("text").GetString(), segments[0].GetProperty("bold").GetBoolean()));
        Assert.Equal((" \u2014 ", false), (segments[1].GetProperty("text").GetString(), segments[1].TryGetProperty("bold", out _)));
        Assert.Equal("Intuneric the Murk", segments[2].GetProperty("text").GetString());
    }

    [Fact]
    public void A_Complete_Result_with_nothing_in_it_is_refused()
    {
        Assert.Throws<ArgumentException>(() => OverlayCompleteResult.For(CompleteResult.None));
    }

    // ---- long chains -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_chain_within_the_overlay_limits_is_shown_whole()
    {
        var result = OverlayCompleteResult.For(LongChain(tables: 8, lines: 2));   // 16 lines
        Assert.Equal(16, result.Lines.Count);
        Assert.False(result.Omitted);
        Assert.DoesNotContain(result.Lines, l => l.Text == OverlayCompleteResult.OmittedMarker);
        Assert.False(OverlayLimits.Apply(result).Truncated);
    }

    [Fact]
    public void Too_many_lines_keep_the_first_part_one_marker_and_as_many_latest_parts_as_fit()
    {
        // 8 tables × 3 lines = 24 lines > 20: the first part (3), "…" (1) and the latest five parts (15) make 19; a sixth would be 22.
        var result = OverlayCompleteResult.For(LongChain(tables: 8, lines: 3));

        Assert.True(result.Omitted);
        var texts = result.Lines.Select(l => l.Text.Split(' ')[0]).ToArray();
        Assert.Equal(["T0.0", "T0.1", "T0.2", "…", "T3.0", "T3.1", "T3.2", "T4.0", "T4.1", "T4.2", "T5.0", "T5.1", "T5.2", "T6.0", "T6.1", "T6.2", "T7.0", "T7.1", "T7.2"], texts);
        Assert.Single(result.Lines, l => l.Text == "…");
        Assert.Equal(new OverlayPartLabel("T3", "4"), result.Lines[4].Part);         // the first part after the gap names its table

        var (limited, truncated) = OverlayLimits.Apply(result);
        Assert.True(truncated);                                                      // reported, as any cut is
        Assert.Equal(result.Lines.Select(l => l.Text), limited.Lines.Select(l => l.Text));   // and nothing more is cut
    }

    [Fact]
    public void Too_much_text_keeps_the_first_part_and_the_latest_parts_too()
    {
        // 8 parts of 900 characters: the first, "…" and the latest three fit 4000 (3601); a fourth would not.
        var result = OverlayCompleteResult.For(LongChain(tables: 8, lines: 1, length: 900));
        Assert.Equal(["T0.0", "…", "T5.0", "T6.0", "T7.0"], result.Lines.Select(l => l.Text.Split(' ')[0]).ToArray());
        Assert.Equal(result.Lines.Select(l => l.Text), OverlayLimits.Apply(result).Result.Lines.Select(l => l.Text));
    }

    [Fact]
    public void The_latest_result_is_never_left_out_even_when_the_first_cannot_stay_beside_it()
    {
        // The latest result alone is longer than the limit: it is shown (its end cut by the limits), after the marker.
        var steps = new[]
        {
            First("Area", R(1, "1", L(new string('a', 3000), "", "Loot"))),
            Next("Loot", R(2, "2", L(new string('b', 4500)))),
        };
        var result = OverlayCompleteResult.For(Build(steps));
        Assert.Equal(["…", new string('b', 4500)], result.Lines.Select(l => l.Text).ToArray());
        Assert.Equal(("Area", "1"), (result.TableName, result.RollValue));          // still introduced by the chain's first table

        var (limited, truncated) = OverlayLimits.Apply(result);
        Assert.True(truncated);
        Assert.StartsWith("bbbb", limited.Lines[1].Text);
        Assert.EndsWith("…", limited.Lines[1].Text);
    }

    [Fact]
    public void A_first_part_too_big_to_stay_beside_the_latest_is_left_out_behind_the_marker()
    {
        var lines = Enumerable.Range(0, 25).Select(n => L($"Line {n}", $"Set {n}")).ToArray();
        var weather = Build(First("W", new CompleteResultRollInput(1, "1", lines) { BatchPosition = 1 }, new CompleteResultRollInput(1, "2", [L("Second.")]) { BatchPosition = 2 }));
        var result = OverlayCompleteResult.For(weather);
        Assert.Equal("…", result.Lines[0].Text);                                     // the first part (25 lines) cannot stay
        Assert.Equal("Second.", result.Lines[1].Text);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void Too_many_styled_stretches_also_count()
    {
        // Each part: 1 line, 120 bold/plain stretches. 500 allows the first, "…" and the latest three (481), not four (601).
        var steps = new List<CompleteResultStepInput>();
        for (var t = 0; t < 6; t++)
        {
            var text = string.Concat(Enumerable.Repeat("ab", 120));
            var styles = TextStyles.FromRuns(Enumerable.Range(0, 60).Select(i => new StyleRun(i * 4, 2, TextStyle.Bold)), text.Length);
            var line = new CompleteResultLineInput("", text) { Styles = styles, Link = t < 5 ? CompleteResultLink.Resolved : CompleteResultLink.None, LinkName = $"T{t + 1}" };
            steps.Add(t == 0 ? First("T0", R(1, "1", line)) : Next($"T{t}", R(t + 1, $"{t + 1}", line)));
        }
        var complete = Build([.. steps]);
        Assert.Equal(120, complete.Pieces[0].Lines[0].Segments.Count);

        var result = OverlayCompleteResult.For(complete);
        Assert.Equal([null, null, "T3", "T4", "T5"], result.Lines.Select(l => l.Part?.TableName).ToArray());
        Assert.Equal("…", result.Lines[1].Text);
        Assert.Equal(481, result.Lines.Sum(l => l.Segments.Count));
        Assert.Equal(result.Lines.Select(l => l.Segments.Count), OverlayLimits.Apply(result).Result.Lines.Select(l => l.Segments.Count));   // nothing more is cut
    }
}
