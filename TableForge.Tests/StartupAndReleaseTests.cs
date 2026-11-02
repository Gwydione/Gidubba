using System.IO;
using Microsoft.Data.Sqlite;
using TableForge.Data;
using TableForge.Domain;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>A scratch folder for database files, so nothing here can touch the real application data.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tableforge-startup-{Guid.NewGuid():N}");

    public string DatabasePath(params string[] subfolders) =>
        System.IO.Path.Combine([Path, .. subfolders, "tableforge.db"]);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); // throws if a handle was leaked: that is the point
    }
}

/// <summary>What the application does when it starts against every kind of database it may meet.</summary>
public class StartupTests
{
    private static SqliteConnection Raw(string path) => new($"Data Source={path};Pooling=False;Foreign Keys=True");

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>A database exactly as Milestone 1-3 builds left it (schema version 1), holding real data.</summary>
    private static void CreateVersion1Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var raw = Raw(path);
        raw.Open();
        DatabaseMigrations.Apply(raw, upToVersion: 1);
        Exec(raw, """
            INSERT INTO Collections (Id, Name, CreatedUtc) VALUES (1, 'Dungeon', '2026-01-01T00:00:00.0000000Z');
            INSERT INTO Tables (Id, CollectionId, Name, DiceCount, DiceSides, CreatedUtc, UpdatedUtc)
                VALUES (10, 1, 'Random Starting Gear', 1, 10, '2026-01-02T00:00:00.0000000Z', '2026-01-02T00:00:00.0000000Z');
            INSERT INTO ResultSets (Id, TableId, Name, SortOrder) VALUES (100, 10, '', 0);
            INSERT INTO Entries (ResultSetId, MinValue, MaxValue, DisplayText, SortOrder) VALUES
                (100, 1, 5, 'Backpack', 0), (100, 6, 10, 'Knife', 1);
            """);
    }

    // ---- the five startup situations ----------------------------------------------------------

    [Fact]
    public void First_launch_with_no_database_creates_the_folder_and_a_current_empty_database()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath("Not", "Yet", "There"); // nested folders that do not exist
        Assert.False(File.Exists(path));

        using (var db = new AppDatabase(path))
        {
            Assert.True(File.Exists(path));
            Assert.Empty(db.GetCollections());
            Assert.Empty(db.GetRollHistory());
        }

        using var raw = Raw(path);
        raw.Open();
        Assert.Equal(DatabaseMigrations.CurrentVersion, DatabaseMigrations.GetVersion(raw));
        Assert.Equal(12, DatabaseMigrations.GetVersion(raw));
    }

    [Fact]
    public void First_launch_leaves_a_working_application_state_ready_for_the_first_collection()
    {
        using var folder = new TempFolder();
        using var db = new AppDatabase(folder.DatabasePath());

        var main = new MainViewModel(db, new FixedDice(1));

        Assert.Empty(main.Collections);
        Assert.Null(main.SelectedCollection);
        Assert.False(main.PasteTableCommand.CanExecute(null));        // nothing to paste into yet...
        main.NewCollectionName = "First";
        main.CreateCollectionCommand.Execute(null);
        Assert.True(main.PasteTableCommand.CanExecute(null));         // ...and then Paste Table is available straight away
    }

    [Fact]
    public void An_empty_but_valid_database_opens_cleanly_again_and_again()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        new AppDatabase(path).Dispose();

        for (var i = 0; i < 3; i++)
        {
            using var db = new AppDatabase(path);
            Assert.Empty(db.GetCollections());
        }
    }

    [Fact]
    public void A_zero_byte_file_is_treated_as_a_new_database()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);

        using var db = new AppDatabase(path);

        Assert.Empty(db.GetCollections());
        db.CreateCollection("Works");
        Assert.Single(db.GetCollections());
    }

    [Fact]
    public void A_current_version_database_with_data_starts_with_everything_intact()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        long tableId;
        using (var db = new AppDatabase(path))
        {
            var collection = db.CreateCollection("Dungeon");
            tableId = db.SaveTable(Fixtures.ParseAndBuild(Fixtures.RandomStartingGear, collection.Id)).Id;
            db.MarkTableUsed(tableId);
            db.AddRollHistory(new RollSnapshot(tableId, "Random Starting Gear", "d10", 9, "D20 Construction Supplies"));
        }

        using var reopened = new AppDatabase(path);
        var main = new MainViewModel(reopened, new FixedDice(3));

        Assert.Equal("Dungeon", main.SelectedCollection!.Name);
        Assert.Equal(["Random Starting Gear"], main.Tables.Select(t => t.Name).ToArray());
        Assert.Equal(["Random Starting Gear"], main.RecentTables.Select(t => t.Name).ToArray());
        Assert.Equal("9", Assert.Single(main.RecentRolls).RollDisplay);
        main.OpenRecentTableCommand.Execute(main.RecentTables[0]);
        ((RollViewModel)main.Current!).RollCommand.Execute(null);
        Assert.Equal("Knife", ((RollViewModel)main.Current!).Results[0].Text);
    }

    [Fact]
    public void A_version_1_database_migrates_on_startup_and_the_application_works_on_it()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        CreateVersion1Database(path);

        using var db = new AppDatabase(path);
        var main = new MainViewModel(db, new FixedDice(7));

        Assert.Equal(["Random Starting Gear"], main.Tables.Select(t => t.Name).ToArray());
        Assert.Empty(main.RecentTables);                              // new features start empty
        Assert.Empty(main.RecentRolls);
        main.SelectedTable = main.Tables[0];
        var roll = (RollViewModel)main.Current!;
        roll.RollCommand.Execute(null);
        Assert.Equal("Knife", roll.Results[0].Text);                  // the migrated table rolls
        Assert.Equal(["Random Starting Gear"], main.RecentTables.Select(t => t.Name).ToArray());
        Assert.Single(main.RecentRolls);

        using var raw = Raw(path);
        raw.Open();
        Assert.Equal(DatabaseMigrations.CurrentVersion, DatabaseMigrations.GetVersion(raw));
        Assert.Equal(2L, Scalar(raw, "SELECT COUNT(*) FROM Entries"));   // original rows untouched
    }

    // ---- when opening fails: meaningful error, data untouched, handle released ---------------------

    [Fact]
    public void A_failed_migration_rolls_back_completely_leaves_the_data_alone_and_frees_the_file()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        CreateVersion1Database(path);
        // Poison the upgrade: migration 2 adds a column, then creates RollHistory. Make the second step fail.
        using (var raw = Raw(path)) { raw.Open(); Exec(raw, "CREATE TABLE RollHistory (Id INTEGER)"); }
        var before = File.ReadAllBytes(path);

        var error = Assert.Throws<DatabaseMigrationException>(() => new AppDatabase(path));
        Assert.IsType<SqliteException>(error.InnerException);

        // The first step of the migration (ALTER TABLE ... ADD COLUMN) had run; it must have been rolled back with the rest.
        using (var raw = Raw(path))
        {
            raw.Open();
            Assert.Equal(1, DatabaseMigrations.GetVersion(raw));
            Assert.Equal(0L, Scalar(raw, "SELECT COUNT(*) FROM pragma_table_info('Tables') WHERE name = 'LastUsedUtc'"));
            Assert.Equal(2L, Scalar(raw, "SELECT COUNT(*) FROM Entries"));
            Assert.Equal(1L, Scalar(raw, "SELECT COUNT(*) FROM Tables"));
        }

        var message = DatabaseOpenError.Describe(path, error);
        Assert.Contains(path, message);
        Assert.Contains("Your original database backup has been preserved", message);
        Assert.Contains(error.BackupPath, message);
        Assert.Equal(before.Length, File.ReadAllBytes(path).Length);
        using (var copy = Raw(error.BackupPath)) { copy.Open(); Assert.Equal(1, DatabaseMigrations.GetVersion(copy)); } // the copy made first
        // folder.Dispose() deletes the file: it fails if the failed open leaked its handle.
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_reported_plainly_and_never_modified()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var junk = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("this is not a sqlite database. ", 200)));
        File.WriteAllBytes(path, junk);

        var error = Assert.ThrowsAny<Exception>(() => new AppDatabase(path));

        Assert.Equal(junk, File.ReadAllBytes(path));                  // not overwritten, not "repaired"
        var message = DatabaseOpenError.Describe(path, error);
        Assert.Contains("not a Gidubba database", message);
        Assert.Contains(path, message);
    }

    [Fact]
    public void A_database_from_a_newer_build_is_refused_with_an_explanation_and_left_alone()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        new AppDatabase(path).Dispose();
        using (var raw = Raw(path)) { raw.Open(); Exec(raw, $"PRAGMA user_version = {DatabaseMigrations.CurrentVersion + 3}"); }

        var error = Assert.Throws<DatabaseTooNewException>(() => new AppDatabase(path));

        var message = DatabaseOpenError.Describe(path, error);
        Assert.StartsWith("This Gidubba data file was created by a newer version of Gidubba. Please install the newer version to open it safely.", message);
        Assert.Contains($"Details: Data format version {DatabaseMigrations.CurrentVersion + 3}; this version of Gidubba reads up to version {DatabaseMigrations.CurrentVersion}.", message);
        Assert.Empty(DatabaseBackup.Existing(path));                   // nothing is copied for a file that is never changed
        using var again = Raw(path);
        again.Open();
        Assert.Equal(DatabaseMigrations.CurrentVersion + 3, DatabaseMigrations.GetVersion(again)); // never downgraded
    }

    [Fact]
    public void A_database_locked_by_another_program_gives_an_explanation_not_a_crash_dump()
    {
        var message = DatabaseOpenError.Describe("C:\\x\\tableforge.db",
            new SqliteException("database is locked", 5));
        Assert.Contains("in use by another program", message);
        Assert.DoesNotContain("SqliteException", message);
    }

    // ---- closing ------------------------------------------------------------------------------

    [Fact]
    public void Disposing_the_database_releases_the_file_even_after_a_failed_operation()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        var db = new AppDatabase(path);
        Assert.Throws<SqliteException>(() => db.SaveTable(Fixtures.ParseAndBuild("d6 T\n1-6 x", collectionId: 999))); // FK failure mid-transaction
        db.Dispose();

        File.Delete(path); // would throw if any handle were still open
        Assert.False(File.Exists(path));
    }

    // ---- transactions -------------------------------------------------------------------------

    [Fact]
    public void History_insert_and_retention_trim_are_one_atomic_step()
    {
        using var folder = new TempFolder();
        var path = folder.DatabasePath();
        using var db = new AppDatabase(path);
        for (var i = 1; i <= AppDatabase.RollHistoryLimit; i++) db.AddRollHistory(new RollSnapshot(null, "T", "d6", i, $"r{i}"));
        // Make the trim fail after the insert has happened.
        using (var raw = Raw(path))
        {
            raw.Open();
            Exec(raw, "CREATE TRIGGER NoTrim BEFORE DELETE ON RollHistory BEGIN SELECT RAISE(ABORT, 'trim blocked'); END;");
        }

        Assert.Throws<SqliteException>(() => db.AddRollHistory(new RollSnapshot(null, "T", "d6", 99, "new")));

        var history = db.GetRollHistory(100);
        Assert.Equal(AppDatabase.RollHistoryLimit, history.Count);      // the new row did not stay behind without its trim
        Assert.DoesNotContain(history, h => h.RollValue == 99);
    }
}

