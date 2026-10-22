using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>One document the Fenix pins (the ECAM procedures it pins for alerts). <c>MatchIndex</c> and <c>Label</c> are optional.</summary>
public sealed record FenixPinnedDocument(string Path, int? MatchIndex, string? Label);

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
            JToken root = JToken.Parse(value);
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
