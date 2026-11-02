using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>A real MainWindow over a temporary database, plus a listener that turns WPF binding errors into test failures.</summary>
internal sealed class UiHarness : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly BindingErrorListener _listener = new();

    public FixedDice Dice { get; }

    /// <summary>Everything Copy Table Text put on the "clipboard" (the real one is never touched by tests).</summary>
    public List<string> Copied { get; } = [];

    /// <summary>Where Save Foundry JSON… or Save Tables+ JSON… "chooses" to save (null: the person cancels), and every file name
    /// and dialog title it asked with.</summary>
    public string? SavePath { get; set; }
    public List<string> SuggestedFileNames { get; } = [];
    public List<string> SaveTitles { get; } = [];

    /// <summary>Every PUM Companion export review shown, and whether the next one answers Save Bundle… (true) or Cancel.</summary>
    public List<PumExportReviewViewModel> PumReviews { get; } = [];
    public bool SavePumReview { get; set; } = true;

    /// <summary>The file Import Collection… "chooses" (null: the person cancels). Its confirmation is always accepted.</summary>
    public string? ImportPath { get; set; }
    public AppDatabase Db { get; }
    public Collection? Collection { get; }
    public MainViewModel Main { get; }
    public MainWindow Window { get; }

    /// <param name="seed">Creates a "Dungeon" collection and lets the test fill it. Omit it for a brand-new installation
    /// (first launch): an empty database with no collections at all.</param>
    public UiHarness(int roll, Action<AppDatabase, Collection>? seed = null)
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        Db = _temp.Open();
        if (seed is not null)
        {
            Collection = Db.CreateCollection("Dungeon");
            seed(Db, Collection);
        }
        Dice = new FixedDice(roll);
        Main = new MainViewModel(Db, Dice, (_, _) => true, copyText: Copied.Add, chooseSaveFile: ChooseSave,
            chooseCollectionFile: ChooseSave, chooseImportFile: () => ImportPath, confirmImport: _ => true,
            reviewPumExport: review => { PumReviews.Add(review); return SavePumReview; }, choosePumFile: ChooseSave,
            now: () => new DateTime(2026, 10, 24, 12, 0, 0));
        Window = new MainWindow
        {
            DataContext = Main,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
        };
        Window.Show();
        Layout();
    }

    private string? ChooseSave(SaveFileRequest request)
    {
        SuggestedFileNames.Add(request.SuggestedName);
        SaveTitles.Add(request.Title);
        return SavePath;
    }

    /// <summary>Lets queued work run as the real message loop would, then lays out.</summary>
    public void Layout()
    {
        Window.Dispatcher.Invoke(DispatcherPriority.ContextIdle, () => { });
        Window.UpdateLayout();
    }

    public void SelectTable(string name)
    {
        var list = One<ListBox>(l => l.Name == "TablesList");
        list.SelectedItem = list.Items.Cast<TableSummary>().Single(t => t.Name == name); // highlights, as a click or arrow key would
        list.ScrollIntoView(list.SelectedItem); // a virtualized row needs to be realized before its container exists
        Layout();
        // ...and the click's mouse-up is what opens it (arrow keys alone only highlight).
        var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        item.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            Source = item,
        });
        Layout();
    }

    public void Click(string content)
    {
        var button = One<Button>(b => b.IsVisible && b.Content as string == content);
        Layout();
        Assert.True(button.IsEnabled, $"'{content}' should be enabled");
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        Layout();
    }

    /// <summary>Opens the Roll screen's Export… menu with its button, then chooses <paramref name="header"/> from it.</summary>
    public void ChooseExport(string header)
    {
        var button = One<Button>(b => b.IsVisible && b.Content as string == "Export…");
        Click("Export…");
        Assert.True(button.ContextMenu.IsOpen, "Export… should open its menu");
        var item = button.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Header as string == header);
        Assert.True(item.IsEnabled, $"'{header}' should be enabled");
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
        Layout();
    }

    /// <summary>
    /// Raises a plain key press (no modifiers) on an element, as the keyboard would. WPF matches key bindings against the
    /// machine's real modifier-key state, so a modifier held down elsewhere at that instant can make a press miss; when an
    /// <paramref name="until"/> condition is given, the press is repeated, with a short settling pause between attempts,
    /// until its effect is visible.
    /// </summary>
    public void Press(FrameworkElement target, System.Windows.Input.Key key, Func<bool>? until = null)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (attempt > 0) Thread.Sleep(30);
            Layout();
            target.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent,
            });
            Layout();
            if (until is null || until()) return;
        }
    }

    /// <summary>The element that currently has logical keyboard focus in the window.</summary>
    public IInputElement? Focused => System.Windows.Input.FocusManager.GetFocusedElement(Window);

    public bool HasVisibleButton(string content) => All<Button>().Any(b => b.IsVisible && b.Content as string == content);

    /// <summary>Visible text on screen (collapsed elements are ignored).</summary>
    public List<TextBlock> Texts() => All<TextBlock>().Where(t => t.IsVisible).ToList();

    public T One<T>(Func<T, bool> where) where T : DependencyObject => All<T>().Single(where);

    public IEnumerable<T> All<T>() where T : DependencyObject => ViewTests.FindAll<T>(Window);

    public void Dispose()
    {
        try { Window.Close(); }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
            _temp.Dispose();
        }
        Assert.Empty(_listener.Errors);
    }
}

