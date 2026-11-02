using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using TableForge.Dice;
using TableForge.Domain;
using TableForge.Import;
using TableForge.Streaming;

namespace TableForge.ViewModels;

public sealed class EntryViewModel(TableEntry entry) : ObservableObject
{
    private bool _isMatched;
    private bool _isUsed;

    public TableEntry Entry { get; } = entry;
    public string RangeLabel => Entry.RangeLabel;
    public string Text => Entry.Text;

    /// <summary><see cref="Text"/> as it is shown, with its bold/italic (one plain segment when it has none).</summary>
    public IReadOnlyList<FormattedSegment> Segments => Entry.Styles.Segments(Entry.Text);

    public bool IsMatched { get => _isMatched; set => Set(ref _isMatched, value); }

    /// <summary>A Unique row that has been used: still shown and readable, tagged "Used" and muted until the table is reset.</summary>
    public bool IsUsed { get => _isUsed; internal set { if (Set(ref _isUsed, value)) { Raise(nameof(UniqueTag)); Raise(nameof(HasUniqueTag)); } } }

    /// <summary>"Unique" for an available Unique row, "Used" for a used one, nothing for a repeatable row: words, never colour alone.</summary>
    public string UniqueTag => IsUsed ? "Used" : Entry.IsUnique ? "Unique" : "";
    public bool HasUniqueTag => UniqueTag.Length > 0;
}

public sealed class ResultSetViewModel(ResultSet set)
{
    public string Name { get; } = set.Name;
    public bool HasName => set.Name.Length > 0;
    public IReadOnlyList<EntryViewModel> Entries { get; } = set.Entries.Select(e => new EntryViewModel(e)).ToList();
}

/// <summary>
/// One row of the aligned multi-column Roll view: the range shared by every result set at this row (they are parallel
/// outputs of one roll, so their ranges are kept in step — see <see cref="RollStepViewModel.IsAligned"/>), plus each
/// result set's own entry for that row, in result-set order. Purely a presentation grouping, mirroring
/// <see cref="AlignedRowViewModel"/> on the Review screen: each cell is still the same <see cref="EntryViewModel"/>
/// the stacked, one-set-at-a-time view would show, so nothing about the underlying result sets is merged.
/// </summary>
public sealed class AlignedEntryRowViewModel : ObservableObject
{
    private bool _isMatched;

    public AlignedEntryRowViewModel(IReadOnlyList<EntryViewModel> cells) => Cells = cells;

    /// <summary>This row's entry in each result set, in the same order as <see cref="RollStepViewModel.ResultSets"/>.</summary>
    public IReadOnlyList<EntryViewModel> Cells { get; }

    public string RangeLabel => Cells[0].RangeLabel;

    /// <summary>True when the latest roll landed on this row. Every cell shares the same range, so they always agree.</summary>
    public bool IsMatched { get => _isMatched; internal set => Set(ref _isMatched, value); }
}

public enum LinkState { None, Resolved, Unresolved, Missing }

/// <summary>
/// One supported dice expression found inside a displayed result's text (see <see cref="InlineDiceDetector"/>), offered as a
/// separate, user-triggered roll. Purely auxiliary: rolling it never touches the result's own text, and it uses whichever
/// <see cref="IDiceProvider"/> the roll screen is already using. It keeps only its latest result, which the owning
/// <see cref="ResultLineViewModel"/> substitutes into a transient resolved copy of the text. Its state lives only as long as
/// that line stays the current, active one (see <see cref="ResultLineViewModel.IsActive"/>).
/// </summary>
public sealed class InlineDiceAction : ObservableObject
{
    private bool _isRolling;
    private int? _latestValue;

    /// <param name="namedOnReroll">True when the result offers other actions too, so "Roll Again" names its expression
    /// ("Roll d6 Again") and the buttons stay distinguishable.</param>
    public InlineDiceAction(DiceExpression expression, bool namedOnReroll = false)
    {
        Expression = expression;
        DisplayExpression = expression.ToString();
        NamedOnReroll = namedOnReroll;
    }

    public DiceExpression Expression { get; }

    /// <summary>The canonical form ("d20", "2d6+1", "d66") — never the casing or spelling found in the source text.</summary>
    public string DisplayExpression { get; }

    public bool NamedOnReroll { get; }

    /// <summary>The latest successful roll's final value (dice plus the expression's own modifier); null until the first one.
    /// Rolling again replaces it — there is no history.</summary>
    public int? LatestValue => _latestValue;
    public bool HasResult => _latestValue is not null;

    /// <summary>The value as substituted into the resolved text: always the plain final number ("9" for 2d6+1, "35" for d66).</summary>
    public string? LatestResult => _latestValue?.ToString();

    /// <summary>True from the moment this specific action's roll is asked for until it has a result (or fails/cancels).</summary>
    public bool IsRolling { get => _isRolling; internal set => Set(ref _isRolling, value); }

    public string RollLabel => !HasResult ? $"Roll {DisplayExpression}" : NamedOnReroll ? $"Roll {DisplayExpression} Again" : "Roll Again";

    public ICommand? RollCommand { get; internal set; }

    internal void SetResult(int? value)
    {
        _latestValue = value;
        Raise(nameof(LatestValue));
        Raise(nameof(HasResult));
        Raise(nameof(LatestResult));
        Raise(nameof(RollLabel));
    }
}

/// <summary>One result set's outcome for a roll, plus what (if anything) its entry links to.</summary>
public sealed class ResultLineViewModel(string heading, string range, string text, bool isProblem) : ObservableObject
{
    private bool _isActive = true;
    private readonly IReadOnlyList<InlineDiceAction> _inlineActions = [];

    public string Heading { get; } = heading;
    public string Range { get; } = range;

    /// <summary>The matched entry's text exactly as stored. Never rewritten — inline results go into <see cref="ResolvedText"/>.</summary>
    public string Text { get; } = text;

    /// <summary>The matched entry's bold/italic (see <see cref="TableEntry.Styles"/>); none for a problem line. Display only.</summary>
    public TextStyles Styles { get; init; } = TextStyles.Empty;

    /// <summary><see cref="Text"/> as it is shown, with its formatting.</summary>
    public IReadOnlyList<FormattedSegment> Segments => Styles.Segments(Text);

    /// <summary>
    /// <see cref="ResolvedText"/> as it is shown — "Resolved: " then the resolved text, each character keeping its formatting and
    /// each rolled value taking its expression's (see <see cref="InlineDiceDetector.Substitute(string, TextStyles, IReadOnlyList{InlineDiceMatch}, Func{DiceExpression, string?})"/>).
    /// </summary>
    public IReadOnlyList<FormattedSegment> ResolvedSegments
    {
        get
        {
            var (resolved, styles) = InlineDiceDetector.Substitute(Text, Styles, InlineMatches,
                dice => InlineActions.FirstOrDefault(a => a.Expression == dice)?.LatestResult);
            return [new FormattedSegment("Resolved: ", TextStyle.None), .. styles.Segments(resolved)];
        }
    }

    public bool IsProblem { get; } = isProblem;
    public bool HasHeading => Heading.Length > 0;

    public LinkState Link { get; init; }

    /// <summary>The destination's current name for a resolved link; the intended name for an unresolved one.</summary>
    public string LinkName { get; init; } = "";
    public RollableTable? LinkTarget { get; init; }
    public ICommand? FollowCommand { get; internal set; }

    /// <summary>Where each supported expression sits in <see cref="Text"/>, every occurrence (see <see cref="InlineDiceDetector.FindAll"/>).</summary>
    public IReadOnlyList<InlineDiceMatch> InlineMatches { get; init; } = [];

