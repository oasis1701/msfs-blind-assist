using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One row of the Fenix data service's settings API: a key the Fenix exposes (a weight unit, a
/// volume, ...), the values it allows and whether it can be written.
/// </summary>
public sealed record FenixSetting(
    string Key,
    string Category,
    string? Value,
    IReadOnlyList<string> PossibleValues,
    string? Type,
    string? Source,
    bool Writable);

/// <summary>
/// The exact GraphQL request texts for the Fenix data service (a local gateway on 127.0.0.1:8083)
/// and the parsing of its answers. Pure text in, pure text out: no network, no state. The parse
/// methods take the answer's <c>data</c> object (already unwrapped from <c>{"data": ...}</c>), and
/// null or malformed input gives Unknown / empty / null, never an exception.
/// </summary>
/// <remarks>
/// The service never runs schema introspection or <c>navigraph { ... }</c> queries from this app.
/// An unknown name reads as the empty string, which <see cref="FenixDataValue.FromJson"/> keeps apart
/// from 0, false and "off".
/// </remarks>
public static class FenixDataQueries
{
    /// <summary>Software version and every setting row, in one query.</summary>
    public const string SettingsQuery =
        "query { settings { softwareVersion state { settings { key category value possibleValues source type writable } } } }";

    /// <summary>Subscription for the changes of the named data refs; the names go in the <c>$names</c> variable.</summary>
    public const string SubscriptionQuery =
        "subscription ($names: [String!]!) { dataRefs(names: $names) { name value } }";

    /// <summary>
    /// One query reading many names: <c>query { dataRef { r0: dataRef(name: "...") { value } r1: ... } }</c>,
    /// the aliases <c>r0</c>, <c>r1</c>, ... in the order of <paramref name="names"/>. A name is
    /// escaped by JSON string rules, so a quote or backslash cannot break the query.
    /// </summary>
    /// <exception cref="ArgumentException">There are no names: an empty selection is invalid GraphQL.</exception>
    public static string BuildReadMany(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0)
            throw new ArgumentException("A read query needs at least one name.", nameof(names));

        var query = new StringBuilder("query { dataRef {");
        for (int i = 0; i < names.Count; i++)
        {
            query.Append(' ').Append(Alias(i))
                 .Append(": dataRef(name: ").Append(Quote(names[i])).Append(") { value }");
        }
        query.Append(" } }");
        return query.ToString();
    }

    /// <summary>
    /// Maps the answer of <see cref="BuildReadMany"/> back to the names. A name whose entry is
    /// missing, null or malformed (a failed read) maps to <see cref="FenixDataValue.Unknown"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, FenixDataValue> ParseReadMany(JObject? data, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var dataRef = data?["dataRef"] as JObject;
        var values = new Dictionary<string, FenixDataValue>(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            values[names[i]] = dataRef?[Alias(i)] is JObject entry
                ? FenixDataValue.FromJson(entry["value"])
                : FenixDataValue.Unknown;
        }
        return values;
    }

    /// <summary>
    /// The full JSON request body (query and variables) for writing one value. <paramref name="kind"/>
    /// is <c>"Int"</c>, <c>"Bool"</c>, <c>"Float"</c> or <c>"String"</c>; <paramref name="value"/> goes
    /// in the <c>$value</c> variable as its own JSON type, so a number is written with an invariant
    /// decimal point under any PC culture.
    /// </summary>
    /// <exception cref="ArgumentException">The kind is not one of the four (a programmer error).</exception>
    public static string BuildWriteBody(string kind, string name, object value)
    {
        string graphQlType = kind switch
        {
            "Int" => "Int!",
            "Bool" => "Boolean!",
            "Float" => "Float!",
            "String" => "String!",
            _ => throw new ArgumentException($"Unknown write kind \"{kind}\"; use Int, Bool, Float or String.", nameof(kind))
        };

        string query =
            $"mutation ($name: String!, $value: {graphQlType}) {{ dataRef {{ write{kind}(name: $name, value: $value) }} }}";
        return JsonConvert.SerializeObject(
            new { query, variables = new { name, value } },
            Formatting.None);
    }

    /// <summary>Every setting row of the <see cref="SettingsQuery"/> answer; a row with no key is skipped.</summary>
    public static IReadOnlyList<FenixSetting> ParseSettings(JObject? data)
    {
        var rows = new List<FenixSetting>();
        var state = (data?["settings"] as JObject)?["state"] as JObject;
        if (state?["settings"] is not JArray items)
            return rows;

        foreach (var item in items)
        {
            if (item is not JObject row)
                continue;
            string? key = TokenText(row["key"]);
            if (string.IsNullOrEmpty(key))
                continue;

            var possibleValues = new List<string>();
            if (row["possibleValues"] is JArray possible)
            {
                foreach (var option in possible)
                {
                    string? text = TokenText(option);
                    if (text != null)
                        possibleValues.Add(text);
                }
            }

            rows.Add(new FenixSetting(
                key,
                TokenText(row["category"]) ?? "",
                TokenText(row["value"]),
                possibleValues,
                TokenText(row["type"]),
                TokenText(row["source"]),
                row["writable"] is { Type: JTokenType.Boolean } writable && (bool)writable));
        }
        return rows;
    }

    /// <summary>The Fenix software version from the <see cref="SettingsQuery"/> answer, or null when it is not there.</summary>
    public static string? ParseSoftwareVersion(JObject? data)
    {
        var version = (data?["settings"] as JObject)?["softwareVersion"];
        return version is { Type: JTokenType.String } && !string.IsNullOrWhiteSpace((string?)version)
            ? (string?)version
            : null;
    }

    private static string Alias(int index) => "r" + index.ToString(CultureInfo.InvariantCulture);

    // A GraphQL string literal follows the JSON string rules, so one escaping helper serves both.
    private static string Quote(string text) => JsonConvert.ToString(text);

    private static string? TokenText(JToken? token) => token?.Type switch
    {
        null or JTokenType.Null or JTokenType.Undefined => null,
        JTokenType.String => (string?)token,
        JTokenType.Boolean => (bool)token! ? "true" : "false",
        _ => token!.ToString(Formatting.None)
    };
}
