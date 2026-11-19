// Characterization tests for FenixFailureRequests: the save body, the reply and the condition entry.
// Run under de-DE: CI's en-US would hide a culture leak.

using System.Globalization;
using MSFSBlindAssist.Services;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixFailureRequestsTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FenixFailureRequestsTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static FenixFailure Fan()
        => FenixFailureCatalog.Parse(FenixFailureCatalogTests.Load())!.Chapters[0].Groups[0].Failures[0];

    private static FenixFailure FromRaw(string json) => FenixFailureRequests.ParseSaved(json)!;

    private static FenixFailureCondition Cond(int? ias = null, int? above = null, int? below = null,
        string? ev = null, int? secs = null)
        => new(ias, above, below, ev, secs, null);

    [Fact]
    public void Paths_are_the_EFBs()
    {
        Assert.Equal("/fenix/failures/manual", FenixFailureRequests.ManualPath);
        Assert.Equal("/fenix/failures/saveManual", FenixFailureRequests.SavePath);
    }

    [Fact]
    public void Fail_now_on_an_unarmed_failure_is_the_EFB_body()
    {
        string body = FenixFailureRequests.BuildSaveBody(Fan(), FenixFailureAction.FailNow);
        Assert.True(JToken.DeepEquals(JObject.Parse(body), JObject.Parse(
            "{\"id\":\"F_PNEUMATIC_LAV_GAL_FAN\",\"title\":\"Lavatory/Galley fan\",\"failureCondition\":null,\"failed\":true}")));
    }

    [Fact]
    public void Arm_sends_integers()
    {
        string body = FenixFailureRequests.BuildSaveBody(Fan(), FenixFailureAction.Arm, Cond(250, 10000, null, "Takeoff", 30));
        var obj = JObject.Parse(body);
        Assert.True(JToken.DeepEquals(obj["failureCondition"],
            JObject.Parse("{\"ias\":250,\"alt\":10000,\"afterEvent\":\"Takeoff\",\"afterEventSeconds\":30}")));
        Assert.False(obj["failed"]!.Value<bool>());
        Assert.Contains("\"alt\":10000", body);
        Assert.DoesNotContain("10000.0", body);
        Assert.DoesNotContain("10.000", body);
    }

    [Fact]
    public void Arm_writes_below_altitude_as_altb()
    {
        var obj = JObject.Parse(FenixFailureRequests.BuildSaveBody(Fan(), FenixFailureAction.Arm, Cond(below: 5000)));
        Assert.True(JToken.DeepEquals(obj["failureCondition"], JObject.Parse("{\"altb\":5000}")));
    }

    [Fact]
    public void Arm_never_writes_time()
    {
        var f = FromRaw("{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":{\"time\":\"x\"},\"failed\":false}");
        var obj = JObject.Parse(FenixFailureRequests.BuildSaveBody(f, FenixFailureAction.Arm, Cond(ias: 250)));
        Assert.Null(obj["failureCondition"]!["time"]);
        Assert.Equal(250, obj["failureCondition"]!["ias"]!.Value<int>());
    }

    [Fact]
    public void Fail_now_keeps_the_condition()
    {
        var f = FromRaw("{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":{\"ias\":250,\"time\":\"x\"},\"failed\":false}");
        var obj = JObject.Parse(FenixFailureRequests.BuildSaveBody(f, FenixFailureAction.FailNow));
        Assert.True(JToken.DeepEquals(obj["failureCondition"], JObject.Parse("{\"ias\":250,\"time\":\"x\"}")));
        Assert.True(obj["failed"]!.Value<bool>());
    }

    [Fact]
    public void Clear_removes_an_armed_condition()
    {
        var f = FromRaw("{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":{\"ias\":250},\"failed\":true}");
        var obj = JObject.Parse(FenixFailureRequests.BuildSaveBody(f, FenixFailureAction.Clear));
        Assert.False(obj.ContainsKey("failureCondition"));
        Assert.False(obj["failed"]!.Value<bool>());
    }

    [Fact]
    public void Clear_keeps_null_on_an_unarmed_failure()
    {
        var f = FromRaw("{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":null,\"failed\":true}");
        var obj = JObject.Parse(FenixFailureRequests.BuildSaveBody(f, FenixFailureAction.Clear));
        Assert.True(obj.ContainsKey("failureCondition"));
        Assert.Equal(JTokenType.Null, obj["failureCondition"]!.Type);
        Assert.False(obj["failed"]!.Value<bool>());
    }

    [Fact]
    public void Unknown_fields_survive_and_raw_is_not_mutated()
    {
        var f = FromRaw("{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":null,\"failed\":false,\"foo\":1}");
        var obj = JObject.Parse(FenixFailureRequests.BuildSaveBody(f, FenixFailureAction.FailNow));
        Assert.Equal(1, obj["foo"]!.Value<int>());
        Assert.False(f.Raw["failed"]!.Value<bool>());
    }

    [Fact]
    public void Arm_needs_a_condition()
        => Assert.Throws<ArgumentException>(() => FenixFailureRequests.BuildSaveBody(Fan(), FenixFailureAction.Arm));

    [Fact]
    public void ParseSaved_reads_the_reply()
    {
        var f = FenixFailureRequests.ParseSaved("{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":{\"ias\":250},\"failed\":false}")!;
        Assert.Equal(FenixFailureState.Armed, f.State);
        Assert.Equal(250, f.Condition!.Ias);
    }

    [Fact]
    public void ParseSaved_keeps_an_ISO_time_as_text()
    {
        var f = FenixFailureRequests.ParseSaved(
            "{\"id\":\"F_X\",\"title\":\"T\",\"failureCondition\":{\"time\":\"2026-10-21T14:32:05Z\"},\"failed\":false}")!;
        Assert.Equal("2026-10-21T14:32:05Z", f.Condition!.Time);
        Assert.Equal(JTokenType.String, f.Raw["failureCondition"]!["time"]!.Type);
        Assert.Equal("2026-10-21T14:32:05Z", f.Raw["failureCondition"]!["time"]!.Value<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"title\":\"x\"}")]
    [InlineData("garbage")]
    public void ParseSaved_rejects_anything_but_a_failure(string? json)
        => Assert.Null(FenixFailureRequests.ParseSaved(json));

    [Fact]
    public void ParseSaved_rejects_JSON_null()
        => Assert.Null(FenixFailureRequests.ParseSaved("null"));

    [Theory]
    [InlineData("250", "", "", null, "", 250, null, null, null, null)]
    [InlineData(" 250 ", "", "", null, "", 250, null, null, null, null)]
    [InlineData("0", "", "", null, "", 0, null, null, null, null)]
    [InlineData("", "", "", "Takeoff", "30", null, null, null, "Takeoff", 30)]
    [InlineData("", "", "", "V1", "", null, null, null, "V1", null)]
    [InlineData("", "20000", "5000", null, "", null, 20000, 5000, null, null)]
    public void TryBuildCondition_accepts(string ias, string above, string below, string? ev, string secs,
        int? eIas, int? eAbove, int? eBelow, string? eEv, int? eSecs)
    {
        Assert.True(FenixFailureRequests.TryBuildCondition(ias, above, below, ev, secs, out var c, out var field, out var error));
        Assert.Equal(new FenixFailureCondition(eIas, eAbove, eBelow, eEv, eSecs, null), c);
        Assert.Equal(FenixFailureConditionField.None, field);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("", "", "", null, "", FenixFailureConditionField.None, "Enter at least one condition.")]
    [InlineData("250,5", "", "", null, "", FenixFailureConditionField.Ias, "Above IAS must be a whole number from 0 to 10000.")]
    [InlineData("10001", "", "", null, "", FenixFailureConditionField.Ias, "Above IAS must be a whole number from 0 to 10000.")]
    [InlineData("10.000", "", "", null, "", FenixFailureConditionField.Ias, "Above IAS must be a whole number from 0 to 10000.")]
    [InlineData("-1", "", "", null, "", FenixFailureConditionField.Ias,"Above IAS must be a whole number from 0 to 10000.")]
    [InlineData("", "1.500", "", null, "", FenixFailureConditionField.AltAbove, "Above altitude AMSL must be a whole number from 0 to 50000.")]
    [InlineData("", "", "50001", null, "", FenixFailureConditionField.AltBelow, "Below altitude AMSL must be a whole number from 0 to 50000.")]
    [InlineData("", "", "", null, "10001", FenixFailureConditionField.Seconds, "Seconds until failure must be a whole number from 0 to 10000.")]
    [InlineData("abc", "", "", null, "", FenixFailureConditionField.Ias, "Above IAS must be a whole number from 0 to 10000.")]
    public void TryBuildCondition_refuses(string ias, string above, string below, string? ev, string secs,
        FenixFailureConditionField expectedField, string expectedError)
    {
        Assert.False(FenixFailureRequests.TryBuildCondition(ias, above, below, ev, secs, out var c, out var field, out var error));
        Assert.Null(c);
        Assert.Equal(expectedField, field);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void TryBuildCondition_rejects_an_event_the_combo_never_offers()
        => Assert.Throws<ArgumentException>(() =>
            FenixFailureRequests.TryBuildCondition("", "", "", "Rotation", "", out _, out _, out _));
}
