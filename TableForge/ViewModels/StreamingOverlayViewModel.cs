using System.Globalization;
using System.Windows.Input;
using TableForge.Dice;
using TableForge.Streaming;

namespace TableForge.ViewModels;

/// <summary>
/// The Streaming Overlay dialog: turn the overlay on, the two ways to add it to OBS (local file, or the URL), its port, what it
/// shows, Show Test Result and Clear Overlay — and, for a connected dddice account, the way to that account's dddice room (whose
/// own Streaming tools give OBS the 3D dice; TableForge never handles that link).
/// </summary>
public sealed class StreamingOverlayViewModel : ObservableObject
{
    private readonly StreamingOverlayController _overlay;
    private readonly DddiceConnection? _dddice;
    private readonly Action<string> _copyText;
    private readonly Action<string> _openUrl;
    private string _portText;
    private string _portError = "";
    private string _copyMessage = "";

    public StreamingOverlayViewModel(StreamingOverlayController overlay, DddiceConnection? dddice, Action<string> copyText, Action<string> openUrl)
    {
        _overlay = overlay;
        _dddice = dddice;
        _copyText = copyText;
        _openUrl = openUrl;
        _portText = overlay.Settings.Port.ToString(CultureInfo.InvariantCulture);

        ApplyPortCommand = new RelayCommand(ApplyPort);
        CopyLocalFileCommand = new RelayCommand(() => Copy(_overlay.EnsurePage(), "Local file path copied."));
        CopyUrlCommand = new RelayCommand(() => Copy(_overlay.BrowserUrl, "URL copied."));
        ShowTestCommand = new RelayCommand(_overlay.ShowTest);
        ClearCommand = new RelayCommand(_overlay.Clear);
        OpenDddiceRoomCommand = new RelayCommand(() => { if (_dddice?.RoomUrl is { } url) _openUrl(url); }, () => ShowDddiceRoom);

        _overlay.Changed += OnOverlayChanged;
        if (_dddice is not null) _dddice.Changed += OnDddiceChanged;
    }

    /// <summary>The dialog closed: stop listening to the long-lived overlay and dddice connection.</summary>
    public void Detach()
    {
        _overlay.Changed -= OnOverlayChanged;
        if (_dddice is not null) _dddice.Changed -= OnDddiceChanged;
    }

    private void OnOverlayChanged(object? sender, EventArgs e) => RaiseOverlay();
    private void OnDddiceChanged(object? sender, DddiceChange e) => RaiseDddice();

    // ---- the overlay ----------------------------------------------------------------------------------------------------

    public bool IsEnabled
    {
        get => _overlay.Settings.Enabled;
        set => _overlay.SetEnabled(value);
    }

    public bool ShowTableName
    {
        get => _overlay.Settings.ShowTableName;
        set => _overlay.SetShowTableName(value);
    }

    public bool ShowRollValue
    {
        get => _overlay.Settings.ShowRollValue;
        set => _overlay.SetShowRollValue(value);
    }

    /// <summary>"Overlay shows: Latest roll" (the default). Only a choice made (true) changes it; the other option clearing does not.</summary>
    public bool ShowsLatestRoll
    {
        get => _overlay.Settings.DisplayMode == OverlayDisplayMode.LatestRoll;
        set { if (value) _overlay.SetDisplayMode(OverlayDisplayMode.LatestRoll); }
    }

    /// <summary>"Overlay shows: Complete result".</summary>
    public bool ShowsCompleteResult
    {
        get => _overlay.Settings.DisplayMode == OverlayDisplayMode.CompleteResult;
        set { if (value) _overlay.SetDisplayMode(OverlayDisplayMode.CompleteResult); }
    }

    public string LocalFilePath => _overlay.LocalFilePath;
    public string BrowserUrl => _overlay.BrowserUrl;
    public string StatusText => _overlay.StatusText;
    public OverlayStatus Status => _overlay.Status;
    public bool IsProblem => _overlay.Status is OverlayStatus.PortUnavailable or OverlayStatus.Error;

    /// <summary>The port as typed; <see cref="ApplyPortCommand"/> checks and uses it.</summary>
    public string PortText { get => _portText; set { if (Set(ref _portText, value)) PortError = ""; } }

    /// <summary>Why the typed port cannot be used; empty otherwise.</summary>
    public string PortError { get => _portError; private set { if (Set(ref _portError, value)) Raise(nameof(HasPortError)); } }
    public bool HasPortError => PortError.Length > 0;

    /// <summary>"Local file path copied." after a Copy; empty otherwise.</summary>
    public string CopyMessage { get => _copyMessage; private set => Set(ref _copyMessage, value); }

    public ICommand ApplyPortCommand { get; }
    public ICommand CopyLocalFileCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand ShowTestCommand { get; }
    public ICommand ClearCommand { get; }

    private void ApplyPort()
    {
        if (!int.TryParse(PortText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || !OverlaySettings.IsValidPort(port))
        {
            PortError = $"Enter a port from {OverlaySettings.MinPort} to {OverlaySettings.MaxPort}.";
            return;
        }
        PortError = "";
        _overlay.SetPort(port);
        PortText = _overlay.Settings.Port.ToString(CultureInfo.InvariantCulture);
    }

    private void Copy(string text, string done)
    {
        try
        {
            _copyText(text);
            CopyMessage = done;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            CopyMessage = $"Could not copy: {ex.Message}";
        }
    }

    private void RaiseOverlay()
    {
        Raise(nameof(IsEnabled));
        Raise(nameof(ShowTableName));
        Raise(nameof(ShowRollValue));
        Raise(nameof(ShowsLatestRoll));
        Raise(nameof(ShowsCompleteResult));
        Raise(nameof(LocalFilePath));
        Raise(nameof(BrowserUrl));
        Raise(nameof(StatusText));
        Raise(nameof(Status));
        Raise(nameof(IsProblem));
    }

    // ---- dddice -----------------------------------------------------------------------------------------------------------

    /// <summary>A connected dddice account with a room: offer "Open my dddice room".</summary>
    public bool ShowDddiceRoom => _dddice?.RoomUrl is not null;

    /// <summary>How to get dddice's 3D dice into OBS too — or, without a connected account, that one is needed.</summary>
    public string DddiceHelp => ShowDddiceRoom
        ? "To show dddice's 3D dice in OBS as well, open your dddice room, choose Streaming tools there, and add its 3D Dice link as a separate Browser Source."
        : "Showing dddice's 3D dice in OBS needs a connected dddice account (Account… under Dice).";

    /// <summary>"dddice room changed…" once dddice had to give the account a new room this run; empty otherwise.</summary>
    public string DddiceNotice => _dddice is { RoomChanged: true } ? DddiceMessages.RoomChanged : "";
    public bool HasDddiceNotice => DddiceNotice.Length > 0;

    public ICommand OpenDddiceRoomCommand { get; }

    private void RaiseDddice()
    {
        Raise(nameof(ShowDddiceRoom));
        Raise(nameof(DddiceHelp));
        Raise(nameof(DddiceNotice));
        Raise(nameof(HasDddiceNotice));
        CommandManager.InvalidateRequerySuggested();
    }
}
