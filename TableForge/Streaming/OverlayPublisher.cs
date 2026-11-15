using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TableForge.Streaming;

/// <summary>Which published result something belongs to. Only the current one may still change what the overlay shows.</summary>
public readonly record struct OverlayToken(long Generation);

/// <summary>What the overlay shows right now. <see cref="Result"/> is null when it is empty (at start, and after Clear).</summary>
public sealed record OverlayState(string InstanceId, long Version, long Generation, OverlayResult? Result, bool Truncated,
    bool ShowTableName, bool ShowRollValue)
{
    public bool IsEmpty => Result is null;
}

/// <summary>
/// The Streaming Overlay's one piece of state: the current resolved result, held in memory only (never saved, so a new run
/// always starts empty). Rolling, Manual Entry and Show Test Result replace it; Clear empties it; an inline roll may update it
/// only while its result is still the current one (<see cref="UpdateIfCurrent"/>).
/// <para>
/// Every change advances <see cref="OverlayState.Version"/>, and the page re-renders when the pair (<see cref="InstanceId"/>,
/// version) changes, so a new run never collides with an old page's counter. Each new result, Clear and Test also advance the
/// generation, which is what makes older results' tokens stale.
/// </para>
/// Changed on the UI thread; <see cref="StateJson"/> is an immutable snapshot that any thread may read at any time.
/// </summary>
public sealed class OverlayPublisher
{
    /// <summary>The largest <c>/state</c> body ever served. Beyond it an empty overlay is served instead (and traced).</summary>
    public const int DefaultMaxStateBytes = 64 * 1024;

    private readonly object _gate = new();
    private readonly int _maxStateBytes;
    private OverlayState _state;
    private byte[] _json;
    private int _oversized;

    public OverlayPublisher(bool showTableName = true, bool showRollValue = true, string? instanceId = null, int maxStateBytes = DefaultMaxStateBytes)
    {
        _maxStateBytes = maxStateBytes;
        InstanceId = instanceId ?? Guid.NewGuid().ToString("N");
        _state = new OverlayState(InstanceId, 0, 0, null, false, showTableName, showRollValue);
        _json = Serialize(_state);
    }

    /// <summary>Random for each run of TableForge.</summary>
    public string InstanceId { get; }

    public OverlayState State { get { lock (_gate) return _state; } }

    /// <summary>The current state as the UTF-8 JSON <c>/state</c> serves. Never larger than the guard allows.</summary>
    public byte[] StateJson => Volatile.Read(ref _json);

    /// <summary>How many times a state was too large to serve and an empty overlay was served instead.</summary>
    public int OversizedCount => Volatile.Read(ref _oversized);

    /// <summary>Raised (on the caller's thread) after every change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// "Overlay shows" (Gidubba 1.6.0): read by the Roll screen when it publishes a new result. Changing it publishes nothing and
    /// leaves what the overlay shows alone; the next result is shown the new way.
    /// </summary>
    public OverlayDisplayMode DisplayMode { get; private set; } = OverlayDisplayMode.LatestRoll;

    public void SetDisplayMode(OverlayDisplayMode mode) => DisplayMode = mode;

