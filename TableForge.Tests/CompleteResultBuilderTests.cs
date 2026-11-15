using System.Diagnostics;
using System.IO;
using System.Reflection;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.Results;
using static TableForge.Results.CompleteResultStepKind;

namespace TableForge.Tests;

/// <summary>
/// Gidubba 1.6 Complete Roll Results: the shared, UI-free <see cref="CompleteResultBuilder"/>, fed plain records exactly as any
/// front end (WPF today, the Linux/Avalonia port later) would feed it.
/// </summary>
public class CompleteResultBuilderTests
{
    private const string CrLf = "\r\n";

    private static CompleteResultLineInput L(string text, string heading = "", CompleteResultLink link = CompleteResultLink.None,
        string linkName = "") => new(heading, text) { Link = link, LinkName = linkName };

    private static CompleteResultLineInput Lk(string text, string to, string heading = "") => L(text, heading, CompleteResultLink.Resolved, to);

    private static CompleteResultLineInput Problem(string text, string heading = "") => new(heading, text, IsProblem: true);

    private static CompleteResultRollInput R(int action, string roll, params CompleteResultLineInput[] lines) => new(action, roll, lines);

    private static CompleteResultRollInput Batch(int action, int position, string roll, params CompleteResultLineInput[] lines) =>
        new(action, roll, lines) { BatchPosition = position };

    private static CompleteResultStepInput S(string table, CompleteResultStepKind kind, params CompleteResultRollInput[] rolls) =>
        new(table, kind, rolls);

    private static CompleteResultStepInput Next(string table, int fromRoll, int fromLine, params CompleteResultRollInput[] rolls) =>
        new(table, Linked, rolls) { OpenedFrom = new CompleteResultLineRef(fromRoll, fromLine) };

    private static CompleteResult Build(params CompleteResultStepInput[] steps) => CompleteResultBuilder.Build(new(steps));

    private static CompleteResult BuildInProgress(params CompleteResultStepInput[] steps) => CompleteResultBuilder.Build(new(steps, InProgress: true));

    private static string[] Texts(CompleteResult result) => result.Pieces.SelectMany(p => p.Lines).Select(l => l.Text).ToArray();

    private static string[] Captions(CompleteResult result) => result.Pieces.Select(p => p.Caption).ToArray();

    // ---- nothing to assemble -----------------------------------------------------------------------------------------------

    [Fact]
    public void An_empty_trail_or_an_unrolled_table_has_no_complete_result()
    {
        Assert.Same(CompleteResult.None, CompleteResultBuilder.Build(new([])));
        var unrolled = Build(S("Area", Original));
        Assert.Empty(unrolled.Pieces);
        Assert.False(unrolled.IsVisible);
        Assert.Equal("", unrolled.ToPlainText(CrLf));
    }

    [Fact]
    public void A_single_roll_is_assembled_but_not_shown_because_it_would_only_repeat_the_result_below()
    {
        var result = Build(S("Area", Original, R(1, "5", L("Ruined watchtower."))));
        Assert.Single(result.Pieces);
        Assert.False(result.IsVisible);
        Assert.Equal("Ruined watchtower.", result.ToPlainText(CrLf));
    }

    [Fact]
    public void A_single_roll_with_several_result_sets_is_still_one_roll_and_not_shown()
    {
        var result = Build(S("NPC", Original, R(1, "3", L("Mira", "Name"), L("Brave", "Trait"), L("Revenge", "Goal"))));
        Assert.False(result.IsVisible);
        Assert.Equal(["Mira", "Brave", "Revenge"], Texts(result));
        Assert.Equal($"Name: Mira{CrLf}Trait: Brave{CrLf}Goal: Revenge", result.ToPlainText(CrLf));
    }

    // ---- multi-roll and repeated actions --------------------------------------------------------------------------------

    [Fact]
    public void A_multi_roll_action_on_one_table_is_shown_with_every_result_in_order()
    {
        var result = Build(S("Traits", Original,
            Batch(1, 1, "2", L("Brave")), Batch(1, 2, "9", L("Greedy")), Batch(1, 3, "4", L("Curious"))));
        Assert.True(result.IsVisible);
        Assert.False(result.SpansTables);
        Assert.Equal(["Traits · roll 1 · 2", "Traits · roll 2 · 9", "Traits · roll 3 · 4"], Captions(result));
        Assert.Equal($"Brave{CrLf}Greedy{CrLf}Curious", result.ToPlainText(CrLf));
    }

