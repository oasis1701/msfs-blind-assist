// Characterization tests for the pure layer of the Fenix data-service client: FenixDataValue (one
// value as the service returns it), FenixDataQueries (the exact GraphQL request texts and the
// parsing of their answers) and FenixSetting (one row of the settings API). The network client
// builds on these, so the shapes pinned here are the wire contract of the Fenix v3 data service on
// 127.0.0.1:8083. The service answers an unknown name with an empty string, never 0 or false, so a
// name that is missing or failed must come back as Unknown, not as a number or a bool.

using MSFSBlindAssist.Services;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixDataQueriesTests
{
    // ---- FenixDataValue ------------------------------------------------------------------------

    [Fact]
    public void FromJson_reads_each_kind()
    {
        var empty = FenixDataValue.FromJson(JToken.Parse("\"\""));
        Assert.Equal(FenixValueKind.EmptyOrUnknown, empty.Kind);

        var number = FenixDataValue.FromJson(JToken.Parse("62.3"));
        Assert.Equal(FenixValueKind.Number, number.Kind);
        Assert.Equal(62.3, number.Number);

        var flag = FenixDataValue.FromJson(JToken.Parse("true"));
        Assert.Equal(FenixValueKind.Bool, flag.Kind);
        Assert.True(flag.Bool);

        var text = FenixDataValue.FromJson(JToken.Parse("\"A321\""));
        Assert.Equal(FenixValueKind.Text, text.Kind);
        Assert.Equal("A321", text.Text);

        var array = FenixDataValue.FromJson(JToken.Parse("[false, true]"));
        Assert.Equal(FenixValueKind.Array, array.Kind);
        Assert.Equal("[false,true]", array.RawJson);

        var jsonNull = FenixDataValue.FromJson(JToken.Parse("null"));
        Assert.Equal(FenixValueKind.Null, jsonNull.Kind);

        var noToken = FenixDataValue.FromJson(null);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, noToken.Kind);
    }

    [Fact]
    public void FromJson_reads_an_integer_as_a_number()
    {
        var value = FenixDataValue.FromJson(JToken.Parse("276"));

        Assert.Equal(FenixValueKind.Number, value.Kind);
        Assert.Equal(276d, value.Number);
        Assert.Equal("276", value.RawJson);
    }

    [Fact]
    public void FromJson_reads_an_object_and_keeps_its_compact_json()
    {
        var value = FenixDataValue.FromJson(JToken.Parse("{ \"a\": 1, \"b\": [ 2 ] }"));

        Assert.Equal(FenixValueKind.Object, value.Kind);
        Assert.Equal("{\"a\":1,\"b\":[2]}", value.RawJson);
    }

    [Fact]
    public void FromJson_keeps_a_string_the_json_reader_turned_into_a_date_as_text()
    {
        // JObject.Parse reads "2026-10-21T10:00:00Z" as a date unless told not to; it is still text.
        var token = JObject.Parse("{\"v\":\"2026-10-21T10:00:00Z\"}")["v"];
        Assert.Equal(JTokenType.Date, token!.Type);

        var value = FenixDataValue.FromJson(token);

        Assert.Equal(FenixValueKind.Text, value.Kind);
        Assert.Contains("2026-10-21", value.Text);
        Assert.True(value.IsKnown);
    }

    [Fact]
    public void FromJson_keeps_the_raw_json_of_text_and_the_empty_string_for_no_token()
    {
        Assert.Equal("\"A321\"", FenixDataValue.FromJson(JToken.Parse("\"A321\"")).RawJson);
        Assert.Equal("\"\"", FenixDataValue.FromJson(JToken.Parse("\"\"")).RawJson);
        Assert.Equal("", FenixDataValue.FromJson(null).RawJson);
    }

    [Fact]
    public void Only_a_value_that_is_neither_empty_nor_null_is_known()
    {
        Assert.False(FenixDataValue.Unknown.IsKnown);
        Assert.False(FenixDataValue.FromJson(JToken.Parse("\"\"")).IsKnown);
        Assert.False(FenixDataValue.FromJson(JToken.Parse("null")).IsKnown);
        Assert.False(FenixDataValue.FromJson(null).IsKnown);

        Assert.True(FenixDataValue.FromJson(JToken.Parse("0")).IsKnown);
        Assert.True(FenixDataValue.FromJson(JToken.Parse("false")).IsKnown);
        Assert.True(FenixDataValue.FromJson(JToken.Parse("\"off\"")).IsKnown);
        Assert.True(FenixDataValue.FromJson(JToken.Parse("[]")).IsKnown);
        Assert.True(FenixDataValue.FromJson(JToken.Parse("{}")).IsKnown);
    }

    [Fact]
    public void Unknown_is_an_empty_value_with_no_text_and_no_json()
    {
        var unknown = FenixDataValue.Unknown;

        Assert.Equal(FenixValueKind.EmptyOrUnknown, unknown.Kind);
        Assert.Null(unknown.Text);
        Assert.Equal("", unknown.RawJson);
    }

    [Fact]
    public void FromJson_is_the_same_under_a_comma_decimal_culture()
    {
        var value = TestCulture.Run("de-DE", () => FenixDataValue.FromJson(JToken.Parse("62.3")));

        Assert.Equal(FenixValueKind.Number, value.Kind);
        Assert.Equal(62.3, value.Number);
        Assert.Equal("62.3", value.RawJson);
    }

    // ---- BuildReadMany -------------------------------------------------------------------------

    [Fact]
    public void BuildReadMany_numbers_the_aliases_in_order_in_the_service_query_shape()
    {
        var query = FenixDataQueries.BuildReadMany(new[] { "system.numerical.N_ELEC_VOLT_BAT_1", "bogus" });

        Assert.Equal(
            "query { dataRef { r0: dataRef(name: \"system.numerical.N_ELEC_VOLT_BAT_1\") { value } " +
            "r1: dataRef(name: \"bogus\") { value } } }",
            query);
    }

    [Fact]
    public void BuildReadMany_escapes_quotes_and_backslashes()
    {
        var query = FenixDataQueries.BuildReadMany(new[] { "a\"b", "c\\d" });

        Assert.Contains("r0: dataRef(name: \"a\\\"b\")", query);
        Assert.Contains("r1: dataRef(name: \"c\\\\d\")", query);
    }

    [Fact]
    public void BuildReadMany_of_no_names_is_refused_because_an_empty_selection_is_invalid_graphql()
    {
        Assert.Throws<ArgumentException>(() => FenixDataQueries.BuildReadMany(Array.Empty<string>()));
    }

    // ---- ParseReadMany -------------------------------------------------------------------------

    [Fact]
    public void ParseReadMany_maps_aliases_back_to_names()
    {
        var data = JObject.Parse("{\"dataRef\":{\"r0\":{\"value\":276},\"r1\":{\"value\":\"\"}}}");
        var names = new[] { "system.numerical.N_ELEC_VOLT_BAT_1", "bogus" };

        var values = FenixDataQueries.ParseReadMany(data, names);

        Assert.Equal(2, values.Count);
        Assert.Equal(FenixValueKind.Number, values["system.numerical.N_ELEC_VOLT_BAT_1"].Kind);
        Assert.Equal(276d, values["system.numerical.N_ELEC_VOLT_BAT_1"].Number);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, values["bogus"].Kind);
    }

    [Fact]
    public void ParseReadMany_gives_a_name_beyond_the_answer_the_unknown_value()
    {
        var data = JObject.Parse("{\"dataRef\":{\"r0\":{\"value\":276}}}");

        var values = FenixDataQueries.ParseReadMany(data, new[] { "a", "b", "c" });

        Assert.Equal(3, values.Count);
        Assert.Equal(276d, values["a"].Number);
        Assert.Equal(FenixDataValue.Unknown, values["b"]);
        Assert.Equal(FenixDataValue.Unknown, values["c"]);
    }

    [Fact]
    public void ParseReadMany_gives_a_failed_entry_the_unknown_value()
    {
        // A name the service failed on arrives as a null entry (the error itself is in "errors").
        var data = JObject.Parse("{\"dataRef\":{\"r0\":null,\"r1\":{\"value\":true}}}");

        var values = FenixDataQueries.ParseReadMany(data, new[] { "failed", "ok" });

        Assert.Equal(FenixDataValue.Unknown, values["failed"]);
        Assert.Equal(FenixValueKind.Bool, values["ok"].Kind);
        Assert.True(values["ok"].Bool);
    }

    [Fact]
    public void ParseReadMany_of_no_data_or_a_malformed_answer_gives_every_name_the_unknown_value()
    {
        var names = new[] { "a", "b" };

        foreach (var data in new JObject?[]
                 {
                     null,
                     new JObject(),
                     JObject.Parse("{\"dataRef\":null}"),
                     JObject.Parse("{\"dataRef\":\"oops\"}"),
                     JObject.Parse("{\"dataRef\":{\"r0\":5,\"r1\":[]}}"),
                 })
        {
            var values = FenixDataQueries.ParseReadMany(data, names);

            Assert.Equal(2, values.Count);
            Assert.Equal(FenixDataValue.Unknown, values["a"]);
            Assert.Equal(FenixDataValue.Unknown, values["b"]);
        }
    }

    [Fact]
    public void ParseReadMany_keeps_a_json_null_value_as_null_not_as_unknown()
    {
        var data = JObject.Parse("{\"dataRef\":{\"r0\":{\"value\":null}}}");

        var values = FenixDataQueries.ParseReadMany(data, new[] { "a" });

        Assert.Equal(FenixValueKind.Null, values["a"].Kind);
    }

    // ---- BuildWriteBody ------------------------------------------------------------------------

    [Fact]
    public void BuildWriteBody_float_is_invariant_under_de_DE()
    {
        var body = TestCulture.Run("de-DE", () => FenixDataQueries.BuildWriteBody("Float", "x", 1.5));

        Assert.Contains("\"value\":1.5", body);
        Assert.DoesNotContain("1,5", body);
    }

    [Theory]
    [InlineData("Int", "Int!", "writeInt")]
    [InlineData("Bool", "Boolean!", "writeBool")]
    [InlineData("Float", "Float!", "writeFloat")]
    [InlineData("String", "String!", "writeString")]
    public void BuildWriteBody_uses_the_mutation_shape_of_each_kind(string kind, string graphQlType, string field)
    {
        var body = JObject.Parse(FenixDataQueries.BuildWriteBody(kind, "some.name", 1));

        Assert.Equal(
            "mutation ($name: String!, $value: " + graphQlType + ") { dataRef { " + field +
            "(name: $name, value: $value) } }",
            (string?)body["query"]);
        Assert.Equal("some.name", (string?)body["variables"]?["name"]);
    }

    [Fact]
    public void BuildWriteBody_sends_the_value_as_its_own_json_type()
    {
        var asInt = JObject.Parse(FenixDataQueries.BuildWriteBody("Int", "n", 3));
        var asBool = JObject.Parse(FenixDataQueries.BuildWriteBody("Bool", "n", true));
        var asString = JObject.Parse(FenixDataQueries.BuildWriteBody("String", "n", "A321"));

        Assert.Equal(JTokenType.Integer, asInt["variables"]!["value"]!.Type);
        Assert.Equal(3, (int)asInt["variables"]!["value"]!);
        Assert.Equal(JTokenType.Boolean, asBool["variables"]!["value"]!.Type);
        Assert.True((bool)asBool["variables"]!["value"]!);
        Assert.Equal(JTokenType.String, asString["variables"]!["value"]!.Type);
        Assert.Equal("A321", (string?)asString["variables"]!["value"]);
    }

    [Fact]
    public void BuildWriteBody_escapes_a_name_with_quotes_and_backslashes_in_the_variables()
    {
        var body = JObject.Parse(FenixDataQueries.BuildWriteBody("Int", "a\"b\\c", 1));

        Assert.Equal("a\"b\\c", (string?)body["variables"]?["name"]);
    }

    [Fact]
    public void BuildWriteBody_refuses_a_kind_it_does_not_know()
    {
        Assert.Throws<ArgumentException>(() => FenixDataQueries.BuildWriteBody("Double", "x", 1.5));
        Assert.Throws<ArgumentException>(() => FenixDataQueries.BuildWriteBody("int", "x", 1));
        Assert.Throws<ArgumentException>(() => FenixDataQueries.BuildWriteBody("", "x", 1));
    }

    // ---- Settings and the subscription ---------------------------------------------------------

    [Fact]
    public void SettingsQuery_and_SubscriptionQuery_are_the_service_texts()
    {
        Assert.Equal(
            "query { settings { softwareVersion state { settings { key category value possibleValues source type writable } } } }",
            FenixDataQueries.SettingsQuery);
        Assert.Equal(
            "subscription ($names: [String!]!) { dataRefs(names: $names) { name value } }",
            FenixDataQueries.SubscriptionQuery);
    }

    [Fact]
    public void ParseSettings_reads_a_row()
    {
        var data = JObject.Parse(
            "{\"settings\":{\"state\":{\"settings\":[" +
            "{\"key\":\"weight_unit\",\"category\":\"Aircraft\",\"value\":\"Pounds\"," +
            "\"possibleValues\":[\"Kilograms\",\"Pounds\"],\"source\":\"Livery\",\"type\":\"Enum\",\"writable\":true}" +
            "]}}}");

        var rows = FenixDataQueries.ParseSettings(data);

        var row = Assert.Single(rows);
        Assert.Equal("weight_unit", row.Key);
        Assert.Equal("Aircraft", row.Category);
        Assert.Equal("Pounds", row.Value);
        Assert.Equal(new[] { "Kilograms", "Pounds" }, row.PossibleValues);
        Assert.Equal("Livery", row.Source);
        Assert.Equal("Enum", row.Type);
        Assert.True(row.Writable);
    }

    [Fact]
    public void ParseSettings_keeps_the_row_order_and_tolerates_missing_and_null_fields()
    {
        var data = JObject.Parse(
            "{\"settings\":{\"state\":{\"settings\":[" +
            "{\"key\":\"first\",\"category\":\"A\",\"value\":null,\"possibleValues\":null,\"source\":null,\"type\":null,\"writable\":false}," +
            "{\"key\":\"second\"}" +
            "]}}}");

        var rows = FenixDataQueries.ParseSettings(data);

        Assert.Equal(new[] { "first", "second" }, rows.Select(r => r.Key));
        Assert.Null(rows[0].Value);
        Assert.Empty(rows[0].PossibleValues);
        Assert.Null(rows[0].Source);
        Assert.Null(rows[0].Type);
        Assert.False(rows[0].Writable);
        Assert.Equal("", rows[1].Category);
        Assert.Null(rows[1].Value);
        Assert.Empty(rows[1].PossibleValues);
        Assert.False(rows[1].Writable);
    }

    [Fact]
    public void ParseSettings_skips_a_row_with_no_key()
    {
        var data = JObject.Parse(
            "{\"settings\":{\"state\":{\"settings\":[" +
            "{\"category\":\"A\",\"value\":\"x\"}," +
            "{\"key\":\"kept\"}," +
            "5" +
            "]}}}");

        var rows = FenixDataQueries.ParseSettings(data);

        Assert.Equal(new[] { "kept" }, rows.Select(r => r.Key));
    }

    [Fact]
    public void ParseSettings_of_no_data_or_a_malformed_answer_is_empty()
    {
        foreach (var data in new JObject?[]
                 {
                     null,
                     new JObject(),
                     JObject.Parse("{\"settings\":null}"),
                     JObject.Parse("{\"settings\":{\"state\":null}}"),
                     JObject.Parse("{\"settings\":{\"state\":{\"settings\":\"oops\"}}}"),
                 })
        {
            Assert.Empty(FenixDataQueries.ParseSettings(data));
        }
    }

    [Fact]
    public void ParseSoftwareVersion_reads_the_version()
    {
        var data = JObject.Parse("{\"settings\":{\"softwareVersion\":\"3.0.0.7426\"}}");

        Assert.Equal("3.0.0.7426", FenixDataQueries.ParseSoftwareVersion(data));
    }

    [Fact]
    public void ParseSoftwareVersion_of_no_data_or_a_malformed_answer_is_null()
    {
        foreach (var data in new JObject?[]
                 {
                     null,
                     new JObject(),
                     JObject.Parse("{\"settings\":null}"),
                     JObject.Parse("{\"settings\":{\"softwareVersion\":null}}"),
                     JObject.Parse("{\"settings\":{\"softwareVersion\":\"\"}}"),
                     JObject.Parse("{\"settings\":{\"softwareVersion\":7}}"),
                 })
        {
            Assert.Null(FenixDataQueries.ParseSoftwareVersion(data));
        }
    }
}
