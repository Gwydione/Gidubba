using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

internal static class InlineFixtures
{
    /// <summary>One entry spanning the whole d8 range, with the given text and (optionally) a link.</summary>
    public static RollableTable TableWithText(string text, long collectionId = 1, long? linkedTableId = null) => new()
    {
        CollectionId = collectionId,
        Name = "Test",
        Dice = DiceExpression.Parse("d8"),
        ResultSets = [new ResultSet { Entries = [new TableEntry { Min = 1, Max = 8, Text = text, LinkedTableId = linkedTableId }] }],
    };

    /// <summary>Two aligned result sets (same single row, same range), each with its own dice-looking text.</summary>
    public static RollableTable AlignedTableWithDiceText() => new()
    {
        Name = "Aligned",
        Dice = DiceExpression.Parse("d8"),
        ResultSets =
        [
            new ResultSet { Name = "Coins", Entries = [new TableEntry { Min = 1, Max = 8, Text = "Gain 1d6 coins" }] },
            new ResultSet { Name = "Gems", Entries = [new TableEntry { Min = 1, Max = 8, Text = "and 1d4 gems" }] },
        ],
    };
}

/// <summary>
/// Pure detection: which dice-looking substrings TableForge already knows how to roll, found with strict token boundaries,
/// using the table's own <see cref="DiceExpression.TryParse"/> — never a second, more permissive grammar.
/// </summary>
public class InlineDiceDetectorTests
{
    private static string[] Names(IEnumerable<DiceExpression> found) => found.Select(d => d.ToString()).ToArray();

    [Fact]
    public void A_bare_die_is_detected_anywhere_in_the_text()
    {
        Assert.Equal(["d20"], Names(InlineDiceDetector.Detect("D20 Construction Supplies")));
        Assert.Equal(["d20"], Names(InlineDiceDetector.Detect("Use d20 if necessary")));
        Assert.Equal(["d20"], Names(InlineDiceDetector.Detect("You find d20 Construction Supplies")));
    }

    [Fact]
    public void A_counted_die_is_detected()
    {
        Assert.Equal(["d6"], Names(InlineDiceDetector.Detect("Obtain 1d6 trinkets")));
    }

    [Fact]
    public void A_die_embedded_in_a_larger_word_is_not_detected()
    {
        Assert.Empty(InlineDiceDetector.Detect("Torch (UD6)"));
        Assert.Empty(InlineDiceDetector.Detect("Torch (ud6)"));
    }

    [Fact]
    public void A_fixed_modifier_is_kept_as_part_of_the_one_action()
    {
        Assert.Equal(["2d6+1"], Names(InlineDiceDetector.Detect("Take 2d6+1 damage")));
        Assert.Equal(["d20-2"], Names(InlineDiceDetector.Detect("Take d20-2 damage")));
    }

    [Fact]
    public void The_d66_convention_is_detected_as_d66_not_as_a_66_sided_die()
    {
        var found = Assert.Single(InlineDiceDetector.Detect("Roll d66 for an encounter"));
        Assert.True(found.IsD66);
        Assert.Equal("d66", found.ToString());
    }

    [Fact]
    public void A_d66_with_a_modifier_is_not_supported_and_produces_nothing()
    {
        Assert.Empty(InlineDiceDetector.Detect("Roll d66+1 for an encounter"));
    }

    [Fact]
    public void Multiple_distinct_expressions_are_all_found_in_order_of_first_appearance()
    {
        Assert.Equal(["d6", "d4"], Names(InlineDiceDetector.Detect("Gain 1d6 coins and 1d4 gems")));
    }

    [Fact]
    public void The_same_expression_repeated_is_reported_only_once()
    {
        Assert.Equal(["d6"], Names(InlineDiceDetector.Detect("d6 food and d6 water")));
    }

    [Fact]
    public void Keep_drop_notation_produces_no_action_the_trailing_letters_break_the_token_boundary()
    {
        Assert.Empty(InlineDiceDetector.Detect("4d6kh3 damage"));
    }

    [Fact]
    public void Unsupported_dice_looking_text_alone_produces_no_action()
    {
        Assert.Empty(InlineDiceDetector.Detect("UD6"));
        Assert.Empty(InlineDiceDetector.Detect("no dice here at all"));
        Assert.Empty(InlineDiceDetector.Detect(""));
    }

    [Fact]
    public void Case_does_not_matter_and_the_canonical_lowercase_form_is_used_for_display()
    {
        var found = Assert.Single(InlineDiceDetector.Detect("D20 Construction Supplies"));
        Assert.Equal("d20", found.ToString());
    }

    // ---- RC12: where each expression sits, and resolving it in place ---------------------------

