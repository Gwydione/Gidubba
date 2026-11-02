using TableForge.Domain;
using TableForge.ViewModels;
using static TableForge.Tests.PortableFixtures;

namespace TableForge.Tests;

/// <summary>
/// Unique Results everywhere a result is produced (Checkpoint 3): auto-follow rolls a linked destination through the same
/// rejection sampling, exhaustion, limit, Stop and recording as the person's own Roll, and a manually entered result obeys the
/// same rules without ever being rerolled. Tables without Unique rows behave exactly as before.
/// </summary>
public class UniqueLinkedAndManualTests
{
    /// <summary>Linked tables in memory, a shared used-results store, and a log of throws and recorded uses in the order they happened.</summary>
    private sealed class World
    {
        public Dictionary<long, RollableTable> Tables { get; } = [];
        public List<string> Log { get; } = [];
        public List<RollSnapshot> History { get; } = [];
        public UsedResultsStore Inner { get; } = UsedResultsStore.InMemory();
        public Func<long, bool> FailGet { get; set; } = _ => false;
        public Func<long, bool> FailMark { get; set; } = _ => false;

        public UsedResultsStore Store => new(
            id => FailGet(id) ? throw new InvalidOperationException("disk full") : Inner.Get(id),
            (id, keys) =>
            {
                if (FailMark(id)) throw new InvalidOperationException("disk full");
                Inner.MarkUsed(id, keys);
                Log.Add("used " + string.Join(",", keys.Select(k => Entry(k).Text)));
            });

        public RollableTable Add(string name, params TableEntry[] rows) => Add(name, "d6", new ResultSet { Entries = [.. rows] });

        public RollableTable Add(string name, string dice, params ResultSet[] sets)
        {
            var table = new RollableTable { Id = Tables.Count + 1, Name = name, Dice = DiceExpression.Parse(dice), ResultSets = [.. sets] };
            Tables[table.Id] = table;
            return table;
        }

        public TableEntry Entry(Guid key) => Tables.Values.SelectMany(t => t.ResultSets).SelectMany(s => s.Entries).Single(e => e.RowKey == key);
        public TableEntry Entry(string text) => Tables.Values.SelectMany(t => t.ResultSets).SelectMany(s => s.Entries).Single(e => e.Text == text);

        public void MarkUsed(params string[] texts)
        {
            foreach (var text in texts)
            {
                var entry = Entry(text);
                var table = Tables.Values.Single(t => t.ResultSets.Any(s => s.Entries.Contains(entry)));
                Inner.MarkUsed(table.Id, [entry.RowKey]);
            }
        }

        public string[] Used() => Tables.Values.SelectMany(t => t.ResultSets.SelectMany(s => s.Entries).Where(e => Inner.Get(t.Id).Contains(e.RowKey)))
            .Select(e => e.Text).ToArray();

        public RollViewModel Open(RollableTable start, ScriptedDice dice, bool autoFollow = true)
        {
            dice.OnThrow = n => Log.Add($"throw {n}");
            return new RollViewModel(start, dice, Tables.GetValueOrDefault, rolled: History.Add, autoFollow: new AutoFollowSetting(autoFollow),
                usedResults: Store);
        }
    }

    private static TableEntry Row(int min, int max, string text, bool unique = true, RollableTable? link = null) =>
        new() { Min = min, Max = max, Text = text, IsUnique = unique, RowKey = Guid.NewGuid(), LinkedTableId = link?.Id };

    private static string[] Titles(RollViewModel s) => s.Steps.Select(st => st.Title).ToArray();
    private static string[][] Shown(RollViewModel s) => s.Steps.SelectMany(st => st.Outcomes).Select(o => o.Lines.Select(l => l.Text).ToArray()).ToArray();

    /// <summary>Monsters: Goblin 1-2, Ogre 3-4, Dragon 5-6, every row Unique.</summary>
    private static RollableTable Monsters(World w, RollableTable? goblinLinksTo = null) =>
        w.Add("Monsters", Row(1, 2, "Goblin", link: goblinLinksTo), Row(3, 4, "Ogre"), Row(5, 6, "Dragon"));

