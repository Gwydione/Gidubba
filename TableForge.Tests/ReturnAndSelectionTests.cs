using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Never settles a roll by itself, so a roll stays "in the air" until Stop.</summary>
internal sealed class GatedDice : IDiceProvider
{
    public Task<int> RollAsync(DiceExpression expression, CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource<int>();
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken)); // Stop ends it, as a real provider would
        return pending.Task;
    }
}

/// <summary>
/// Back to the original table on the Roll screen, and the table list following the table actually shown there, so that Edit,
/// Delete and Enter always act on that table (never on the one the screen was first opened on, never on a hidden one).
/// </summary>
public class ReturnAndSelectionTests
{
    // ---- Back to the original table (RollViewModel) -----------------------------------------------------------------------

    /// <summary>In-memory linked d6 tables: every face is its own row, "A 1" … "A 6".</summary>
    private sealed class Tables
    {
        private readonly Dictionary<long, RollableTable> _all = [];

        public RollableTable Add(string name, string dice = "d6", bool clamp = false)
        {
            var parsed = DiceExpression.Parse(dice);
            var table = new RollableTable
            {
                Id = _all.Count + 1,
                Name = name,
                Dice = parsed,
                ClampResultsToRange = clamp,
                ResultSets =
                [
                    new ResultSet
                    {
                        Entries = Enumerable.Range(parsed.Min, parsed.Max - parsed.Min + 1)
                            .Select(v => new TableEntry { Min = v, Max = v, Text = $"{name} {v}", SortOrder = v }).ToList(),
                    },
                ],
            };
            _all[table.Id] = table;
            return table;
        }

        public void Link(RollableTable from, int value, RollableTable to) =>
            from.ResultSets[0].Entries.Single(e => e.Min == value).LinkedTableId = to.Id;

        public RollableTable? Load(long id) => _all.GetValueOrDefault(id);
    }

    private static string[] Titles(RollViewModel s) => s.Steps.Select(st => st.Title).ToArray();

    private static string[] Shown(RollViewModel s) => s.Steps.SelectMany(st => st.Outcomes).Select(o => o.Lines[0].Text).ToArray();

    private static void Resolve(RollViewModel s, int value)
    {
        s.ManualRollText = value.ToString();
        s.ResolveManualCommand.Execute(null);
    }

    [Fact]
    public void Back_is_offered_only_away_from_the_original_table_and_names_it()
    {
        var t = new Tables();
        var a = t.Add("Area Type");
        var b = t.Add("Ruins");
        t.Link(a, 5, b);
        var session = new RollViewModel(a, new SequenceDice(5), t.Load);

        Assert.False(session.ShowReturnToOriginal);
        Assert.False(session.ReturnToOriginalCommand.CanExecute(null));
        session.RollCommand.Execute(null);
        Assert.False(session.ShowReturnToOriginal);                         // a roll is not a move

        session.Results[0].FollowCommand!.Execute(null);

        Assert.True(session.ShowReturnToOriginal);
        Assert.True(session.ReturnToOriginalCommand.CanExecute(null));
        Assert.Equal("← Back to Area Type", session.ReturnToOriginalLabel);
    }

    [Fact]
    public void Back_adds_the_original_table_as_a_new_step_keeping_every_earlier_result_and_rolls_nothing()
    {
        var t = new Tables();
        var a = t.Add("A");
        var b = t.Add("B");
        t.Link(a, 1, b);
        var dice = new SequenceDice(1, 4);
        var history = new List<RollSnapshot>();
        var used = new List<long>();
        var session = new RollViewModel(a, dice, t.Load, used.Add, history.Add);

        session.RollCommand.Execute(null);                                  // A 1 → B
        session.Results[0].FollowCommand!.Execute(null);
        session.RollCommand.Execute(null);                                  // B 4
        session.ModifierText = "+2";
        session.RollCount = 3;
        session.ManualRollText = "3";

        session.ReturnToOriginalCommand.Execute(null);

        Assert.Equal(["A", "B", "A"], Titles(session));
        Assert.Equal(["A 1", "B 4"], Shown(session));                       // nothing removed, nothing rolled
        var back = session.Current;
        Assert.True(back.IsReturn);
        Assert.False(back.IsFirst);
        Assert.False(back.HasOutcomes);
        Assert.Equal(2, dice.Requested.Count);
        Assert.Equal(2, history.Count);
        Assert.Equal([b.Id, a.Id], used);                                   // recent tables (and the list) hear of both moves
        Assert.Equal(("0", 1, ""), (session.ModifierText, session.RollCount, session.ManualRollText));
        Assert.False(session.ShowReturnToOriginal);
        Assert.False(session.Steps[0].Outcomes[0].Lines[0].IsActive);       // the earlier link is spent, as after any move

        session.RollCommand.Execute(null);                                  // rolling the original again goes on as usual
        Assert.Equal(["A 1", "B 4", "A 1"], Shown(session));
        Assert.Equal(3, session.Steps.Count);
    }

