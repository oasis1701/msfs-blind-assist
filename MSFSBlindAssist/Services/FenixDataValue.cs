using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What kind of JSON value the Fenix data service returned for a name. An unknown name reads as an
/// empty string, so <see cref="EmptyOrUnknown"/> is "the service has no such value", never 0, false
/// or "off".
/// </summary>
public enum FenixValueKind
{
    EmptyOrUnknown,
    Null,
    Number,
    Bool,
    Text,
    Array,
    Object
}

/// <summary>
/// One value read from the Fenix data service (the local GraphQL gateway on 127.0.0.1:8083). Only
/// the field that matches <see cref="Kind"/> is meaningful; <see cref="RawJson"/> is the value's
/// compact JSON ("" when the service sent no value at all).
/// </summary>
public readonly record struct FenixDataValue(
    FenixValueKind Kind,
    double Number,
    bool Bool,
    string? Text,
    string RawJson)
{
    /// <summary>A name the service did not answer for: missing, failed or not a known data ref.</summary>
    public static readonly FenixDataValue Unknown = new(FenixValueKind.EmptyOrUnknown, 0, false, null, "");

    /// <summary>True when the service holds a value: not the empty string it answers an unknown name with, and not JSON null.</summary>
    public bool IsKnown => Kind is not (FenixValueKind.EmptyOrUnknown or FenixValueKind.Null);

    /// <summary>
    /// Classifies a JSON token from the service. A C# <c>null</c> token (no value in the answer) and
    /// the empty string both give <see cref="FenixValueKind.EmptyOrUnknown"/>; a JSON <c>null</c>
    /// gives <see cref="FenixValueKind.Null"/>.
    /// </summary>
    public static FenixDataValue FromJson(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Undefined)
            return Unknown;

        string raw = token.ToString(Formatting.None);
        switch (token.Type)
        {
            case JTokenType.Null:
                return new FenixDataValue(FenixValueKind.Null, 0, false, null, raw);
            case JTokenType.Integer:
            case JTokenType.Float:
                return new FenixDataValue(FenixValueKind.Number, token.Value<double>(), false, null, raw);
            case JTokenType.Boolean:
                return new FenixDataValue(FenixValueKind.Bool, 0, token.Value<bool>(), null, raw);
            case JTokenType.String:
            {
                string text = token.Value<string>() ?? "";
                return text.Length == 0
                    ? new FenixDataValue(FenixValueKind.EmptyOrUnknown, 0, false, null, raw)
                    : new FenixDataValue(FenixValueKind.Text, 0, false, text, raw);
            }
            case JTokenType.Array:
                return new FenixDataValue(FenixValueKind.Array, 0, false, null, raw);
            case JTokenType.Object:
                return new FenixDataValue(FenixValueKind.Object, 0, false, null, raw);
            default:
                // A string the JSON reader turned into a date, GUID, URI or bytes is still text: keep
                // its JSON form without the quotes (a reader set to DateParseHandling.None never gets here).
                return new FenixDataValue(FenixValueKind.Text, 0, false, raw.Trim('"'), raw);
        }
    }
}
