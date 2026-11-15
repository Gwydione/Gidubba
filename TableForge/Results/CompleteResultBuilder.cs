using TableForge.Dice;
using TableForge.Domain;

namespace TableForge.Results;

// Complete Roll Results: one readable block assembled from a Roll screen's trail — the latest roll and every linked roll that
// followed from it — in resolution order, using each result's own text exactly as written (inline dice substituted only where
// they were actually rolled). Pure and UI-free on purpose: this namespace depends only on TableForge.Domain and
// InlineDiceDetector, never on WPF or the view models, so another front end (the Linux/Avalonia port) can reuse it as it is.
// A front end only turns its own trail into a CompleteResultInput and shows (or copies) the CompleteResult that comes back.

/// <summary>How a step of the trail was reached.</summary>
public enum CompleteResultStepKind
{
    /// <summary>The table the Roll screen was opened on (always the first step).</summary>
    Original,

    /// <summary>A step added by Back to the original table.</summary>
    Return,

    /// <summary>A step opened by following a link, by hand (Open) or by auto-follow.</summary>
    Linked,
}

/// <summary>What a result's entry links to, as the Roll screen shows it.</summary>
public enum CompleteResultLink { None, Resolved, Unresolved, Missing }

/// <summary>One result set's output for one roll.</summary>
/// <param name="Heading">The result set's name; "" for an unnamed set.</param>
/// <param name="Text">The matched entry's text exactly as stored, or the Roll screen's No Match / Ambiguous message.</param>
/// <param name="IsProblem">True for a No Match or Ambiguous message (it is not a table's text).</param>
public sealed record CompleteResultLineInput(string Heading, string Text, bool IsProblem = false)
{
    public TextStyles Styles { get; init; } = TextStyles.Empty;

    /// <summary>Where each inline dice expression sits in <see cref="Text"/> (see <see cref="InlineDiceDetector.FindAll"/>).</summary>
    public IReadOnlyList<InlineDiceMatch> InlineMatches { get; init; } = [];

    /// <summary>The value each inline expression was actually rolled to. An expression without one stays as written.</summary>
    public IReadOnlyDictionary<DiceExpression, int> InlineValues { get; init; } = NoValues;

    public CompleteResultLink Link { get; init; }

    /// <summary>The destination's name for a resolved link; the intended name for an unresolved one.</summary>
    public string LinkName { get; init; } = "";

    internal static readonly IReadOnlyDictionary<DiceExpression, int> NoValues = new Dictionary<DiceExpression, int>();
}

/// <summary>One roll on a step's table (one result of a Roll action, or one entered result).</summary>
/// <param name="ActionNumber">Which of the person's actions produced it. Results of one multi-roll action share it; a step's
/// latest action is the one with the highest number, and only that action counts towards the Complete Result.</param>
/// <param name="RollText">The roll exactly as the Roll screen shows it ("14", "00", "35").</param>
public sealed record CompleteResultRollInput(int ActionNumber, string RollText, IReadOnlyList<CompleteResultLineInput> Lines)
{
    /// <summary>"Roll 2" of a multi-roll action is 2; 0 for an ordinary single roll.</summary>
    public int BatchPosition { get; init; }

    /// <summary>The value the table was looked up with when Clamp changed it; null when it was not clamped.</summary>
    public int? ResolvedAs { get; init; }

    /// <summary>True for a result the person entered (typed) rather than rolled.</summary>
    public bool Entered { get; init; }
}

/// <summary>A line of the previous step: its roll (index into that step's <see cref="CompleteResultStepInput.Rolls"/>) and result set.</summary>
public readonly record struct CompleteResultLineRef(int RollIndex, int LineIndex);

/// <summary>One step of the trail, with every roll made on it (oldest first).</summary>
public sealed record CompleteResultStepInput(string TableName, CompleteResultStepKind Kind, IReadOnlyList<CompleteResultRollInput> Rolls)
{
    public bool IsManualSelection { get; init; }

    /// <summary>True for a step auto-follow opened.</summary>
    public bool IsAutoFollowed { get; init; }

