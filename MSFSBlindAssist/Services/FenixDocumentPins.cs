using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>One document the Fenix pins (the ECAM procedures it pins for alerts). <c>MatchIndex</c> and <c>Label</c> are optional.</summary>
public sealed record FenixPinnedDocument(string Path, int? MatchIndex, string? Label);

public enum FenixPinReadBack { Confirmed, Opposite, NoAnswer }

/// <summary>The Fenix <c>pinned_documents</c> setting: reading it as the EFB's own parser does.</summary>
public static class FenixDocumentPins
{
    public const string SettingKey = "pinned_documents";

    /// <summary>
    /// Reads a JSON array of pins. Anything else, or bad JSON, is an empty list. An entry without a string path is dropped,
    /// MatchIndex is kept only from a finite integer, the label is trimmed (empty is null). Duplicates (path, MatchIndex,
    /// label) keep the first; order is kept. Never throws.
    /// </summary>
    public static IReadOnlyList<FenixPinnedDocument> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<FenixPinnedDocument>();
        try
        {
            // DateParseHandling.None: a date-shaped label stays a string (Ctrl+D rewrites the whole setting).
            JToken? root = JsonConvert.DeserializeObject<JToken>(value, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
            if (root is not JArray array) return Array.Empty<FenixPinnedDocument>();

            var pins = new List<FenixPinnedDocument>();
            var seen = new HashSet<FenixPinnedDocument>();
            foreach (var token in array)
            {
                if (token is not JObject obj || obj["path"] is not { Type: JTokenType.String } pathToken) continue;
                string path = (string)pathToken!;

                int? matchIndex = null;
                JToken? m = obj["matchIndex"];
                if (m is { Type: JTokenType.Integer })
                {
                    try { matchIndex = checked((int)m); }
                    catch (OverflowException) { matchIndex = null; }
                }
                else if (m is { Type: JTokenType.Float })
                {
                    double d = (double)m;
                    if (double.IsFinite(d) && d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue) matchIndex = (int)d;
                }

                string? label = obj["label"] is { Type: JTokenType.String } l ? ((string)l!).Trim() : null;
                if (string.IsNullOrEmpty(label)) label = null;

                var pin = new FenixPinnedDocument(path, matchIndex, label);
                if (seen.Add(pin)) pins.Add(pin);
            }
            return pins;
        }
        catch (Exception)
        {
            return Array.Empty<FenixPinnedDocument>();
        }
    }

    /// <summary>The pins in a settings answer; null when there is no <c>pinned_documents</c> row (no answer).</summary>
    public static IReadOnlyList<FenixPinnedDocument>? FromSettings(IReadOnlyList<FenixSetting> settings)
    {
        foreach (var s in settings)
        {
            if (s.Key == SettingKey) return Parse(s.Value);
        }
        return null;
    }

    /// <summary>The Fenix EFB's own write of the setting; the one write the documents window makes.
    /// The same mutation the options window sends, so <see cref="FenixOptionEdits.SetSettingDocument"/> is its one text.</summary>
    public const string SetSettingDocument = FenixOptionEdits.SetSettingDocument;

    public const string NotAccepted = "The Fenix did not accept the change.";

    /// <summary>Every entry with the path (ordinal) is removed; with none, a new pin goes first (the EFB prepends).</summary>
    public static IReadOnlyList<FenixPinnedDocument> Toggle(IReadOnlyList<FenixPinnedDocument> pins, string path)
    {
        if (pins.Any(p => string.Equals(p.Path, path, StringComparison.Ordinal)))
            return pins.Where(p => !string.Equals(p.Path, path, StringComparison.Ordinal)).ToList();
        var list = new List<FenixPinnedDocument>(pins.Count + 1) { new(path, null, null) };
        list.AddRange(pins);
        return list;
    }

    /// <summary>The EFB's JSON.stringify of the list: path, then matchIndex when set, then label when set; no spaces.</summary>
    public static string Serialize(IReadOnlyList<FenixPinnedDocument> pins)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < pins.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"path\":").Append(JsonConvert.ToString(pins[i].Path));
            if (pins[i].MatchIndex is int m) sb.Append(",\"matchIndex\":").Append(m.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (pins[i].Label is string l) sb.Append(",\"label\":").Append(JsonConvert.ToString(l));
            sb.Append('}');
        }
        return sb.Append(']').ToString();
    }

    public static object Variables(string serializedPins)
        => new { setSetting = new { key = SettingKey, value = serializedPins, persist = true } };

    public static bool Succeeded(JObject? data)
        => data?["settings"]?["setSetting"]?["success"] is { Type: JTokenType.Boolean } s && (bool)s;

    public static string ReadBack(string path, bool pinned)
        => (pinned ? "Pinned: " : "Unpinned: ") + FenixDocumentNames.DisplayName(path) + ".";

    /// <summary>
    /// Re-reads the setting until the Fenix holds the wanted state of the path: its settings query lags the write by
    /// up to about 2 s (<see cref="FenixSettingReadBack"/>, which the options window waits with too). Waits
    /// <paramref name="interval"/> between reads, at most <paramref name="attempts"/> reads, and goes on through a
    /// read that got no answer. Opposite when at least one read answered but never showed the wanted state;
    /// NoAnswer when none answered.
    /// </summary>
    public static async Task<FenixPinReadBack> AwaitStateAsync(
        Func<CancellationToken, Task<IReadOnlyList<FenixSetting>>> readSettings, string path, bool wantPinned,
        TimeSpan interval, int attempts, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        var result = await FenixSettingReadBack.AwaitAsync<IReadOnlyList<FenixPinnedDocument>>(
            async t => FromSettings(await readSettings(t)),
            pins => pins.Any(p => string.Equals(p.Path, path, StringComparison.Ordinal)) == wantPinned,
            interval, attempts, delay, ct, stopOnNoAnswer: false);
        return result.Landed ? FenixPinReadBack.Confirmed : result.Answered ? FenixPinReadBack.Opposite : FenixPinReadBack.NoAnswer;
    }

    /// <summary>The pins whose path is in the document list, in order; the EFB drops the others from its Pinned section.</summary>
    public static IReadOnlyList<FenixPinnedDocument> Listed(IReadOnlyList<FenixPinnedDocument> pins, IReadOnlyDictionary<string, FenixDocumentItem> byPath)
    {
        var listed = new List<FenixPinnedDocument>();
        foreach (var pin in pins)
        {
            if (byPath.ContainsKey(pin.Path.Replace('\\', '/'))) listed.Add(pin);
        }
        Log.Debug("FenixDocuments", $"pins: {pins.Count}, listed: {listed.Count}");
        return listed;
    }
}