    [Fact]
    public void After_an_auto_follow_chain_Back_goes_to_the_table_the_screen_was_opened_on_not_the_previous_one()
    {
        var t = new Tables();
        var a = t.Add("A");
        var b = t.Add("B");
        var c = t.Add("C");
        var d = t.Add("D");
        t.Link(a, 1, b);
        t.Link(b, 1, c);
        t.Link(c, 1, d);
        var session = new RollViewModel(a, new SequenceDice(1), t.Load, autoFollow: new AutoFollowSetting(true));

        session.RollCommand.Execute(null);
        Assert.Equal(["A", "B", "C", "D"], Titles(session));
        Assert.True(session.ShowReturnToOriginal);
        Assert.Equal("← Back to A", session.ReturnToOriginalLabel);

        session.ReturnToOriginalCommand.Execute(null);

        Assert.Equal(["A", "B", "C", "D", "A"], Titles(session));
        Assert.Equal(["A 1", "B 1", "C 1", "D 1"], Shown(session));
        Assert.False(session.ShowReturnToOriginal);
    }

    [Fact]
    public void Back_never_happens_by_itself_and_a_followed_link_back_to_the_original_table_hides_it()
    {
        var t = new Tables();
        var a = t.Add("A");
        var b = t.Add("B");
        t.Link(a, 2, b);
        t.Link(b, 3, a);                                                    // a loop: B's 3 leads back to A
        var session = new RollViewModel(a, new SequenceDice(1), t.Load);

        Resolve(session, 2);
        session.Results[0].FollowCommand!.Execute(null);
        Resolve(session, 1);                                                // rolling the destination stays there
        Assert.Equal(["A", "B"], Titles(session));
        Assert.True(session.ShowReturnToOriginal);

        Resolve(session, 3);
        session.Results[0].FollowCommand!.Execute(null);                    // the loop itself returns to A

        Assert.Equal(["A", "B", "A"], Titles(session));
        Assert.False(session.ShowReturnToOriginal);
        Assert.False(session.ReturnToOriginalCommand.CanExecute(null));
        session.ReturnToOriginalCommand.Execute(null);                      // nothing to go back to: no new step
        Assert.Equal(3, session.Steps.Count);
    }

    [Fact]
    public void An_auto_follow_loop_still_offers_Back_whenever_it_stops_away_from_the_original_table()
    {
        var t = new Tables();
        var a = t.Add("A");
        var b = t.Add("B");
        t.Link(a, 1, b);
        t.Link(b, 1, a);
        var session = new RollViewModel(a, new SequenceDice(1), t.Load, autoFollow: new AutoFollowSetting(true));

        session.RollCommand.Execute(null);                                  // A → B → A → … until a limit stops it

        Assert.StartsWith("Auto-follow stopped", session.Message);
        Assert.Equal(session.Current.Table.Id != a.Id, session.ShowReturnToOriginal);
        if (session.ShowReturnToOriginal)
        {
            session.ReturnToOriginalCommand.Execute(null);
            Assert.Equal("A", session.Current.Title);
        }
        Assert.False(session.ShowReturnToOriginal);
    }

    [Fact]
    public void Back_is_unavailable_while_a_roll_is_in_the_air()
    {
        var t = new Tables();
        var a = t.Add("A");
        var b = t.Add("B");
        t.Link(a, 1, b);
        var dice = new GatedDice();
        var session = new RollViewModel(a, dice, t.Load);
        Resolve(session, 1);
        session.Results[0].FollowCommand!.Execute(null);

        session.RollCommand.Execute(null);                                  // rolling B: not settled yet
        Assert.True(session.IsRolling);
        Assert.True(session.ShowReturnToOriginal);
        Assert.False(session.ReturnToOriginalCommand.CanExecute(null));
        session.ReturnToOriginalCommand.Execute(null);
        Assert.Equal(["A", "B"], Titles(session));

        session.StopCommand.Execute(null);
        Assert.False(session.IsRolling);
        Assert.True(session.ReturnToOriginalCommand.CanExecute(null));
    }