    /// <summary>Area: an ordinary table whose 1 links to <paramref name="to"/>.</summary>
    private static RollableTable Area(World w, RollableTable to) =>
        w.Add("Area", Row(1, 1, "Lair", unique: false, link: to), Row(2, 6, "Empty", unique: false));

    // ---- auto-follow -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Auto_follow_into_an_unused_unique_destination_uses_up_its_result_like_any_roll()
    {
        var w = new World();
        var area = Area(w, Monsters(w));
        var dice = new ScriptedDice(1, 3);
        var session = w.Open(area, dice);
        session.RollCommand.Execute(null);

        Assert.Equal(["Area", "Monsters"], Titles(session));
        Assert.Equal([["Lair"], ["Ogre"]], Shown(session));
        Assert.Equal(["Ogre"], w.Used());
        Assert.Equal([0, 0], w.History.Select(h => h.RerolledCount).ToArray());
        Assert.Equal("", session.Message);
    }

    [Fact]
    public void Auto_follow_rerolls_a_used_result_on_the_destination_with_the_destinations_own_dice()
    {
        var w = new World();
        var monsters = w.Add("Monsters", "2d6", new ResultSet { Entries = [Row(2, 6, "Goblin"), Row(7, 7, "Ogre"), Row(8, 12, "Dragon")] });
        var area = Area(w, monsters);
        var dice = new ScriptedDice(1, 10, 9, 7);
        w.MarkUsed("Dragon");
        var session = w.Open(area, dice);
        session.RollCommand.Execute(null);

        Assert.Equal(["d6", "2d6", "2d6", "2d6"], dice.Requested);
        Assert.Equal([["Lair"], ["Ogre"]], Shown(session));                           // 10 and 9 were Dragon, used
        var record = w.History[1];
        Assert.Equal(("Monsters", 7, 2), (record.TableName, record.RollValue, record.RerolledCount));
        Assert.Equal(2, session.Current.Outcomes.Single().RerolledCount);
        Assert.Equal(["Ogre", "Dragon"], w.Used());
    }

    [Fact]
    public void Auto_follow_into_an_exhausted_destination_opens_it_unrolled_and_says_why()
    {
        var w = new World();
        var area = Area(w, Monsters(w));
        w.MarkUsed("Goblin", "Ogre", "Dragon");
        var dice = new ScriptedDice(1);
        var session = w.Open(area, dice);
        session.RollCommand.Execute(null);

        Assert.Equal(["Area", "Monsters"], Titles(session));
        Assert.Empty(session.Current.Outcomes);
        Assert.Equal(["d6"], dice.Requested);                                         // Monsters was never thrown
        Assert.Equal("Auto-follow stopped. All results used — Reset to roll again.", session.Message);
        Assert.Single(w.History);
        Assert.False(session.IsRolling);
        Assert.True(session.RollCommand.CanExecute(null));
    }

    [Fact]
    public void Auto_follow_rolls_a_destination_whose_unique_rows_are_used_while_a_repeatable_row_is_left()
    {
        var w = new World();
        var monsters = w.Add("Monsters", Row(1, 2, "Goblin"), Row(3, 4, "Ogre"), Row(5, 6, "Rats", unique: false));
        var area = Area(w, monsters);
        w.MarkUsed("Goblin", "Ogre");
        var session = w.Open(area, new ScriptedDice(1, 2, 3, 6));
        session.RollCommand.Execute(null);

        Assert.Equal([["Lair"], ["Rats"]], Shown(session));
        Assert.Equal(2, w.History[1].RerolledCount);
    }

    [Fact]
    public void Auto_follow_that_runs_out_of_attempts_leaves_the_destination_unrolled_and_uses_nothing()
    {
        var w = new World();
        var area = Area(w, Monsters(w));
        w.MarkUsed("Dragon");
        var dice = new ScriptedDice([1, .. Enumerable.Repeat(6, 10)]);
        var session = w.Open(area, dice);
        session.RollCommand.Execute(null);

        Assert.Equal(11, dice.Requested.Count);                                       // Area once, then Monsters' 10 attempts
        Assert.Empty(session.Current.Outcomes);
        Assert.Equal($"Auto-follow stopped. {RollViewModel.AttemptLimitMessage(10)}", session.Message);
        Assert.Equal(["Dragon"], w.Used());
        Assert.Single(w.History);
    }