    /// <summary>Supported dice expressions found in <see cref="Text"/> (see <see cref="InlineDiceDetector"/>), one per distinct
    /// expression; empty for a problem line. A repeated expression's one action resolves every one of its occurrences.</summary>
    public IReadOnlyList<InlineDiceAction> InlineActions
    {
        get => _inlineActions;
        init
        {
            _inlineActions = value;
            foreach (var action in value)
                action.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(InlineDiceAction.LatestValue)) return;
                    Raise(nameof(ResolvedText));
                    Raise(nameof(ResolvedSegments));
                    Raise(nameof(HasResolved));
                    Raise(nameof(ShowResolved));
                };
        }
    }

    public bool HasInlineActions => InlineActions.Count > 0;

    /// <summary>True once any inline expression in this result has been rolled.</summary>
    public bool HasResolved => InlineActions.Any(a => a.HasResult);

    /// <summary>
    /// A transient copy of <see cref="Text"/> with each rolled expression replaced, in place, by its latest value
    /// ("You gain +1d4 Armor" → "You gain +3 Armor"); expressions not rolled yet stay as written. Display only.
    /// </summary>
    public string ResolvedText => InlineDiceDetector.Substitute(Text, InlineMatches,
        dice => InlineActions.FirstOrDefault(a => a.Expression == dice)?.LatestResult);

    /// <summary>The resolved copy is shown, below the source text, only for the current result once something has been rolled.</summary>
    public bool ShowResolved => HasResolved && IsActive;

    /// <summary>Only the latest roll on the current table offers its links; once the trail moves on they become plain notes.</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set
        {
            if (!Set(ref _isActive, value)) return;
            // Inline results belong to the current result only: once it is superseded (a new roll, a followed link) they are cleared.
            if (!value)
                foreach (var action in InlineActions)
                    action.SetResult(null);
            Raise(nameof(ShowFollow));
            Raise(nameof(ShowLinkedNote));
            Raise(nameof(ShowInlineActions));
            Raise(nameof(ShowResolved));
        }
    }

    /// <summary>Inline roll buttons are offered only for the currently active (latest) result, exactly like <see cref="ShowFollow"/>.</summary>
    public bool ShowInlineActions => HasInlineActions && IsActive;

    public bool ShowFollow => Link == LinkState.Resolved && IsActive;
    public bool ShowLinkedNote => Link == LinkState.Resolved && !IsActive;
    public bool ShowUnresolved => Link == LinkState.Unresolved;
    public bool ShowMissing => Link == LinkState.Missing;
    public string FollowLabel => $"Open {LinkName}";
    public string LinkedNote => $"→ {LinkName}";
    public string UnresolvedNote => LinkName.Length > 0
        ? $"⚠ Unresolved link to “{LinkName}”: no table is linked."
        : "⚠ Unresolved link: no table is linked.";
}

/// <summary>One numeric roll on a step's table, shown once, with every result set's output.</summary>
/// <param name="Breakdown">How a situational modifier reached the roll ("11 +3 situational"); empty for an unmodified roll.</param>
/// <param name="ClampNote">"Resolved as 6 (clamped)" when the table's Clamp to Range looked the roll up at a boundary; empty otherwise.</param>
/// <param name="BatchLabel">"Roll 2" for the second result of one multi-roll action; empty for an ordinary single roll.</param>
/// <param name="RerolledCount">How many throws landed on used Unique rows and were rerolled before this result (see <see cref="UniqueRoller"/>).</param>
public sealed record RollOutcomeViewModel(string Display, IReadOnlyList<ResultLineViewModel> Lines, string Breakdown = "", string ClampNote = "",
    string BatchLabel = "", int RerolledCount = 0)
{
    public bool HasBreakdown => Breakdown.Length > 0;
    public bool HasClampNote => ClampNote.Length > 0;
    public bool HasBatchLabel => BatchLabel.Length > 0;

    /// <summary>"Rerolled 2×" when used Unique results came up first; empty (and not shown) for a result accepted on its first throw.</summary>
    public string RerollNote => UniqueResults.RerollNote(RerolledCount);
    public bool HasRerollNote => RerolledCount > 0;

    /// <summary>This result's place on the Streaming Overlay (see <see cref="OverlayPublisher"/>); null when no overlay is wired in.
    /// Each result of a multi-roll action has its own.</summary>
    public OverlayToken? OverlayToken { get; internal set; }

    /// <summary>What the overlay heads this result with: the table's name and the roll exactly as shown ("Rolled 14" → "14").</summary>
    internal (string TableName, string RollValue) OverlayHeader { get; set; }
}

/// <summary>A table in the linked-roll trail and every roll made on it. Repeat rolls stay in the same step.</summary>
public sealed class RollStepViewModel(RollableTable table, bool isFirst) : ObservableObject
{
    public RollableTable Table { get; } = table;
    public bool IsFirst { get; } = isFirst;
    public string Title => Table.Name;
    public string DiceInfo => Table.Dice.IsD66
        ? "d66 · two d6 read as tens and ones · legal rolls 11–66"
        : $"{Table.Dice} · legal rolls {Table.Dice.FormatValue(Table.Dice.Min)}–{Table.Dice.FormatValue(Table.Dice.Max)}";

    /// <summary>
    /// For a step auto-follow opened, where it came from, compactly: "Auto-followed from Area Type (5)". "" for a step the
    /// person opened. The parent's full result stays on the step above; it is never repeated here.
    /// </summary>
    public string AutoFollowedFrom { get; init; } = "";
    public bool IsAutoFollowed => AutoFollowedFrom.Length > 0;

    /// <summary>A step added by Back to the original table (see <see cref="RollViewModel.ReturnToOriginalCommand"/>): it says so.</summary>
    public bool IsReturn { get; init; }

    /// <summary>A Manual-selection table (see <see cref="RollableTable.IsManualSelection"/>): the step says its result is meant to be chosen.</summary>
    public bool IsManualSelection => Table.IsManualSelection;

    /// <summary>This step's own table's description, shown under its dice; "" for none (then nothing is shown at all).</summary>
    public string Description => Table.Description;
    public bool HasDescription => Description.Length > 0;
    public IReadOnlyList<ResultSetViewModel> ResultSets { get; } = table.ResultSets.Select(s => new ResultSetViewModel(s)).ToList();
    public ObservableCollection<RollOutcomeViewModel> Outcomes { get; } = [];
    public bool HasOutcomes => Outcomes.Count > 0;

    /// <summary>
    /// True when this table's result sets are parallel outputs of one shared roll — two or more result sets, all with
    /// the same non-zero entry count, and the same numeric range at every row position — the same evidence
    /// <see cref="ReviewViewModel.IsAligned"/> uses on the Review screen. Computed once: a saved table's structure
    /// does not change while rolling it.
    /// </summary>
    public bool IsAligned { get; } = ComputeIsAligned(table.ResultSets);

    /// <summary>The aligned view of the entries, one row per position across every result set. Empty unless <see cref="IsAligned"/>.</summary>
    public IReadOnlyList<AlignedEntryRowViewModel> AlignedRows { get; } = ComputeIsAligned(table.ResultSets)
        ? Enumerable.Range(0, table.ResultSets[0].Entries.Count)
            .Select(i => new AlignedEntryRowViewModel(table.ResultSets.Select(s => new EntryViewModel(s.Entries[i])).ToList()))
            .ToList()
        : [];

    /// <param name="keepHighlights">True for the second and later results of one multi-roll action: their rows are highlighted
    /// alongside the earlier ones in that action instead of replacing them.</param>
    /// <summary>Every row of this table, once, as the listing shows it (the aligned view has its own copies of the same rows).</summary>
    private IEnumerable<EntryViewModel> AllEntries => ResultSets.SelectMany(s => s.Entries).Concat(AlignedRows.SelectMany(r => r.Cells));

    /// <summary>Marks which Unique rows are used, as the store says now; returns (used, Unique) row counts for the summary.</summary>
    internal (int Used, int Unique) ShowUsed(IReadOnlySet<Guid> used)
    {
        foreach (var entry in AllEntries)
            entry.IsUsed = entry.Entry.IsUnique && entry.Entry.RowKey != Guid.Empty && used.Contains(entry.Entry.RowKey);
        var unique = ResultSets.SelectMany(s => s.Entries).Where(e => e.Entry.IsUnique).ToList();
        return (unique.Count(e => e.IsUsed), unique.Count);
    }

    internal void Add(RollOutcomeViewModel outcome, int roll, bool keepHighlights = false)
    {
        Outcomes.Add(outcome);
        Raise(nameof(HasOutcomes));
        foreach (var set in ResultSets)
            foreach (var entry in set.Entries)
                entry.IsMatched = entry.Entry.Covers(roll) || (keepHighlights && entry.IsMatched);
        foreach (var row in AlignedRows)
            row.IsMatched = row.Cells[0].Entry.Covers(roll) || (keepHighlights && row.IsMatched); // every cell in a row shares the same range
    }

    /// <summary>See <see cref="IsAligned"/>.</summary>
    private static bool ComputeIsAligned(IReadOnlyList<ResultSet> sets)
    {
        if (sets.Count < 2) return false;
        var count = sets[0].Entries.Count;
        if (count == 0) return false;
        if (sets.Any(s => s.Entries.Count != count)) return false;
        for (var i = 0; i < count; i++)
        {
            var (min, max) = (sets[0].Entries[i].Min, sets[0].Entries[i].Max);
            if (sets.Any(s => s.Entries[i].Min != min || s.Entries[i].Max != max)) return false;
        }
        return true;
    }
}