    // ---- the out-of-range modifier hint ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(3, "+3", "The modifier took the roll above this table's highest row. To use the nearest row instead, turn on Clamp in Edit table.")]
    [InlineData(1, "-2", "The modifier took the roll below this table's lowest row. To use the nearest row instead, turn on Clamp in Edit table.")]
    public void A_modifier_past_the_table_explains_Clamp_beside_the_controls_and_changes_nothing_else(int die, string modifier, string hint)
    {
        var t = new Tables();
        var d4 = t.Add("Demands", "d4");
        var history = new List<RollSnapshot>();
        var session = new RollViewModel(d4, new SequenceDice(die), t.Load, rolled: history.Add);
        session.ModifierText = modifier;

        session.RollCommand.Execute(null);

        Assert.Equal(hint, session.Message);
        var total = die + int.Parse(modifier);
        Assert.Equal($"No entry covers {total}.", Assert.Single(session.Results).Text);   // the result itself, as always
        Assert.Equal($"No entry covers {total}.", Assert.Single(history).ResultText);     // and Recent Rolls

        session.RollCommand.Execute(null);                                  // the modifier is used up: the next roll is ordinary
        Assert.Equal("", session.Message);
    }

    [Fact]
    public void No_hint_without_a_modifier_with_Clamp_on_or_for_a_gap_inside_the_table()
    {
        var t = new Tables();
        var clamped = t.Add("Clamped", "d4", clamp: true);
        var session = new RollViewModel(clamped, new SequenceDice(3), t.Load);
        session.ModifierText = "+3";
        session.RollCommand.Execute(null);
        Assert.Equal("Clamped 4", Assert.Single(session.Results).Text);
        Assert.Equal("", session.Message);

        var gappy = new RollableTable
        {
            Id = 99, Name = "Gappy", Dice = DiceExpression.Parse("d6"),
            ResultSets = [new ResultSet { Entries = [new TableEntry { Min = 1, Max = 2, Text = "Low" }, new TableEntry { Min = 5, Max = 6, Text = "High" }] }],
        };
        session = new RollViewModel(gappy, new SequenceDice(1), t.Load);
        session.ModifierText = "+2";
        session.RollCommand.Execute(null);
        Assert.Equal("No entry covers 3.", Assert.Single(session.Results).Text);
        Assert.Equal("", session.Message);                                  // Clamp would not help inside the range

        Assert.Equal("", RollViewModel.ModifierOutsideTableHint(gappy, 4));
        Assert.Equal("", RollViewModel.ModifierOutsideTableHint(clamped, 9));
    }

    // ---- the table list follows the Roll screen (MainViewModel, real database) -------------------------------------------

    private sealed class World : IDisposable
    {
        private readonly TempDatabase _temp = new();
        public AppDatabase Db { get; }
        public Collection Collection { get; }
        public FixedDice Dice { get; } = new(6);
        public List<string> Asked { get; } = [];
        public List<string> Titles { get; } = [];
        public bool Answer { get; set; }
        public PortableFixtures.TempFile File { get; } = new();
        public MainViewModel Main { get; private set; } = null!;

        public World()
        {
            Db = _temp.Open();
            Collection = Db.CreateCollection("Dungeon");
        }

        public MainViewModel Start(bool autoFollow = false, IDiceProvider? dice = null) => Main = new MainViewModel(Db, dice ?? Dice,
            (m, title) => { Asked.Add(m); Titles.Add(title); return Answer; }, chooseCollectionFile: _ => File.Path, chooseImportFile: () => File.Path,
            autoFollow: new AutoFollowSetting(autoFollow));

        public RollableTable Save(string name, long? linkTo = null, long? folderId = null) => Db.SaveTable(new RollableTable
        {
            CollectionId = Collection.Id,
            Name = name,
            Dice = DiceExpression.Parse("d6"),
            FolderId = folderId,
            ResultSets = [new ResultSet { Entries = [new TableEntry { Min = 1, Max = 6, Text = $"{name} result", LinkedTableId = linkTo }] }],
        });