    [Fact]
    public void Each_level_of_a_chain_is_recorded_as_used_before_the_next_table_is_thrown()
    {
        var w = new World();
        var hoard = w.Add("Hoard", Row(1, 3, "Gold"), Row(4, 6, "Gems"));
        var monsters = Monsters(w, goblinLinksTo: hoard);
        var area = Area(w, monsters);
        var session = w.Open(area, new ScriptedDice(1, 2, 5));
        session.RollCommand.Execute(null);

        Assert.Equal(["Area", "Monsters", "Hoard"], Titles(session));
        Assert.Equal([["Lair"], ["Goblin"], ["Gems"]], Shown(session));
        Assert.Equal(["throw 1", "throw 2", "used Goblin", "throw 3", "used Gems"], w.Log);
        Assert.Equal(["Gems", "Goblin"], w.Used().Order().ToArray());
    }

    [Fact]
    public void A_table_that_links_to_itself_never_uses_the_same_result_twice_in_one_chain()
    {
        var w = new World();
        var again = w.Add("Again");
        again.ResultSets = [new ResultSet { Entries = [Row(1, 3, "Roll again", link: again), Row(4, 6, "Done")] }];
        var session = w.Open(again, new ScriptedDice(2, 1, 5));
        session.RollCommand.Execute(null);

        Assert.Equal([["Roll again"], ["Done"]], Shown(session));                      // the chain's 1 was "Roll again", already used
        Assert.Equal([0, 1], w.History.Select(h => h.RerolledCount).ToArray());
        Assert.Equal(["used Roll again", "used Done"], w.Log.Where(l => l.StartsWith("used")).ToArray());
    }

    [Fact]
    public void Auto_follow_into_a_destination_with_several_result_sets_rejects_the_whole_roll_on_any_used_row()
    {
        var w = new World();
        var monsters = w.Add("Monsters", "d6",
            new ResultSet { Name = "Who", Entries = [Row(1, 3, "Bandits", unique: false), Row(4, 6, "Knights", unique: false)] },
            new ResultSet { Name = "Where", Entries = [Row(1, 3, "Ford"), Row(4, 6, "Bridge")] });
        var area = Area(w, monsters);
        w.MarkUsed("Bridge");
        var session = w.Open(area, new ScriptedDice(1, 5, 2));
        session.RollCommand.Execute(null);

        Assert.Equal([["Lair"], ["Bandits", "Ford"]], Shown(session));
        Assert.Equal(1, w.History[1].RerolledCount);
        Assert.Equal(["Ford", "Bridge"], w.Used());
    }

    [Fact]
    public void Auto_follow_into_a_destination_with_overlapping_rows_rejects_a_roll_on_any_used_one()
    {
        var w = new World();
        var cave = w.Add("Cave", Row(1, 6, "Corridor", unique: false), Row(3, 4, "Treasure"));
        var area = Area(w, cave);
        w.MarkUsed("Treasure");
        var session = w.Open(area, new ScriptedDice(1, 3, 6));
        session.RollCommand.Execute(null);

        Assert.Equal([["Lair"], ["Corridor"]], Shown(session));
        Assert.Equal(1, w.History[1].RerolledCount);
    }

    [Fact]
    public void Stop_during_a_destinations_rerolls_stops_the_chain_and_uses_nothing()
    {
        var w = new World();
        var area = Area(w, Monsters(w));
        w.MarkUsed("Dragon");
        var dice = new ScriptedDice(1, 6, 2);
        var session = w.Open(area, dice);
        dice.OnThrow = n => { if (n == 2) session.CancelRoll(); };                    // while Monsters' first (rejected) throw lands
        session.RollCommand.Execute(null);

        Assert.Equal(2, dice.Requested.Count);
        Assert.Equal("Auto-follow stopped. The roll was cancelled.", session.Message);
        Assert.Empty(session.Current.Outcomes);
        Assert.Equal(["Dragon"], w.Used());
        Assert.Single(w.History);
        Assert.False(session.IsRolling);
    }

