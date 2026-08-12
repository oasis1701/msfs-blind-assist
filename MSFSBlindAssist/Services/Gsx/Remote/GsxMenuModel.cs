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

    public bool IsSelectable(int index)
        => index >= 0 && index < Count && !Disabled[index];

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
        if (string.IsNullOrEmpty(expectedLabel) || Count == 0) return -1;

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
            "gsx-state-performing" => "In progress",
            "gsx-state-unavailable" => "Unavailable",
            _ => null,
        };
    }

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