        public RollViewModel Open(string name)
        {
            Main.SelectedTable = Main.Tables.Single(t => t.Name == name);
            return Assert.IsType<RollViewModel>(Main.Current);
        }

        public string? Selected => Main.SelectedTable?.Name;
        public string? Highlighted => Main.HighlightedTable?.Name;

        public void Dispose()
        {
            _temp.Dispose();
            File.Dispose();
        }
    }

    // ---- confirmation titles ---------------------------------------------------------------------------------------------

    [Fact]
    public void Every_confirmation_is_titled_for_its_action_keeps_its_question_and_declining_changes_nothing()
    {
        using var w = new World();
        var loot = w.Db.CreateFolder(w.Collection.Id, "Loot");
        w.Save("Weather", folderId: loot.Id);
        w.Start();
        w.Answer = false;

        w.Open("Weather");
        w.Main.DeleteTableCommand.Execute(null);
        w.Main.SelectedFolderNav = w.Main.FolderNav.Single(f => f.Name == "Loot");
        w.Main.DeleteFolderCommand.Execute(null);
        w.Main.ExportCollectionCommand.Execute(null);
        w.Main.ImportCollectionCommand.Execute(null);                       // no confirmImport given: the general question, titled
        w.Main.DeleteCollectionCommand.Execute(null);

        Assert.Equal(["Delete Table", "Delete Folder", "Import Collection", "Delete Collection"], w.Titles);
        Assert.StartsWith("Delete the table \"Weather\"?", w.Asked[0]);
        Assert.StartsWith("Delete \"Loot\"?", w.Asked[1]);
        Assert.StartsWith("Delete the collection \"Dungeon\"", w.Asked[3]);
        Assert.Equal(["Dungeon"], w.Main.Collections.Select(c => c.Name).ToArray());   // every one declined: nothing changed
        Assert.Single(w.Db.GetFolders(w.Collection.Id));
        Assert.Equal(["Weather"], w.Main.Tables.Select(t => t.Name).ToArray());
    }

    // ---- editing and cancelling with the list following the Roll screen --------------------------------------------------

    [Fact]
    public void Editing_a_followed_table_leaves_the_list_alone_and_Cancel_or_Save_comes_back_to_that_table_without_rolling()
    {
        using var w = new World();
        var items = w.Save("Scavenged Items");
        w.Save("Scavenging", items.Id);
        w.Start();
        var session = w.Open("Scavenging");
        session.RollCommand.Execute(null);
        session.Results[0].FollowCommand!.Execute(null);

        w.Main.EditTableCommand.Execute(null);
        var review = Assert.IsType<ReviewViewModel>(w.Main.Current);
        Assert.Equal(("Scavenged Items", "Scavenged Items"), (w.Selected, w.Highlighted));      // opening the editor moved nothing
        session.ReturnToOriginalCommand.Execute(null);                     // the Roll screen that was left can't move the list
        Assert.Same(review, w.Main.Current);
        Assert.Equal(("Scavenged Items", "Scavenged Items"), (w.Selected, w.Highlighted));

        review.CancelCommand.Execute(null);
        var afterCancel = Assert.IsType<RollViewModel>(w.Main.Current);
        Assert.Equal(["Scavenged Items"], Titles(afterCancel));             // a fresh screen on the edited table, as Cancel always gave
        Assert.False(afterCancel.ShowReturnToOriginal);
        Assert.Equal(("Scavenged Items", "Scavenged Items"), (w.Selected, w.Highlighted));

        w.Main.EditTableCommand.Execute(null);
        review = Assert.IsType<ReviewViewModel>(w.Main.Current);
        review.TableName = "Salvage";
        review.SaveCommand.Execute(null);
        var afterSave = Assert.IsType<RollViewModel>(w.Main.Current);
        Assert.Equal(["Salvage"], Titles(afterSave));
        Assert.Equal(("Salvage", "Salvage"), (w.Selected, w.Highlighted));
        Assert.Equal(1, w.Dice.Calls);                                      // only the one roll made by hand
        Assert.Empty(w.Asked);
    }