[Collection("UI")]
public class LinkedViewTests
{
    [Fact]
    public void Multi_result_set_table_opens_from_the_list_and_one_roll_shows_every_output()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(68, (db, c) => db.SaveTable(Fixtures.RoomFeatures(c.Id)));

            ui.SelectTable("Room Features");
            Assert.Contains(ui.Texts(), t => t.Text == "Room Features" && t.FontSize == 24);
            Assert.Contains(ui.Texts(), t => t.Text == "Not rolled yet.");
            ui.Click("Roll");

            // The number is shown once, not once per result set.
            Assert.Equal(["Rolled 68"], ui.Texts().Where(t => t.Text.StartsWith("Rolled")).Select(t => t.Text).ToArray());

            var outputs = ui.Texts().Where(t => t.FontSize == 26).Select(t => t.Text).ToArray();
            Assert.Equal(["Smell of burning flesh", "Hissing", "Grated floors reveal dozens of people below"], outputs);
            foreach (var heading in new[] { "Ambient", "Noise", "General Feature" })
                Assert.Contains(ui.Texts(), t => t.Text == heading && t.FontWeight == FontWeights.SemiBold);
            Assert.Equal(1, ui.Dice.Calls);
        });
    }

    [Fact]
    public void A_linked_result_can_be_followed_and_the_child_is_rolled_only_when_the_user_presses_Roll()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(6, (db, c) => Fixtures.SeedScavenging(db, c.Id));

            ui.SelectTable("Scavenging");
            ui.Click("Roll");
            Assert.Contains(ui.Texts(), t => t.Text == "1x Scavenged Item" && t.FontSize == 26);
            Assert.True(ui.HasVisibleButton("Open Scavenged Items"));

            // Follow: the child becomes current but is NOT rolled.
            ui.Click("Open Scavenged Items");
            Assert.Equal(1, ui.Dice.Calls);
            Assert.Equal(2, ((RollViewModel)ui.Main.Current!).Steps.Count);
            Assert.Contains(ui.Texts(), t => t.Text == "Scavenged Items" && t.FontSize == 24);
            Assert.Contains(ui.Texts(), t => t.Text == "Not rolled yet.");               // the child shows as un-rolled
            Assert.Equal(["Rolled 6"], ui.Texts().Where(t => t.Text.StartsWith("Rolled")).Select(t => t.Text).ToArray());
            Assert.Equal(["1x Scavenged Item"], ui.Texts().Where(t => t.FontSize == 26).Select(t => t.Text).ToArray());
            Assert.False(ui.HasVisibleButton("Open Scavenged Items"));                    // parent's action is spent
            Assert.Contains(ui.Texts(), t => t.Text == "→ Scavenged Items");

            // Now the user rolls the child: parent context stays visible above it.
            ui.Dice.Value = 12;
            ui.Click("Roll");
            Assert.Equal(2, ui.Dice.Calls);
            Assert.Equal(["Rolled 6", "Rolled 12"], ui.Texts().Where(t => t.Text.StartsWith("Rolled")).Select(t => t.Text).ToArray());
            Assert.Equal(["1x Scavenged Item", "D4 rations"], ui.Texts().Where(t => t.FontSize == 26).Select(t => t.Text).ToArray());
            Assert.Equal(["Scavenging", "Scavenged Items"], ui.Texts().Where(t => t.FontSize == 24).Select(t => t.Text).ToArray());

            // Rolling the child again stays grouped under it rather than adding a level.
            ui.Dice.Value = 20;
            ui.Click("Roll");
            Assert.Equal(2, ((RollViewModel)ui.Main.Current!).Steps.Count);
            Assert.Equal(["Rolled 6", "Rolled 12", "Rolled 20"], ui.Texts().Where(t => t.Text.StartsWith("Rolled")).Select(t => t.Text).ToArray());
        });
    }

    [Fact]
    public void Rolling_a_followed_child_rolls_the_childs_own_dice_and_leaves_the_parent_alone()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(19, (db, c) => Fixtures.SeedTemperature(db, c.Id));

            ui.SelectTable("Temperature");
            ui.Click("Roll");
            ui.Click("Open Unusual Temperature");
            Assert.Contains(ui.Texts(), t => t.Text == "d4 · legal rolls 1–4");
            Assert.Contains(ui.Texts(), t => t.Text == "Not rolled yet.");
            Assert.Equal(["d20"], ui.Dice.Requested);                                  // following never rolls

            ui.Dice.Value = 3;
            ui.Click("Roll");

            Assert.Equal(["d20", "d4"], ui.Dice.Requested);                            // the child's expression, not the parent's
            var session = (RollViewModel)ui.Main.Current!;
            Assert.Equal("Unusual Temperature", session.Current.Table.Name);
            Assert.Single(session.Steps[0].Outcomes);                                   // the parent was not rerolled
            Assert.Single(session.Steps[1].Outcomes);
            Assert.DoesNotContain(ui.Texts(), t => t.Text == "Not rolled yet.");
            Assert.Equal(["Rolled 19", "Rolled 3"], ui.Texts().Where(t => t.Text.StartsWith("Rolled")).Select(t => t.Text).ToArray());
            Assert.Equal(["Unusual Temperature", "Heatwave"], ui.Texts().Where(t => t.FontSize == 26).Select(t => t.Text).ToArray());
        });
    }

    [Fact]
    public void An_unresolved_link_is_shown_as_such_with_no_follow_action()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(9, (db, c) => db.SaveTable(Fixtures.Scavenging(c.Id, null, unresolvedName: "Scavenged Items")));

            ui.SelectTable("Scavenging");
            ui.Click("Roll");

            Assert.Contains(ui.Texts(), t => t.Text == "2x Scavenged Items" && t.FontSize == 26);
            Assert.Contains(ui.Texts(), t => t.Text.StartsWith("⚠ Unresolved link to “Scavenged Items”"));
            Assert.False(ui.HasVisibleButton("Open Scavenged Items"));
            Assert.Single(((RollViewModel)ui.Main.Current!).Steps);
        });
    }

    [Fact]
    public void Editing_a_multi_set_table_switches_sets_adds_and_deletes_rows_and_sets()
    {
        Sta.Run(() =>
        {
            using var ui = new UiHarness(1, (db, c) =>
            {
                var (items, _) = Fixtures.SeedScavenging(db, c.Id);
                db.SaveTable(Fixtures.RoomFeatures(c.Id));
            });

            ui.SelectTable("Room Features");
            ui.Click("Edit table");

            var tabs = ui.One<ListBox>(l => l.Name == "ResultSetTabs");
            var rows = ui.One<ItemsControl>(i => i.Name == "RowsList");
            Assert.Equal(3, tabs.Items.Count);
            Assert.Equal(4, rows.Items.Count);                                   // Ambient
            Assert.Equal("Ambient", ui.One<TextBox>(t => t.Name == "SetNameBox").Text);

            tabs.SelectedIndex = 1;                                             // Noise, with its own five rows
            ui.Layout();
            Assert.Equal(5, rows.Items.Count);
            Assert.Equal("Noise", ui.One<TextBox>(t => t.Name == "SetNameBox").Text);
            Assert.Equal(5, ui.All<ComboBox>().Count(c => rows.IsAncestorOf(c)));  // each row has a link chooser
            var noiseLinkChoices = ui.All<ComboBox>().First(c => rows.IsAncestorOf(c)).Items.Cast<LinkChoice>().Select(c => c.Label).ToArray();
            Assert.Equal(["(no link)", "Unresolved name…", "Room Features", "Scavenged Items", "Scavenging"], noiseLinkChoices);

            ui.Click("Add result set");
            Assert.Equal(4, tabs.Items.Count);
            Assert.Single(rows.Items);
            ui.One<TextBox>(t => t.Name == "SetNameBox").Text = "Lighting";
            ui.Layout();
            Assert.Equal("Lighting", ((ResultSetEditorViewModel)tabs.SelectedItem).DisplayName);

            ui.Click("Add row");
            Assert.Equal(2, rows.Items.Count);
            Assert.Contains(ui.Texts(), t => t.Text.Contains("Lighting: Row 2 includes 101"));   // validation reported under the set's name
            Assert.Contains(ui.All<TextBlock>(), t => t.Name == "NotesText" && t.IsVisible);     // and attached to the row

            ui.Click("Delete result set");
            Assert.Equal(3, tabs.Items.Count);
            Assert.DoesNotContain(ui.Texts(), t => t.Text.Contains("Lighting"));
            Assert.True(ui.One<Button>(b => b.Content as string == "Save Table").IsEnabled);
        });
    }
}