    [Fact]
    public void FindAll_reports_every_occurrence_with_its_exact_span_and_leaves_surrounding_punctuation_outside_it()
    {
        const string text = "+1d4 Armor, then roll 1d6.";

        var found = InlineDiceDetector.FindAll(text);

        Assert.Equal(["1d4", "1d6"], found.Select(m => text.Substring(m.Index, m.Length)).ToArray());
        Assert.Equal(["d4", "d6"], found.Select(m => m.Expression.ToString()).ToArray());
    }

    [Fact]
    public void FindAll_reports_a_repeated_expression_once_per_occurrence_while_Detect_still_reports_it_once()
    {
        const string text = "Gain 1d6 gold and lose 1d6 reputation";

        Assert.Equal([5, 23], InlineDiceDetector.FindAll(text).Select(m => m.Index).ToArray());
        Assert.Single(InlineDiceDetector.Detect(text));
    }

    [Fact]
    public void FindAll_includes_a_stored_modifier_in_the_span()
    {
        var match = Assert.Single(InlineDiceDetector.FindAll("Gain 2d6+1 supplies"));
        Assert.Equal((5, 5), (match.Index, match.Length));
    }

    [Fact]
    public void FindAll_finds_nothing_in_unsupported_notation()
    {
        Assert.Empty(InlineDiceDetector.FindAll("Roll 4d6kh3"));
        Assert.Empty(InlineDiceDetector.FindAll("Torch (UD6)"));
        Assert.Empty(InlineDiceDetector.FindAll("Roll d66+1 for an encounter"));
        Assert.Empty(InlineDiceDetector.FindAll(null));
    }

    private static string Resolve(string text, params (string Dice, string Value)[] rolled)
    {
        var values = rolled.ToDictionary(r => DiceExpression.Parse(r.Dice), r => r.Value);
        return InlineDiceDetector.Substitute(text, InlineDiceDetector.FindAll(text), d => values.GetValueOrDefault(d));
    }

    [Theory]
    [InlineData("You gain +1d4 Armor", "d4", "3", "You gain +3 Armor")]
    [InlineData("Encounter 2d6 Skeletons", "2d6", "7", "Encounter 7 Skeletons")]
    [InlineData("Encounter 2d6 Skeletons", "2d6", "1", "Encounter 1 Skeletons")] // no grammar fixing
    [InlineData("Gain d20+2 gold", "d20+2", "14", "Gain 14 gold")]
    [InlineData("Gain 2d6+1 supplies", "2d6+1", "9", "Gain 9 supplies")]
    [InlineData("Consult entry d66", "d66", "35", "Consult entry 35")]
    [InlineData("Gain +1d4 Armor, then rest.", "d4", "2", "Gain +2 Armor, then rest.")]
    [InlineData("D20 Construction Supplies", "d20", "14", "14 Construction Supplies")]
    public void Substitute_replaces_only_the_recognized_span_with_the_value(string text, string dice, string value, string expected)
    {
        Assert.Equal(expected, Resolve(text, (dice, value)));
    }

    [Fact]
    public void Substitute_keeps_an_expression_with_no_value_yet_exactly_as_written()
    {
        Assert.Equal("Gain 4 food and 1d4 water", Resolve("Gain 1d6 food and 1d4 water", ("d6", "4")));
        Assert.Equal("Gain 1D6 food", Resolve("Gain 1D6 food")); // original casing kept, too
    }

    [Fact]
    public void Substitute_fills_every_occurrence_of_the_same_expression_with_the_same_value()
    {
        Assert.Equal("Gain 4 gold and lose 4 reputation", Resolve("Gain 1d6 gold and lose 1d6 reputation", ("d6", "4")));
        Assert.Equal("4 food and 4 water", Resolve("d6 food and 1d6 water", ("d6", "4"))); // same expression, spelled differently
    }

    [Fact]
    public void Substitute_handles_two_different_expressions_and_the_punctuation_between_them()
    {
        Assert.Equal("+3 Armor, then roll 5.", Resolve("+1d4 Armor, then roll 1d6.", ("d4", "3"), ("d6", "5")));
    }

    [Fact]
    public void Substitute_does_not_touch_a_similar_looking_word_near_a_real_expression()
    {
        Assert.Equal("Torch (UD6) and 5 oil", Resolve("Torch (UD6) and d6 oil", ("d6", "5")));
    }
}

/// <summary>The roll screen: buttons appear beside a matched result, roll through the table's own provider, and never touch the source text.</summary>
public class InlineDiceSessionTests
{
    private static ResultLineViewModel OnlyLine(RollViewModel s) => Assert.Single(s.Results);