/// <summary>Where data lives, what the build calls itself, and how a release is produced.</summary>
public class ReleaseTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TableForge.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    private static string ReadRepoFile(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    [Fact]
    public void The_production_database_lives_under_the_users_local_application_data_folder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableForge", "tableforge.db");

        Assert.Equal(expected, AppDatabase.DefaultPath);
        Assert.EndsWith(Path.Combine("TableForge", "tableforge.db"), AppDatabase.DefaultPath);
    }

    [Fact]
    public void Test_databases_can_never_be_the_production_file()
    {
        using var temp = new TempDatabase();
        using var folder = new TempFolder();

        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        Assert.NotEqual(AppDatabase.DefaultPath, temp.Path);
        Assert.StartsWith(tempRoot, Path.GetFullPath(temp.Path));
        Assert.StartsWith(tempRoot, Path.GetFullPath(folder.DatabasePath()));
        Assert.DoesNotContain("TableForge" + Path.DirectorySeparatorChar + "tableforge.db", temp.Path);
    }

    [Fact]
    public void Only_this_file_among_the_tests_refers_to_the_production_path()
    {
        var offenders = Directory.GetFiles(Path.Combine(RepoRoot(), "TableForge.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) != "StartupAndReleaseTests.cs")
            .Where(f => File.ReadAllText(f).Contains("DefaultPath"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders); // a test that opened DefaultPath would read or write the user's real tables
    }

    [Fact]
    public void The_build_identifies_itself_as_the_stable_release_1_5_0()
    {
        Assert.Equal("1.5.0", AppInfo.Version);
        Assert.DoesNotContain("-", AppInfo.Version);                                    // a stable release: no prerelease suffix
        Assert.Contains("<Version>1.5.0</Version>", ReadRepoFile("TableForge", "TableForge.csproj"));
        Assert.Contains("VersionInfoVersion=1.5.0", ReadRepoFile("installer", "TableForge.iss"));   // the installer's own file version
    }

    [Fact]
    public void The_rename_to_Gidubba_keeps_every_name_that_installs_data_and_files_depend_on()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableForge"), AppDatabase.ResolveDataFolder(null));
        Assert.Equal("tableforge.db", Path.GetFileName(AppDatabase.DefaultPath));
        Assert.Equal("TABLEFORGE_DATA_DIR", AppDatabase.DataFolderVariable);
        Assert.Equal(("TableForgeCollection", ".tfcollection"), (TableForge.Portable.PortableFormat.FormatName, TableForge.Portable.PortableFormat.Extension));
        Assert.Equal(("TableForge table", "TableForge"), (TableForge.Export.Pum.PumNames.FallbackName, TableForge.Export.Pum.PumNames.FallbackCategory));   // part of PUM table ids
        Assert.Contains("\"tf\" + System.Convert.ToHexString(SHA256.HashData(", ReadRepoFile("TableForge", "Export", "Pum", "PumTableConverter.cs"));
        Assert.Contains("Encoding.UTF8.GetBytes(\"TableForge dddice account token v1\")", ReadRepoFile("TableForge", "Dice", "DddiceAccountStore.cs"));   // saved dddice logins
        Assert.Contains("private const string Host = \"tableforge-app.local\";", ReadRepoFile("TableForge", "Dice", "WebViewDddiceRoller.cs"));       // the dice page's origin
        foreach (var name in new[] { "dice-provider.txt", "dddice-account.json", "streaming-overlay.json", "streaming-overlay.html" })
            Assert.Contains($"\"{name}\"", string.Join("\n", Directory.GetFiles(Path.Combine(RepoRoot(), "TableForge"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).Select(File.ReadAllText)));
    }

    [Fact]
    public void The_supported_publish_profile_is_self_contained_windows_x64_without_trimming_or_single_file()
    {
        var profile = ReadRepoFile("TableForge", "Properties", "PublishProfiles", "win-x64-folder.pubxml");

        Assert.Contains("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", profile);
        Assert.Contains("<SelfContained>true</SelfContained>", profile);
        Assert.Contains("<PublishTrimmed>false</PublishTrimmed>", profile);
        Assert.Contains("<PublishSingleFile>false</PublishSingleFile>", profile);
        Assert.Contains("<PublishDir>..\\publish\\win-x64\\</PublishDir>", profile);
        Assert.Contains("<TargetFramework>net10.0-windows</TargetFramework>", ReadRepoFile("TableForge", "TableForge.csproj"));
    }

    [Fact]
    public void The_readme_documents_the_exact_publish_command_and_where_data_is_kept()
    {
        var readme = ReadRepoFile("README.md");

        Assert.Contains("dotnet publish TableForge\\TableForge.csproj -p:PublishProfile=win-x64-folder", readme);
        Assert.Contains("publish\\win-x64", readme);
        Assert.Contains("%LOCALAPPDATA%\\TableForge\\tableforge.db", readme);
        Assert.Contains("Version 1.5.0 ·", readme);
        Assert.DoesNotContain("(release candidate)", readme);
    }
}
