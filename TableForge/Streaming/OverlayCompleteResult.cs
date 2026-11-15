using TableForge.Results;

namespace TableForge.Streaming;

/// <summary>
/// Gidubba 1.6.0 "Overlay shows: Complete result": the Roll screen's <see cref="CompleteResult"/> (built by the one shared
/// <see cref="CompleteResultBuilder"/>; nothing is assembled here) as the overlay's existing <see cref="OverlayResult"/>, so
/// the page, its CSS and <c>/state</c>'s shape stay exactly as they are.
/// <list type="bullet">
/// <item>The first part is the result as Latest Roll shows it: the overlay's table name and roll value are its own.</item>
/// <item>Every later part follows in order; its first line carries an <see cref="OverlayPartLabel"/> (its table, blank when the
/// same as the part shown before it, and its roll), shown as Show Table Name and Show Roll Value say.</item>
/// <item>Only result text is shown: never the Roll screen's captions, pending line or notes.</item>
/// <item>Too long for <see cref="OverlayLimits"/>: the first part (for context) and as many of the latest parts as fit are kept,
/// with one "…" line where the others were. The latest part is never left out; if it alone is too long, the limits cut its
/// end with "…", as they would for Latest Roll.</item>
/// </list>
/// </summary>
public static class OverlayCompleteResult
{
    /// <summary>The line standing in for parts left out of a long chain.</summary>
    public const string OmittedMarker = OverlayLimits.Ellipsis;

    public static OverlayResult For(CompleteResult result)
    {
        var pieces = result.Pieces;
        if (pieces.Count == 0) throw new ArgumentException("A Complete Result with no results has nothing to show.", nameof(result));

        var shown = Choose(pieces.Select(Cost).ToList());
        var lines = new List<OverlayLine>();
        string? previousTable = null;
        var omitted = false;
        var expected = 0;
        foreach (var index in shown)
        {
            if (index != expected)
            {
                lines.Add(Marker);
                omitted = true;
            }
            expected = index + 1;

            var piece = pieces[index];
            var label = index == 0 ? null : new OverlayPartLabel(piece.TableName == previousTable ? "" : piece.TableName, piece.RollText);
            for (var n = 0; n < piece.Lines.Count; n++)
            {
                var line = piece.Lines[n];
                lines.Add(new OverlayLine(line.Heading, line.Segments.Select(OverlaySegment.From).ToList()) { Part = n == 0 ? label : null });
            }
            previousTable = piece.TableName;
        }

        return new OverlayResult(pieces[0].TableName, pieces[0].RollText, lines) { Omitted = omitted };
    }

    private static OverlayLine Marker { get; } = new("", [new OverlaySegment(OmittedMarker)]);

    /// <summary>What a part takes of the overlay's limits: lines, result-text characters and styled stretches (as
    /// <see cref="OverlayLimits.Apply"/> counts them: empty stretches show nothing and count for nothing).</summary>
    private readonly record struct Size(int Lines, int Text, int Segments)
    {
        public static Size operator +(Size a, Size b) => new(a.Lines + b.Lines, a.Text + b.Text, a.Segments + b.Segments);
        public bool Fits => Lines <= OverlayLimits.MaxLines && Text <= OverlayLimits.MaxTextLength && Segments <= OverlayLimits.MaxSegments;
    }

    private static Size Cost(CompleteResultPiece piece) => new(
        piece.Lines.Count,
        piece.Lines.Sum(l => l.Segments.Sum(s => s.Text.Length)),
        piece.Lines.Sum(l => l.Segments.Count(s => s.Text.Length > 0)));

    private static readonly Size MarkerCost = new(1, OmittedMarker.Length, 1);

    /// <summary>The indexes of the parts to show, in order.</summary>
    private static List<int> Choose(IReadOnlyList<Size> costs)
    {
        var all = Enumerable.Range(0, costs.Count).ToList();
        if (costs.Aggregate(new Size(), (a, b) => a + b).Fits) return all;

        // Not everything fits, so something is left out and the "…" line is shown. The latest part always stays.
        var last = costs.Count - 1;
        var used = costs[last] + MarkerCost;
        var keepFirst = last > 0 && (used + costs[0]).Fits;
        if (keepFirst) used += costs[0];

        var latest = new List<int> { last };
        for (var i = last - 1; i > 0; i--)
        {
            if (!(used + costs[i]).Fits) break;   // the latest parts only: never a gap among them
            used += costs[i];
            latest.Insert(0, i);
        }

        // Everything between the first and the latest fitted after all (only the marker's own line was in the way): show it all.
        if (keepFirst && latest[0] == 1) return all;
        return keepFirst ? [0, .. latest] : latest;
    }
}