    [Fact]
    public void A_destinations_used_results_that_cannot_be_read_or_recorded_stop_the_chain_without_showing_a_result()
    {
        var w = new World();
        var monsters = Monsters(w);
        var area = Area(w, monsters);
        w.FailMark = id => id == monsters.Id;
        var session = w.Open(area, new ScriptedDice(1, 3));
        session.RollCommand.Execute(null);

        Assert.Equal("Auto-follow stopped. The result could not be recorded as used, so it was not shown: disk full", session.Message);
        Assert.Empty(session.Current.Outcomes);
        Assert.Single(w.History);

        var r = new World();
        var rMonsters = Monsters(r);
        var rArea = Area(r, rMonsters);
        r.FailGet = id => id == rMonsters.Id;
        var reading = r.Open(rArea, new ScriptedDice(1, 3));
        reading.RollCommand.Execute(null);
        Assert.Equal("Auto-follow stopped. The used results could not be read: disk full", reading.Message);
        Assert.Empty(reading.Current.Outcomes);
    }

    [Fact]
    public void A_chain_of_tables_without_unique_rows_never_touches_used_results()
    {
        var w = new World();
        var b = w.Add("B", Row(1, 6, "B row", unique: false));
        var a = w.Add("A", Row(1, 6, "A row", unique: false, link: b));
        w.FailGet = _ => true;
        w.FailMark = _ => true;
        var dice = new ScriptedDice(4, 2);
        var session = w.Open(a, dice);
        session.RollCommand.Execute(null);

        Assert.Equal([["A row"], ["B row"]], Shown(session));
        Assert.Equal(["d6", "d6"], dice.Requested);
        Assert.Equal("", session.Message);
        Assert.All(w.History, h => Assert.Equal(0, h.RerolledCount));
    }

    // ---- manual entry ----------------------------------------------------------------------------------------------------

    private static void Enter(RollViewModel session, string value)
    {
        session.ManualRollText = value;
        session.ResolveManualCommand.Execute(null);
    }

    [Fact]
    public void An_entered_unused_unique_result_is_shown_and_used_up_without_any_dice()
    {
        var w = new World();
        var dice = new ScriptedDice(1);
        var session = w.Open(Monsters(w), dice, autoFollow: false);
        Enter(session, "3");

        Assert.Equal([["Ogre"]], Shown(session));
        Assert.Equal(["Ogre"], w.Used());
        Assert.Empty(dice.Requested);
        Assert.Equal((3, 0), (w.History.Single().RollValue, w.History.Single().RerolledCount));
        Assert.Equal("", session.Message);
    }

    [Fact]
    public void An_entered_used_result_is_refused_never_rerolled_and_changes_nothing()
    {
        var w = new World();
        var dice = new ScriptedDice(1);
        var session = w.Open(Monsters(w), dice, autoFollow: false);
        w.MarkUsed("Ogre");
        Enter(session, "4");

        Assert.Equal("Result already used — choose another result.", session.Message);
        Assert.Empty(session.Current.Outcomes);
        Assert.Empty(w.History);
        Assert.Empty(dice.Requested);
        Assert.Equal(["Ogre"], w.Used());
        Assert.Equal("4", session.ManualRollText);                                   // kept, to be corrected

        Enter(session, "6");                                                         // another result is fine
        Assert.Equal([["Dragon"]], Shown(session));
    }

    [Fact]
    public void An_entered_repeatable_result_or_gap_is_accepted_even_when_every_unique_row_is_used()
    {
        var w = new World();
        var session = w.Open(w.Add("Monsters", "d8", new ResultSet { Entries = [Row(1, 3, "Goblin"), Row(4, 6, "Rats", unique: false)] }),
            new ScriptedDice(1), autoFollow: false);
        w.MarkUsed("Goblin");

        Enter(session, "5");
        Enter(session, "8");
        Assert.Equal([["Rats"], ["No entry covers 8."]], Shown(session));
        Assert.Equal(["Goblin"], w.Used());

        Enter(session, "2");
        Assert.Equal(UniqueResults.AlreadyUsedMessage, session.Message);
    }