    [Fact]
    public void A_matched_result_offers_one_button_per_detected_expression()
    {
        var session = new RollViewModel(InlineFixtures.TableWithText("D20 Construction Supplies"), new FixedDice(1));

        session.RollCommand.Execute(null);

        var line = OnlyLine(session);
        var action = Assert.Single(line.InlineActions);
        Assert.Equal("d20", action.DisplayExpression);
        Assert.Equal("Roll d20", action.RollLabel);
        Assert.True(line.ShowInlineActions);
        Assert.False(action.HasResult);
        Assert.False(line.ShowResolved);
    }

    [Fact]
    public void Pressing_the_button_rolls_through_the_tables_own_provider_and_never_changes_the_text()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("D20 Construction Supplies"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        var action = Assert.Single(line.InlineActions);

        dice.Value = 14;
        action.RollCommand!.Execute(null);

        Assert.Equal(14, action.LatestValue);
        Assert.Equal("14 Construction Supplies", line.ResolvedText);
        Assert.Equal("D20 Construction Supplies", line.Text); // the source text is never rewritten
        Assert.Equal(2, dice.Calls); // the table's own roll, plus this one
    }

    [Fact]
    public void Roll_Again_replaces_the_resolved_value_instead_of_accumulating_a_history()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("You gain +1d4 Armor"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        var action = Assert.Single(line.InlineActions);

        dice.Value = 3; action.RollCommand!.Execute(null);
        Assert.Equal("You gain +3 Armor", line.ResolvedText);
        Assert.Equal("Roll Again", action.RollLabel);

        dice.Value = 1; action.RollCommand.Execute(null);
        Assert.Equal("You gain +1 Armor", line.ResolvedText);
        Assert.Equal(1, action.LatestValue);

        dice.Value = 4; action.RollCommand.Execute(null);
        Assert.Equal("You gain +4 Armor", line.ResolvedText); // only ever the latest value
        Assert.Equal("You gain +1d4 Armor", line.Text);
    }

    [Fact]
    public void The_resolved_line_is_announced_when_an_inline_roll_lands()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("You gain +1d4 Armor"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        var changed = new List<string?>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        dice.Value = 3;
        line.InlineActions[0].RollCommand!.Execute(null);

        Assert.Contains(nameof(ResultLineViewModel.ResolvedText), changed);
        Assert.Contains(nameof(ResultLineViewModel.ShowResolved), changed);
        Assert.True(line.ShowResolved);
    }