    [Fact]
    public void Only_the_latest_action_on_the_original_table_counts()
    {
        var result = Build(S("Traits", Original,
            Batch(1, 1, "2", L("Brave")), Batch(1, 2, "9", L("Greedy")),
            R(2, "4", L("Curious"))));
        Assert.Equal(["Curious"], Texts(result));
        Assert.False(result.IsVisible); // one roll again
    }

    // ---- linked chains --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_linked_chain_is_assembled_in_resolution_order()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruined watchtower.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "11", Lk("Bandits, nervous.", "Loot"))),
            Next("Loot", 0, 0, R(3, "4", L("30 gp."))));

        Assert.True(result.IsVisible);
        Assert.True(result.SpansTables);
        Assert.False(result.IsPrevious);
        Assert.Null(result.Pending);
        Assert.Equal(["Area · 5", "Occupants · 11", "Loot · 4"], Captions(result));
        Assert.Equal($"Ruined watchtower.{CrLf}Bandits, nervous.{CrLf}30 gp.", result.ToPlainText(CrLf));
        Assert.Equal([0, 1, 2], result.Pieces.Select(p => p.StepIndex));
    }

    [Fact]
    public void Rolling_a_linked_table_again_replaces_its_part_in_place()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruined watchtower.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "3", L("Wolves.")), R(3, "11", L("Bandits."))));
        Assert.Equal(["Ruined watchtower.", "Bandits."], Texts(result));
        Assert.Equal(["Area · 5", "Occupants · 11"], Captions(result));
    }

    [Fact]
    public void A_linked_table_rolled_again_several_times_contributes_every_result_of_that_action_in_the_same_place()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruined watchtower.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "3", L("Wolves.")), Batch(3, 1, "11", L("Bandits.")), Batch(3, 2, "8", L("Ghosts."))));
        Assert.Equal(["Ruined watchtower.", "Bandits.", "Ghosts."], Texts(result));
        Assert.Equal(["Area · 5", "Occupants · roll 1 · 11", "Occupants · roll 2 · 8"], Captions(result));
    }

    [Fact]
    public void A_table_followed_from_one_result_of_a_multi_roll_comes_after_the_whole_action_and_says_which_roll()
    {
        var result = Build(
            S("Area", Original,
                Batch(1, 1, "2", Lk("Bridge.", "Loot")), Batch(1, 2, "9", Lk("Chapel.", "Loot")), Batch(1, 3, "4", L("Stones."))),
            Next("Loot", 1, 0, R(2, "4", L("30 gp."))));
        Assert.Equal(["Bridge.", "Chapel.", "Stones.", "30 gp."], Texts(result));
        Assert.Equal("Loot · 4 (from roll 2)", result.Pieces[^1].Caption);
    }

    [Fact]
    public void A_table_followed_from_one_of_several_linked_result_sets_names_the_set()
    {
        var named = Build(
            S("Room", Original, R(1, "3", Lk("A chest.", "Loot", "Treasure"), Lk("A trap.", "Traps", "Hazard"))),
            Next("Traps", 0, 1, R(2, "2", L("Darts."))));
        Assert.Equal("Traps · 2 (from Hazard)", named.Pieces[^1].Caption);

        var unnamed = Build(
            S("Room", Original, R(1, "3", Lk("A chest.", "Loot"), Lk("A trap.", "Traps"))),
            Next("Traps", 0, 1, R(2, "2", L("Darts."))));
        Assert.Equal("Traps · 2 (from result set 2)", unnamed.Pieces[^1].Caption);
    }

    [Fact]
    public void A_table_followed_from_the_only_link_needs_no_origin()
    {
        var result = Build(
            S("Room", Original, R(1, "3", Lk("A chest.", "Loot", "Treasure"), L("Dusty.", "Feel"))),
            Next("Loot", 0, 0, R(2, "2", L("30 gp."))));
        Assert.Equal("Loot · 2", result.Pieces[^1].Caption);
    }

    [Fact]
    public void A_table_reached_twice_in_one_chain_contributes_once_for_each_visit()
    {
        var result = Build(
            S("Weather", Original, R(1, "6", Lk("Storm; roll again.", "Weather"))),
            Next("Weather", 0, 0, R(2, "2", L("Clear."))));
        Assert.Equal(["Storm; roll again.", "Clear."], Texts(result));
    }

    // ---- pending and unfinished ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, false, CompleteResultPendingReason.NotRolled, "→ Loot — not rolled yet")]
    [InlineData(false, false, true, CompleteResultPendingReason.Rolling, "→ Loot — rolling…")]
    [InlineData(true, false, false, CompleteResultPendingReason.ChooseResult, "→ Loot — choose a result")]
    [InlineData(true, true, true, CompleteResultPendingReason.ChooseResult, "→ Loot — choose a result")]
    [InlineData(false, true, false, CompleteResultPendingReason.AutoFollowStopped, "→ Loot — not rolled (auto-follow stopped)")]
    [InlineData(false, true, true, CompleteResultPendingReason.Rolling, "→ Loot — rolling…")]
    public void A_linked_table_without_a_result_is_shown_as_pending_and_never_copied(bool manual, bool auto, bool inProgress,
        CompleteResultPendingReason reason, string text)
    {
        var steps = new[]
        {
            S("Area", Original, R(1, "5", Lk("Ruined watchtower.", "Loot"))),
            Next("Loot", 0, 0) with { IsManualSelection = manual, IsAutoFollowed = auto },
        };
        var result = CompleteResultBuilder.Build(new(steps, inProgress));

        Assert.True(result.IsVisible); // one roll, but it reaches a linked table
        Assert.Equal(reason, result.Pending!.Reason);
        Assert.Equal(text, result.Pending.Text);
        Assert.Equal("Ruined watchtower.", result.ToPlainText(CrLf));
        Assert.All(result.Pieces.SelectMany(p => p.Lines), l => Assert.False(l.HasNote)); // the pending line says where it went
    }

    [Fact]
    public void The_last_result_notes_links_it_did_not_or_could_not_follow_but_they_are_never_copied()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruins.", "Occupants"), Lk("Fog.", "Weather", "Sky"))),
            Next("Occupants", 0, 0, R(2, "11",
                Lk("Bandits.", "Loot", "Who"), L("Hungry.", "Mood", CompleteResultLink.Unresolved, "Moods"),
                L("Lost.", "Where", CompleteResultLink.Missing), L("Asleep.", "State"))));

        var earlier = result.Pieces[0].Lines;
        Assert.All(earlier, l => Assert.Equal("", l.Note)); // only the chain's last result says what is left
        var last = result.Pieces[1].Lines;
        Assert.Equal(["→ Loot (not followed)", "⚠ link unavailable", "⚠ link unavailable", ""], last.Select(l => l.Note));
        Assert.DoesNotContain("→", result.ToPlainText(CrLf));
        Assert.DoesNotContain("⚠", result.ToPlainText(CrLf));
    }

    [Fact]
    public void Link_notes_wait_while_anything_is_still_under_way()
    {
        var result = BuildInProgress(S("Area", Original, R(1, "5", Lk("Ruins.", "Occupants"))));
        Assert.Equal("", result.Pieces[0].Lines[0].Note);
    }

    // ---- Back to the original table --------------------------------------------------------------------------------------

    [Fact]
    public void After_Return_the_previous_chain_stays_until_the_next_result()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruins.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "11", L("Bandits."))),
            S("Area", Return));
        Assert.True(result.IsPrevious);
        Assert.True(result.IsVisible);
        Assert.Equal(["Ruins.", "Bandits."], Texts(result));
        Assert.Equal($"Ruins.{CrLf}Bandits.", result.ToPlainText(CrLf));
    }

    [Fact]
    public void The_first_result_after_Return_starts_a_new_chain()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruins.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "11", L("Bandits."))),
            S("Area", Return, R(3, "2", Lk("Bridge.", "Occupants"))),
            Next("Occupants", 0, 0, R(4, "3", L("Wolves."))));
        Assert.False(result.IsPrevious);
        Assert.Equal(["Bridge.", "Wolves."], Texts(result));
        Assert.Equal([2, 3], result.Pieces.Select(p => p.StepIndex));
    }

    [Fact]
    public void A_single_roll_after_Return_replaces_the_previous_chain_and_hides_the_panel()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruins.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "11", L("Bandits."))),
            S("Area", Return, R(3, "6", L("Nothing here."))));
        Assert.Equal(["Nothing here."], Texts(result));
        Assert.False(result.IsVisible);
    }

    // ---- text: problems, headings, inline dice, line breaks ------------------------------------------------------------

    [Fact]
    public void No_Match_and_Ambiguous_messages_are_copied_with_their_table_name_and_ordinary_text_without()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruins.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "23", Problem("No entry covers 23.", "Who"), Problem("No entry covers 23."), L("Hungry.", "Mood"))));

        Assert.True(result.Pieces[1].Lines[0].IsProblem);
        Assert.Equal(
            $"Ruins.{CrLf}Occupants (Who): No entry covers 23.{CrLf}Occupants: No entry covers 23.{CrLf}Mood: Hungry.",
            result.ToPlainText(CrLf));
    }

    [Fact]
    public void Only_inline_dice_that_were_actually_rolled_are_substituted_and_formatting_is_kept()
    {
        const string text = "You find 2d6 gold and d4 gems.";
        var matches = InlineDiceDetector.FindAll(text);
        var bold = TextStyles.FromRuns([new StyleRun(9, 3, TextStyle.Bold)], text.Length); // "2d6"
        var line = new CompleteResultLineInput("", text)
        {
            Styles = bold, InlineMatches = matches,
            InlineValues = new Dictionary<DiceExpression, int> { [DiceExpression.Parse("2d6")] = 7 },
        };
        var result = Build(S("Loot", Original, R(1, "3", line)));

        var resolved = result.Pieces[0].Lines[0];
        Assert.Equal("You find 7 gold and d4 gems.", resolved.Text); // d4 was never rolled: it stays exactly as written
        Assert.Contains(resolved.Segments, s => s.Text == "7" && s.IsBold);
        Assert.Equal("You find 7 gold and d4 gems.", result.ToPlainText(CrLf));
    }

    [Fact]
    public void Line_breaks_inside_a_result_follow_the_chosen_newline()
    {
        var result = Build(
            S("Area", Original, R(1, "5", Lk("Ruins.\nCold wind.", "Occupants"))),
            Next("Occupants", 0, 0, R(2, "1", L("Bandits.\r\nArmed."))));
        Assert.Equal("Ruins.\r\nCold wind.\r\nBandits.\r\nArmed.", result.ToPlainText(CrLf));
        Assert.Equal("Ruins.\nCold wind.\nBandits.\nArmed.", result.ToPlainText("\n"));
    }

    [Fact]
    public void Captions_say_when_a_roll_was_clamped_or_entered()
    {
        var result = Build(
            S("Area", Original, new CompleteResultRollInput(1, "14", [Lk("Ruins.", "Occupants")]) { Entered = true }),
            Next("Occupants", 0, 0, new CompleteResultRollInput(2, "11", [L("Bandits.")]) { ResolvedAs = 10 }));
        Assert.Equal(["Area · 14 (entered)", "Occupants · 11 (resolved as 10)"], Captions(result));
    }

    // ---- scale ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_very_long_manual_loop_is_assembled_quickly()
    {
        var steps = new List<CompleteResultStepInput> { S("Weather", Original, R(1, "6", Lk("Storm.", "Weather"))) };
        for (var i = 2; i <= 1000; i++) steps.Add(Next("Weather", 0, 0, R(i, "6", Lk("Storm.", "Weather"))));

        var timer = Stopwatch.StartNew();
        var result = CompleteResultBuilder.Build(new(steps));
        timer.Stop();

        Assert.Equal(1000, result.Pieces.Count);
        Assert.True(timer.ElapsedMilliseconds < 2000, $"took {timer.ElapsedMilliseconds} ms");
    }

    // ---- reusable outside WPF ------------------------------------------------------------------------------------------

    [Fact]
    public void The_builder_depends_on_nothing_of_WPF_or_the_view_models()
    {
        var types = typeof(CompleteResultBuilder).Assembly.GetTypes().Where(t => t.Namespace == "TableForge.Results").ToList();
        Assert.NotEmpty(types);

        static IEnumerable<Type> Signature(Type t) =>
            t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(m => m switch
                {
                    PropertyInfo p => [p.PropertyType],
                    FieldInfo f => [f.FieldType],
                    MethodInfo mi => mi.GetParameters().Select(p => p.ParameterType).Append(mi.ReturnType),
                    ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
                    _ => [],
                });

        static IEnumerable<Type> Expand(Type t) => t.IsGenericType ? t.GetGenericArguments().SelectMany(Expand).Append(t) : [t];

        foreach (var used in types.SelectMany(Signature).SelectMany(Expand).Distinct())
        {
            var assembly = used.Assembly.GetName().Name!;
            Assert.False(assembly is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml",
                $"{used} comes from {assembly}");
            Assert.False(used.Namespace is { } ns && (ns.StartsWith("System.Windows") || ns.StartsWith("TableForge.ViewModels")
                || ns.StartsWith("TableForge.Views") || ns.StartsWith("TableForge.Streaming") || ns.StartsWith("TableForge.Data")),
                $"{used} is not shared code");
        }

        // The source too, so a body can't quietly reach for them either.
        var folder = Path.Combine(RepoRoot(), "TableForge", "Results");
        foreach (var file in Directory.GetFiles(folder, "*.cs"))
        {
            var source = File.ReadAllText(file);
            foreach (var banned in new[] { "System.Windows", "TableForge.ViewModels", "TableForge.Views", "TableForge.Streaming", "TableForge.Data", "Clipboard", "Dispatcher" })
                Assert.False(source.Contains(banned), $"{Path.GetFileName(file)} mentions {banned}");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TableForge.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