    /// <summary>For a linked step, the previous step's line whose link opened it; null otherwise (or when unknown).</summary>
    public CompleteResultLineRef? OpenedFrom { get; init; }
}

/// <summary>A Roll screen's whole trail, oldest step first.</summary>
/// <param name="InProgress">True while a roll, an inline roll or an auto-follow chain is under way.</param>
public sealed record CompleteResultInput(IReadOnlyList<CompleteResultStepInput> Steps, bool InProgress = false);

/// <summary>Why the Complete Result ends with a linked table that has no result yet.</summary>
public enum CompleteResultPendingReason
{
    /// <summary>Opened, not rolled yet (Open without Roll).</summary>
    NotRolled,

    /// <summary>Its roll is under way.</summary>
    Rolling,

    /// <summary>A Manual-selection table: its result is the person's to choose.</summary>
    ChooseResult,

    /// <summary>Auto-follow opened it but stopped before a result (Stop, dice not ready, Unique exhausted, …).</summary>
    AutoFollowStopped,
}

/// <summary>The linked table the chain is waiting on. Shown, never copied.</summary>
public sealed record CompleteResultPending(string TableName, CompleteResultPendingReason Reason)
{
    public string Text => Reason switch
    {
        CompleteResultPendingReason.Rolling => $"→ {TableName} — rolling…",
        CompleteResultPendingReason.ChooseResult => $"→ {TableName} — choose a result",
        CompleteResultPendingReason.AutoFollowStopped => $"→ {TableName} — not rolled (auto-follow stopped)",
        _ => $"→ {TableName} — not rolled yet",
    };
}

/// <summary>One result set's output in the Complete Result, inline dice substituted where they were rolled.</summary>
/// <param name="Note">Shown, never copied: a link the chain did not follow ("→ Loot (not followed)") or could not
/// ("⚠ link unavailable"), on the chain's last result only; "" otherwise.</param>
/// <param name="IsWarningNote">True when <paramref name="Note"/> is a warning (a link that could not be followed).</param>
public sealed record CompleteResultLine(string TableName, string Heading, IReadOnlyList<FormattedSegment> Segments, bool IsProblem,
    string Note = "", bool IsWarningNote = false)
{
    public string Text => string.Concat(Segments.Select(s => s.Text));
    public bool HasHeading => Heading.Length > 0;
    public bool HasNote => Note.Length > 0;

    /// <summary>The line as a front end shows it: "Heading: " (bold) before the text for a named result set, the text alone otherwise.</summary>
    public IReadOnlyList<FormattedSegment> ShownSegments =>
        HasHeading ? [new FormattedSegment($"{Heading}: ", TextStyle.Bold), .. Segments] : Segments;

    /// <summary>
    /// What Copy Complete Result copies for this line: the text alone ("Heading: text" for a named result set). A No Match or
    /// Ambiguous message also names its table ("Occupants: No entry covers 23."), so it still makes sense outside Gidubba.
    /// </summary>
    public string CopyText
    {
        get
        {
            var text = HasHeading ? $"{Heading}: {Text}" : Text;
            if (!IsProblem) return text;
            return HasHeading ? $"{TableName} ({Heading}): {Text}" : $"{TableName}: {Text}";
        }
    }
}

/// <summary>One roll's part of the Complete Result: a caption ("Occupants · 11 (resolved as 10)") and each result set's line.</summary>
/// <param name="StepIndex">Which step of the trail it came from.</param>
public sealed record CompleteResultPiece(int StepIndex, string TableName, string Caption, IReadOnlyList<CompleteResultLine> Lines)
{
    /// <summary>The roll exactly as the Roll screen shows it ("14", "00"): the Streaming Overlay's roll value for this part.</summary>
    public string RollText { get; init; } = "";
}

/// <summary>The assembled outcome of the newest chain of rolls (see <see cref="CompleteResultBuilder"/>).</summary>
/// <param name="IsPrevious">True after Back to the original table, until the next result: the chain shown is the one before it.</param>
/// <param name="SpansTables">True when the chain reaches at least one linked table (rolled or pending).</param>
public sealed record CompleteResult(IReadOnlyList<CompleteResultPiece> Pieces, CompleteResultPending? Pending, bool IsPrevious, bool SpansTables)
{
    public static CompleteResult None { get; } = new([], null, false, false);