    [Theory]
    [InlineData("You gain +1d4 Armor", 3, "You gain +3 Armor")]
    [InlineData("Encounter 2d6 Skeletons", 7, "Encounter 7 Skeletons")]
    [InlineData("Gain 2d6+1 supplies", 9, "Gain 9 supplies")]
    [InlineData("Gain d20+2 gold", 14, "Gain 14 gold")]
    [InlineData("Gain +1d4 Armor, then rest.", 2, "Gain +2 Armor, then rest.")]
    [InlineData("Consult entry d66", 35, "Consult entry 35")]
    public void A_built_in_inline_roll_shows_the_result_resolved_in_context(string text, int value, string expected)
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText(text), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);

        dice.Value = value;
        Assert.Single(line.InlineActions).RollCommand!.Execute(null);

        Assert.Equal(expected, line.ResolvedText);
        Assert.True(line.ShowResolved);
        Assert.Equal(text, line.Text);
    }

    [Fact]
    public void An_inline_d66_is_rolled_as_d66_and_its_legal_result_is_substituted()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("Consult entry d66"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        var action = Assert.Single(line.InlineActions);
        Assert.True(action.Expression.IsD66);

        dice.Value = 35;
        action.RollCommand!.Execute(null);

        Assert.Equal("d66", dice.Requested[^1]);
        Assert.Equal("Consult entry 35", line.ResolvedText);
    }

    [Fact]
    public void Multiple_distinct_expressions_in_one_result_each_get_their_own_independent_action()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 1d6 coins and 1d4 gems"), dice);
        session.RollCommand.Execute(null);
        var actions = OnlyLine(session).InlineActions;
        Assert.Equal(["d6", "d4"], actions.Select(a => a.DisplayExpression).ToArray());

        dice.Value = 3;
        actions[0].RollCommand!.Execute(null);

        Assert.Equal(3, actions[0].LatestValue);
        Assert.False(actions[1].HasResult); // rolling one never rolls the other
    }

    [Fact]
    public void Multiple_distinct_expressions_resolve_independently_and_unrolled_ones_stay_as_written()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 1d6 food and 1d4 water"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        var (d6, d4) = (line.InlineActions[0], line.InlineActions[1]);
        Assert.Equal(("Roll d6", "Roll d4"), (d6.RollLabel, d4.RollLabel));

        dice.Value = 4; d6.RollCommand!.Execute(null);
        Assert.Equal("Gain 4 food and 1d4 water", line.ResolvedText);

        dice.Value = 2; d4.RollCommand!.Execute(null);
        Assert.Equal("Gain 4 food and 2 water", line.ResolvedText);

        dice.Value = 6; d6.RollCommand.Execute(null);
        Assert.Equal("Gain 6 food and 2 water", line.ResolvedText);

        // with several buttons, each "Roll Again" says which expression it rolls
        Assert.Equal(("Roll d6 Again", "Roll d4 Again"), (d6.RollLabel, d4.RollLabel));
        Assert.Equal("Gain 1d6 food and 1d4 water", line.Text);
    }

    [Fact]
    public void A_repeated_identical_expression_keeps_one_action_and_one_roll_fills_every_occurrence()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 1d6 gold and lose 1d6 reputation"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        var action = Assert.Single(line.InlineActions);
        Assert.Equal(2, line.InlineMatches.Count);

        dice.Value = 4;
        action.RollCommand!.Execute(null);

        Assert.Equal("Gain 4 gold and lose 4 reputation", line.ResolvedText);
        Assert.Equal(2, dice.Calls); // the parent roll plus exactly one inline roll
        Assert.Equal("Roll Again", action.RollLabel); // a single action needs no name
    }

    [Fact]
    public void Punctuation_around_several_expressions_survives_resolution()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("+1d4 Armor, then roll 1d6."), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);

        dice.Value = 3; line.InlineActions[0].RollCommand!.Execute(null);
        dice.Value = 5; line.InlineActions[1].RollCommand!.Execute(null);

        Assert.Equal("+3 Armor, then roll 5.", line.ResolvedText);
    }

    [Fact]
    public void Keep_highest_notation_offers_no_action_and_no_resolved_line()
    {
        var session = new RollViewModel(InlineFixtures.TableWithText("Roll 4d6kh3"), new FixedDice(3));

        session.RollCommand.Execute(null);

        var line = OnlyLine(session);
        Assert.Empty(line.InlineActions);
        Assert.False(line.ShowResolved);
        Assert.Equal("Roll 4d6kh3", line.ResolvedText);
    }

    [Fact]
    public void An_inline_roll_ignores_the_pending_situational_modifier_and_leaves_it_in_place()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 1d6 Armor"), dice);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        session.ModifierText = "+3";

        dice.Value = 4;
        line.InlineActions[0].RollCommand!.Execute(null);

        Assert.Equal("d6", dice.Requested[^1]); // an ordinary 1d6
        Assert.Equal("Gain 4 Armor", line.ResolvedText); // not 7
        Assert.Equal("+3", session.ModifierText); // neither used up nor reset
        Assert.Same(line, OnlyLine(session)); // and the parent was not re-resolved
    }

    [Fact]
    public void The_parent_roll_then_uses_the_modifier_normally_after_an_inline_roll()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 1d6 Armor"), dice);
        session.RollCommand.Execute(null);
        session.ModifierText = "+3";
        OnlyLine(session).InlineActions[0].RollCommand!.Execute(null);

        dice.Value = 2;
        session.RollCommand.Execute(null);

        Assert.Equal("Rolled 5", session.RollDisplay); // 2 +3
        Assert.Equal("0", session.ModifierText);
    }

    [Fact]
    public void Unsupported_notation_offers_no_button_at_all()
    {
        var session = new RollViewModel(InlineFixtures.TableWithText("Torch (UD6)"), new FixedDice(3));

        session.RollCommand.Execute(null);

        Assert.Empty(OnlyLine(session).InlineActions);
        Assert.False(OnlyLine(session).ShowInlineActions);
    }

    [Fact]
    public void Each_result_set_in_an_aligned_multi_column_roll_gets_its_own_inline_actions_and_rendering_is_unaffected()
    {
        var session = new RollViewModel(InlineFixtures.AlignedTableWithDiceText(), new FixedDice(3));

        session.RollCommand.Execute(null);

        Assert.True(session.IsAligned);
        Assert.Equal(["d6"], session.Results[0].InlineActions.Select(a => a.DisplayExpression).ToArray());
        Assert.Equal(["d4"], session.Results[1].InlineActions.Select(a => a.DisplayExpression).ToArray());
        var row = Assert.Single(session.AlignedRows); // the browsable aligned grid still shows every row untouched
        Assert.True(row.IsMatched);
    }

    // ---- lifecycle ---------------------------------------------------------------------------

    [Fact]
    public void Rolling_the_parent_table_again_clears_the_previous_rolls_inline_state()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("D20 Construction Supplies"), dice);
        session.RollCommand.Execute(null);
        var first = OnlyLine(session);
        var firstAction = Assert.Single(first.InlineActions);
        dice.Value = 9;
        firstAction.RollCommand!.Execute(null);
        Assert.True(first.ShowInlineActions);

        dice.Value = 2;
        session.RollCommand.Execute(null);

        Assert.False(first.ShowInlineActions); // the superseded roll's action is no longer offered
        var second = OnlyLine(session);
        Assert.NotSame(first, second);
        var secondAction = Assert.Single(second.InlineActions);
        Assert.False(secondAction.HasResult); // brand-new action: nothing carried over
        Assert.True(second.ShowInlineActions);
    }

    [Fact]
    public void Rerolling_the_parent_keeps_the_superseded_resolved_line_without_its_buttons()
    {
        var dice = new FixedDice(1);
        var session = new RollViewModel(InlineFixtures.TableWithText("You gain +1d4 Armor"), dice);
        session.RollCommand.Execute(null);
        var first = OnlyLine(session);
        dice.Value = 3;
        first.InlineActions[0].RollCommand!.Execute(null);
        Assert.True(first.ShowResolved);

        dice.Value = 2;
        session.RollCommand.Execute(null);

        Assert.True(first.ShowResolved); // kept as history (1.6), never rolled again
        Assert.True(first.HasResolved);
        Assert.Equal("You gain +3 Armor", first.ResolvedText);
        Assert.Equal("You gain +1d4 Armor", first.Text);
        Assert.False(first.ShowInlineActions);
        var second = OnlyLine(session);
        Assert.False(second.ShowResolved);
        Assert.Equal("You gain +1d4 Armor", second.ResolvedText);
    }

    [Fact]
    public void Following_a_link_deactivates_the_parents_inline_actions()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        var target = db.SaveTable(Fixtures.ScavengedItems(collection.Id));
        var table = db.SaveTable(InlineFixtures.TableWithText("D20 Construction Supplies", collection.Id, target.Id));
        var session = new RollViewModel(db.LoadTable(table.Id)!, new FixedDice(3), db.LoadTable);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        Assert.True(line.ShowInlineActions);

        line.FollowCommand!.Execute(null);

        Assert.False(line.ShowInlineActions);
        Assert.Empty(session.Current.Outcomes); // the child was not rolled
    }

    [Fact]
    public void Following_a_link_keeps_the_parents_resolved_line_without_its_buttons()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        var target = db.SaveTable(Fixtures.ScavengedItems(collection.Id));
        var table = db.SaveTable(InlineFixtures.TableWithText("You gain +1d4 Armor", collection.Id, target.Id));
        var dice = new FixedDice(3);
        var session = new RollViewModel(db.LoadTable(table.Id)!, dice, db.LoadTable);
        session.RollCommand.Execute(null);
        var line = OnlyLine(session);
        line.InlineActions[0].RollCommand!.Execute(null);
        Assert.Equal("You gain +3 Armor", line.ResolvedText);

        line.FollowCommand!.Execute(null);

        Assert.True(line.ShowResolved); // kept (1.6): the parent still reads as it was resolved
        Assert.True(line.HasResolved);
        Assert.Equal("You gain +3 Armor", line.ResolvedText);
        Assert.Equal("You gain +1d4 Armor", line.Text);
        Assert.False(line.ShowInlineActions);
        Assert.False(line.InlineActions[0].RollCommand!.CanExecute(null));
    }

    [Fact]
    public void Opening_another_table_and_coming_back_starts_with_no_resolved_state()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        var saved = db.SaveTable(InlineFixtures.TableWithText("You gain +1d4 Armor", collection.Id));
        var other = db.SaveTable(Fixtures.ScavengedItems(collection.Id));
        var dice = new FixedDice(3);
        var main = new MainViewModel(db, dice);
        TableSummary Summary(RollableTable t) => new(t.Id, t.Name, t.Dice, null, "Unfiled");

        main.OpenRecentTableCommand.Execute(Summary(saved));
        var first = (RollViewModel)main.Current!;
        first.RollCommand.Execute(null);
        first.Results.Single().InlineActions[0].RollCommand!.Execute(null);
        Assert.True(first.Results.Single().ShowResolved);

        main.OpenRecentTableCommand.Execute(Summary(other));
        Assert.NotSame(first, main.Current);
        main.OpenRecentTableCommand.Execute(Summary(saved));

        var again = (RollViewModel)main.Current!;
        Assert.NotSame(first, again);
        Assert.Empty(again.Results); // a fresh screen: no result, so nothing resolved
    }

    [Fact]
    public void Source_text_stays_byte_for_byte_unchanged_in_the_model_the_database_and_history_after_many_inline_rolls()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        const string source = "Gain +1d4 Armor, 1d6 food and 1d6 water.";
        var saved = db.SaveTable(InlineFixtures.TableWithText(source, collection.Id));
        var dice = new FixedDice(3);
        var main = new MainViewModel(db, dice);
        main.OpenRecentTableCommand.Execute(new TableSummary(saved.Id, saved.Name, saved.Dice, null, "Unfiled"));
        var session = (RollViewModel)main.Current!;
        session.RollCommand.Execute(null);
        var line = session.Results.Single();

        for (var i = 1; i <= 20; i++)
        {
            dice.Value = i % 4 + 1;
            line.InlineActions[i % 2].RollCommand!.Execute(null);
        }

        Assert.NotEqual(source, line.ResolvedText);
        Assert.Equal(source, line.Text);
        Assert.Equal(source, session.Current.Table.ResultSets[0].Entries[0].Text);
        Assert.Equal(source, session.ResultSets[0].Entries[0].Text);
        Assert.Equal(source, db.LoadTable(saved.Id)!.ResultSets[0].Entries[0].Text);
        var history = Assert.Single(db.GetRollHistory());
        Assert.Equal(source, history.ResultText);
        Assert.Equal(source, Assert.Single(main.RecentRolls).Item.ResultText);
    }

    [Fact]
    public void An_entry_can_offer_both_an_inline_dice_action_and_a_linked_table_follow_action_independently()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        var target = db.SaveTable(Fixtures.ScavengedItems(collection.Id));
        var table = db.SaveTable(InlineFixtures.TableWithText("D20 Construction Supplies", collection.Id, target.Id));
        var session = new RollViewModel(db.LoadTable(table.Id)!, new FixedDice(3), db.LoadTable);

        session.RollCommand.Execute(null);
        var line = OnlyLine(session);

        Assert.True(line.ShowInlineActions);
        Assert.True(line.ShowFollow);
        var action = Assert.Single(line.InlineActions);

        action.RollCommand!.Execute(null); // rolling the inline action does not follow the link
        Assert.Single(session.Steps);
        Assert.True(line.ShowFollow);

        line.FollowCommand!.Execute(null); // following the link is a separate action; it supersedes the inline result (kept, not offered)
        Assert.Equal(2, session.Steps.Count);
        Assert.True(action.HasResult);
        Assert.False(line.ShowInlineActions);
    }

    [Fact]
    public void Inline_rolls_are_never_written_to_the_stored_roll_history_only_the_parent_roll_is()
    {
        using var temp = new TempDatabase();
        using var db = temp.Open();
        var collection = db.CreateCollection("C");
        var saved = db.SaveTable(InlineFixtures.TableWithText("D20 Construction Supplies", collection.Id));
        var dice = new FixedDice(3);
        var main = new MainViewModel(db, dice);
        main.OpenRecentTableCommand.Execute(new TableSummary(saved.Id, saved.Name, saved.Dice, null, "Unfiled"));
        var session = (RollViewModel)main.Current!;

        session.RollCommand.Execute(null);
        Assert.Single(db.GetRollHistory());

        var action = Assert.Single(session.Results.Single().InlineActions);
        dice.Value = 14;
        action.RollCommand!.Execute(null);
        action.RollCommand.Execute(null);

        Assert.Equal(14, action.LatestValue);
        Assert.Single(db.GetRollHistory()); // still only the one parent-table roll
        Assert.Single(main.RecentRolls);
    }
}

