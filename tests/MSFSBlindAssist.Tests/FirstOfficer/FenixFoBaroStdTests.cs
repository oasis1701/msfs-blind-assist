using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The First Officer's baro read-back ("Baro reference: SET") says standard or QNH from the EFIS QNH light
/// (<c>I_FCU_EFISn_QNH</c>: 1 = QNH, 0 = STD, read live 2026-10-21), never from the baro knob's counter
/// <c>S_FCU_EFISn_BARO_STD</c>. That counter is the knob's push/pull count: a pull adds 1, a push takes 1
/// away, so after cockpit pulls and pushes it holds any number (3 while in STD was measured).
/// </summary>
public class FenixFoBaroStdTests
{
    // The two baro knobs' push/pull counters, which the baro line must not read; Counter is the captain's.
    private const string Counter = "S_FCU_EFIS1_BARO_STD";
    private const string Counter2 = "S_FCU_EFIS2_BARO_STD";
    private const string Light1 = "I_FCU_EFIS1_QNH";
    private const string Light2 = "I_FCU_EFIS2_QNH";

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static T InCulture<T>(string name, Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            return f();
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    private static Func<string, double> Reader(Dictionary<string, double> values)
        => f => values.TryGetValue(f, out double v) ? v : double.NaN;

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(-2)]
    public void Std_comes_from_the_qnh_light(double counter)
    {
        // Light 0 is STD whatever the knob's counter holds, 1 or 0 or below.
        var read = Reader(new() { [Light1] = 0, [Counter] = counter });
        Assert.Equal("standard", FenixStateEvaluator.BaroText(read));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Qnh_comes_from_the_light_even_with_the_counter_at_3(string culture)
    {
        var read = Reader(new()
        {
            [Light1] = 1, [Counter] = 3, ["S_FCU_EFIS1_BARO_MODE"] = 1,
            ["N_FCU_EFIS1_BARO_HPA"] = 1010, ["N_FCU_EFIS1_BARO_INCH"] = 29.82,
        });
        Assert.Equal("QNH 1010", InCulture(culture, () => FenixStateEvaluator.BaroText(read)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Qnh_in_inHg_comes_from_the_light_too(string culture)
    {
        var read = Reader(new()
        {
            [Light1] = 1, [Counter] = -2, ["S_FCU_EFIS1_BARO_MODE"] = 0,
            ["N_FCU_EFIS1_BARO_HPA"] = 1013.2, ["N_FCU_EFIS1_BARO_INCH"] = 29.921,
        });
        Assert.Equal("QNH 29.92", InCulture(culture, () => FenixStateEvaluator.BaroText(read)));
    }

    [Fact]
    public void An_unread_light_reads_unknown_as_an_unread_std_flag_did()
    {
        // The shared formatter says null for a std flag it was never given; the light unread must say the
        // same, even when the old counter and the unit and value are all there to tempt a guess.
        string? asBefore = AirbusReadbackFormat.Baro(double.NaN, double.NaN, false);
        Assert.Null(asBefore);

        var read = Reader(new()
        {
            [Counter] = 3, ["S_FCU_EFIS1_BARO_MODE"] = 1, ["N_FCU_EFIS1_BARO_HPA"] = 1010,
        });
        Assert.Equal(asBefore, FenixStateEvaluator.BaroText(read));
        Assert.Null(FenixStateEvaluator.BaroText(Reader(new())));
    }

    [Fact]
    public void The_baro_line_reads_the_light_through_the_evaluator_and_the_checklist()
    {
        // The instance path the checklist's live value takes: the cache, then BaroText().
        var e = new FenixStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With((Light1, 0), (Counter, 3)));
        Assert.Equal("standard", e.BaroText());

        var line = FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == "CPC_BARO");
        Assert.Equal("standard", line.ReadLiveValue(e));

        e.SetSimConnect(SeededSimConnectCache.With(
            (Light1, 1), (Counter, 3), ("S_FCU_EFIS1_BARO_MODE", 1), ("N_FCU_EFIS1_BARO_HPA", 1010)));
        Assert.Equal("QNH 1010", e.BaroText());
        Assert.Equal("QNH 1010", line.ReadLiveValue(e));
    }

    [Fact]
    public void The_evaluator_polls_the_two_qnh_lights_and_not_the_knob_counters()
    {
        var polled = new FenixStateEvaluator().OnRequestPollFields;
        Assert.Contains(Light1, polled);
        Assert.Contains(Light2, polled);
        Assert.DoesNotContain(Counter, polled);
        Assert.DoesNotContain(Counter2, polled);

        // Both lights are registered Continuous and announced, so the cache holds them.
        var vars = new FenixA320Definition().GetVariables();
        foreach (var light in new[] { Light1, Light2 })
        {
            Assert.True(vars.TryGetValue(light, out var def), $"{light} is not registered");
            Assert.Equal(UpdateFrequency.Continuous, def!.UpdateFrequency);
        }
    }

    [Fact]
    public void The_evaluator_source_names_the_lights_and_no_knob_counter()
    {
        string source = File.ReadAllText(
            FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("Fenix", "FenixStateEvaluator.cs"));
        Assert.Contains("\"I_FCU_EFIS1_QNH\"", source);
        Assert.Contains("\"I_FCU_EFIS2_QNH\"", source);
        Assert.DoesNotContain(Counter, source);
        Assert.DoesNotContain(Counter2, source);
    }
}
