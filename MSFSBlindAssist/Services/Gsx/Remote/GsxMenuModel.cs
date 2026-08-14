using System.Globalization;
using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// GSX's published menu. <c>entries</c>, <c>disabled</c> and <c>stateClass</c> are
/// PARALLEL arrays — GSX can send them ragged, so every accessor is bounds-safe.
///
/// Icons (icons / iconsSvg / iconWide) are base64 blobs with no screen-reader
/// value and are deliberately not parsed.
/// </summary>
public sealed class GsxMenuModel
{
    public string Title { get; private init; } = "";
    public string Header { get; private init; } = "";
    public string Subtitle { get; private init; } = "";
    public IReadOnlyList<string> Entries { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<bool> Disabled { get; private init; } = Array.Empty<bool>();
    public IReadOnlyList<string> StateClass { get; private init; } = Array.Empty<string>();

    public int Count => Entries.Count;

    public static readonly GsxMenuModel Empty = new();

    public static GsxMenuModel Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return Empty;

        var entries = StrList(v, "entries");
        int n = entries.Count;

        // Pad the parallel arrays to the entry count so index access is always safe.
        var disabledRaw = BoolList(v, "disabled");
        var stateRaw = StrList(v, "stateClass");
        var disabled = new bool[n];
        var state = new string[n];
        for (int i = 0; i < n; i++)
        {
            disabled[i] = i < disabledRaw.Count && disabledRaw[i];
            state[i] = i < stateRaw.Count ? stateRaw[i] : "";
        }

        return new GsxMenuModel
        {
            Title = Str(v, "title") ?? "",
            Header = Str(v, "header") ?? "",
            Subtitle = Str(v, "subtitle") ?? "",
            Entries = entries,
            Disabled = disabled,
            StateClass = state,
        };
    }

    /// <summary>
    /// True when <paramref name="entry"/> is GSX's own "no option here" padding —
    /// null, empty, or whitespace-only. GSX's parking-search results are a fixed
    /// 10-slot menu; unused slots come back as empty strings (confirmed live at
    /// EDDF: searching "A15" returned one real match at index 0, "Back" at index
    /// 9, and "" at every slot in between) and GSX does NOT mark them disabled —
    /// <c>disabled</c> stayed [false x10] across the whole padded response — so a
    /// blank slot needs its OWN check, entirely separate from <see cref="Disabled"/>.
    /// </summary>
    public static bool IsBlank(string? entry) => string.IsNullOrWhiteSpace(entry);

    /// <summary>
    /// True when <paramref name="index"/> names a real, pickable option: in range,
    /// not <see cref="Disabled"/>, and not one of GSX's blank padding slots (see
    /// <see cref="IsBlank"/>) — a blank slot is never disabled on the wire, so
    /// without this second check it would read back as selectable.
    /// </summary>
    public bool IsSelectable(int index)
        => index >= 0 && index < Count && !Disabled[index] && !IsBlank(Entries[index]);

    /// <summary>
    /// Re-resolve an index against the CURRENT menu, verifying the label still
    /// matches what was presented to the pilot.
    ///
    /// CRITICAL: a menu navigation can land between the moment an entry is read
    /// out and the moment a key is pressed. GSX's own client re-reads the index
    /// at click time for exactly this reason; trusting a stale index presses an
    /// arbitrary wrong entry. Returns -1 when the label is gone OR when more than
    /// one entry carries the same label (ambiguous) — the caller must then do nothing.
    /// </summary>
    public int ResolveIndex(int paintedIndex, string expectedLabel)
    {
        // A blank expectedLabel can never legitimately identify a real option —
        // refuse it up front. Without this, a blank expectedLabel could match one
        // of GSX's own blank padding slots via the equality checks below and
        // "resolve" to it (see IsBlank).
        if (IsBlank(expectedLabel) || Count == 0) return -1;

        // Fast path: if the painted index still holds the expected label, that is
        // positive evidence — return it even if duplicates exist elsewhere.
        if (paintedIndex >= 0 && paintedIndex < Count &&
            string.Equals(Entries[paintedIndex], expectedLabel, StringComparison.Ordinal))
            return paintedIndex;

        // Fallback scan: find the label in the menu. Return -1 if zero matches
        // (label is gone) or if more than one match exists (ambiguous).
        int matchIndex = -1;
        for (int i = 0; i < Count; i++)
        {
            if (string.Equals(Entries[i], expectedLabel, StringComparison.Ordinal))
            {
                if (matchIndex >= 0) return -1; // More than one match — ambiguous, refuse
                matchIndex = i;
            }
        }

        return matchIndex;
    }

    /// <summary>
    /// Spoken suffix for GSX's service-state cue. The sighted client renders this
    /// as an icon tint, so without it the cue is lost to a screen reader entirely.
    /// </summary>
    public string? StateSuffix(int index)
    {
        if (index < 0 || index >= Count) return null;
        return StateClass[index] switch
        {
            "gsx-state-completed" => "Completed",
            // GSX's real wire value is "performed" -- confirmed live at EDDF
            // (2026-08) on "113/143 passengers boarded" mid-boarding. Keep
            // "-performing" too: it was never once observed across that whole
            // session, but costs nothing and we cannot prove no GSX build emits it.
            "gsx-state-performed" => "In progress",
            "gsx-state-performing" => "In progress",
            // Confirmed live at EDDF on "Deboarding requested" right after
            // requesting deboarding. The entry text already says "requested" --
            // this suffix isn't recovering lost information, just keeping the
            // cue consistent with the other two live-observed states above.
            "gsx-state-requested" => "Requested",
            "gsx-state-unavailable" => "Unavailable",
            _ => null,
        };
    }

    /// <summary>
    /// The literal keyboard key AccessGSXForm binds to entry <paramref name="index"/>
    /// (0-based): <c>1</c>-<c>9</c> for entries 0-8 and <c>0</c> for entry 9 — the
    /// in-sim GSX numpad layout, and every menu observed so far fits in it.
    ///
    /// <c>A</c>-<c>E</c> are deliberately NOT returned for entries 10-14. Those
    /// letters are GSX's own system block (Customize Airport/Airplane, Settings,
    /// Restart GSX, Reload SimBrief — see <see cref="GsxSystemCommands"/>), which
    /// is what they have always done in AccessGSX and what the pilot expects them
    /// to do. Advertising "A" beside menu entry 11 would tell a blind pilot to
    /// press a key that runs something else entirely.
    ///
    /// An entry past 9 therefore has no single-keypress shortcut. It renders its
    /// 1-based position instead, so a screen reader stepping the list still reads
    /// a stable, sensible marker.
    /// </summary>
    public static string Shortcut(int index) => index switch
    {
        >= 0 and <= 8 => (index + 1).ToString(CultureInfo.InvariantCulture),
        9 => "0",
        _ => (index + 1).ToString(CultureInfo.InvariantCulture),
    };

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IReadOnlyList<string> StrList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var i in v.EnumerateArray())
            list.Add(i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : "");
        return list;
    }

    private static IReadOnlyList<bool> BoolList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<bool>();
        var list = new List<bool>();
        foreach (var i in v.EnumerateArray())
            list.Add(i.ValueKind == JsonValueKind.True);
        return list;
    }
}