/// <summary>The dddice path: an inline roll shows the visible dice and only reveals its result once they settle, exactly like a normal roll.</summary>
public class InlineDiceDddiceTests
{
    [Fact]
    public async Task An_inline_roll_hides_its_result_until_the_dice_settle_and_is_never_recorded_to_history()
    {
        var roller = new FakeDddiceRoller();
        var provider = new DddiceDiceProvider(roller);
        var history = new List<RollSnapshot>();
        var session = new RollViewModel(InlineFixtures.TableWithText("D20 Construction Supplies"), provider, rolled: history.Add);

        session.RollCommand.Execute(null);
        roller.Settle(("d8", 5)); // the parent table itself is a d8
        await session.RollTask;
        Assert.Single(history);

        var action = Assert.Single(session.Results.Single().InlineActions);
        action.RollCommand!.Execute(null);

        Assert.True(action.IsRolling);
        Assert.False(action.HasResult);
        Assert.False(session.Results.Single().ShowResolved); // nothing resolved while the dice are in the air
        Assert.Single(history); // the inline roll has not settled: nothing new recorded

        roller.Settle(("d20", 14));
        await session.RollTask;

        Assert.False(action.IsRolling);
        Assert.Equal(14, action.LatestValue);
        Assert.Equal("14 Construction Supplies", session.Results.Single().ResolvedText);
        Assert.True(session.Results.Single().ShowResolved);
        Assert.Single(history); // and never gets recorded once it does settle, either
    }

