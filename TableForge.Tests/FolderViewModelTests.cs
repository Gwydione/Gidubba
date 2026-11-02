using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>Folder navigation, search, recent tables, paste defaults and editing, through <see cref="MainViewModel"/> on a real database.</summary>
public class FolderViewModelTests
{
    private sealed class World : IDisposable
    {
        public TempDatabase Temp { get; } = new();
        public AppDatabase Db { get; }
        public Collection Collection { get; }
        public bool ConfirmAnswer { get; set; } = true;
        public List<string> Confirmations { get; } = [];
        public MainViewModel Main { get; private set; } = null!;

        public World()
        {
            Db = Temp.Open();
            Collection = Db.CreateCollection("Broken Shores");
        }

        /// <summary>Builds the view model once the database has been seeded, exactly as the app would open on existing data.</summary>
        public MainViewModel Start() => Main = new MainViewModel(Db, new FixedDice(1), (message, _) => { Confirmations.Add(message); return ConfirmAnswer; });

        public void Save(string name, long? folderId = null) =>
            Db.SaveTable(Fixtures.ParseAndBuild($"d6 {name}\n1-6 x", Collection.Id).Also(t => t.FolderId = folderId));

        /// <summary>Selects a folder scope by name in <see cref="MainViewModel.FolderNav"/> ("All Tables" or "Unfiled" work too).</summary>
        public void SelectFolder(string name) => Main.SelectedFolderNav = Main.FolderNav.Single(f => f.Name == name);

        public void Dispose() => Temp.Dispose();
    }

    // ---- navigation ------------------------------------------------------------------------------

    [Fact]
    public void Folder_nav_always_starts_with_all_tables_and_unfiled_in_fixed_position()
    {
        using var w = new World();
        w.Start();

        Assert.Equal(["All Tables", "Unfiled"], w.Main.FolderNav.Take(2).Select(f => f.Name).ToArray());
        Assert.True(w.Main.FolderNav[0].IsAllTables);
        Assert.True(w.Main.FolderNav[1] is { IsAllTables: false, FolderId: null });
    }

    [Fact]
    public void All_tables_shows_every_table_regardless_of_folder()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Weather");
        w.Save("Critical Injuries", combat.Id);
        w.Start();

        w.SelectFolder("All Tables");

        Assert.Equal(["Critical Injuries", "Weather"], w.Main.Tables.Select(t => t.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Unfiled_shows_only_tables_with_no_folder()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Weather");
        w.Save("Critical Injuries", combat.Id);
        w.Start();

        w.SelectFolder("Unfiled");

        Assert.Equal(["Weather"], w.Main.Tables.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void A_folder_view_shows_only_tables_assigned_to_it()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        var exploration = w.Db.CreateFolder(w.Collection.Id, "Exploration");
        w.Save("Weather", exploration.Id);
        w.Save("Critical Injuries", combat.Id);
        w.Save("Reactions", combat.Id);
        w.Start();

        w.SelectFolder("Combat");

        Assert.Equal(["Critical Injuries", "Reactions"], w.Main.Tables.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void Empty_folders_remain_visible_in_navigation()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();

        Assert.Contains(w.Main.FolderNav, f => f.Name == "Combat");
        w.SelectFolder("Combat");
        Assert.Empty(w.Main.Tables);
    }

    [Fact]
    public void Switching_collection_refreshes_folder_navigation_and_defaults_to_all_tables()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        var other = w.Db.CreateCollection("Ker Nethalas");
        w.Db.CreateFolder(other.Id, "Rituals");
        w.Start();
        w.SelectFolder("Combat");

        w.Main.SelectedCollection = w.Main.Collections.Single(c => c.Name == "Ker Nethalas");

        Assert.Equal(["All Tables", "Unfiled", "Rituals"], w.Main.FolderNav.Select(f => f.Name).ToArray());
        Assert.True(w.Main.SelectedFolderNav!.IsAllTables);
    }

    // ---- table list heading --------------------------------------------------------------------------

    [Fact]
    public void Table_list_heading_names_the_selected_scope_and_follows_folder_changes()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Character Creation");
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        var raised = new List<string?>();
        w.Main.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("All tables", w.Main.TableListHeading);

        w.SelectFolder("Unfiled");
        Assert.Equal("Unfiled tables", w.Main.TableListHeading);

        w.SelectFolder("Character Creation");
        Assert.Equal("Character Creation tables", w.Main.TableListHeading);

        w.SelectFolder("Combat");
        Assert.Equal("Combat tables", w.Main.TableListHeading);

        w.SelectFolder("All Tables");
        Assert.Equal("All tables", w.Main.TableListHeading);
        Assert.Equal(4, raised.Count(p => p == nameof(MainViewModel.TableListHeading)));
    }

    [Fact]
    public void Table_list_heading_says_search_results_while_searching_regardless_of_folder()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        w.SelectFolder("Combat");

        w.Main.TableFilter = "weather";
        Assert.Equal("Search results", w.Main.TableListHeading);

        w.Main.TableFilter = "   ";
        Assert.Equal("Combat tables", w.Main.TableListHeading); // whitespace-only is not a search, matching the list filter

        w.Main.TableFilter = "x";
        w.Main.ClearFilterCommand.Execute(null);
        Assert.Equal("Combat tables", w.Main.TableListHeading);
    }

