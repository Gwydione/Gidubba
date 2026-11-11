using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Input;
using TableForge.Dice;

namespace TableForge.ViewModels;

public enum DddiceAccountState { Disconnected, CreatingActivation, WaitingForApproval, LoadingThemes, Connected, Expired }

/// <summary>One theme in the Account dialog's list. Incompatible themes stay listed, disabled, with the reason beside them.</summary>
public sealed class DddiceThemeOption(DddiceTheme theme)
{
    public DddiceTheme Theme { get; } = theme;
    public string Name => Theme.Name;
    public bool IsEnabled => Theme.IsCompatible;
    public string Reason => Theme.Reason;
    public bool HasReason => Reason.Length > 0;

    /// <summary>What a screen reader or UI Automation reads for this row.</summary>
    public override string ToString() => IsEnabled ? Name : $"{Name} — {Reason}";
}

/// <summary>
/// The Account… dialog: connect a dddice account with an activation code the person approves on dddice.com in their own
/// browser (TableForge never sees or asks for a password), choose one compatible theme from their Digital Dice Box, refresh it,
/// or disconnect (local only). Nothing is saved until an activation succeeds; the activation secret never leaves memory.
/// Everything under way stops when the dialog closes (<see cref="Close"/>).
/// </summary>
public sealed class DddiceAccountViewModel : ObservableObject
{
    public const string ActivationUrl = "https://dddice.com/activate";

    private readonly DddiceConnection _connection;
    private readonly DddiceRest _rest;
    private readonly DddiceActivationPoller _poller;
    private readonly Action<string> _copy;
    private readonly Action<string> _openUrl;
    private DddiceAccountState _state;
    private string _message = "";
    private string _activationCode = "";
    private DddiceThemeOption? _selectedTheme;
    private bool _showingLoadedSelection;
    private CancellationTokenSource? _cancel;