    [Fact]
    public async Task A_dddice_inline_roll_with_a_stored_modifier_resolves_to_the_final_number()
    {
        var roller = new FakeDddiceRoller();
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 2d6+1 supplies"), new DddiceDiceProvider(roller));
        session.RollCommand.Execute(null);
        roller.Settle(("d8", 5));
        await session.RollTask;
        var line = session.Results.Single();

        line.InlineActions[0].RollCommand!.Execute(null);
        roller.Settle(FakeDddiceRoller.Twod6(3, 5));
        await session.RollTask;

        Assert.Equal("Gain 9 supplies", line.ResolvedText); // 3 + 5 + 1, never "8+1"
    }

    [Fact]
    public async Task A_failed_dddice_reroll_keeps_the_previous_resolved_value()
    {
        var roller = new FakeDddiceRoller();
        var session = new RollViewModel(InlineFixtures.TableWithText("Gain 1d6 food"), new DddiceDiceProvider(roller));
        session.RollCommand.Execute(null);
        roller.Settle(("d8", 5));
        await session.RollTask;
        var line = session.Results.Single();
        var action = line.InlineActions[0];

        action.RollCommand!.Execute(null);
        roller.Settle(("d6", 4));
        await session.RollTask;
        Assert.Equal("Gain 4 food", line.ResolvedText);

        action.RollCommand.Execute(null); // Roll Again...
        roller.FailRoll("dddice lost the connection.");
        await session.RollTask;

        Assert.Equal("Gain 4 food", line.ResolvedText); // ...fails: the last good value stays
        Assert.True(line.ShowResolved);
        Assert.Equal(4, action.LatestValue);
        Assert.Equal("dddice lost the connection.", session.Message);

        action.RollCommand.Execute(null); // and a cancelled one likewise
        session.CancelRoll();
        await session.RollTask;
        Assert.Equal("Gain 4 food", line.ResolvedText);
    }

