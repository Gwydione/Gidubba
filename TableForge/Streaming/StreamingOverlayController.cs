using System.IO;

namespace TableForge.Streaming;

public enum OverlayStatus { Disabled, Running, PortUnavailable, Error }

/// <summary>
/// The Streaming Overlay as a whole: its settings, the page file OBS loads, the loopback adapter and the current result. The
/// adapter runs only while "Enable streaming overlay" is on. A port that cannot be used is reported, never swapped for another:
/// the person chooses a new one, which rewrites the page (OBS then needs one refresh). Clear and Show Test Result only change
/// what the overlay shows.
/// </summary>
public sealed class StreamingOverlayController : IDisposable
{
    private readonly string _dataFolder;
    private readonly OverlaySettingsStore _store;
    private OverlayServer? _server;
    private string? _portNote;
    private string? _saveNote;
    private string _statusBase = DisabledText;

    public StreamingOverlayController(OverlayPublisher publisher, string dataFolder)
    {
        Publisher = publisher;
        _dataFolder = dataFolder;
        _store = new OverlaySettingsStore(dataFolder);
    }

    public OverlayPublisher Publisher { get; }
    public OverlaySettings Settings { get; private set; } = OverlaySettings.Default;
    public OverlayStatus Status { get; private set; } = OverlayStatus.Disabled;

    /// <summary>One line for the dialog: what is happening and, when something is wrong, what to do.</summary>
    public string StatusText { get; private set; } = DisabledText;

    /// <summary>The page OBS's Browser Source loads with "Local file" ticked.</summary>
    public string LocalFilePath => Path.GetFullPath(Path.Combine(_dataFolder, OverlayPage.FileName));

    /// <summary>The same page from the adapter, for a URL Browser Source or a quick look in a web browser.</summary>
    public string BrowserUrl => $"http://127.0.0.1:{Settings.Port}/";

    public bool IsRunning => _server?.IsRunning ?? false;

    /// <summary>Raised after every change to <see cref="Settings"/> or <see cref="Status"/>.</summary>
    public event EventHandler? Changed;

    private const string DisabledText = "Off. Turn on \"Enable streaming overlay\" to use it in OBS.";

    /// <summary>At start-up: reads the settings and, if the overlay was left on, starts it.</summary>
    public void Start()
    {
        Settings = _store.Load();
        Publisher.SetPresentation(Settings.ShowTableName, Settings.ShowRollValue);
        Publisher.SetDisplayMode(Settings.DisplayMode);
        if (Settings.Enabled) Activate(); else SetStatus(OverlayStatus.Disabled, DisabledText);
    }

    /// <summary>
    /// "Overlay shows": saved, and used from the next result on. What the overlay shows now stays as it is (nothing is
    /// published), and the page and its address do not change, so OBS needs no refresh.
    /// </summary>
    public void SetDisplayMode(OverlayDisplayMode mode)
    {
        if (mode == Settings.DisplayMode) return;
        Persist(Settings with { DisplayMode = mode });
        Publisher.SetDisplayMode(mode);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == Settings.Enabled && enabled == (Status != OverlayStatus.Disabled)) return;
        Persist(Settings with { Enabled = enabled });
        _portNote = null;
        if (enabled) Activate();
        else
        {
            Deactivate();
            SetStatus(OverlayStatus.Disabled, DisabledText);
        }
    }

    /// <summary>A new port: validated, saved, written into the page and, while enabled, listened on instead. False for an invalid one.</summary>
    public bool SetPort(int port)
    {
        if (!OverlaySettings.IsValidPort(port)) return false;
        if (port == Settings.Port && Status != OverlayStatus.PortUnavailable) return true;
        var changed = port != Settings.Port;
        Deactivate();
        Persist(Settings with { Port = port });
        _portNote = changed ? $" Port changed: in OBS, refresh the Browser Source once (Properties → Refresh cache of current page)." : null;
        if (Settings.Enabled) Activate();
        else
        {
            TryWritePage();
            SetStatus(OverlayStatus.Disabled, DisabledText + _portNote);
        }
        return true;
    }

    public void SetShowTableName(bool show) => SetPresentation(Settings with { ShowTableName = show });
    public void SetShowRollValue(bool show) => SetPresentation(Settings with { ShowRollValue = show });

    public void ShowTest() => Publisher.ShowTest();
    public void Clear() => Publisher.Clear();

    /// <summary>Writes the page for the current port (Copy uses it, so the copied path always exists) and returns its path.</summary>
    public string EnsurePage()
    {
        TryWritePage();
        return LocalFilePath;
    }

    /// <summary>At exit: stops the adapter (waiting at most <see cref="OverlayServer.StopWait"/>).</summary>
    public void Dispose() => Deactivate();

    private void SetPresentation(OverlaySettings settings)
    {
        Persist(settings);
        Publisher.SetPresentation(settings.ShowTableName, settings.ShowRollValue);
    }

    private void Activate()
    {
        Deactivate();
        if (!TryWritePage(out var pageError))
        {
            SetStatus(OverlayStatus.Error, $"The overlay page could not be written: {pageError}");
            return;
        }
        var server = new OverlayServer(Publisher, Settings.Port);
        try
        {
            server.Start();
            _server = server;
            SetStatus(OverlayStatus.Running, $"Running on port {Settings.Port}." + _portNote);
        }
        catch (OverlayPortUnavailableException)
        {
            SetStatus(OverlayStatus.PortUnavailable,
                $"Port {Settings.Port} is already in use or unavailable (another program, or another copy of Gidubba, may be using it). Choose a different port and Apply.");
        }
    }

    private void Deactivate()
    {
        _server?.Stop();
        _server = null;
    }

    private void TryWritePage() => TryWritePage(out _);

    private bool TryWritePage(out string? error)
    {
        try
        {
            OverlayPage.WriteLocalFile(_dataFolder, Settings.Port);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Keeps the new settings even if they cannot be saved (they then last for this run); the status says so.</summary>
    private void Persist(OverlaySettings settings)
    {
        Settings = settings;
        try
        {
            _store.Save(settings);
            _saveNote = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _saveNote = $" (The settings could not be saved, so they last only until Gidubba closes: {ex.Message})";
        }
        StatusText = _statusBase + _saveNote;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(OverlayStatus status, string text)
    {
        Status = status;
        _statusBase = text;
        StatusText = text + _saveNote;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