/// <summary>
/// The rolling screen for one opened table. Following a link appends a new step for the destination
/// without rolling it; earlier steps stay visible. The trail lives only in memory.
/// The properties below without "Step" describe the current (latest) step.
/// </summary>
public sealed class RollViewModel : ObservableObject
{
    private readonly IDiceProvider _dice;
    private readonly Func<long, RollableTable?>? _loadTable;
    private readonly Action<long>? _tableUsed;
    private readonly Action<RollSnapshot>? _rolled;
    private readonly Func<bool>? _diceReady;
    private readonly Action<string> _copyText;
    private readonly Func<SaveFileRequest, string?> _chooseSaveFile;
    private readonly OverlayPublisher? _overlay;
    private readonly UsedResultsStore _usedResults;
    private int _copyResultSetIndex;
    private string _copyMessage = "";
    private string _exportWarning = "";
    private string _manualRollText = "";
    private string _modifierText = "0";
    private string _message = "";
    private bool _isRolling;
    private int _rollCount = 1;
    private int _batchPosition;
    private int _autoFollowStep;
    private bool _stopRequested;
    private CancellationTokenSource? _rollCancel;
    private readonly Func<string, string, bool> _confirm;
    private (int Used, int Unique) _usedCounts;
    private bool _usedUnreadable;

    /// <param name="tableUsed">Called when a table becomes the current rollable table by being followed to, or by Back to the
    /// original table (recent tables, and the table selected in the list).</param>
    /// <param name="rolled">Called once for every actual resolved roll, with a snapshot of what was shown (recent rolls).
    /// Never called for a followed link or an invalid manual entry.</param>
    /// <param name="diceReady">Whether the chosen dice provider can roll right now (dddice is still preparing, for example). Null means always.</param>
    /// <param name="copyText">Puts text on the clipboard (Copy Table Text, Copy Table Text (Spaces), Copy for Sojour, Copy Foundry JSON,
    /// Copy Tables+ JSON).
    /// Null means the Windows clipboard.</param>
    /// <param name="chooseSaveFile">Asks where to save a file, given the dialog's title and a suggested file name; null when the person
    /// cancels. Null means the Windows Save dialog.</param>
    /// <param name="overlay">The Streaming Overlay: every resolved result is published to it, and a successful inline roll updates
    /// it while that result is still the current one. Nothing else on this screen (links, exports, navigation) touches it.</param>
    /// <param name="exportPum">Save PUM Companion Bundle…: exports the table this screen shows (review, choose a file, save) and gives
    /// the message to show, or null when the person cancelled. Null disables the command.</param>
    /// <param name="autoFollow">The app-wide Auto-follow linked tables setting (see <see cref="AutoFollowAsync"/>). Null means a
    /// setting of this screen's own, off.</param>
    /// <param name="usedResults">Which Unique rows of a table have been used (see <see cref="UniqueResults"/>). Null means kept in
    /// this screen's memory only.</param>
    /// <param name="confirm">Asks the person a yes/no question, given the question and a title naming the action ("Reset Used
    /// Results"). Null means never confirmed, so nothing is reset.</param>
    public RollViewModel(RollableTable table, IDiceProvider dice, Func<long, RollableTable?>? loadTable = null,
        Action<long>? tableUsed = null, Action<RollSnapshot>? rolled = null, Func<bool>? diceReady = null, Action<string>? copyText = null,
        Func<SaveFileRequest, string?>? chooseSaveFile = null, OverlayPublisher? overlay = null, Func<RollableTable, string?>? exportPum = null,
        AutoFollowSetting? autoFollow = null, UsedResultsStore? usedResults = null, Func<string, string, bool>? confirm = null)
    {
        _dice = dice;
        _usedResults = usedResults ?? UsedResultsStore.InMemory();
        _confirm = confirm ?? ((_, _) => false);
        AutoFollow = autoFollow ?? new AutoFollowSetting();
        _overlay = overlay;
        _copyText = copyText ?? ClipboardText.Set;
        _chooseSaveFile = chooseSaveFile ?? SaveFileChooser.ChooseJson;
        _diceReady = diceReady;
        _loadTable = loadTable;
        _tableUsed = tableUsed;
        _rolled = rolled;
        Steps.Add(new RollStepViewModel(table, isFirst: true));
        ShowUsedResults();
        RollCommand = new RelayCommand(() => _ = RollAsync(), () => !IsRolling && (_diceReady?.Invoke() ?? true) && IsModifierValid);
        ResolveManualCommand = new RelayCommand(ResolveManual, () => !IsRolling);
        StopCommand = new RelayCommand(CancelRoll, () => IsRolling);
        ResetUsedResultsCommand = new RelayCommand(ResetUsedResults, () => !IsRolling && _usedCounts.Used > 0);
        ReturnToOriginalCommand = new RelayCommand(ReturnToOriginal, () => !IsRolling && ShowReturnToOriginal);
        CopyTableTextCommand = new RelayCommand(() => CopyTableText(TableTextSeparator.Tab), () => Current.Table.ResultSets.Count > 0);
        CopyTableTextSpacesCommand = new RelayCommand(() => CopyTableText(TableTextSeparator.Space), () => Current.Table.ResultSets.Count > 0);
        CopyForSojourCommand = new RelayCommand(CopyForSojour, () => Current.Table.ResultSets.Count > 0);
        CopyFoundryJsonCommand = new RelayCommand(CopyFoundryJson, () => Current.Table.ResultSets.Count > 0);
        SaveFoundryJsonCommand = new RelayCommand(SaveFoundryJson, () => Current.Table.ResultSets.Count > 0);
        CopyTablesPlusJsonCommand = new RelayCommand(CopyTablesPlusJson, () => Current.Table.ResultSets.Count > 0);
        SaveTablesPlusJsonCommand = new RelayCommand(SaveTablesPlusJson, () => Current.Table.ResultSets.Count > 0);
        _exportPum = exportPum;
        SavePumBundleCommand = new RelayCommand(SavePumBundle, () => _exportPum is not null && Current.Table.ResultSets.Count > 0);
    }

    // ---- Copy Table Text -----------------------------------------------------------------------------------------------

    /// <summary>The current table's result sets, by name ("Result set 2" for an unnamed one), for choosing which one to copy.</summary>
    public IReadOnlyList<string> CopyResultSetNames => Current.Table.ResultSets
        .Select((s, i) => s.Name.Trim().Length > 0 ? s.Name.Trim() : $"Result set {i + 1}").ToList();

    /// <summary>The choice is offered only when there is more than one result set; otherwise the one set is copied.</summary>
    public bool ShowCopyResultSetChoice => Current.Table.ResultSets.Count > 1;

    /// <summary>Which result set Copy Table Text copies (one at a time: result sets are never combined).</summary>
    public int CopyResultSetIndex
    {
        get => _copyResultSetIndex;
        set
        {
            if (value < 0 || value >= Current.Table.ResultSets.Count || !Set(ref _copyResultSetIndex, value)) return;
            CopyMessage = "";
            ExportWarning = "";
        }
    }

    /// <summary>"Table text copied." (or "Foundry JSON copied.", and so on) after an export; empty otherwise.</summary>
    public string CopyMessage { get => _copyMessage; private set { if (Set(ref _copyMessage, value)) Raise(nameof(HasCopyMessage)); } }
    public bool HasCopyMessage => CopyMessage.Length > 0;

    /// <summary>After a Foundry or Tables+ export, what the export cannot carry (only Clamp, today); empty otherwise. Never blocks the export.</summary>
    public string ExportWarning { get => _exportWarning; private set { if (Set(ref _exportWarning, value)) Raise(nameof(HasExportWarning)); } }
    public bool HasExportWarning => ExportWarning.Length > 0;

    public ICommand CopyTableTextCommand { get; }

    /// <summary>Copy Table Text (Spaces): the same text as Copy Table Text, with one space instead of the tab before each result.</summary>
    public ICommand CopyTableTextSpacesCommand { get; }

    /// <summary>
    /// Copies the current table's chosen result set as plain text (see <see cref="TableTextExporter"/>), with a tab or one space
    /// between range and result. Read-only: rolls, links, inline dice, the modifier and Recent Rolls are all left exactly as they were.
    /// </summary>
    private void CopyTableText(TableTextSeparator separator)
    {
        var table = Current.Table;
        if (table.ResultSets.Count == 0) return;
        var text = TableTextExporter.Export(table, table.ResultSets[Math.Clamp(CopyResultSetIndex, 0, table.ResultSets.Count - 1)], separator);
        ExportWarning = "";
        try
        {
            _copyText(text);
            CopyMessage = separator == TableTextSeparator.Space ? "Table text copied (spaces)." : "Table text copied.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            CopyMessage = $"The table text could not be copied: {ex.Message}";
        }
    }