    /// <summary>
    /// Worth showing: the chain reaches a linked table, or holds more than one roll result (one multi-roll action). A single
    /// roll — even with several result sets — is not: it would only repeat the result shown right below it.
    /// </summary>
    public bool IsVisible => SpansTables || Pieces.Count >= 2;

    public bool HasPending => Pending is not null;

    /// <summary>
    /// The text Copy Complete Result copies: every line's <see cref="CompleteResultLine.CopyText"/>, one per line, in order,
    /// with no captions, notes or pending placeholder. Line breaks inside a result's own text become <paramref name="newline"/> too.
    /// </summary>
    public string ToPlainText(string newline) =>
        string.Join(newline, Pieces.SelectMany(p => p.Lines).Select(l => l.CopyText.ReplaceLineEndings(newline)));
}

/// <summary>
/// Builds the Complete Result from a Roll screen's trail. The rules:
/// <list type="bullet">
/// <item>A chain starts at the latest Original or Return step that has a result, and runs through every linked step after it,
/// up to (not including) the next Return step.</item>
/// <item>When a Return step has no result yet, the chain before it is still shown (<see cref="CompleteResult.IsPrevious"/>).</item>
/// <item>Each step contributes its latest action only — every result of it, in order. Rolling a table again replaces that
/// step's part in place; earlier rolls stay in the trail, never here.</item>
/// <item>Steps follow in trail order, which is resolution order: a linked table's results always come after the results of
/// the action it was followed from.</item>
/// <item>A linked step with no result ends the chain as <see cref="CompleteResult.Pending"/>.</item>
/// </list>
/// Text is never generated: each line is the entry's own text, its inline dice replaced only by values actually rolled.
/// </summary>
public static class CompleteResultBuilder
{
    public static CompleteResult Build(CompleteResultInput input)
    {
        var steps = input.Steps;

        var root = -1;
        for (var i = steps.Count - 1; i >= 0; i--)
            if (steps[i].Kind != CompleteResultStepKind.Linked && steps[i].Rolls.Count > 0) { root = i; break; }
        if (root < 0) return CompleteResult.None;

        var end = root + 1;
        while (end < steps.Count && steps[end].Kind == CompleteResultStepKind.Linked) end++;
        var isPrevious = end < steps.Count;

        var pieces = new List<CompleteResultPiece>();
        CompleteResultPending? pending = null;
        var spansTables = false;
        var lastWithResults = -1;

        for (var i = root; i < end; i++)
        {
            var step = steps[i];
            if (step.Kind == CompleteResultStepKind.Linked) spansTables = true;
            var action = LatestAction(step);
            if (action.Count == 0)
            {
                // Only the last step of a chain can be without a result: nothing can be followed from it.
                if (i == end - 1) pending = new CompleteResultPending(step.TableName, PendingReason(step, input.InProgress));
                continue;
            }

            lastWithResults = pieces.Count;
            var origin = i > root ? Origin(steps[i - 1], step.OpenedFrom) : "";
            for (var r = 0; r < action.Count; r++)
            {
                var roll = action[r];
                var lines = roll.Lines.Select(l => new CompleteResultLine(step.TableName, l.Heading, Segments(l), l.IsProblem)).ToList();
                pieces.Add(new CompleteResultPiece(i, step.TableName, Caption(step.TableName, roll, r == 0 ? origin : ""), lines) { RollText = roll.RollText });
            }
        }

        // The chain's last results say which of their links it did not (or could not) follow — but not while something is still
        // under way (auto-follow may be about to follow it), and not once a pending table shows where the chain went.
        if (pending is null && !input.InProgress && lastWithResults >= 0)
        {
            var lastStep = steps[pieces[lastWithResults].StepIndex];
            var action = LatestAction(lastStep);
            for (var p = lastWithResults; p < pieces.Count; p++)
            {
                var source = action[p - lastWithResults].Lines;
                var lines = pieces[p].Lines;
                pieces[p] = pieces[p] with
                {
                    Lines = lines.Select((l, n) => l with { Note = Note(source[n]), IsWarningNote = IsUnavailable(source[n]) }).ToList(),
                };
            }
        }

        return new CompleteResult(pieces, pending, isPrevious, spansTables);
    }