    /// <summary>A newly resolved result: it becomes the current one, whatever was shown before.</summary>
    public OverlayToken Publish(OverlayResult result)
    {
        OverlayToken token;
        lock (_gate)
        {
            token = new OverlayToken(_state.Generation + 1);
            SetLocked(result, token.Generation);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return token;
    }

    /// <summary>
    /// The result <paramref name="token"/> belongs to now reads differently (an inline roll resolved in context). Applied only
    /// while that result is still the current one; false (and nothing changes) once anything newer, Clear or Test has replaced it.
    /// </summary>
    public bool UpdateIfCurrent(OverlayToken token, OverlayResult result)
    {
        lock (_gate)
        {
            if (_state.IsEmpty || token.Generation != _state.Generation) return false;
            SetLocked(result, _state.Generation);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Show Test Result: a transient result that is never a roll (no Recent Rolls, no history, no table).</summary>
    public void ShowTest() => Publish(OverlayResult.Test);

    /// <summary>Clear Overlay: empty, and no earlier result can come back through an inline roll.</summary>
    public void Clear()
    {
        lock (_gate) SetLocked(null, _state.Generation + 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Show Table Name / Show Roll Value. Only how the current result is shown changes; it stays the current result.</summary>
    public void SetPresentation(bool showTableName, bool showRollValue)
    {
        lock (_gate)
        {
            if (_state.ShowTableName == showTableName && _state.ShowRollValue == showRollValue) return;
            Store(_state with { Version = _state.Version + 1, ShowTableName = showTableName, ShowRollValue = showRollValue });
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetLocked(OverlayResult? result, long generation)
    {
        var (limited, truncated) = result is null ? (null, false) : OverlayLimits.Apply(result);   // Omitted parts count as truncated
        Store(_state with { Version = _state.Version + 1, Generation = generation, Result = limited, Truncated = truncated });
    }

    private void Store(OverlayState state)
    {
        var json = Serialize(state);
        if (json.Length > _maxStateBytes)
        {
            Interlocked.Increment(ref _oversized);
            Trace.TraceWarning($"TableForge Streaming Overlay: a result's state was {json.Length} bytes (limit {_maxStateBytes}); an empty overlay is shown instead.");
            state = state with { Result = null, Truncated = false };
            json = Serialize(state);
        }
        _state = state;
        Volatile.Write(ref _json, json);
    }

    /// <summary>The separator the page shows after the first line's roll value (its <c>tf-roll-separator</c>).</summary>
    public const string RollSeparator = " — ";

    /// <summary>
    /// A line as the page is sent it. Every line is unchanged except a Complete Result part's first line (see
    /// <see cref="OverlayLine.Part"/>), labelled as Show Table Name and Show Roll Value say: its table goes in front of its
    /// heading ("Loot · Treasure", "Loot", or the set's own heading), and its roll value starts its text in bold, followed by
    /// " — ", as the page shows the first line's roll value: "Loot: <b>4</b> — A rusted key.", "<b>5</b> — Heavy rain.".
    /// </summary>
    public static OverlayLine Shown(OverlayLine line, bool showTableName, bool showRollValue)
    {
        if (line.Part is not { } part) return line;
        var heading = showTableName && part.TableName.Length > 0
            ? (line.Heading.Length > 0 ? $"{part.TableName} · {line.Heading}" : part.TableName)
            : line.Heading;
        IReadOnlyList<OverlaySegment> segments = showRollValue && part.RollValue.Length > 0
            ? [new OverlaySegment(part.RollValue, Bold: true), new OverlaySegment(RollSeparator), .. line.Segments]
            : line.Segments;
        return new OverlayLine(heading, segments);
    }

    /// <summary>
    /// The <c>/state</c> JSON. Text is only ever data: System.Text.Json's default encoder escapes &lt;, &gt;, &amp;, quotes and
    /// non-ASCII, and the page puts every value in with textContent.
    /// </summary>
    public static byte[] Serialize(OverlayState state)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("instance", state.InstanceId);
            w.WriteNumber("version", state.Version);
            w.WriteBoolean("empty", state.IsEmpty);
            w.WriteBoolean("showTableName", state.ShowTableName);
            w.WriteBoolean("showRollValue", state.ShowRollValue);
            if (state.Result is { } result)
            {
                w.WriteBoolean("truncated", state.Truncated);
                w.WriteString("tableName", result.TableName);
                w.WriteString("rollValue", result.RollValue);
                w.WriteStartArray("lines");
                foreach (var line in result.Lines.Select(l => Shown(l, state.ShowTableName, state.ShowRollValue)))
                {
                    w.WriteStartObject();
                    w.WriteString("heading", line.Heading);
                    w.WriteStartArray("segments");
                    foreach (var s in line.Segments)
                    {
                        w.WriteStartObject();
                        w.WriteString("text", s.Text);
                        if (s.Bold) w.WriteBoolean("bold", true);
                        if (s.Italic) w.WriteBoolean("italic", true);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return stream.ToArray();
    }
}