    [Fact]
    public void Starting_an_edit_while_an_auto_follow_roll_is_in_the_air_stops_it_and_nothing_navigates_afterwards()
    {
        using var w = new World();
        var b = w.Save("B");
        w.Save("A", b.Id);
        w.Start(autoFollow: true, dice: new GatedDice());
        var session = w.Open("A");
        session.RollCommand.Execute(null);                                  // A's roll is in the air
        Assert.True(session.IsRolling);

        w.Main.EditTableCommand.Execute(null);                              // leaving the Roll screen stops the roll

        var review = Assert.IsType<ReviewViewModel>(w.Main.Current);
        Assert.False(session.IsRolling);
        Assert.Equal(["A"], Titles(session));                               // nothing was followed
        Assert.Equal("A", review.TableName);
        Assert.Equal(("A", "A"), (w.Selected, w.Highlighted));
        Assert.Empty(w.Main.RecentRolls);

        review.CancelCommand.Execute(null);
        Assert.Equal(["A"], Titles(Assert.IsType<RollViewModel>(w.Main.Current)));
        Assert.Equal(("A", "A"), (w.Selected, w.Highlighted));
    }

    [Fact]
    public void Following_a_link_selects_the_destination_in_the_list_without_reopening_or_rolling_anything()
    {
        using var w = new World();
        var items = w.Save("Scavenged Items");
        w.Save("Scavenging", items.Id);
        w.Start();
        var session = w.Open("Scavenging");
        session.RollCommand.Execute(null);

        session.Results[0].FollowCommand!.Execute(null);

        Assert.Same(session, w.Main.Current);                               // the same Roll screen, trail intact
        Assert.Equal(["Scavenging", "Scavenged Items"], Titles(session));
        Assert.Equal(("Scavenged Items", "Scavenged Items"), (w.Selected, w.Highlighted));
        Assert.Equal(1, w.Dice.Calls);
        Assert.Single(w.Main.RecentRolls);
        Assert.Equal("Scavenged Items", w.Main.RecentTables[0].Name);

        session.ReturnToOriginalCommand.Execute(null);                      // and Back selects the original again

        Assert.Same(session, w.Main.Current);
        Assert.Equal(("Scavenging", "Scavenging"), (w.Selected, w.Highlighted));
        Assert.Equal(["Scavenging", "Scavenged Items", "Scavenging"], Titles(session));
        Assert.Equal(1, w.Dice.Calls);
    }

    [Fact]
    public void Edit_table_after_following_a_link_edits_the_table_shown_and_Cancel_comes_back_to_it()
    {
        using var w = new World();
        var items = w.Save("Scavenged Items");
        w.Save("Scavenging", items.Id);
        w.Start();
        var session = w.Open("Scavenging");
        session.RollCommand.Execute(null);
        session.Results[0].FollowCommand!.Execute(null);

        w.Main.EditTableCommand.Execute(null);

        var review = Assert.IsType<ReviewViewModel>(w.Main.Current);
        Assert.Equal("Scavenged Items", review.TableName);
        review.CancelCommand.Execute(null);
        Assert.Equal("Scavenged Items", Assert.IsType<RollViewModel>(w.Main.Current).Title);
    }

    [Fact]
    public void Delete_table_after_following_a_link_names_and_deletes_the_table_shown_and_never_the_original()
    {
        using var w = new World();
        var items = w.Save("Scavenged Items");
        w.Save("Scavenging", items.Id);
        w.Start();
        var session = w.Open("Scavenging");
        session.RollCommand.Execute(null);
        session.Results[0].FollowCommand!.Execute(null);

        w.Answer = false;
        w.Main.DeleteTableCommand.Execute(null);                            // declined: nothing changes

        Assert.StartsWith("Delete the table \"Scavenged Items\"?", Assert.Single(w.Asked));
        Assert.DoesNotContain("\"Scavenging\"", w.Asked[0]);
        Assert.Same(session, w.Main.Current);
        Assert.Equal(2, w.Main.Tables.Count);

        w.Answer = true;
        w.Main.DeleteTableCommand.Execute(null);

        Assert.Equal(["Scavenging"], w.Main.Tables.Select(t => t.Name).ToArray());
        Assert.NotNull(w.Db.LoadTable(w.Main.Tables[0].Id));
    }