    // ---- folder commands (create / rename / delete) ------------------------------------------------

    [Fact]
    public void New_folder_command_creates_and_selects_the_folder()
    {
        using var w = new World();
        w.Start();
        w.Main.NewFolderName = "Combat";

        w.Main.NewFolderCommand.Execute(null);

        Assert.Contains(w.Main.FolderNav, f => f.Name == "Combat");
        Assert.Equal("Combat", w.Main.SelectedFolderNav!.Name);
        Assert.Equal("", w.Main.NewFolderName);
    }

    [Fact]
    public void New_folder_command_is_disabled_for_a_blank_name()
    {
        using var w = new World();
        w.Start();
        w.Main.NewFolderName = "   ";
        Assert.False(w.Main.NewFolderCommand.CanExecute(null));
    }

    [Fact]
    public void Duplicate_new_folder_name_reports_a_status_message_and_creates_nothing()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        w.Main.NewFolderName = "combat";

        w.Main.NewFolderCommand.Execute(null);

        Assert.Single(w.Db.GetFolders(w.Collection.Id));
        Assert.Contains("Could not create the folder", w.Main.Status);
    }

    [Fact]
    public void Rename_folder_command_renames_the_selected_folder_and_refills_recent_names()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        w.SelectFolder("Combat");
        Assert.Equal("Combat", w.Main.RenameFolderName); // pre-filled when a real folder is selected

        w.Main.RenameFolderName = "Skirmishes";
        w.Main.RenameFolderCommand.Execute(null);

        Assert.Equal("Skirmishes", w.Main.SelectedFolderNav!.Name);
        Assert.Contains(w.Main.FolderNav, f => f.Name == "Skirmishes");
    }

    [Fact]
    public void Rename_and_delete_folder_commands_are_disabled_for_all_tables_and_unfiled()
    {
        using var w = new World();
        w.Start();

        w.SelectFolder("All Tables");
        Assert.False(w.Main.RenameFolderCommand.CanExecute(null));
        Assert.False(w.Main.DeleteFolderCommand.CanExecute(null));

        w.SelectFolder("Unfiled");
        Assert.False(w.Main.RenameFolderCommand.CanExecute(null));
        Assert.False(w.Main.DeleteFolderCommand.CanExecute(null));
    }

    [Fact]
    public void Delete_folder_command_asks_for_confirmation_moves_tables_to_unfiled_and_selects_all_tables()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Critical Injuries", combat.Id);
        w.Save("Reactions", combat.Id);
        w.Start();
        w.SelectFolder("Combat");

        w.Main.DeleteFolderCommand.Execute(null);

        Assert.Contains(w.Confirmations, m => m.Contains("Combat") && m.Contains("2") && m.Contains("Unfiled"));
        Assert.Empty(w.Db.GetFolders(w.Collection.Id));
        Assert.All(w.Db.GetTableSummaries(w.Collection.Id), t => Assert.Null(t.FolderId));
        Assert.True(w.Main.SelectedFolderNav!.IsAllTables); // the deleted folder can no longer be selected
    }

    [Fact]
    public void Declining_the_delete_confirmation_keeps_the_folder_and_its_tables()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Critical Injuries", combat.Id);
        w.Start();
        w.SelectFolder("Combat");
        w.ConfirmAnswer = false;

        w.Main.DeleteFolderCommand.Execute(null);

        Assert.Single(w.Db.GetFolders(w.Collection.Id));
        Assert.Equal(combat.Id, Assert.Single(w.Db.GetTableSummaries(w.Collection.Id)).FolderId);
    }

    // ---- search: always collection-wide -----------------------------------------------------------

    [Fact]
    public void Search_finds_tables_in_other_folders_while_a_specific_folder_is_selected()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        var exploration = w.Db.CreateFolder(w.Collection.Id, "Exploration");
        w.Save("Weather", exploration.Id);
        w.Save("Critical Injuries", combat.Id);
        w.Start();
        w.SelectFolder("Combat");

        w.Main.TableFilter = "weather";

        Assert.Equal(["Weather"], w.Main.Tables.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void Clearing_the_search_restores_the_folder_scoped_view()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Weather");
        w.Save("Critical Injuries", combat.Id);
        w.Start();
        w.SelectFolder("Combat");

        w.Main.TableFilter = "weather";
        Assert.Single(w.Main.Tables);
        w.Main.TableFilter = "";

        Assert.Equal(["Critical Injuries"], w.Main.Tables.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void Show_folder_in_list_is_true_while_searching_or_viewing_all_tables_and_false_for_a_specific_scope()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();

        w.SelectFolder("All Tables");
        Assert.True(w.Main.ShowFolderInList);

        w.SelectFolder("Unfiled");
        Assert.False(w.Main.ShowFolderInList);

        w.SelectFolder("Combat");
        Assert.False(w.Main.ShowFolderInList);

        w.Main.TableFilter = "x";
        Assert.True(w.Main.ShowFolderInList);
    }

    // ---- recent tables: collection-wide, unaffected by folders --------------------------------------

    [Fact]
    public void Recent_tables_stay_collection_wide_regardless_of_folder_assignment()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Weather");
        w.Save("Critical Injuries", combat.Id);
        w.Start();
        w.SelectFolder("Combat"); // viewing just Combat...

        var weather = w.Db.GetTableSummaries(w.Collection.Id).Single(t => t.Name == "Weather");
        w.Main.OpenRecentTableCommand.Execute(weather); // ...but opening a table anywhere still makes it recent

        Assert.Contains("Weather", w.Main.RecentTables.Select(t => t.Name));
    }

    [Fact]
    public void Moving_a_table_between_folders_does_not_change_its_recency()
    {
        using var w = new World();
        w.Save("Weather");
        w.Save("Critical Injuries");
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        var weather = w.Db.GetTableSummaries(w.Collection.Id).Single(t => t.Name == "Weather");
        var critical = w.Db.GetTableSummaries(w.Collection.Id).Single(t => t.Name == "Critical Injuries");
        w.Main.OpenRecentTableCommand.Execute(critical);
        w.Main.OpenRecentTableCommand.Execute(weather);
        Assert.Equal(["Weather", "Critical Injuries"], w.Main.RecentTables.Select(t => t.Name).ToArray());

        var moved = w.Db.LoadTable(critical.Id)!;
        moved.FolderId = combat.Id;
        w.Db.SaveTable(moved);

        // Reopening the database (as a restart would) is the cleanest way to see the persisted, DB-level order.
        var reopened = new MainViewModel(w.Db, new FixedDice(1));
        Assert.Equal(["Weather", "Critical Injuries"], reopened.RecentTables.Select(t => t.Name).ToArray());
    }

    // ---- paste default folder ------------------------------------------------------------------------

    [Fact]
    public void Paste_from_a_specific_folder_defaults_the_new_table_to_that_folder()
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        w.SelectFolder("Combat");

        w.Main.PasteTableCommand.Execute(null);
        ((PasteViewModel)w.Main.Current!).SourceText = "d6 Reactions\n1-6 Flee";
        ((PasteViewModel)w.Main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;

        Assert.Equal("Combat", review.SelectedFolder.Name);
    }

    [Theory]
    [InlineData("All Tables")]
    [InlineData("Unfiled")]
    public void Paste_from_all_tables_or_unfiled_defaults_to_unfiled(string scope)
    {
        using var w = new World();
        w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Start();
        w.SelectFolder(scope);

        w.Main.PasteTableCommand.Execute(null);
        ((PasteViewModel)w.Main.Current!).SourceText = "d6 Reactions\n1-6 Flee";
        ((PasteViewModel)w.Main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;

        Assert.Equal("Unfiled", review.SelectedFolder.Name);
    }

    [Fact]
    public void Paste_while_searching_inside_a_folder_defaults_to_unfiled()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Reactions", combat.Id);
        w.Start();
        w.SelectFolder("Combat");
        w.Main.TableFilter = "react"; // a general/search context, even though Combat is technically still "selected"

        w.Main.PasteTableCommand.Execute(null);
        ((PasteViewModel)w.Main.Current!).SourceText = "d6 New\n1-6 x";
        ((PasteViewModel)w.Main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;

        Assert.Equal("Unfiled", review.SelectedFolder.Name);
    }

    [Fact]
    public void The_folder_chosen_on_the_review_screen_can_still_be_changed_before_saving()
    {
        using var w = new World();
        var exploration = w.Db.CreateFolder(w.Collection.Id, "Exploration");
        w.Start();
        w.SelectFolder("Unfiled");

        w.Main.PasteTableCommand.Execute(null);
        ((PasteViewModel)w.Main.Current!).SourceText = "d6 Weather\n1-6 x";
        ((PasteViewModel)w.Main.Current!).InterpretCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;
        Assert.Equal("Unfiled", review.SelectedFolder.Name);

        review.SelectedFolder = review.FolderOptions.Single(o => o.Id == exploration.Id);
        review.SaveCommand.Execute(null);

        var saved = w.Db.GetTableSummaries(w.Collection.Id).Single(t => t.Name == "Weather");
        Assert.Equal(exploration.Id, saved.FolderId);
    }

    // ---- editing an existing table's folder ---------------------------------------------------------

    [Fact]
    public void Editing_a_table_shows_its_current_folder_and_only_the_collections_folders()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Db.CreateFolder(w.Collection.Id, "Exploration");
        w.Save("Reactions", combat.Id);
        w.Start();

        w.Main.SelectedTable = w.Main.Tables.Single(t => t.Name == "Reactions");
        w.Main.EditTableCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;

        Assert.Equal("Combat", review.SelectedFolder.Name);
        Assert.Equal(["Unfiled", "Combat", "Exploration"], review.FolderOptions.Select(o => o.Name).ToArray());
    }

    [Fact]
    public void Changing_a_tables_folder_while_editing_preserves_its_id_and_result_data()
    {
        using var w = new World();
        var combat = w.Db.CreateFolder(w.Collection.Id, "Combat");
        w.Save("Reactions");
        w.Start();
        var before = w.Db.GetTableSummaries(w.Collection.Id).Single();

        w.Main.SelectedTable = w.Main.Tables.Single();
        w.Main.EditTableCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;
        review.SelectedFolder = review.FolderOptions.Single(o => o.Id == combat.Id);
        review.SaveCommand.Execute(null);

        var after = w.Db.LoadTable(before.Id)!;
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(combat.Id, after.FolderId);
        Assert.Equal("x", after.ResultSets[0].Entries[0].Text);
    }

    [Fact]
    public void Moving_a_linked_tables_target_between_folders_keeps_the_link_resolvable()
    {
        using var w = new World();
        var exploration = w.Db.CreateFolder(w.Collection.Id, "Exploration");
        var (items, scavenging) = Fixtures.SeedScavenging(w.Db, w.Collection.Id);
        w.Start();

        w.Main.SelectedTable = w.Main.Tables.Single(t => t.Name == "Scavenged Items");
        w.Main.EditTableCommand.Execute(null);
        var review = (ReviewViewModel)w.Main.Current!;
        review.SelectedFolder = review.FolderOptions.Single(o => o.Id == exploration.Id);
        review.SaveCommand.Execute(null);

        var reloaded = w.Db.LoadTable(scavenging.Id)!;
        Assert.Equal(items.Id, reloaded.ResultSets[0].Entries[1].LinkedTableId);
        Assert.Equal("1x Scavenged Item", reloaded.ResultSets[0].Entries[1].Text);
    }
}