    // ---- Copy for Sojour -----------------------------------------------------------------------------------------------

    public ICommand CopyForSojourCommand { get; }

    /// <summary>
    /// Copies only the rows of the chosen result set, range TAB result (see <see cref="TableTextExporter.ExportRows"/>), for
    /// pasting into a Sojour Lookup Table. Read-only, like Copy Table Text. A result set with no rows copies nothing.
    /// </summary>
    private void CopyForSojour()
    {
        var table = Current.Table;
        if (table.ResultSets.Count == 0) return;
        var set = table.ResultSets[Math.Clamp(CopyResultSetIndex, 0, table.ResultSets.Count - 1)];
        ExportWarning = "";
        if (set.Entries.Count == 0)
        {
            CopyMessage = "This result set has no rows to copy.";
            return;
        }
        try
        {
            _copyText(TableTextExporter.ExportRows(table, set));
            CopyMessage = "Copied for Sojour.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            CopyMessage = $"The rows could not be copied: {ex.Message}";
        }
    }

    // ---- Foundry VTT and Tables+ JSON export ---------------------------------------------------------------------------

    public ICommand CopyFoundryJsonCommand { get; }
    public ICommand SaveFoundryJsonCommand { get; }
    public ICommand CopyTablesPlusJsonCommand { get; }
    public ICommand SaveTablesPlusJsonCommand { get; }

    /// <summary>What a JSON export produced, whichever the format.</summary>
    private sealed record JsonExport(string Name, string Json, IReadOnlyList<string> Warnings);

    /// <summary>One JSON format: its name in messages ("Foundry", "Tables+"), its exporter (an export, or why not) and its file name.</summary>
    private sealed record JsonFormat(string Label, Func<RollableTable, ResultSet, (JsonExport? Export, string? Error)> Export,
        Func<string, string> SuggestedFileName);

    /// <summary>Roll Table Importer JSON (see <see cref="FoundryTableExporter"/>).</summary>
    private static readonly JsonFormat Foundry = new("Foundry",
        (table, set) => FoundryTableExporter.TryExport(table, set, out var x, out var error) ? (new(x!.Name, x.Json, x.Warnings), null) : (null, error),
        name => FoundryTableExporter.SuggestedFileName(name));

    /// <summary>Owlbear Rodeo Tables+ JSON (see <see cref="TablesPlusTableExporter"/>).</summary>
    private static readonly JsonFormat TablesPlus = new("Tables+",
        (table, set) => TablesPlusTableExporter.TryExport(table, set, out var x, out var error) ? (new(x!.Name, x.Json, x.Warnings), null) : (null, error),
        TablesPlusTableExporter.SuggestedFileName);

    private void CopyFoundryJson() => CopyJson(Foundry);
    private void SaveFoundryJson() => SaveJson(Foundry);
    private void CopyTablesPlusJson() => CopyJson(TablesPlus);
    private void SaveTablesPlusJson() => SaveJson(TablesPlus);

    /// <summary>The chosen result set in this format, or null with the reason shown.</summary>
    private JsonExport? ExportJson(JsonFormat format)
    {
        var table = Current.Table;
        ExportWarning = "";
        if (table.ResultSets.Count == 0) return null;
        var set = table.ResultSets[Math.Clamp(CopyResultSetIndex, 0, table.ResultSets.Count - 1)];
        var (export, error) = format.Export(table, set);
        if (export is not null) return export;
        CopyMessage = $"This table cannot be exported to {format.Label}: {error}";
        return null;
    }

    /// <summary>Copies the JSON. Read-only, like Copy Table Text.</summary>
    private void CopyJson(JsonFormat format)
    {
        if (ExportJson(format) is not { } export) return;
        try
        {
            _copyText(export.Json);
            CopyMessage = $"{format.Label} JSON copied.";
            ExportWarning = string.Join(" ", export.Warnings);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            CopyMessage = $"The {format.Label} JSON could not be copied: {ex.Message}";
        }
    }

