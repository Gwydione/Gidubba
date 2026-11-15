using TableForge.Domain;

namespace TableForge.Streaming;

/// <summary>A stretch of overlay text in one style. Always plain text: it is never interpreted as markup anywhere.</summary>
public sealed record OverlaySegment(string Text, bool Bold = false, bool Italic = false)
{
    public static OverlaySegment From(FormattedSegment segment) => new(segment.Text, segment.IsBold, segment.IsItalic);
}

/// <summary>One result set's output: its heading ("" for an unnamed set) and its text, resolved in context.</summary>
public sealed record OverlayLine(string Heading, IReadOnlyList<OverlaySegment> Segments)
{
    public string Text => string.Concat(Segments.Select(s => s.Text));

    /// <summary>
    /// Complete Result mode only: set on the first line of every part after the first, saying which table ("" when it is the
    /// same table as the part shown before it) and roll that part came from. Shown as Show Table Name and Show Roll Value say
    /// (<see cref="OverlayPublisher.Shown"/>); null on every other line.
    /// </summary>
    public OverlayPartLabel? Part { get; init; }
}

/// <summary>Where a part of a Complete Result on the overlay came from: "Loot" and "4".</summary>
public sealed record OverlayPartLabel(string TableName, string RollValue);

/// <summary>
/// What the Streaming Overlay shows for one resolved result: a transient presentation copy, never an authoritative copy of
/// table data. <see cref="RollValue"/> is the final calculated roll exactly as it is shown on the Roll screen.
/// </summary>
public sealed record OverlayResult(string TableName, string RollValue, IReadOnlyList<OverlayLine> Lines)
{
    /// <summary>True when parts of a long Complete Result were left out to fit (an "…" line stands in their place).</summary>
    public bool Omitted { get; init; }

    /// <summary>The Show Test Result content: proves the OBS source works without making a real roll.</summary>
    public static OverlayResult Test { get; } =
        new("Gidubba Overlay Test", "12", [new OverlayLine("", [new OverlaySegment("Your streaming overlay is working.")])]);
}

/// <summary>
/// Overlay-only output limits. Real tables come nowhere near them; they only make the size of <c>/state</c>, the work of
/// building it and what OBS has to lay out deterministic for pathological imported or typed text. They apply to the overlay's
/// COPY: table data, Recent Rolls and everything else stay exactly as they are.
/// <para>In order: lines beyond <see cref="MaxLines"/> are dropped (the last kept line ends " …"); each heading is cut to
/// <see cref="MaxHeadingLength"/>; the table name to <see cref="MaxTableNameLength"/>; the result text, read in order across
/// lines, is cut at <see cref="MaxTextLength"/> (later text and lines are dropped); finally a line whose formatting would take
/// the total past <see cref="MaxSegments"/> keeps what fits and ends in one plain segment holding the rest of its text. A cut
/// never splits a surrogate pair and always ends in "…" inside the limit.</para>
/// </summary>
public static class OverlayLimits
{
    public const int MaxTableNameLength = 200;
    public const int MaxLines = 20;
    public const int MaxHeadingLength = 100;
    public const int MaxTextLength = 4000;
    public const int MaxSegments = 500;
    public const string Ellipsis = "…";

    /// <summary>The bounded copy of <paramref name="result"/>, and whether anything had to be cut. The input is never changed.</summary>
    public static (OverlayResult Result, bool Truncated) Apply(OverlayResult result)
    {
        var truncated = false;

        // 1. At most MaxLines lines; the last one kept says that more followed.
        var lines = result.Lines.Select(l => new LineDraft(l.Heading, [.. l.Segments]) { Part = l.Part }).ToList();
        if (lines.Count > MaxLines)
        {
            lines = lines.Take(MaxLines).ToList();
            lines[^1].Segments.Add(new OverlaySegment(" " + Ellipsis));
            truncated = true;
        }

        // 2. Headings and the table name.
        foreach (var line in lines)
        {
            if (Cut(line.Heading, MaxHeadingLength) is { } heading) { line.Heading = heading; truncated = true; }
            if (line.Part is { } part && Cut(part.TableName, MaxTableNameLength) is { } partName) { line.Part = part with { TableName = partName }; truncated = true; }
        }
        var tableName = result.TableName;
        if (Cut(tableName, MaxTableNameLength) is { } name) { tableName = name; truncated = true; }

        // 3. The result text, in reading order across lines.
        var budget = MaxTextLength;
        var kept = new List<LineDraft>();
        foreach (var line in lines)
        {
            if (budget <= 0 && line.Segments.Any(s => s.Text.Length > 0)) { truncated = true; break; }
            var segments = new List<OverlaySegment>();
            foreach (var segment in line.Segments)
            {
                if (segment.Text.Length == 0) continue;                   // shows nothing
                if (segment.Text.Length <= budget)
                {
                    segments.Add(segment);
                    budget -= segment.Text.Length;
                    continue;
                }
                truncated = true;
                if (budget > 0) segments.Add(segment with { Text = Cut(segment.Text, budget)! });
                budget = 0;
                break;
            }
            kept.Add(new LineDraft(line.Heading, segments) { Part = line.Part });
        }

        // 4. At most MaxSegments segments, keeping room for at least one segment on every later line.
        var used = 0;
        for (var i = 0; i < kept.Count; i++)
        {
            var allowed = MaxSegments - used - (kept.Count - 1 - i);
            var segments = kept[i].Segments;
            if (segments.Count > allowed)
            {
                var rest = string.Concat(segments.Skip(allowed - 1).Select(s => s.Text));
                kept[i].Segments = [.. segments.Take(allowed - 1), new OverlaySegment(rest)];
                truncated = true;
            }
            used += kept[i].Segments.Count;
        }

        return (new OverlayResult(tableName, result.RollValue, kept.Select(l => new OverlayLine(l.Heading, l.Segments) { Part = l.Part }).ToList())
        {
            Omitted = result.Omitted,
        }, truncated || result.Omitted);
    }

    /// <summary>
    /// <paramref name="text"/> cut so that, with "…" added, it is at most <paramref name="max"/> UTF-16 characters, never
    /// splitting a surrogate pair; null when it already fits.
    /// </summary>
    public static string? Cut(string text, int max)
    {
        if (text.Length <= max) return null;
        var keep = Math.Max(0, max - Ellipsis.Length);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1])) keep--;
        return text[..keep] + Ellipsis;
    }

    private sealed class LineDraft(string heading, List<OverlaySegment> segments)
    {
        public string Heading { get; set; } = heading;
        public List<OverlaySegment> Segments { get; set; } = segments;
        public OverlayPartLabel? Part { get; set; }
    }
}