    [Fact]
    public void An_entered_result_follows_the_multiple_result_set_and_overlap_rules()
    {
        var w = new World();
        var paired = w.Add("Paired", "d6",
            new ResultSet { Name = "Who", Entries = [Row(1, 3, "Bandits", unique: false), Row(4, 6, "Knights")] },
            new ResultSet { Name = "Where", Entries = [Row(1, 3, "Ford"), Row(4, 6, "Bridge")] });
        var cave = w.Add("Cave", Row(1, 6, "Corridor", unique: false), Row(3, 4, "Treasure"));
        w.MarkUsed("Ford", "Treasure");

        var pairedSession = w.Open(paired, new ScriptedDice(1), autoFollow: false);
        Enter(pairedSession, "2");                                                   // Bandits is repeatable, but Ford is used
        Assert.Equal(UniqueResults.AlreadyUsedMessage, pairedSession.Message);
        Enter(pairedSession, "5");
        Assert.Equal([["Knights", "Bridge"]], Shown(pairedSession));
        Assert.Contains("Knights", w.Used());
        Assert.Contains("Bridge", w.Used());

        var caveSession = w.Open(cave, new ScriptedDice(1), autoFollow: false);
        Enter(caveSession, "3");                                                     // Corridor also covers 3, but Treasure is used
        Assert.Equal(UniqueResults.AlreadyUsedMessage, caveSession.Message);
        Enter(caveSession, "5");
        Assert.Equal([["Corridor"]], Shown(caveSession));
    }

    [Fact]
    public void An_entered_result_is_looked_up_exactly_as_typed_never_clamped()
    {
        var w = new World();
        var table = w.Add("Morale", "d20", new ResultSet { Entries = [Row(3, 10, "Flee"), Row(11, 18, "Fight")] });
        table.ClampResultsToRange = true;
        var session = w.Open(table, new ScriptedDice(1), autoFollow: false);
        w.MarkUsed("Flee");

        Enter(session, "1");                                                         // a roll of 1 would be clamped onto Flee; typed, it is a gap
        Assert.Equal([["No entry covers 1."]], Shown(session));
        Enter(session, "5");
        Assert.Equal(UniqueResults.AlreadyUsedMessage, session.Message);
    }

    [Fact]
    public void An_entered_result_whose_use_cannot_be_read_or_recorded_is_not_shown_and_does_not_continue_a_chain()
    {
        var w = new World();
        var monsters = Monsters(w, goblinLinksTo: w.Add("Hoard", Row(1, 6, "Gold")));
        w.FailMark = id => id == monsters.Id;
        var dice = new ScriptedDice(1);
        var session = w.Open(monsters, dice);
        Enter(session, "1");

        Assert.Equal("The result could not be recorded as used, so it was not shown: disk full", session.Message);
        Assert.Empty(session.Current.Outcomes);
        Assert.Equal(["Monsters"], Titles(session));
        Assert.Empty(dice.Requested);
        Assert.Empty(w.History);

        w.FailMark = _ => false;
        w.FailGet = id => id == monsters.Id;
        Enter(session, "1");
        Assert.Equal("The used results could not be read: disk full", session.Message);
        Assert.Empty(session.Current.Outcomes);
    }

    [Fact]
    public void An_entered_result_is_recorded_as_used_before_auto_follow_continues_from_it()
    {
        var w = new World();
        var hoard = w.Add("Hoard", Row(1, 3, "Gold"), Row(4, 6, "Gems"));
        var monsters = Monsters(w, goblinLinksTo: hoard);
        var session = w.Open(monsters, new ScriptedDice(2));
        Enter(session, "1");
        session.RollTask.GetAwaiter().GetResult();

        Assert.Equal([["Goblin"], ["Gold"]], Shown(session));
        Assert.Equal(["used Goblin", "throw 1", "used Gold"], w.Log);
    }

