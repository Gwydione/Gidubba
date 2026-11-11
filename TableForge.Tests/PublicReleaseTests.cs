using System.IO;
using System.Reflection;
using System.Windows.Controls;
using TableForge.ViewModels;

namespace TableForge.Tests;

/// <summary>RC18: identity, icon, About, release artifacts and the public documents.</summary>
public class PublicReleaseTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TableForge.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    private static string PathOf(params string[] parts) => Path.Combine([RepoRoot(), .. parts]);
    private static string Read(params string[] parts) => File.ReadAllText(PathOf(parts));

    [Fact]
    public void The_exe_says_Gidubba_by_RPG_Frequencies_but_keeps_its_TableForge_file_name()
    {
        var assembly = typeof(AppInfo).Assembly;
        Assert.Equal("Gidubba", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal("Gidubba", assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);       // Windows' file description
        Assert.Equal("Gidubba - RPG Rollable Tables", assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description);
        Assert.Equal("RPG Frequencies", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal("Copyright (c) 2026 RPG Frequencies", assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        Assert.Equal("1.5.1", AppInfo.Version);
        Assert.Equal(("Gidubba", "Gidubba — RPG Rollable Tables"), (AppInfo.Name, AppInfo.Title));
        Assert.Equal("TableForge", assembly.GetName().Name);                                         // TableForge.exe: installs, pins and shortcuts keep working
        Assert.Contains("<AssemblyName>TableForge</AssemblyName>", Read("TableForge", "TableForge.csproj"));
    }

    [Fact]
    public void The_icon_is_the_approved_Gidubba_icon_unchanged_and_used_everywhere()
    {
        var ico = File.ReadAllBytes(PathOf("TableForge", "Assets", "Gidubba.ico"));
        Assert.Equal(File.ReadAllBytes(PathOf("branding", "gidubba_concept.ico")), ico);           // the approved file, byte for byte
        Assert.Equal((0, 1), (BitConverter.ToUInt16(ico, 0), BitConverter.ToUInt16(ico, 2)));   // ICO header, not a renamed PNG
        var count = BitConverter.ToUInt16(ico, 4);
        var sizes = Enumerable.Range(0, count).Select(i => ico[6 + 16 * i] == 0 ? 256 : ico[6 + 16 * i]).ToList();
        foreach (var size in new[] { 16, 24, 32, 48, 256 }) Assert.Contains(size, sizes);
        Assert.All(Enumerable.Range(0, count), i => Assert.Equal(32, BitConverter.ToUInt16(ico, 6 + 16 * i + 6)));   // 32-bit colour

        var project = Read("TableForge", "TableForge.csproj");
        Assert.Contains(@"<ApplicationIcon>Assets\Gidubba.ico</ApplicationIcon>", project);       // the exe's icon: taskbar, Start Menu, Apps
        Assert.Contains(@"<Resource Include=""Assets\Gidubba.ico"" />", project);
        foreach (var window in new[] { "MainWindow.xaml", "AboutWindow.xaml", "PumExportReviewWindow.xaml", "StreamingOverlayWindow.xaml" })
            Assert.Contains(@"Icon=""Assets/Gidubba.ico""", Read("TableForge", window));
        Assert.False(File.Exists(PathOf("TableForge", "Assets", "TableForge.ico")));
        var iss = Read("installer", "TableForge.iss");
        Assert.Contains(@"SetupIconFile=TableForge\Assets\Gidubba.ico", iss);
        Assert.Contains(@"IconFilename: ""{app}\TableForge.exe""", iss);                           // the shortcut shows the exe's icon
    }

    [Fact]
    public void The_installer_keeps_its_identity_and_names_its_artifacts_by_version()
    {
        var iss = Read("installer", "TableForge.iss");
        Assert.Contains("AppId={{89A2A71F-CF3F-4C91-BDCB-125354F776EB}", iss);                 // never changes
        Assert.Contains("AppName=Gidubba", iss);                                                 // 1.2.0 changed the name, never the identity
        Assert.Contains("AppVerName=Gidubba {#AppVersion}", iss);
        Assert.Contains("UninstallDisplayName=Gidubba", iss);
        Assert.Contains("VersionInfoProductName=Gidubba", iss);
        Assert.Contains("AppPublisher=RPG Frequencies", iss);
        Assert.Contains("AppPublisherURL=https://github.com/Gwydione/Gidubba", iss);
        Assert.Contains("AppSupportURL=https://github.com/Gwydione/Gidubba/issues", iss);
        Assert.Contains("AppUpdatesURL=https://github.com/Gwydione/Gidubba/releases", iss);
        Assert.Contains("VersionInfoCompany=RPG Frequencies", iss);
        Assert.Contains("VersionInfoCopyright=Copyright (c) 2026 RPG Frequencies", iss);
        Assert.Contains(@"OutputDir=publish\release", iss);
        Assert.Contains("OutputBaseFilename=Gidubba-{#AppVersion}-Setup", iss);
        Assert.Contains("PrivilegesRequired=lowest", iss);
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\TableForge", iss);                // existing installs stay where they are
        var sections = iss.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('[')).ToList();   // not comments
        Assert.DoesNotContain("[UninstallDelete]", sections);
        Assert.Contains("[InstallDelete]", sections);                                            // the old shortcut only: see PackagingTests
    }

    [Fact]
    public void publish_ps1_makes_the_installer_zip_and_checksums_and_refuses_developer_files()
    {
        var script = Read("publish.ps1");
        Assert.Contains(@"""publish\release""", script);
        Assert.Contains("Gidubba-$csprojVersion-Setup.exe", script);
        Assert.Contains("Gidubba-$csprojVersion-win-x64.zip", script);
        Assert.Contains("Join-Path $publishDir \"TableForge.exe\"", script);                        // the exe keeps its name
        Assert.DoesNotContain("]::CreateFromDirectory(", script);                               // it writes "\" entry names on .NET Framework
        Assert.Contains(".Replace('\\', '/')", script);
        Assert.Contains("entries using '\\'", script);
        Assert.Contains("SHA256SUMS.txt", script);
        Assert.Contains("Get-FileHash $artifact -Algorithm SHA256", script);
        Assert.Contains("'.xml', '.db'", script);
        Assert.Contains("'*Tests*'", script);
        Assert.Contains("Development-only files were published", script);
    }

    [Fact]
    public void The_license_names_RPG_Frequencies()
    {
        Assert.Contains("Copyright (c) 2026 RPG Frequencies. All rights reserved.", Read("LICENSE.txt"));
    }

    [Fact]
    public void The_readme_answers_a_newcomers_questions_first()
    {
        var readme = Read("README.md").ReplaceLineEndings("\n");                               // a Windows checkout may have CRLF
        var sections = new[] { "# Gidubba", "## Install", "## First use", "## Your data and backups", "## Privacy",
            "## Known limitations", "## Feedback and bug reports", "# Using Gidubba in detail", "# For developers" };
        var positions = sections.Select(s => readme.IndexOf(s + "\n", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);                                     // in this order

        foreach (var fact in new[] { "**Formerly TableForge.**", "`TableForge.exe`", "Paste → Review/Correct → Save → Find → Roll → Read", "Gidubba-<version>-Setup.exe",
                     "not code-signed", "More info", @"%LOCALAPPDATA%\TableForge", "does not delete this folder",
                     "no telemetry", "PRIVACY.txt", "GitHub Issues", "Never post dddice tokens", "OCR", "Windows x64 only" })
            Assert.Contains(fact, readme);
        Assert.DoesNotContain("disable SmartScreen", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(https://github.com/Gwydione/Gidubba/releases)", readme);
        Assert.Contains("(https://github.com/Gwydione/Gidubba/issues)", readme);
    }

    [Fact]
    public void The_release_notes_and_testing_checklist_exist()
    {
        var notes = Read("docs", "release-notes", "1.5.1.md");
        Assert.Equal("# Gidubba 1.5.1", notes.Split('\n')[0].TrimEnd('\r'));                  // 1.5.1, not an rc
        Assert.DoesNotContain("release candidate", notes, StringComparison.OrdinalIgnoreCase);
        foreach (var heading in new[] { "## Highlights", "## Installation", "## Known Issues", "## Data and Upgrades", "## Feedback" })
            Assert.Contains(heading, notes);
        Assert.Contains("**Disconnect still removes your dddice login.**", notes);
        Assert.Contains("dddice-rooms.json", notes);
        Assert.Contains("**The data format does not change** (still database schema version 12", notes);
        Assert.Contains("**Gidubba 1.5.0 can still open data that 1.5.1 has used.**", notes);
        Assert.Contains("Gidubba-1.5.1-Setup.exe", notes);
        Assert.Contains("Gidubba-1.5.1-win-x64.zip", notes);
        Assert.Contains("(https://github.com/Gwydione/Gidubba/issues)", notes);
        Assert.Contains("**Never post dddice tokens or account credentials.**", notes);

        notes = Read("docs", "release-notes", "1.5.0.md");                                     // the 1.5.0 notes stay as released
        Assert.Equal("# Gidubba 1.5.0", notes.Split('\n')[0].TrimEnd('\r'));
        Assert.DoesNotContain("release candidate", notes, StringComparison.OrdinalIgnoreCase);
        foreach (var heading in new[] { "## Highlights", "## Installation", "## What's Included", "## Known Issues", "## Data and Upgrades", "## Feedback" })
            Assert.Contains(heading, notes);
        Assert.Contains("**The data format does not change** (still database schema version 12", notes);   // 1.4.0 and 1.5.0 share the data
        Assert.Contains("**Gidubba 1.4.0 can still open data that 1.5.0 has used.**", notes);
        Assert.Contains("**Back to the original table.**", notes);
        Assert.Contains("**The table list follows the Roll screen.**", notes);
        Assert.Contains("[Gidubba guide](https://rpgfrequencies.com/gidubba-guide.html)", notes);
        Assert.Contains("**Back adds a new step.**", notes);                                     // by design, said plainly
        Assert.Contains("**The installer, the program's folder and your data folder are unchanged.**", notes);
        Assert.Contains("Gidubba-1.5.0-Setup.exe", notes);
        Assert.Contains("Gidubba-1.5.0-win-x64.zip", notes);
        Assert.Contains("(https://github.com/Gwydione/Gidubba/issues)", notes);

        notes = Read("docs", "release-notes", "1.4.0.md");                                     // the 1.4.0 notes stay as released
        Assert.StartsWith("# Gidubba 1.4.0", notes);
        Assert.True(notes.IndexOf("**This version updates your data, and 1.3.0 cannot open it afterwards.**", StringComparison.Ordinal)
            < notes.IndexOf("## Highlights", StringComparison.Ordinal));                       // the one-way update, before anything else
        Assert.Contains("tableforge.pre-v12-from-v11.backup.db", notes);
        Assert.Contains("**1.4.0 changes the data format (database schema version 11 to 12).**", notes);
        Assert.Contains("**Going back to 1.3.0:** 1.3.0 refuses data that 1.4.0 has updated", notes);
        Assert.Contains("**The automatic safety copy is the only way back.**", notes);
        Assert.Contains("**Every existing row starts as an ordinary, repeatable row.**", notes);  // Unique is the user's to mark
        Assert.Contains("format version 3", notes);
        Assert.Contains("never which have been used", notes);                                    // used state never travels
        Assert.Contains("**The project moved on GitHub**", notes);
        Assert.Contains("**The installer, the program's folder and your data folder are unchanged.**", notes);
        Assert.Contains("Gidubba-1.4.0-Setup.exe", notes);
        Assert.Contains("Gidubba-1.4.0-win-x64.zip", notes);
        Assert.Contains("(https://github.com/Gwydione/Gidubba/issues)", notes);

        notes = Read("docs", "release-notes", "1.3.0.md");                                     // the 1.3.0 notes stay as released
        Assert.Equal("# Gidubba 1.3.0", notes.Split('\n')[0].TrimEnd('\r'));
        Assert.True(notes.IndexOf("**This version updates your data, and 1.2.0 cannot open it afterwards.**", StringComparison.Ordinal)
            < notes.IndexOf("## Highlights", StringComparison.Ordinal));                       // the one-way update, before anything else
        Assert.Contains("tableforge.pre-v11-from-v10.backup.db", notes);
        Assert.Contains("**1.3.0 changes the data format (database schema version 10 to 11).**", notes);
        Assert.Contains("**Going back to 1.2.0:** 1.2.0 refuses data that 1.3.0 has updated", notes);
        Assert.Contains("**Every existing table starts as an ordinary table (Manual-selection off).**", notes);   // selectors are the user's to mark
        Assert.Contains("It is **unticked every time the review opens**", notes);                // PUM nested rolls: optional, off by default
        Assert.Contains("byte for byte", notes);
        Assert.Contains("**The installer, the program's folder and your data folder are unchanged.**", notes);
        Assert.Contains("Gidubba-1.3.0-Setup.exe", notes);
        Assert.Contains("Gidubba-1.3.0-win-x64.zip", notes);

        notes = Read("docs", "release-notes", "1.2.0.md");                                     // the 1.2.0 notes stay as released
        Assert.Equal("# Gidubba 1.2.0 (formerly TableForge)", notes.Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("**TableForge has been renamed Gidubba**", notes);                     // the rename, said plainly to TableForge users:
        Assert.Contains("You do not need to uninstall TableForge first.", notes);
        Assert.Contains("**your existing tables, collections and settings are preserved**", notes);
        Assert.Contains("not a new or separate one", notes);
        Assert.Contains("Gidubba-1.2.0-Setup.exe", notes);
        Assert.Contains("Gidubba-1.2.0-win-x64.zip", notes);
        Assert.Contains("**Formerly TableForge.**", notes);                                     // the rename, and what it keeps:
        Assert.Contains("**one** entry, now named **Gidubba**", notes);
        Assert.Contains("still called `TableForge.exe`", notes);
        Assert.Contains("It stays in `%LOCALAPPDATA%\\TableForge`", notes);
        Assert.Contains("**The data format does not change** (still database schema version 10", notes);
        Assert.Contains("**TableForge 1.1.0 can still open data that Gidubba 1.2.0 has used**", notes);

        notes = Read("docs", "release-notes", "1.1.0.md");                                     // earlier notes keep the TableForge name
        Assert.Equal("# TableForge 1.1.0", notes.Split('\n')[0].TrimEnd('\r'));
        Assert.Contains("TableForge-1.1.0-Setup.exe", notes);
        Assert.Contains("New: **PUM Companion export**", notes);                               // new in 1.1.0, stated precisely:
        Assert.Contains("**Export to PUM Companion…**", notes);
        Assert.Contains("**Save PUM Companion Bundle…**", notes);
        Assert.Contains("**Copy Report**", notes);
        Assert.Contains("written completely or not at all", notes);
        Assert.Contains("PUM Companion offers to overwrite", notes);                             // what importing again does
        Assert.Contains("A table whose every result is blank is skipped", notes);
        Assert.Contains("**Checked in PUM Companion:**", notes);                                 // what was checked live and what was not
        Assert.Contains("**Not checked there:**", notes);
        Assert.Contains("table descriptions", notes);                                            // not exported, like the other exports
        Assert.Contains("**Existing exports are unchanged**", notes);
        Assert.Contains("**The data format does not change** (still database schema version 10", notes);    // 1.0.0 and 1.1.0 share the data
        Assert.Contains("**1.0.0 can still open data that 1.1.0 has used**", notes);
        Assert.Contains("**Questline import has not been verified.**", notes);                   // earlier statuses are kept
        Assert.Contains("**Checked in Tables+:**", notes);
        Assert.Contains("Still untested there:", notes);
        Assert.Contains("this is not full", notes);
        Assert.StartsWith("# TableForge 1.0.0", Read("docs", "release-notes", "1.0.0.md"));       // earlier notes stay

        var changelog = Read("CHANGELOG.md");
        var newest = changelog.IndexOf("## 1.5.1", StringComparison.Ordinal);
        Assert.InRange(newest, 0, changelog.IndexOf("## 1.5.0", StringComparison.Ordinal));        // newest first
        Assert.InRange(changelog.IndexOf("## 1.5.0", StringComparison.Ordinal), 0, changelog.IndexOf("## 1.4.0", StringComparison.Ordinal));
        Assert.InRange(changelog.IndexOf("## 1.4.0", StringComparison.Ordinal), 0, changelog.IndexOf("## 1.3.0", StringComparison.Ordinal));
        Assert.InRange(changelog.IndexOf("## 1.3.0", StringComparison.Ordinal), 0, changelog.IndexOf("## 1.2.0", StringComparison.Ordinal));
        Assert.InRange(changelog.IndexOf("## 1.2.0", StringComparison.Ordinal), 0, changelog.IndexOf("## 1.1.0", StringComparison.Ordinal));
        Assert.InRange(changelog.IndexOf("## 1.1.0", StringComparison.Ordinal), 0, changelog.IndexOf("## 1.0.0", StringComparison.Ordinal));
        Assert.Contains("docs/release-notes/1.5.1.md", changelog);
        Assert.Contains("docs/release-notes/1.5.0.md", changelog);
        Assert.Contains("docs/release-notes/1.4.0.md", changelog);
        Assert.Contains("docs/release-notes/1.3.0.md", changelog);
        Assert.Contains("docs/release-notes/1.2.0.md", changelog);
        Assert.Contains("docs/release-notes/1.1.0.md", changelog);
        Assert.Contains("same result in PUM as in TableForge", changelog);                         // the 1.1.0 entry is kept as released

        var checklist = Read("docs", "RELEASE_TESTING.md");
        foreach (var heading in new[] { "## Fresh install", "## Bold and italic result text", "## Table description", "## Situational modifier entry", "## Unique Results", "## Back, the table list and Help (1.5.0)", "## Streaming Overlay (OBS)", "## PUM Companion export", "## Portable Collections", "## dddice", "## Foundry VTT export", "## Sojour", "## Owlbear Rodeo (Tables+)", "## Uninstall and reinstall", "## Upgrade" })
            Assert.Contains(heading, checklist);
    }
}

/// <summary>About Gidubba.</summary>
public class AboutTests
{
    [Fact]
    public void About_shows_the_name_version_publisher_and_purpose_and_opens_the_shipped_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tableforge-about-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "LICENSE.txt"), "x");
            File.WriteAllText(Path.Combine(folder, "PRIVACY.txt"), "x");
            var opened = new List<string>();
            var about = new AboutViewModel("1.0.0-rc18", folder, @"C:\Data\TableForge", opened.Add);

            Assert.Equal(("Gidubba", "Version 1.0.0-rc18", "© 2026 RPG Frequencies"), (about.Name, about.VersionLine, about.PublisherLine));
            Assert.Contains("rollable digital tables", about.PurposeLine);

            about.OpenLicenseCommand.Execute(null);
            about.OpenPrivacyCommand.Execute(null);
            about.OpenDataFolderCommand.Execute(null);
            Assert.Equal([Path.Combine(folder, "LICENSE.txt"), Path.Combine(folder, "PRIVACY.txt"), @"C:\Data\TableForge"], opened);
            Assert.False(about.OpenNoticesCommand.CanExecute(null));                           // not there: not offered

            Assert.Equal("https://github.com/Gwydione/Gidubba", about.ProjectUrlText);
            about.OpenProjectPageCommand.Execute(null);                                         // only when clicked
            Assert.Equal("https://github.com/Gwydione/Gidubba", opened[^1]);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void The_shipped_text_files_are_beside_the_built_exe()
    {
        foreach (var file in new[] { "LICENSE.txt", "PRIVACY.txt", "THIRD-PARTY-NOTICES.txt" })
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(typeof(AppInfo).Assembly.Location)!, file)), file);
    }

    [Fact]
    public void About_is_offered_from_the_main_window_only_when_wired()
    {
        using var temp = new TempDatabase();
        var shown = 0;
        var main = new MainViewModel(temp.Open(), new FixedDice(1), showAbout: () => shown++);
        Assert.True(main.HasAbout);
        main.AboutCommand.Execute(null);
        Assert.Equal(1, shown);
        Assert.False(new MainViewModel(temp.Open(), new FixedDice(1)).HasAbout);
    }

    [Fact]
    public void The_About_window_renders_its_details()
    {
        Sta.Run(() =>
        {
            var window = new AboutWindow
            {
                DataContext = new AboutViewModel("1.0.0-rc18", AppContext.BaseDirectory, null, _ => { }),
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
            };
            window.Show();
            try
            {
                window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => { });
                Assert.Equal("Version 1.0.0-rc18", ((TextBlock)window.FindName("AboutVersion")).Text);
                Assert.Equal("© 2026 RPG Frequencies", ((TextBlock)window.FindName("AboutPublisher")).Text);
                Assert.NotNull(window.Icon);
            }
            finally { window.Close(); }
        });
    }
}