    /// <summary>Saves exactly the same JSON Copy copies (UTF-8), where the person chooses. Cancelling changes nothing.</summary>
    private void SaveJson(JsonFormat format)
    {
        if (ExportJson(format) is not { } export) return;
        string? path;
        try
        {
            path = _chooseSaveFile(new SaveFileRequest($"Save {format.Label} JSON", format.SuggestedFileName(export.Name)));
        }
        catch (InvalidOperationException ex)
        {
            CopyMessage = $"The {format.Label} JSON could not be saved: {ex.Message}";
            return;
        }
        if (path is null) return;
        try
        {
            File.WriteAllText(path, export.Json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            CopyMessage = $"{format.Label} JSON saved to {Path.GetFileName(path)}.";
            ExportWarning = string.Join(" ", export.Warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CopyMessage = $"The {format.Label} JSON could not be saved: {ex.Message}";
        }
    }

    // ---- PUM Companion bundle ------------------------------------------------------------------------------------------

    private readonly Func<RollableTable, string?>? _exportPum;

    /// <summary>
    /// Save PUM Companion Bundle…: the whole table shown (every result set, so the result-set choice doesn't apply), reviewed
    /// before anything is saved. Read-only, like the other exports.
    /// </summary>
    public ICommand SavePumBundleCommand { get; }

    private void SavePumBundle()
    {
        ExportWarning = "";
        CopyMessage = _exportPum!(Current.Table) ?? "";   // cancelled: nothing to say
    }

    public ObservableCollection<RollStepViewModel> Steps { get; } = [];

    /// <summary>The largest number of rolls one Roll action can make.</summary>
    public const int MaxRollCount = 10;

    /// <summary>The choices offered beside Roll: 1 through <see cref="MaxRollCount"/>.</summary>
    public IReadOnlyList<int> RollCountOptions { get; } = Enumerable.Range(1, MaxRollCount).ToList();

    /// <summary>
    /// How many independent rolls the next Roll makes (1-10), chosen by the person; never inferred from a table's text. 1 is
    /// exactly the single roll TableForge always made. It stays as chosen while on this table, goes back to 1 when a link is
    /// followed, and is never saved (a newly opened table, or TableForge started again, starts at 1). Locked during a roll.
    /// </summary>
    public int RollCount
    {
        get => _rollCount;
        set
        {
            if (IsRolling) { Raise(); return; }
            if (!Set(ref _rollCount, Math.Clamp(value, 1, MaxRollCount))) return;
            Raise(nameof(RollButtonLabel));
        }
    }

    /// <summary>"Roll", or "Roll 3 Times" when several rolls are chosen.</summary>
    public string RollButtonLabel => RollCount == 1 ? "Roll" : $"Roll {RollCount} Times";

    /// <summary>The choice of how many rolls is locked while a roll is in the air.</summary>
    public bool IsRollCountEditable => !IsRolling;

    /// <summary>Every result of the latest Roll action on the current table, in order (one for an ordinary roll).</summary>
    public IReadOnlyList<RollOutcomeViewModel> LatestRolls { get; private set; } = [];
    public RollStepViewModel Current => Steps[^1];

    public string Title => Current.Title;
    public string DiceInfo => Current.DiceInfo;
    public IReadOnlyList<ResultSetViewModel> ResultSets => Current.ResultSets;

    /// <summary>See <see cref="RollStepViewModel.IsAligned"/>, for the current (latest) step's table.</summary>
    public bool IsAligned => Current.IsAligned;
    public bool IsNotAligned => !IsAligned;

    /// <summary>See <see cref="RollStepViewModel.AlignedRows"/>, for the current (latest) step's table.</summary>
    public IReadOnlyList<AlignedEntryRowViewModel> AlignedRows => Current.AlignedRows;

    /// <summary>The latest roll's per-result-set outputs on the current table.</summary>
    public IReadOnlyList<ResultLineViewModel> Results => Current.Outcomes.LastOrDefault()?.Lines ?? [];

    /// <summary>The latest numeric roll on the current table; empty before its first roll.</summary>
    public string RollDisplay => Current.Outcomes.LastOrDefault()?.Display ?? "";

    /// <summary>How the latest roll on the current table was reached, when a situational modifier was used ("11 +3 situational"); empty otherwise.</summary>
    public string RollBreakdown => Current.Outcomes.LastOrDefault()?.Breakdown ?? "";

    /// <summary>"Resolved as 6 (clamped)" when the latest roll on the current table was clamped to its range; empty otherwise.</summary>
    public string RollClampNote => Current.Outcomes.LastOrDefault()?.ClampNote ?? "";

    public string ManualRollText { get => _manualRollText; set => Set(ref _manualRollText, value); }

    /// <summary>
    /// The situational modifier as typed (see <see cref="SituationalModifier"/>): added to the next successful Roll on the
    /// current table, then reset to 0. Manual entry and inline rolls ignore it. Lives only in this screen's memory, and
    /// starts at 0 whenever the current table changes.
    /// </summary>
    public string ModifierText
    {
        get => _modifierText;
        set
        {
            if (!Set(ref _modifierText, value)) return;
            Raise(nameof(IsModifierValid));
            Raise(nameof(HasModifierError));
            Raise(nameof(ModifierError));
        }
    }

    /// <summary>True while <see cref="ModifierText"/> cannot be read (the inverse of <see cref="IsModifierValid"/>, for showing <see cref="ModifierError"/>).</summary>
    public bool HasModifierError => !IsModifierValid;

    /// <summary>False while <see cref="ModifierText"/> cannot be read; Roll is then unavailable. Always true where the modifier does not apply (d66).</summary>
    public bool IsModifierValid => !IsModifierAvailable || SituationalModifier.TryParse(ModifierText, out _);

    /// <summary>Why Roll is unavailable, when the modifier cannot be read; empty otherwise.</summary>
    public string ModifierError => IsModifierValid ? "" : SituationalModifier.InvalidMessage;

    /// <summary>
    /// A d66 has no modifier: its legal results are 36 separate values, which ordinary arithmetic does not respect (35 + 2 is
    /// 37, not a d66 result), so the field is not offered there.
    /// </summary>
    public bool IsModifierAvailable => !Current.Table.Dice.IsD66;

    /// <summary>The field is locked while a roll is in the air: the roll already captured its value.</summary>
    public bool IsModifierEditable => !IsRolling;

    /// <summary>Feedback for a manual value that cannot be used.</summary>
    public string Message { get => _message; private set => Set(ref _message, value); }

    /// <summary>True from pressing Roll until the provider has a final number (for dddice: until the dice settle). Roll cannot be pressed again meanwhile.</summary>
    public bool IsRolling
    {
        get => _isRolling;
        private set
        {
            if (!Set(ref _isRolling, value)) return;
            Raise(nameof(RollStatus));
            Raise(nameof(IsModifierEditable));
            Raise(nameof(IsRollCountEditable));
        }
    }

    /// <summary>"Rolling…" while a roll is in progress ("Rolling 2 of 3…" during several, "Auto-following… step 2" during a
    /// chain). Each table result is shown only once its roll ends.</summary>
    public string RollStatus => !IsRolling ? ""
        : _autoFollowStep > 0 ? $"Auto-following… step {_autoFollowStep}"
        : _batchPosition > 0 && RollCount > 1 ? $"Rolling {_batchPosition} of {RollCount}…" : "Rolling…";

    /// <summary>The roll under way, or the last one that finished (for callers that need to wait for it).</summary>
    public Task RollTask { get; private set; } = Task.CompletedTask;

    public ICommand RollCommand { get; }
    public ICommand ResolveManualCommand { get; }

    /// <summary>Stop: abandons whatever is under way — a roll, a multi-roll action, an inline roll or an auto-follow chain (see <see cref="CancelRoll"/>).</summary>
    public ICommand StopCommand { get; }

    // ---- Auto-follow linked tables -------------------------------------------------------------------------------------

    /// <summary>The most linked tables one auto-follow chain opens. Each new action of the person's starts a new chain.</summary>
    public const int MaxAutoFollows = 8;

    /// <summary>
    /// The most times one chain rolls the same table by itself. A table that links to itself ("reroll on this table") is
    /// rolled again at most this many times after the person's own roll; any loop of links ends within this many passes.
    /// </summary>
    public const int MaxAutoRollsPerTable = 2;

    /// <summary>Auto-follow linked tables, shared by every Roll screen (see <see cref="AutoFollowSetting"/>).</summary>
    public AutoFollowSetting AutoFollow { get; }

    /// <summary>True while the current table is a Manual-selection table: the screen says its result is meant to be chosen.</summary>
    public bool IsManualSelection => Current.Table.IsManualSelection;

    /// <summary>
    /// Asks the chosen provider for a final number and, only when it has one, resolves and shows it. A failed or cancelled roll
    /// changes nothing on screen except a message; it never falls back to another provider by itself.
    /// The situational modifier is read once, here, before the provider is asked: an unreadable one stops the roll before it
    /// starts, and a readable one is added to the provider's number after it succeeds (see <see cref="ModifierText"/>).
    /// </summary>
    public Task RollAsync()
    {
        if (IsRolling || !(_diceReady?.Invoke() ?? true)) return RollTask; // a second press while rolling joins the roll already under way
        var situational = 0;
        if (IsModifierAvailable && !SituationalModifier.TryParse(ModifierText, out situational))
        {
            Message = SituationalModifier.InvalidMessage;
            return RollTask;
        }
        return RollTask = RollCoreAsync(situational, RollCount);
    }

    /// <summary>
    /// One Roll action: <paramref name="count"/> independent rolls, strictly one after another (a dddice roll settles and is shown
    /// before the next one is thrown), each through the same pipeline as a single roll and each recorded in Recent Rolls on its own.
    /// The situational modifier belongs to the first roll that succeeds, then resets as always. A roll that fails or is cancelled
    /// stops the action there: the results already shown (and recorded) stay, and nothing is made up for the rest.
    /// On a table with Unique rows each result is one accepted roll (see <see cref="ThrowForResultAsync"/>), used up before the next
    /// roll is thrown, and the action stops the same way once the table is exhausted.
    /// </summary>
    private async Task RollCoreAsync(int situational, int count)
    {
        Message = "";
        _stopRequested = false;
        var step = Current;
        IsRolling = true;
        try
        {
            for (var position = 1; position <= count; position++)
            {
                SetBatchPosition(position);
                if (await ThrowForResultAsync(step.Table, situational, position, count) is not var (roll, rerolled)) return;
                // The provider produced a real roll: the modifier is used now, and used up, even if no row covers the sum.
                // (A failed or cancelled roll returned above, so the modifier is still there to retry with.)
                Apply(roll + situational, situational, fromProvider: true, position: position, count: count, rerolled: rerolled);
                if (position == 1 || situational != 0) ModifierText = "0";
                situational = 0;
            }

            // Auto-follow continues only from a single roll, and never from a Roll on a Manual-selection table: that table's
            // result is the person's to choose, so only a manually entered one continues the chain.
            if (!AutoFollow.IsOn || step.Table.IsManualSelection) return;
            if (count == 1) await AutoFollowAsync();
            else if (LatestRolls.Any(o => o.Lines.Any(l => l.Link != LinkState.None)))
                Message = "Auto-follow runs after a single roll only. Choose a link below.";
        }
        finally
        {
            SetBatchPosition(0);
            IsRolling = false;
        }
    }

    // ---- Unique Results ------------------------------------------------------------------------------------------------
    //
    // Every way a table's result is produced on this screen — the person's Roll (single or several), an auto-follow roll and a
    // manually entered result — goes through the same rules (UniqueResults) and the same two steps below: the table's used
    // results are read fresh from the store for every result, and an accepted result's Unique rows are recorded as used before
    // the result is shown (or a chain continues from it). A table without Unique rows never touches the store.

    /// <summary>True when the current table has Unique rows: only then are its used results summarised and resettable here.</summary>
    public bool HasUniqueRows => UniqueResults.HasUniqueRows(Current.Table);

    /// <summary>"Unique results: 3 of 8 used" for the current table.</summary>
    public string UsedResultsSummary => _usedUnreadable
        ? "Unique results: which have been used could not be read."
        : $"Unique results: {_usedCounts.Used} of {_usedCounts.Unique} used";

    /// <summary>Reset Used Results…: after a confirmation, every used result of the current table can be rolled again. Only the
    /// used results are forgotten — which rows are Unique is the table's own setting and never changes here.</summary>
    public ICommand ResetUsedResultsCommand { get; }

    /// <summary>Shows the current table's used results as the store says now: its rows' Used tags and the summary.</summary>
    private void ShowUsedResults()
    {
        var table = Current.Table;
        IReadOnlySet<Guid> used = new HashSet<Guid>();
        _usedUnreadable = false;
        if (UniqueResults.HasUniqueRows(table))
        {
            try { used = _usedResults.Get(table.Id); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _usedUnreadable = true; }
        }
        _usedCounts = Current.ShowUsed(used);
        Raise(nameof(HasUniqueRows));
        Raise(nameof(UsedResultsSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ResetUsedResults()
    {
        var table = Current.Table;
        var count = _usedCounts.Used;
        var message = $"Reset the used results for \"{table.Name}\"?\n\n{count} used Unique {(count == 1 ? "result" : "results")} will be " +
            "available to roll again. Which rows are Unique does not change.";
        if (!_confirm(message, "Reset Used Results")) return;
        try
        {
            _usedResults.Reset(table.Id);
            Message = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Message = $"The used results could not be reset: {ex.Message}";
        }
        ShowUsedResults(); // what the store says now, so a failed reset never looks like a successful one
    }

    /// <summary>The table's used results, fresh from the store; false, with the reason in <see cref="Message"/>, when they cannot be read.</summary>
    private bool TryReadUsed(RollableTable table, out IReadOnlySet<Guid> used, int position = 1, int count = 1)
    {
        try
        {
            used = _usedResults.Get(table.Id);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            used = new HashSet<Guid>();
            Message = Stopped($"The used results could not be read: {ex.Message}", position, count);
            return false;
        }
    }

    /// <summary>
    /// Records an accepted result's Unique rows (see <see cref="UniqueResults.RowsUsedBy"/>) as used. False, with the reason in
    /// <see cref="Message"/>, when that cannot be recorded: the caller must then not show the result, or it could come up again unnoticed.
    /// </summary>
    private bool TryUse(RollableTable table, IReadOnlyList<Guid> rows, int position = 1, int count = 1)
    {
        if (rows.Count == 0) return true; // a gap or a repeatable row uses nothing up
        try
        {
            _usedResults.MarkUsed(table.Id, rows);
            ShowUsedResults();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Message = Stopped($"The result could not be recorded as used, so it was not shown: {ex.Message}", position, count);
            return false;
        }
    }

    /// <summary>
    /// One result's roll, for the person's Roll and for auto-follow alike: the provider's number and how many throws were rerolled
    /// for it, or null with the reason in <see cref="Message"/>. A table without Unique rows is exactly one throw, as it always was.
    /// A table with them is rolled by <see cref="UniqueRoller"/> with this provider's <see cref="IDiceProvider.MaxAttemptsPerResult"/>:
    /// never rolled when it is exhausted, and every throw the original dice with the same situational modifier and Clamp. Only the
    /// accepted result's Unique rows are used up, and before it is shown (see <see cref="TryUse"/>). Exhausted, out of attempts,
    /// failed or cancelled, nothing is used and the modifier stays for another try.
    /// </summary>
    private async Task<(int Roll, int Rerolled)?> ThrowForResultAsync(RollableTable table, int situational, int position = 1, int count = 1)
    {
        if (!UniqueResults.HasUniqueRows(table))
            return await ThrowAsync(table.Dice, position, count) is { } roll ? (roll, 0) : null;

        if (!TryReadUsed(table, out var used, position, count)) return null;

        var limit = _dice.MaxAttemptsPerResult;
        var result = await UniqueRoller.RollAsync(table, situational, used, limit, () => ThrowAsync(table.Dice, position, count), () => _stopRequested);
        switch (result.Status)
        {
            case UniqueRollStatus.Exhausted:
                Message = Stopped(UniqueResults.ExhaustedMessage, position, count);
                return null;
            case UniqueRollStatus.AttemptLimitReached:
                Message = Stopped(AttemptLimitMessage(limit), position, count);
                return null;
            case UniqueRollStatus.NotRolled:
                if (Message.Length == 0) Message = Stopped("The roll was cancelled.", position, count); // Stop between two throws
                return null;
        }

        return TryUse(table, result.RowsUsed, position, count) ? (result.Roll, result.RerolledCount) : null;
    }

    /// <summary>Why a Unique table's roll stopped with every throw rejected. Nothing was used, and the dice were never changed.</summary>
    public static string AttemptLimitMessage(int attempts) =>
        $"No unused result came up in {attempts.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} rolls, so nothing was used. Roll again, or enter a result manually.";

    /// <summary>
    /// Asks the provider for one final number. Null when it could not: the reason is already in <see cref="Message"/> (with where a
    /// multi-roll action stopped). Cancellable through <see cref="CancelRoll"/> while the dice are in the air, and a number that
    /// arrives once Stop has been pressed counts as cancelled too, so a batch or chain never continues from it.
    /// </summary>
    private async Task<int?> ThrowAsync(DiceExpression dice, int position = 1, int count = 1)
    {
        var cancel = _rollCancel = new CancellationTokenSource();
        try
        {
            var roll = await _dice.RollAsync(dice, cancel.Token);
            // Dice that settle just as Stop is pressed (or the table is left) are not accepted: Stop always means no further result.
            cancel.Token.ThrowIfCancellationRequested();
            return roll;
        }
        catch (OperationCanceledException) { Message = Stopped("The roll was cancelled.", position, count); return null; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Message = Stopped(ex is DddiceException ? ex.Message : $"The roll could not be made: {ex.Message}", position, count);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_rollCancel, cancel)) _rollCancel = null;
            cancel.Dispose();
        }
    }

    /// <summary>
    /// Auto-follow linked tables: after one of the person's own actions (a single Roll, or a manually entered result) resolved on
    /// the current table, follows its one link and rolls the destination, exactly as the person would with Open and Roll — the same
    /// <see cref="Follow"/>, the same provider, the same <see cref="Apply"/> (so Recent Rolls, the overlay and Clamp behave as
    /// for any roll) — and repeats from that result. It stops, leaving every manual control in place, when:
    /// <list type="bullet">
    /// <item>the result has no link (the chain is complete), or more than one (one per result set: the person chooses);</item>
    /// <item>the link is unresolved or its table is missing;</item>
    /// <item>the destination is a Manual-selection table: it is opened but not rolled, and a result entered there starts a new chain;</item>
    /// <item><see cref="MaxAutoFollows"/> tables have been opened, or the destination has been rolled by this chain
    /// <see cref="MaxAutoRollsPerTable"/> times already;</item>
    /// <item>the dice are not ready, a roll fails or is cancelled (Stop, leaving the table, switching provider), or the setting is
    /// turned off;</item>
    /// <item>a destination with Unique rows is exhausted, runs out of attempts, or its result cannot be recorded as used: it is
    /// opened but not rolled, exactly as when its roll fails.</item>
    /// </list>
    /// Counts belong to one chain: every new action of the person's starts again from zero. The caller holds <see cref="IsRolling"/>.
    /// </summary>
    private async Task AutoFollowAsync()
    {
        var rolledHere = new Dictionary<long, int>();
        try
        {
            for (var follows = 0; ; follows++)
            {
                var linked = LatestRolls.Count == 1 ? LatestRolls[0].Lines.Where(l => l.Link != LinkState.None).ToList() : [];
                if (linked.Count == 0) return;
                if (_stopRequested || !AutoFollow.IsOn) { Message = "Auto-follow stopped."; return; }
                if (linked.Count > 1) { Message = "Auto-follow stopped: this result has more than one link. Choose one below."; return; }

                var line = linked[0];
                if (line.Link != LinkState.Resolved || line.LinkTarget is not { } target)
                {
                    Message = "Auto-follow stopped: the linked table isn't available.";
                    return;
                }
                if (follows == MaxAutoFollows)
                {
                    Message = $"Auto-follow stopped after {MaxAutoFollows} linked tables. Continue with the link below.";
                    return;
                }
                if (!target.IsManualSelection && rolledHere.GetValueOrDefault(target.Id) == MaxAutoRollsPerTable)
                {
                    Message = $"Auto-follow stopped: {target.Name} was already rolled automatically {MaxAutoRollsPerTable} times. Continue with the link below.";
                    return;
                }
                if (!target.IsManualSelection && !(_diceReady?.Invoke() ?? true))
                {
                    Message = "Auto-follow stopped: the dice aren't ready. Continue with the link below.";
                    return;
                }

                var from = $"Auto-followed from {Current.Table.Name}{(line.Range.Length > 0 ? $" ({line.Range})" : "")}";
                Follow(line, from);
                if (target.IsManualSelection) return; // opened, not rolled: the step itself asks for the person's choice

                rolledHere[target.Id] = rolledHere.GetValueOrDefault(target.Id) + 1;
                SetAutoFollowStep(follows + 1);
                // The same roll as the person's own (Unique Results included): a destination that is exhausted, runs out of attempts
                // or cannot record its result is left open and unrolled, and the chain continues only from a result already recorded.
                if (await ThrowForResultAsync(Current.Table, situational: 0) is not var (roll, rerolled))
                {
                    Message = $"Auto-follow stopped. {Message}";
                    return;
                }
                Apply(roll, fromProvider: true, rerolled: rerolled);
            }
        }
        finally
        {
            SetAutoFollowStep(0);
        }
    }

    private void SetAutoFollowStep(int step)
    {
        _autoFollowStep = step;
        Raise(nameof(RollStatus));
    }

    /// <summary>Auto-follow from a result the person entered by hand, which is how a Manual-selection table continues a chain.</summary>
    private async Task AutoFollowFromManualAsync()
    {
        _stopRequested = false;
        IsRolling = true;
        try
        {
            await AutoFollowAsync();
        }
        finally
        {
            IsRolling = false;
        }
    }

    private void SetBatchPosition(int position)
    {
        _batchPosition = position;
        Raise(nameof(RollStatus));
    }

    /// <summary>The provider's own message; for several rolls, also where the action stopped and how many results there are.</summary>
    private static string Stopped(string message, int position, int count) => count == 1 ? message
        : position == 1 ? $"{message} None of the {count} rolls was made."
        : $"{message} Stopped at roll {position} of {count}: rolls 1–{position - 1} are shown; the rest were not rolled.";

    /// <summary>
    /// Rolls one auxiliary <see cref="InlineDiceAction"/> found inside a displayed result (see <see cref="InlineDiceDetector"/>),
    /// through the same provider and the same single-flight machinery as the table's own Roll: while any roll (this one, another
    /// inline action, or the parent table) is under way, a second press is simply ignored rather than raced against it, and
    /// leaving the table (<see cref="CancelRoll"/>) abandons it exactly like a parent roll in the air. Never recorded to history
    /// and never changes the result's own text: a successful roll replaces the action's latest value, which the line shows
    /// resolved in context. A failed or cancelled one leaves the previous value where it was. The situational modifier is
    /// neither applied, used up nor reset.
    /// </summary>
    private Task RollInlineAsync(InlineDiceAction action)
    {
        if (IsRolling) return RollTask; // a roll (or a multi-roll action) is under way on this screen; this press joins it
        return RollTask = RollInlineCoreAsync(action);
    }

    private async Task RollInlineCoreAsync(InlineDiceAction action)
    {
        Message = "";
        var cancel = _rollCancel = new CancellationTokenSource();
        IsRolling = true;
        action.IsRolling = true;
        int roll;
        try
        {
            roll = await _dice.RollAsync(action.Expression, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested(); // settled just as Stop was pressed: the previous value stays, as for any cancel
        }
        catch (OperationCanceledException) { Message = "The roll was cancelled."; return; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Message = ex is DddiceException ? ex.Message : $"The roll could not be made: {ex.Message}";
            return;
        }
        finally
        {
            IsRolling = false;
            action.IsRolling = false;
            if (ReferenceEquals(_rollCancel, cancel)) _rollCancel = null;
            cancel.Dispose();
        }
        action.SetResult(roll);
        PublishInline(action);
    }

    /// <summary>
    /// A successful inline roll: the result it belongs to now reads differently, so the overlay shows it resolved in context —
    /// but only while that result is still the one on the overlay (a newer result, Clear or Test has the final say).
    /// </summary>
    private void PublishInline(InlineDiceAction action)
    {
        if (_overlay is null) return;
        var outcome = Steps.SelectMany(s => s.Outcomes).FirstOrDefault(o => o.Lines.Any(l => l.InlineActions.Contains(action)));
        if (outcome?.OverlayToken is { } token) _overlay.UpdateIfCurrent(token, OverlayResultFor(outcome));
    }

    /// <summary>The overlay's copy of one result: each result set's line, its inline rolls substituted in context, with its bold/italic.</summary>
    private static OverlayResult OverlayResultFor(RollOutcomeViewModel outcome) =>
        new(outcome.OverlayHeader.TableName, outcome.OverlayHeader.RollValue, outcome.Lines.Select(OverlayLineFor).ToList());

    private static OverlayLine OverlayLineFor(ResultLineViewModel line)
    {
        var segments = line.Segments;
        if (line.HasResolved)
        {
            var (text, styles) = InlineDiceDetector.Substitute(line.Text, line.Styles, line.InlineMatches,
                dice => line.InlineActions.FirstOrDefault(a => a.Expression == dice)?.LatestResult);
            segments = styles.Segments(text);
        }
        return new OverlayLine(line.Heading, segments.Select(OverlaySegment.From).ToList());
    }

    /// <summary>Abandons a roll still in progress (the person left this table, or switched provider) — the parent table's own
    /// roll or an <see cref="InlineDiceAction"/>'s, whichever is currently under way.</summary>
    public void CancelRoll()
    {
        _stopRequested = true; // an auto-follow chain between two rolls stops too, before it opens or rolls anything more
        try { _rollCancel?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void ResolveManual()
    {
        var dice = Current.Table.Dice;
        // A typed roll is the FINAL result (dice plus any modifier, worked out by hand), so it is checked as it is and never modified again.
        var parsed = RangeText.TryParseRollValue(ManualRollText, dice, out var value);
        // A table with rows beyond its dice (-10-0 or 26+ on a d20) was authored for modified rolls: a value one of its rows
        // covers is a legitimate final result too, even though the dice alone never give it.
        var rows = Current.Table.ResultSets.SelectMany(s => s.Entries).ToList();
        var extended = !dice.IsD66 && rows.Any(e => e.Min < dice.Min || e.Max > dice.Max);
        if (parsed && (dice.IsLegal(value) || (extended && rows.Any(e => e.Covers(value)))))
        {
            // Unique Results: the same rule as a roll, looked up exactly as typed (never clamped), but a person's choice is never
            // rerolled — a value on a used Unique row (in any result set) is refused, and an accepted one uses its rows up first.
            var table = Current.Table;
            if (UniqueResults.HasUniqueRows(table))
            {
                if (!TryReadUsed(table, out var used)) return;
                if (UniqueResults.IsRejected(table, value, used))
                {
                    Message = UniqueResults.AlreadyUsedMessage;
                    return;
                }
                if (!TryUse(table, UniqueResults.RowsUsedBy(table, value))) return;
            }
            Apply(value);
            // A chosen result continues auto-follow from any table — on a Manual-selection table, this is the only way it does.
            if (AutoFollow.IsOn) RollTask = AutoFollowFromManualAsync();
        }
        else if (dice.IsD66)
            // The final d66 value is typed (35), never two separate dice; say why an impossible one is refused.
            Message = parsed ? $"{value} is not a possible d66 result. Enter two digits from 1 to 6, such as 35." : "Enter a d66 result: two digits from 1 to 6, such as 35.";
        else if (extended)
            Message = "Enter a value covered by this table.";
        else
            Message = $"Enter a whole number from {dice.FormatValue(dice.Min)} to {dice.FormatValue(dice.Max)}.";
    }

    /// <param name="roll">The final value to resolve: the provider's roll plus <paramref name="situational"/>, or a manual entry as typed.</param>
    /// <param name="situational">The situational modifier already included in <paramref name="roll"/>; 0 for none.</param>
    /// <param name="fromProvider">True for a provider-driven roll: only those are clamped (see <see cref="TableClamp"/>), after all
    /// the roll's arithmetic and before lookup. A manual entry is the user's explicit final value and is looked up exactly as typed.</param>
    /// <param name="position">Which roll of a multi-roll action this is (1-based).</param>
    /// <param name="count">How many rolls that action asked for; 1 for an ordinary roll or a manual entry.</param>
    /// <param name="rerolled">How many throws were rerolled before this one because they landed on used Unique rows.</param>
    private void Apply(int roll, int situational = 0, bool fromProvider = false, int position = 1, int count = 1, int rerolled = 0)
    {
        Message = "";
        var step = Current;
        var dice = step.Table.Dice;
        // A modified roll is a calculated number, not a die face, so it is never shown as d100's "00".
        string Format(int value) => situational == 0 ? dice.FormatValue(value) : value.ToString();
        var formatted = Format(roll);
        var display = formatted != roll.ToString() ? $"Rolled {formatted} (numeric {roll})"
            : dice.IsD66 ? $"Rolled {roll} (d66)"   // never shown as a sum: 3 then 5 is 35
            : dice.Modifier != 0 ? $"Rolled {roll} ({dice})"   // keep the expression visible when a modifier was involved: "Rolled 7 (2d6+1)"
            : $"Rolled {roll}";
        var breakdown = situational == 0 ? "" : $"{dice.FormatValue(roll - situational)} {SituationalModifier.Signed(situational)} situational";

        // The calculated roll stays what is shown as rolled; only the value the table is looked up with is clamped.
        var lookup = fromProvider ? TableClamp.LookupValue(step.Table, roll) : roll;
        int? clamped = lookup != roll ? lookup : null;
        var clampNote = clamped is null ? "" : $"Resolved as {lookup} (clamped)";

        var lines = new List<ResultLineViewModel>();
        var resolved = TableResolver.Resolve(step.Table, lookup).Results;
        foreach (var r in resolved)
        {
            var heading = r.ResultSet.Name;
            lines.Add(r.Status switch
            {
                ResolutionStatus.Matched => MatchedLine(heading, r.Entry!),
                ResolutionStatus.NoMatch => new(heading, "", $"No entry covers {Format(lookup)}.", true),
                _ => new(heading, "", $"Ambiguous: {string.Join(" and ", r.Matches.Select(m => $"\"{m.Text}\" ({m.RangeLabel})"))} both cover {Format(lookup)}.", true),
            });
        }

        // Links and inline dice offered by earlier Roll actions are superseded by this one; the other results of the same
        // multi-roll action stay active, each with its own.
        if (position == 1) DeactivateLinks();
        var outcome = new RollOutcomeViewModel(display, lines, breakdown, clampNote, count > 1 ? $"Roll {position}" : "", rerolled);
        step.Add(outcome, lookup, keepHighlights: position > 1);
        LatestRolls = position == 1 ? [outcome] : [.. LatestRolls, outcome];
        Raise(nameof(LatestRolls));
        Raise(nameof(Results));
        Raise(nameof(RollDisplay));
        Raise(nameof(RollBreakdown));
        Raise(nameof(RollClampNote));

        // The Streaming Overlay: this result becomes the current one (a No Match line included). It shows the roll exactly as
        // calculated and displayed above — never the clamped lookup value, the clamp note or the modifier's arithmetic.
        if (_overlay is not null)
        {
            outcome.OverlayHeader = (step.Table.Name, formatted);
            outcome.OverlayToken = _overlay.Publish(OverlayResultFor(outcome));
        }

        // What the user saw, as text: each set's output, headed by the set's name when it has one.
        var shown = string.Join("\n", lines.Select(l => l.HasHeading ? $"{l.Heading}: {l.Text}" : l.Text));
        _rolled?.Invoke(new RollSnapshot(step.Table.Id, step.Table.Name, dice.ToString(), roll, shown, situational, clamped, rerolled));

        // Only a hint beside the controls: the result itself, Recent Rolls and the overlay say exactly what they always did.
        if (fromProvider && situational != 0 && resolved.Any(r => r.Status == ResolutionStatus.NoMatch))
            Message = ModifierOutsideTableHint(step.Table, roll);
    }

    /// <summary>
    /// When a situational modifier took a roll past the rows of a table that could clamp but doesn't, where the fix is: "The
    /// modifier took the roll above this table's highest row. To use the nearest row instead, turn on Clamp in Edit table."
    /// Empty otherwise (Clamp on, not available for this table, or a roll inside the table's range that falls in a gap).
    /// </summary>
    public static string ModifierOutsideTableHint(RollableTable table, int roll)
    {
        if (table.ClampResultsToRange || !TableClamp.TryGetRange(table, out var min, out var max) || (roll >= min && roll <= max)) return "";
        return $"The modifier took the roll {(roll < min ? "below this table's lowest" : "above this table's highest")} row. " +
            "To use the nearest row instead, turn on Clamp in Edit table.";
    }

    private ResultLineViewModel MatchedLine(string heading, TableEntry entry)
    {
        LinkState link = LinkState.None;
        string linkName = "";
        RollableTable? target = null;

        if (entry.LinkedTableId is long id)
        {
            try { target = _loadTable?.Invoke(id); }
            catch (Exception) { target = null; } // an unreadable destination is shown as unavailable; it must not stop the roll
            link = target is null ? LinkState.Missing : LinkState.Resolved;
            linkName = target?.Name ?? "";
        }
        else if (entry.UnresolvedLinkName is not null)
        {
            link = LinkState.Unresolved;
            linkName = entry.UnresolvedLinkName;
        }

        var inlineMatches = InlineDiceDetector.FindAll(entry.Text);
        var expressions = inlineMatches.Select(m => m.Expression).Distinct().ToList();
        var inlineActions = expressions.Select(e => new InlineDiceAction(e, namedOnReroll: expressions.Count > 1)).ToList();
        var line = new ResultLineViewModel(heading, entry.RangeLabel, entry.Text, false)
        {
            Link = link, LinkName = linkName, LinkTarget = target, InlineMatches = inlineMatches, InlineActions = inlineActions,
            Styles = entry.Styles,
        };
        foreach (var action in inlineActions)
            action.RollCommand = new RelayCommand(() => _ = RollInlineAsync(action), () => !IsRolling && (_diceReady?.Invoke() ?? true));
        if (link == LinkState.Resolved) line.FollowCommand = new RelayCommand(() => Follow(line), () => line.IsActive && !IsRolling);
        return line;
    }

    /// <summary>
    /// Makes the destination the current table, keeps the trail, and does not roll it. User-triggered (Open), or the first half of
    /// one auto-follow step, which passes <paramref name="autoFollowedFrom"/> (see <see cref="RollStepViewModel.AutoFollowedFrom"/>).
    /// </summary>
    private void Follow(ResultLineViewModel line, string autoFollowedFrom = "")
    {
        if (!line.IsActive || line.LinkTarget is null) return;
        EnterStep(new RollStepViewModel(line.LinkTarget, isFirst: false) { AutoFollowedFrom = autoFollowedFrom });
    }

    // ---- Back to the original table ------------------------------------------------------------------------------------

    /// <summary>
    /// True when the current table is not the one this screen was opened on (after following one or more links, by hand or by
    /// auto-follow). Never true on the original table itself, including when a chain of links loops back to it.
    /// </summary>
    public bool ShowReturnToOriginal => Current.Table.Id != Steps[0].Table.Id;

    /// <summary>"← Back to Area Type": names the table this screen was opened on.</summary>
    public string ReturnToOriginalLabel => $"← Back to {Steps[0].Title}";

    /// <summary>
    /// Back to the original table: makes the table this screen was opened on the current table again, exactly as following a
    /// link to it would (a new step at the end of the trail; every earlier step and result stays as it was). Never rolls,
    /// never returns by itself, and is unavailable while anything is being rolled.
    /// </summary>
    public ICommand ReturnToOriginalCommand { get; }

    private void ReturnToOriginal()
    {
        if (IsRolling || !ShowReturnToOriginal) return;
        EnterStep(new RollStepViewModel(Steps[0].Table, isFirst: false) { IsReturn = true });
    }

    /// <summary>
    /// Makes <paramref name="step"/> the current table: earlier links stop being followable, and everything that belongs to the
    /// table it was typed or chosen for (manual entry, modifier, roll count, export choice, message) starts afresh.
    /// </summary>
    private void EnterStep(RollStepViewModel step)
    {
        DeactivateLinks();
        Steps.Add(step);
        ManualRollText = "";
        ModifierText = "0"; // a modifier belongs to the table it was typed for, never the one followed to
        RollCount = 1;      // and so does a choice of several rolls
        _copyResultSetIndex = 0;
        CopyMessage = "";
        ExportWarning = "";
        Raise(nameof(CopyResultSetIndex));
        Raise(nameof(CopyResultSetNames));
        Raise(nameof(ShowCopyResultSetChoice));
        LatestRolls = [];
        Raise(nameof(LatestRolls));
        Message = "";
        Raise(nameof(Title));
        Raise(nameof(DiceInfo));
        Raise(nameof(ResultSets));
        Raise(nameof(IsAligned));
        Raise(nameof(IsNotAligned));
        Raise(nameof(AlignedRows));
        Raise(nameof(Results));
        Raise(nameof(RollDisplay));
        Raise(nameof(RollBreakdown));
        Raise(nameof(RollClampNote));
        Raise(nameof(IsModifierAvailable));
        Raise(nameof(IsModifierValid));
        Raise(nameof(HasModifierError));
        Raise(nameof(ModifierError));
        Raise(nameof(IsManualSelection));
        Raise(nameof(ShowReturnToOriginal));
        ShowUsedResults();
        _tableUsed?.Invoke(step.Table.Id);
    }

    private void DeactivateLinks()
    {
        foreach (var step in Steps)
            foreach (var outcome in step.Outcomes)
                foreach (var line in outcome.Lines)
                    line.IsActive = false;
    }
}