    [Fact]
    public void An_auto_follow_chain_leaves_the_last_table_selected_and_Back_selects_the_first()
    {
        using var w = new World();
        var c = w.Save("C");
        var b = w.Save("B", c.Id);
        w.Save("A", b.Id);
        w.Start(autoFollow: true);
        var session = w.Open("A");

        session.RollCommand.Execute(null);

        Assert.Equal(["A", "B", "C"], Titles(session));
        Assert.Same(session, w.Main.Current);
        Assert.Equal(("C", "C"), (w.Selected, w.Highlighted));
        Assert.Equal(3, w.Dice.Calls);                                      // one roll per table, nothing extra from selecting
        Assert.Equal(3, w.Main.RecentRolls.Count);

        w.Main.EditTableCommand.Execute(null);
        Assert.Equal("C", Assert.IsType<ReviewViewModel>(w.Main.Current).TableName);
    }

    [Fact]
    public void A_destination_the_list_does_not_show_leaves_nothing_selected_so_Edit_and_Delete_cannot_act_on_another_table()
    {
        using var w = new World();
        var loot = w.Db.CreateFolder(w.Collection.Id, "Loot");
        var items = w.Save("Scavenged Items", folderId: loot.Id);
        w.Save("Scavenging", items.Id);
        w.Save("Weather");
        w.Start();
        w.Main.SelectedFolderNav = w.Main.FolderNav.Single(f => f.Name == "Unfiled");   // the list shows only Scavenging and Weather
        var session = w.Open("Scavenging");
        session.RollCommand.Execute(null);

        session.Results[0].FollowCommand!.Execute(null);

        Assert.Same(session, w.Main.Current);
        Assert.Null(w.Selected);
        Assert.Null(w.Highlighted);
        Assert.False(w.Main.EditTableCommand.CanExecute(null));
        Assert.False(w.Main.DeleteTableCommand.CanExecute(null));

        w.Main.TableFilter = "Weath";                                       // a search that doesn't show it either
        Assert.Null(w.Selected);
        Assert.False(w.Main.DeleteTableCommand.CanExecute(null));
        w.Main.TableFilter = "";

        w.Main.SelectedFolderNav = w.Main.FolderNav.Single(f => f.IsAllTables);          // once the list shows it, it is selected
        Assert.Equal(("Scavenged Items", "Scavenged Items"), (w.Selected, w.Highlighted));
        Assert.Same(session, w.Main.Current);
        w.Answer = false;
        w.Main.DeleteTableCommand.Execute(null);
        Assert.StartsWith("Delete the table \"Scavenged Items\"?", Assert.Single(w.Asked));
    }

    [Fact]
    public void A_Roll_screen_no_longer_shown_never_moves_the_list()
    {
        using var w = new World();
        var items = w.Save("Scavenged Items");
        w.Save("Scavenging", items.Id);
        w.Save("Weather");
        w.Start();
        var old = w.Open("Scavenging");
        old.RollCommand.Execute(null);
        var spare = old.Results[0].FollowCommand!;
        var weather = w.Open("Weather");

        spare.Execute(null);                                                // a stale link on the screen that was left

        Assert.Same(weather, w.Main.Current);
        Assert.Equal(("Weather", "Weather"), (w.Selected, w.Highlighted));
    }

    [Fact]
    public void Help_and_Tips_opens_the_guide_in_the_browser_and_is_hidden_without_one()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var opened = new List<string>();
        var main = new MainViewModel(db, new FixedDice(1), openWebPage: opened.Add);

        Assert.True(main.HasHelp);
        main.HelpCommand.Execute(null);

        Assert.Equal(["https://rpgfrequencies.com/gidubba-guide.html"], opened);

        var without = new MainViewModel(db, new FixedDice(1));
        Assert.False(without.HasHelp);
        Assert.False(without.HelpCommand.CanExecute(null));
    }
}