    [Fact]
    public void Manual_entry_on_a_table_without_unique_rows_never_touches_used_results()
    {
        var w = new World();
        var table = w.Add("Plain", Row(1, 3, "Low", unique: false), Row(4, 6, "High", unique: false));
        w.FailGet = _ => true;
        w.FailMark = _ => true;
        var session = w.Open(table, new ScriptedDice(1), autoFollow: false);
        Enter(session, "5");
        Enter(session, "5");

        Assert.Equal([["High"], ["High"]], Shown(session));
        Assert.Equal("", session.Message);
    }

    // ---- unused rows the roll cannot reach -------------------------------------------------------------------------------

    [Fact]
    public void A_table_whose_only_unused_rows_the_modifier_and_clamp_put_out_of_reach_is_exhausted_and_not_thrown()
    {
        var w = new World();
        // Clamp range 1-8. With +3 a d6 reaches 4-9, looked up as 4-8: only High, which is used. Low is unused but unreachable.
        var table = w.Add("Depth", "d6", new ResultSet { Entries = [Row(1, 2, "Low"), Row(3, 8, "High")] });
        table.ClampResultsToRange = true;
        w.MarkUsed("High");
        var dice = new ScriptedDice(1);
        var session = w.Open(table, dice, autoFollow: false);

        session.ModifierText = "3";
        session.RollCommand.Execute(null);
        Assert.Empty(dice.Requested);
        Assert.Equal(UniqueResults.ExhaustedMessage, session.Message);
        Assert.Equal("3", session.ModifierText);

        session.ModifierText = "0";                                                  // without it Low is reachable again
        session.RollCommand.Execute(null);
        Assert.Equal([["Low"]], Shown(session));
    }

    // ---- the app, with its database ------------------------------------------------------------------------------------

    [Fact]
    public void In_the_app_manual_and_auto_followed_results_stay_used_after_a_restart()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var c = db.CreateCollection("Dungeon");
        var monsters = Save(db, c.Id, "Monsters", "d6", null, Set("", E(1, 3, "Goblin"), E(4, 6, "Ogre")));
        foreach (var e in monsters.ResultSets[0].Entries) e.IsUnique = true;
        db.SaveTable(monsters);
        Save(db, c.Id, "Area", "d6", null, Set("", E(1, 6, "Lair", link: monsters.Id)));

        RollViewModel OpenIn(MainViewModel main, string name)
        {
            main.SelectedTable = main.Tables.Single(t => t.Name == name);
            return Assert.IsType<RollViewModel>(main.Current);
        }

        var dice = new FixedDice(5);
        var first = OpenIn(new MainViewModel(db, dice, (_, _) => true), "Monsters");
        Enter(first, "2");                                                           // Goblin, by hand
        Assert.Equal("Goblin", first.Results.Single().Text);

        db.Dispose();                                                                // a restart
        var reopened = temp.Open();
        var main = new MainViewModel(reopened, dice, (_, _) => true, autoFollow: new AutoFollowSetting(true));
        var monstersAgain = OpenIn(main, "Monsters");
        Enter(monstersAgain, "1");
        Assert.Equal(UniqueResults.AlreadyUsedMessage, monstersAgain.Message);

        var area = OpenIn(main, "Area");                                             // auto-follow: 5 → Lair → Monsters 5 → Ogre
        area.RollCommand.Execute(null);
        area.RollTask.GetAwaiter().GetResult();
        Assert.Equal(["Area", "Monsters"], area.Steps.Select(s => s.Title).ToArray());
        Assert.Equal("Ogre", area.Results.Single().Text);

        area.RollCommand.Execute(null);                                              // Monsters is now exhausted
        Assert.Equal(UniqueResults.ExhaustedMessage, area.Message);
        Assert.Equal(2, reopened.GetUsedRowKeys(monsters.Id).Count);
        Assert.Equal(["Monsters", "Area", "Monsters"], reopened.GetRollHistory().Select(h => h.TableName).ToArray());
    }
}