    [Fact]
    public async Task Pressing_an_inline_button_while_any_roll_is_in_flight_is_ignored_not_raced()
    {
        var roller = new FakeDddiceRoller();
        var provider = new DddiceDiceProvider(roller);
        var session = new RollViewModel(InlineFixtures.AlignedTableWithDiceText(), provider);
        session.RollCommand.Execute(null);
        roller.Settle(("d8", 5));
        await session.RollTask;

        var a = session.Results[0].InlineActions[0];
        var b = session.Results[1].InlineActions[0];

        a.RollCommand!.Execute(null);
        Assert.Equal(["d8", "d6"], roller.Requests.Select(r => string.Join(",", r)).ToArray()); // the parent's roll, then a's

        b.RollCommand!.Execute(null); // ignored: a roll is already under way
        Assert.False(b.RollCommand.CanExecute(null));
        session.RollCommand.Execute(null); // the parent Roll button is likewise ignored while inline is in flight
        Assert.False(session.RollCommand.CanExecute(null));
        Assert.Equal(2, roller.Requests.Count); // no third request from either ignored press

        roller.Settle(("d6", 4));
        await session.RollTask;

        Assert.Equal(4, a.LatestValue);
        Assert.False(b.HasResult);
        Assert.True(session.RollCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_while_an_inline_roll_is_in_flight_abandons_it_cleanly()
    {
        var roller = new FakeDddiceRoller();
        var provider = new DddiceDiceProvider(roller);
        var session = new RollViewModel(InlineFixtures.TableWithText("D20 Construction Supplies"), provider);
        session.RollCommand.Execute(null);
        roller.Settle(("d8", 5));
        await session.RollTask;
        var action = Assert.Single(session.Results.Single().InlineActions);

        action.RollCommand!.Execute(null);
        session.CancelRoll();
        await session.RollTask;

        Assert.True(roller.LastRollToken.IsCancellationRequested);
        Assert.False(session.IsRolling);
        Assert.False(action.IsRolling);
        Assert.False(action.HasResult);
        Assert.Equal("The roll was cancelled.", session.Message);
        Assert.True(action.RollCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_late_dddice_completion_after_cancellation_is_discarded()
    {
        var roller = new FakeDddiceRoller();
        var provider = new DddiceDiceProvider(roller);
        var session = new RollViewModel(InlineFixtures.TableWithText("D20 Construction Supplies"), provider);
        session.RollCommand.Execute(null);
        roller.Settle(("d8", 5));
        await session.RollTask;
        var action = Assert.Single(session.Results.Single().InlineActions);

        action.RollCommand!.Execute(null);
        session.CancelRoll();
        await session.RollTask;

        roller.Settle(("d20", 20)); // the animation finishes after TableForge stopped waiting for it

        Assert.False(action.HasResult);
        Assert.False(action.IsRolling);
    }
}

/// <summary>The real view: the button renders next to the matched text and works end to end.</summary>
[Collection("UI")]
public class InlineDiceViewTests
{
    [Fact]
    public void Roll_button_appears_beside_a_matched_result_and_rolling_it_changes_nothing_but_itself()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(3, (db, c) => db.SaveTable(InlineFixtures.TableWithText("D20 Construction Supplies", c.Id)));

            ui.SelectTable("Test");
            ui.Click("Roll");

            Assert.Contains(ui.Texts(), t => t.Text == "D20 Construction Supplies" && t.FontSize == 26);
            Assert.True(ui.HasVisibleButton("Roll d20"));

            ui.Click("Roll d20");

            Assert.Contains(ui.Texts(), t => t.Text == "D20 Construction Supplies"); // source text unchanged
            Assert.True(ui.HasVisibleButton("Roll Again"));
            Assert.False(ui.HasVisibleButton("Roll d20"));
        });
    }

    private static List<string> ResolvedLines(UiHarness ui) =>
        ui.Texts().Where(t => t.Name == "ResolvedInlineText").Select(t => t.Text).ToList();

    [Fact]
    public void Rolling_an_inline_expression_shows_the_result_resolved_in_context_and_Roll_Again_replaces_it()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(3, (db, c) => db.SaveTable(InlineFixtures.TableWithText("You gain +1d4 Armor", c.Id)));

            ui.SelectTable("Test");
            ui.Click("Roll");
            Assert.Contains(ui.Texts(), t => t.Text == "You gain +1d4 Armor" && t.FontSize == 26);
            Assert.Empty(ResolvedLines(ui)); // nothing resolved before an inline roll

            ui.Dice.Value = 3;
            ui.Click("Roll d4");
            Assert.Equal(["Resolved: You gain +3 Armor"], ResolvedLines(ui));
            var resolved = ui.Texts().Single(t => t.Name == "ResolvedInlineText");
            Assert.True(resolved.FontSize < 26); // subordinate to the table result

            ui.Dice.Value = 1;
            ui.Click("Roll Again");
            Assert.Equal(["Resolved: You gain +1 Armor"], ResolvedLines(ui)); // replaced, not appended
            Assert.DoesNotContain(ui.Texts(), t => t.Text.Contains('→')); // no detached "d4 → 3" presentation
            Assert.Contains(ui.Texts(), t => t.Text == "You gain +1d4 Armor" && t.FontSize == 26); // source unchanged

            ui.Dice.Value = 5;
            ui.Click("Roll"); // a new parent result: the earlier one keeps its resolved line (1.6); the new one is unrolled
            Assert.Equal(["Resolved: You gain +1 Armor"], ResolvedLines(ui));
            Assert.True(ui.HasVisibleButton("Roll d4"));
        });
    }

    [Fact]
    public void Two_distinct_inline_expressions_resolve_independently_in_the_real_view()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(3, (db, c) => db.SaveTable(InlineFixtures.TableWithText("Gain 1d6 food and 1d4 water", c.Id)));

            ui.SelectTable("Test");
            ui.Click("Roll");

            ui.Dice.Value = 4;
            ui.Click("Roll d6");
            Assert.Equal(["Resolved: Gain 4 food and 1d4 water"], ResolvedLines(ui));

            ui.Dice.Value = 2;
            ui.Click("Roll d4");
            Assert.Equal(["Resolved: Gain 4 food and 2 water"], ResolvedLines(ui));

            ui.Dice.Value = 6;
            ui.Click("Roll d6 Again");
            Assert.Equal(["Resolved: Gain 6 food and 2 water"], ResolvedLines(ui));
            Assert.True(ui.HasVisibleButton("Roll d4 Again"));
            Assert.Contains(ui.Texts(), t => t.Text == "Gain 1d6 food and 1d4 water" && t.FontSize == 26);
        });
    }

    [Fact]
    public void Unsupported_notation_shows_no_button_in_the_real_view()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(3, (db, c) => db.SaveTable(InlineFixtures.TableWithText("Torch (UD6)", c.Id)));

            ui.SelectTable("Test");
            ui.Click("Roll");

            Assert.Contains(ui.Texts(), t => t.Text == "Torch (UD6)");
            Assert.DoesNotContain(ui.All<System.Windows.Controls.Button>(), b => b.IsVisible && (b.Content as string)?.StartsWith("Roll d") == true);
        });
    }
}