    /// <param name="copy">Puts text on the clipboard (only ever the activation code).</param>
    /// <param name="openUrl">Opens a web page in the person's own browser.</param>
    /// <param name="now">Clock for the activation's lifetime (tests).</param>
    /// <param name="delay">Wait between polls (tests).</param>
    public DddiceAccountViewModel(DddiceConnection connection, DddiceRest rest, Action<string> copy, Action<string> openUrl,
        Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _connection = connection;
        _rest = rest;
        _copy = copy;
        _openUrl = openUrl;
        _poller = new DddiceActivationPoller(rest.PollActivationAsync, now, delay);
        _state = RestingState();
        if (_state == DddiceAccountState.Expired) _message = "Connection expired. Reconnect.";

        ConnectCommand = new RelayCommand(() => _ = ConnectAsync(), () => IsResting);
        CopyCodeCommand = new RelayCommand(() => _copy(ActivationCode), () => ActivationCode.Length > 0);
        OpenDddiceCommand = new RelayCommand(() => _openUrl(ActivationUrl), () => ActivationCode.Length > 0);
        CancelCommand = new RelayCommand(CancelActivation, () => IsActivating);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => State == DddiceAccountState.Connected);
        DisconnectCommand = new RelayCommand(Disconnect, () => _connection.IsAccountMode && !IsActivating);
    }

    public DddiceAccountState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            foreach (var name in new[] { nameof(IsActivating), nameof(IsResting), nameof(ShowConnect), nameof(ConnectLabel), nameof(ShowActivation),
                         nameof(ShowThemes), nameof(IsLoadingThemes), nameof(AccountText), nameof(ShowDisconnect) })
                Raise(name);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>What happened last, or what to do next.</summary>
    public string Message { get => _message; private set { if (Set(ref _message, value)) Raise(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;

    /// <summary>The code to enter on dddice.com; empty unless an activation is under way.</summary>
    public string ActivationCode { get => _activationCode; private set => Set(ref _activationCode, value); }

    public bool IsActivating => State is DddiceAccountState.CreatingActivation or DddiceAccountState.WaitingForApproval;
    private bool IsResting => State is DddiceAccountState.Disconnected or DddiceAccountState.Expired or DddiceAccountState.Connected;

    /// <summary>Connect is offered when not connected; Reconnect when the connection expired. A working connection needs neither.</summary>
    public bool ShowConnect => State is DddiceAccountState.Disconnected or DddiceAccountState.Expired;
    public string ConnectLabel => State == DddiceAccountState.Expired ? "Reconnect" : "Connect";
    public bool ShowActivation => IsActivating;
    public bool ShowThemes => State is DddiceAccountState.Connected or DddiceAccountState.LoadingThemes;
    public bool IsLoadingThemes => State == DddiceAccountState.LoadingThemes;
    public bool ShowDisconnect => _connection.IsAccountMode && !IsActivating;

    /// <summary>"dddice: Allen", "dddice: Connected", "dddice: Connection expired" or "dddice: Guest", as in the sidebar.</summary>
    public string AccountText => _connection.State switch
    {
        DddiceConnectionState.Connected => $"Connected: {_connection.DisplayName}",
        DddiceConnectionState.Expired => "Connection expired",
        _ => "Not connected. dddice rolls as a guest with the Bees theme.",
    };

    public ObservableCollection<DddiceThemeOption> Themes { get; } = [];

    /// <summary>The theme TableForge rolls with. Only a compatible theme can be chosen; choosing one saves it straight away.</summary>
    public DddiceThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not null && !value.IsEnabled) { Raise(); return; } // a disabled row can never become the choice
            if (!Set(ref _selectedTheme, value) || value is null || _showingLoadedSelection) return;
            try
            {
                _connection.SelectTheme(value.Theme);
                Message = $"Gidubba's dddice rolls now use {value.Name}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Message = $"Gidubba could not save the theme: {ex.Message}";
            }
        }
    }

    public ICommand ConnectCommand { get; }
    public ICommand CopyCodeCommand { get; }
    public ICommand OpenDddiceCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DisconnectCommand { get; }

    /// <summary>The dialog opened: a connected account's Dice Box is loaded once (nothing happens in the background after that).</summary>
    public Task OpenedAsync() => State == DddiceAccountState.Connected ? RefreshAsync() : Task.CompletedTask;

    /// <summary>The dialog is closing: anything still under way (an activation wait, a Dice Box load) stops, and nothing pending is kept.</summary>
    public void Close()
    {
        Cancel();
        ActivationCode = "";
        if (IsActivating || State == DddiceAccountState.LoadingThemes) State = RestingState();
    }

    public async Task ConnectAsync()
    {
        if (!IsResting) return;
        var cancel = Begin();
        Message = "";
        State = DddiceAccountState.CreatingActivation;
        try
        {
            var activation = await _rest.CreateActivationAsync(cancel.Token);
            ActivationCode = activation.Code;
            State = DddiceAccountState.WaitingForApproval;
            var approved = await _poller.WaitForApprovalAsync(activation, cancel.Token);

            _connection.Connect(approved.Token!, approved.DisplayName, approved.AccountId); // only now is anything saved
            ActivationCode = "";
            State = DddiceAccountState.Connected;
            await LoadThemesAsync(cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            ActivationCode = "";
            if (IsActivating || State == DddiceAccountState.LoadingThemes) State = RestingState();
        }
        catch (Exception ex) when (ex is DddiceException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            ActivationCode = "";
            State = RestingState();
            Message = ex is DddiceException ? ex.Message : $"Gidubba could not save the dddice connection: {ex.Message}";
        }
        finally { End(cancel); }
    }

    public async Task RefreshAsync()
    {
        if (State != DddiceAccountState.Connected) return;
        var cancel = Begin();
        try { await LoadThemesAsync(cancel); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (State == DddiceAccountState.LoadingThemes) State = RestingState();
        }
        finally { End(cancel); }
    }

    /// <summary>Loads the whole Dice Box and judges every theme again; the saved theme is checked, never replaced by another.</summary>
    private async Task LoadThemesAsync(CancellationTokenSource cancel)
    {
        if (_connection.Account is not { } account) return;
        State = DddiceAccountState.LoadingThemes;
        try
        {
            var themes = await _rest.LoadDiceBoxAsync(account.Token, cancel.Token);
            _showingLoadedSelection = true;
            Themes.Clear();
            foreach (var theme in themes) Themes.Add(new DddiceThemeOption(theme));
            var saved = account.ThemeId is null ? null : Themes.FirstOrDefault(t => t.Theme.Id == account.ThemeId);
            SelectedTheme = saved is { IsEnabled: true } ? saved : null;
            _showingLoadedSelection = false;

            Message = account.ThemeId is null
                ? Themes.Any(t => t.IsEnabled) ? "Choose a theme for Gidubba's dddice rolls." : "None of the themes in your Dice Box has every die Gidubba needs."
                : saved is null ? DddiceMessages.ThemeMissing(account.ThemeName ?? account.ThemeId)
                : !saved.IsEnabled ? DddiceMessages.ThemeIncompatible(saved.Name, saved.Reason)
                : "";
            State = DddiceAccountState.Connected;
        }
        catch (DddiceException ex) when (ex.IsAuthProblem)
        {
            _connection.MarkExpired();
            Themes.Clear();
            State = DddiceAccountState.Expired;
            Message = "Connection expired. Reconnect.";
        }
        catch (DddiceException ex)
        {
            State = DddiceAccountState.Connected; // the account is kept; the list stays as it was
            Message = ex.Message;
        }
        finally { _showingLoadedSelection = false; }
    }

    private void CancelActivation()
    {
        Cancel();
        ActivationCode = "";
        State = RestingState();
        Message = "";
    }

    private void Disconnect()
    {
        Cancel();
        try { _connection.Disconnect(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Gidubba could not remove the saved dddice connection: {ex.Message}";
            return;
        }
        _showingLoadedSelection = true;
        Themes.Clear();
        SelectedTheme = null;
        _showingLoadedSelection = false;
        State = DddiceAccountState.Disconnected;
        Raise(nameof(AccountText));
        Raise(nameof(ShowDisconnect));
        Message = "Disconnected. dddice rolls as a guest again.";
    }

    private DddiceAccountState RestingState() => _connection.State switch
    {
        DddiceConnectionState.Connected => DddiceAccountState.Connected,
        DddiceConnectionState.Expired => DddiceAccountState.Expired,
        _ => DddiceAccountState.Disconnected,
    };

    private CancellationTokenSource Begin()
    {
        Cancel();
        return _cancel = new CancellationTokenSource();
    }

    private void End(CancellationTokenSource cancel)
    {
        if (ReferenceEquals(_cancel, cancel)) _cancel = null;
        cancel.Dispose();
    }

    private void Cancel()
    {
        try { _cancel?.Cancel(); } catch (ObjectDisposedException) { }
        _cancel = null;
    }
}
