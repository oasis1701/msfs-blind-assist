// The Fenix v3 manual-failures write path (POST /fenix/failures/saveManual): builds the body exactly as the
// EFB's Apply does, parses the gateway's reply, and validates the pilot's typed condition fields. Pure, no I/O.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

public enum FenixFailureConditionField { None, Ias, AltAbove, AltBelow, Seconds }

public static class FenixFailureRequests
{
    public const string ManualPath = "/fenix/failures/manual";
    public const string SavePath = "/fenix/failures/saveManual";

    private const int MaxIas = 10000;
    private const int MaxAltitude = 50000;
    private const int MaxSeconds = 10000;

    /// <summary>
    /// The GET's own failure object (deep-cloned, unknown fields kept) with only failed/failureCondition changed.
    /// FailNow: failed true, condition untouched. Arm: failed kept, condition written from its non-null fields
    /// (never "time"). Clear: failed false; an object condition is removed (the EFB's undefined), a null stays null.
    /// </summary>
    public static string BuildSaveBody(FenixFailure failure, FenixFailureAction action, FenixFailureCondition? condition = null)
    {
        if (action == FenixFailureAction.Arm && condition == null)
            throw new ArgumentException("Arming needs a condition.", nameof(condition));

        JObject body = (JObject)failure.Raw.DeepClone();
        switch (action)
        {
            case FenixFailureAction.FailNow:
                body["failed"] = true;
                break;
            case FenixFailureAction.Arm:
                var c = new JObject();
                if (condition!.Ias is int ias) c["ias"] = new JValue((long)ias);
                if (condition.AltAbove is int alt) c["alt"] = new JValue((long)alt);
                if (condition.AltBelow is int altb) c["altb"] = new JValue((long)altb);
                if (condition.AfterEvent != null) c["afterEvent"] = condition.AfterEvent;
                if (condition.AfterEventSeconds is int secs) c["afterEventSeconds"] = new JValue((long)secs);
                body["failureCondition"] = c;
                break;
            case FenixFailureAction.Clear:
                body["failed"] = false;
                if (body["failureCondition"] is { Type: not JTokenType.Null and not JTokenType.Undefined })
                    body.Remove("failureCondition");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
        return body.ToString(Formatting.None);
    }

    /// <summary>The gateway's reply to a save: the saved failure object; null when it is anything else.</summary>
    public static FenixFailure? ParseSaved(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // DateParseHandling.None, as the catalog: an ISO "time" string must stay the text the gateway sent.
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return FenixFailureCatalog.ParseFailure(JToken.ReadFrom(reader));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Validates the typed condition fields. Empty fields are absent; at least one value is needed.
    /// afterEvent is null (None), "V1", "Takeoff" or "Landing"; anything else throws.
    /// </summary>
    public static bool TryBuildCondition(string? ias, string? altAbove, string? altBelow, string? afterEvent, string? seconds,
        out FenixFailureCondition? condition, out FenixFailureConditionField errorField, out string? error)
    {
        condition = null;
        errorField = FenixFailureConditionField.None;
        error = null;

        string? ev = string.IsNullOrEmpty(afterEvent) ? null : afterEvent;
        if (ev != null && ev is not ("V1" or "Takeoff" or "Landing"))
            throw new ArgumentException($"Unknown after-event \"{ev}\".", nameof(afterEvent));

        if (!TryField(ias, MaxIas, FenixFailureConditionField.Ias, "Above IAS", out int? iasValue, out errorField, out error)
            || !TryField(altAbove, MaxAltitude, FenixFailureConditionField.AltAbove, "Above altitude AMSL", out int? aboveValue, out errorField, out error)
            || !TryField(altBelow, MaxAltitude, FenixFailureConditionField.AltBelow, "Below altitude AMSL", out int? belowValue, out errorField, out error)
            || !TryField(seconds, MaxSeconds, FenixFailureConditionField.Seconds, "Seconds until failure", out int? secondsValue, out errorField, out error))
            return false;

        var result = new FenixFailureCondition(iasValue, aboveValue, belowValue, ev, secondsValue, null);
        if (result.IsEmpty)
        {
            error = "Enter at least one condition.";
            return false;
        }
        condition = result;
        return true;
    }

    private static bool TryField(string? text, int max, FenixFailureConditionField field, string name,
        out int? value, out FenixFailureConditionField errorField, out string? error)
    {
        value = null;
        errorField = FenixFailureConditionField.None;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (NumberParsing.TryParseTyped(text, out double d) && d == Math.Floor(d) && d >= 0 && d <= max)
        {
            value = (int)d;
            return true;
        }
        errorField = field;
        error = $"{name} must be a whole number from 0 to {max}.";
        return false;
    }
}