/// <summary>The same, in the real window: the Back button, the list's selection, and where the moved controls now sit.</summary>
[Collection("UI")]
public class ReturnAndSelectionViewTests
{
    private static Rect BoundsIn(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    [Fact]
    public void Back_appears_after_following_a_link_returns_below_the_trail_and_the_list_follows_the_table_shown()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));
            var list = ui.One<ListBox>(l => l.Name == "TablesList");
            ui.SelectTable("Scavenging");
            Assert.False(ui.HasVisibleButton("← Back to Scavenging"));

            ui.Click("Roll");
            ui.Click("Open Scavenged Items");

            Assert.Equal("Scavenged Items", ((TableSummary)list.SelectedItem).Name);
            Assert.True(ui.HasVisibleButton("← Back to Scavenging"));
            ui.Dice.Value = 12;
            ui.Click("Roll");

            ui.Click("← Back to Scavenging");

            var session = (RollViewModel)ui.Main.Current!;
            Assert.Equal(3, session.Steps.Count);
            Assert.Equal("Scavenging", ((TableSummary)list.SelectedItem).Name);
            Assert.False(ui.HasVisibleButton("← Back to Scavenging"));
            Assert.Equal(["Scavenging", "Scavenged Items", "Scavenging"], ui.Texts().Where(t => t.FontSize == 24).Select(t => t.Text).ToArray());
            Assert.Equal(["1x Scavenged Item", "D4 rations"], ui.Texts().Where(t => t.FontSize == 26).Select(t => t.Text).ToArray());
            Assert.Contains(ui.Texts(), t => t.Name == "ReturnedText");
            Assert.Equal(2, ui.Dice.Calls);

            ui.Click("Edit table");
            Assert.Equal("Scavenging", ((ReviewViewModel)ui.Main.Current!).TableName);
        });
    }

    [Fact]
    public void The_Help_and_Tips_button_opens_the_guide_and_F1_is_left_alone()
    {
        Sta.Run(() =>
        {
            using var temp = new TempDatabase();
            var db = temp.Open();
            db.CreateCollection("Dungeon");
            var opened = new List<string>();
            var window = new MainWindow
            {
                DataContext = new MainViewModel(db, new FixedDice(1), openWebPage: opened.Add),
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false,
            };
            window.Show();
            var source = PresentationSource.FromVisual(window)!;
            void Pump() => window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => { });
            bool Press(RoutedEvent routed)
            {
                var target = (IInputElement?)System.Windows.Input.Keyboard.FocusedElement ?? window;
                var args = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.F1)
                    { RoutedEvent = routed };
                target.RaiseEvent(args);
                Pump();
                return args.Handled;
            }
            try
            {
                Pump();
                Assert.False(Press(System.Windows.Input.Keyboard.PreviewKeyDownEvent)); // F1 is not a Gidubba shortcut: not taken,
                Assert.False(Press(System.Windows.Input.Keyboard.KeyDownEvent));        // so ordinary keyboard handling is untouched
                Assert.False(Press(System.Windows.Input.Keyboard.PreviewKeyUpEvent));
                Assert.Empty(opened);

                var help = ViewTests.FindAll<Button>(window).Single(b => b.Name == "HelpButton");
                Assert.Equal("Help & Tips", help.Content);
                Assert.True(help.IsVisible);
                ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(help)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();          // a click
                Pump();
                Assert.Equal([MainViewModel.HelpUrl], opened);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void The_used_results_bar_is_part_of_the_Roll_controls_right_under_Roll()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(1, (db, c) => db.SaveTable(new RollableTable
            {
                CollectionId = c.Id,
                Name = "Demands",
                Dice = DiceExpression.Parse("d4"),
                ResultSets = [new ResultSet { Entries = Enumerable.Range(1, 4).Select(v => new TableEntry { Min = v, Max = v, Text = $"Demand {v}", IsUnique = true }).ToList() }],
            }));
            ui.SelectTable("Demands");
            ui.Click("Roll");

            var bar = ui.One<WrapPanel>(p => p.Name == "UsedResultsBar");
            var controls = ui.One<StackPanel>(p => p.Name == "RollControls");
            Assert.True(bar.IsVisible);
            Assert.Same(controls, VisualTreeHelper.GetParent(bar));
            Assert.True(BoundsIn(bar, ui.Window).Top > BoundsIn(ui.One<Button>(b => b.Name == "RollButton"), ui.Window).Bottom);
            Assert.Contains(ui.Texts(), t => t.Text == "Unique results: 1 of 4 used");
            Assert.True(ui.One<Button>(b => b.Name == "ResetUsedResultsButton").IsEnabled);
        });
    }

    [Fact]
    public void Mark_All_Unique_and_Clear_Unique_sit_above_the_rows_in_both_editor_views()
    {
        Sta.Run(() =>
        {
            static ResultSet Set(string name) => new()
            {
                Name = name,
                Entries = [new TableEntry { Min = 1, Max = 3, Text = $"{name} low" }, new TableEntry { Min = 4, Max = 6, Text = $"{name} high", SortOrder = 1 }],
            };
            using var ui = new UiHarness(1, (db, c) => db.SaveTable(new RollableTable
            {
                CollectionId = c.Id, Name = "Obstacle", Dice = DiceExpression.Parse("d6"), ResultSets = [Set("Difficulty"), Set("Modifier")],
            }));
            ui.SelectTable("Obstacle");
            ui.Click("Edit table");
            var review = (ReviewViewModel)ui.Main.Current!;

            void AboveRows(string rowsList)
            {
                var rows = BoundsIn(ui.All<ItemsControl>().Single(i => i.Name == rowsList && i.IsVisible), ui.Window);
                foreach (var name in new[] { "Mark All Unique", "Clear Unique" })
                    Assert.True(BoundsIn(ui.One<Button>(b => b.IsVisible && b.Content as string == name), ui.Window).Bottom <= rows.Top,
                        $"'{name}' should be above {rowsList}");
                AfterClearFormatting(ui);
            }

            Assert.True(review.IsAligned);
            AboveRows("AlignedRowsList");
            ui.Click("Mark All Unique");
            Assert.All(review.ResultSets.SelectMany(s => s.Rows), r => Assert.True(r.IsUnique));     // every set, as before

            review.ResultSets[0].Rows[0].RangeText = "1-2";                                         // no longer aligned
            ui.Layout();
            Assert.False(review.IsAligned);
            AboveRows("RowsList");
            ui.Click("Clear Unique");
            Assert.All(review.SelectedResultSet.Rows, r => Assert.False(r.IsUnique));                // just this set, as before
            Assert.Contains(review.ResultSets.Where(s => s != review.SelectedResultSet).SelectMany(s => s.Rows), r => r.IsUnique);
        });
    }

    /// <summary>
    /// The Unique group comes right after Clear Formatting on the formatting toolbar, a little apart, and fits inside the editor pane:
    /// on the same line when there is room, otherwise wrapped onto the next line rather than clipped. Returns whether it shared the line.
    /// </summary>
    private static bool AfterClearFormatting(UiHarness ui)
    {
        var clear = BoundsIn(ui.One<Button>(b => b.Name == "ClearFormattingButton"), ui.Window);
        var group = ui.One<StackPanel>(p => p.Name == "UniqueBulkBar");
        var bar = ui.One<WrapPanel>(p => p.Name == "FormattingBar");
        var groupBounds = BoundsIn(group, ui.Window);
        var barBounds = BoundsIn(bar, ui.Window);
        var pane = BoundsIn((FrameworkElement)VisualTreeHelper.GetParent(bar), ui.Window);
        Assert.Same(bar, VisualTreeHelper.GetParent(group));
        Assert.Equal(bar.Children.IndexOf(ui.One<Button>(b => b.Name == "ClearFormattingButton")) + 1, bar.Children.IndexOf(group));
        var sameLine = groupBounds.Top < clear.Bottom;
        if (sameLine) Assert.InRange(groupBounds.Left - clear.Right, 8, 30);
        else Assert.InRange(groupBounds.Left - barBounds.Left, 0, 30);
        foreach (var button in ui.All<Button>().Where(b => b.IsVisible && group.IsAncestorOf(b)))
            Assert.True(BoundsIn(button, ui.Window).Right <= pane.Right + 1, $"'{button.Content}' is clipped by the editor pane: {BoundsIn(button, ui.Window)} vs {pane}");
        return sameLine;
    }

    [Theory]
    [InlineData(1000, 640, false)]
    [InlineData(1366, 768, true)]
    [InlineData(1920, 1080, true)]
    public void The_Unique_group_follows_Clear_Formatting_and_is_never_clipped(int width, int height, bool sameLine)
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(1, (db, c) => db.SaveTable(Fixtures.ScavengedItems(c.Id)));
            ui.Window.Width = width;
            ui.Window.Height = height;
            ui.Layout();
            ui.SelectTable("Scavenged Items");
            ui.Click("Edit table");
            ui.Layout();
            Assert.Equal(sameLine, AfterClearFormatting(ui));
        });
    }
}
