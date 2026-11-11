using System.Windows;
using System.Windows.Threading;
using TableForge.Data;
using TableForge.Dice;
using TableForge.Streaming;
using TableForge.ViewModels;

namespace TableForge;

public partial class App : Application
{
    private AppDatabase? _db;
    private WebViewDddiceRoller? _dddice;

    // The Streaming Overlay's current result: in memory only, so every run starts with an empty overlay.
    private readonly OverlayPublisher _overlay = new();
    private StreamingOverlayController? _overlayController;

    // The dice choice is one word in a small file next to the database (no schema change for a single setting).
    private static string DataFolder => System.IO.Path.GetDirectoryName(AppDatabase.DefaultPath)!;
    private static string PreferencePath => System.IO.Path.Combine(DataFolder, "dice-provider.txt");
    private static string AutoFollowPath => System.IO.Path.Combine(DataFolder, "auto-follow.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Developer-only: TableForge.exe --clipboard-diagnostic [--out <file>]. Reports what is on the clipboard and touches
        // no database. With --out it writes the report to that file and exits; without it, it shows a window.
        if (e.Args.Contains("--clipboard-diagnostic", StringComparer.OrdinalIgnoreCase))
        {
            RunClipboardDiagnostic(e.Args);
            return;
        }

        // Last resort only: commands already report their own failures in the status bar. This keeps the app open, and
        // says something a person can read, if something unexpected slips through. Saved data is never touched here.
        DispatcherUnhandledException += OnUnhandled;

        var path = AppDatabase.DefaultPath;
        try
        {
            _db = new AppDatabase(path); // creates the folder and database on first run, and upgrades older ones
            var window = new MainWindow();
            // Built-in is the default and does no network work. dddice is only prepared once it has been chosen (or was chosen last time).
            // A connected dddice account is read from its local file here, but only checked with dddice when dddice is prepared.
            var rest = new DddiceRest();
            var connection = new DddiceConnection(new DddiceAccountStore(DataFolder), new DddiceRoomMemoryStore(DataFolder));
            _dddice = new WebViewDddiceRoller(window.AttachDiceView, System.IO.Path.Combine(DataFolder, "WebView2"), rest, connection);
            var dice = new DiceProviderViewModel(new BuiltInDiceProvider(), new DddiceDiceProvider(_dddice), LoadDicePreference, SaveDicePreference,
                connection, openAccount: () => OpenDddiceAccount(window, connection, rest));
            var main = new MainViewModel(_db, dice,
                confirm: (message, title) => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes,
                confirmImport: message => MessageBox.Show(message, "Import Collection", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK,
                showMessage: message => MessageBox.Show(window, message, AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning),
                dataFolder: DataFolder, openFolder: OpenInExplorer,
                showAbout: () => new AboutWindow
                {
                    Owner = window,
                    DataContext = new AboutViewModel(AppInfo.Version, AppContext.BaseDirectory, DataFolder, OpenWithWindows),
                }.ShowDialog(),
                overlay: _overlay,
                reviewPumExport: review => new PumExportReviewWindow { Owner = window, DataContext = review }.ShowDialog() == true,
                showStreamingOverlay: () => new StreamingOverlayWindow
                {
                    Owner = window,
                    DataContext = new StreamingOverlayViewModel(_overlayController!, connection, CopyText, OpenInBrowser),
                }.ShowDialog(),
                autoFollow: new AutoFollowSetting(LoadAutoFollow(), SaveAutoFollow),
                openWebPage: OpenInBrowser);
            // The Streaming Overlay listens (on 127.0.0.1 only) just while it is enabled; a port problem is shown in its dialog.
            _overlayController = new StreamingOverlayController(_overlay, DataFolder);
            _overlayController.Start();
            window.DataContext = main;
            window.Show();
            dice.RestorePreference();
        }
        catch (Exception ex)
        {
            _db?.Dispose(); // don't hold the file open after a failed start
            _db = null;
            MessageBox.Show(DatabaseOpenError.Describe(path, ex), AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>The Account… dialog. Modal, so there is only ever one activation or Dice Box load at a time; closing it stops them.</summary>
    private static void OpenDddiceAccount(Window owner, DddiceConnection connection, DddiceRest rest)
    {
        var vm = new DddiceAccountViewModel(connection, rest, CopyText, OpenInBrowser);
        new DddiceAccountWindow { Owner = owner, DataContext = vm }.ShowDialog();
    }

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* another program is holding the clipboard; the code is still on screen */ }
    }

    /// <summary>Shows a folder in File Explorer (Open Data Folder).</summary>
    private static void OpenInExplorer(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>Opens a file (Notepad for .txt) or folder with Windows, for About. A failure is shown, never thrown.</summary>
    private static void OpenWithWindows(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Gidubba could not open {path}: {ex.Message}", AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Opens a page in the person's default browser (never inside TableForge).</summary>
    private static void OpenInBrowser(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Gidubba could not open your web browser. Go to {url} yourself.", AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static string? LoadDicePreference() =>
        System.IO.File.Exists(PreferencePath) ? System.IO.File.ReadAllText(PreferencePath).Trim() : null;

    private static void SaveDicePreference(string value)
    {
        System.IO.Directory.CreateDirectory(DataFolder);
        System.IO.File.WriteAllText(PreferencePath, value);
    }

    /// <summary>Auto-follow linked tables is on only when auto-follow.txt says "on"; missing or unreadable is off.</summary>
    private static bool LoadAutoFollow()
    {
        try { return System.IO.File.Exists(AutoFollowPath) && System.IO.File.ReadAllText(AutoFollowPath).Trim() == "on"; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return false; }
    }

    private static void SaveAutoFollow(bool on)
    {
        System.IO.Directory.CreateDirectory(DataFolder);
        System.IO.File.WriteAllText(AutoFollowPath, on ? "on" : "off");
    }

    private void RunClipboardDiagnostic(string[] args)
    {
        var outIndex = Array.FindIndex(args, a => a.Equals("--out", StringComparison.OrdinalIgnoreCase));
        if (outIndex >= 0 && outIndex + 1 < args.Length)
        {
            string report;
            try { report = ClipboardReader.Report(); }
            catch (Exception ex) { report = $"The clipboard could not be read: {ex.Message}"; }
            System.IO.File.WriteAllText(args[outIndex + 1], report, new System.Text.UTF8Encoding(false));
            Shutdown(0);
            return;
        }
        new ClipboardDiagnosticWindow().Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something unexpected went wrong:\n\n{e.Exception.Message}\n\nYour saved tables are safe. You can carry on; if it keeps happening, close and reopen Gidubba.",
            AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _overlayController?.Dispose();
        _dddice?.Dispose();
        _db?.Dispose();
        base.OnExit(e);
    }
}