    /// <summary>Every result of the step's latest action, in the order they were made.</summary>
    private static IReadOnlyList<CompleteResultRollInput> LatestAction(CompleteResultStepInput step)
    {
        if (step.Rolls.Count == 0) return [];
        var latest = step.Rolls.Max(r => r.ActionNumber);
        return step.Rolls.Where(r => r.ActionNumber == latest).ToList();
    }

    private static CompleteResultPendingReason PendingReason(CompleteResultStepInput step, bool inProgress) =>
        step.IsManualSelection ? CompleteResultPendingReason.ChooseResult
        : inProgress ? CompleteResultPendingReason.Rolling
        : step.IsAutoFollowed ? CompleteResultPendingReason.AutoFollowStopped
        : CompleteResultPendingReason.NotRolled;

    /// <summary>The text as shown, with every inline expression that was actually rolled replaced by its value, in place.</summary>
    private static IReadOnlyList<FormattedSegment> Segments(CompleteResultLineInput line)
    {
        if (line.InlineValues.Count == 0 || line.InlineMatches.Count == 0) return line.Styles.Segments(line.Text);
        var (text, styles) = InlineDiceDetector.Substitute(line.Text, line.Styles, line.InlineMatches,
            dice => line.InlineValues.TryGetValue(dice, out var value) ? value.ToString() : null);
        return styles.Segments(text);
    }

    /// <summary>"Area Type · 14", "Area Type · roll 2 · 9", "Occupants · 11 (resolved as 10)", "Area Type · 14 (entered)",
    /// "Loot · 4 (from roll 2)".</summary>
    private static string Caption(string tableName, CompleteResultRollInput roll, string origin)
    {
        var caption = roll.BatchPosition > 0 ? $"{tableName} · roll {roll.BatchPosition} · {roll.RollText}" : $"{tableName} · {roll.RollText}";
        if (roll.ResolvedAs is { } resolvedAs) caption += $" (resolved as {resolvedAs})";
        if (roll.Entered) caption += " (entered)";
        if (origin.Length > 0) caption += $" ({origin})";
        return caption;
    }

    /// <summary>
    /// Where a linked step came from, only when the action it was followed from offered more than one link (otherwise there is
    /// nothing to tell apart): "from roll 2", "from Treasure", "from roll 2, Treasure". "" when it needs no saying.
    /// </summary>
    private static string Origin(CompleteResultStepInput parent, CompleteResultLineRef? openedFrom)
    {
        if (openedFrom is not { } from || from.RollIndex < 0 || from.RollIndex >= parent.Rolls.Count) return "";
        var roll = parent.Rolls[from.RollIndex];
        if (from.LineIndex < 0 || from.LineIndex >= roll.Lines.Count) return "";
        var action = LatestAction(parent);
        if (action.Sum(r => r.Lines.Count(l => l.Link != CompleteResultLink.None)) < 2) return "";

        var parts = new List<string>();
        if (action.Count > 1 && roll.BatchPosition > 0) parts.Add($"roll {roll.BatchPosition}");
        if (roll.Lines.Count(l => l.Link != CompleteResultLink.None) > 1)
        {
            var heading = roll.Lines[from.LineIndex].Heading;
            parts.Add(heading.Length > 0 ? heading : $"result set {from.LineIndex + 1}");
        }
        return parts.Count == 0 ? "" : "from " + string.Join(", ", parts);
    }

    private static bool IsUnavailable(CompleteResultLineInput line) =>
        line.Link is CompleteResultLink.Unresolved or CompleteResultLink.Missing;

    private static string Note(CompleteResultLineInput line) => line.Link switch
    {
        CompleteResultLink.Resolved => $"→ {line.LinkName} (not followed)",
        CompleteResultLink.Unresolved or CompleteResultLink.Missing => "⚠ link unavailable",
        _ => "",
    };
}
