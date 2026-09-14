using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The things the COWS POH says that MSFSBA could not say back.
///
/// Each of these is a fact stated in the aircraft's own manual, verified against the
/// model's own XML, and previously unreachable from MSFSBA — either because no variable
/// was bound or because the variant that has it was being handed the other variant's
/// list. What is pinned here is the CONCLUSION of each, because the manual and the model
/// are both outside the repository and neither can be read from CI.
/// </summary>
public class CowsDA40PohFindingsTests
{
    private static CowsDA40Definition Xls() => new(DA40Variant.XLS);
    private static CowsDA40Definition Ng() => new(DA40Variant.NG);

    // ------------------------------------------------------------------ Automixture (XLS)

    /// <summary>
    /// ⚠️ AUTOMIXTURE IS A REGIME CHANGE THE PILOT CANNOT SEE. It is not a cockpit switch:
    /// the model DETECTS the simulator's own mixture assistance and ramps `L:AUTOMIXTURE`
    /// to 1. With it set the lever picks an air/fuel target instead of driving the valve,
    /// and every cylinder's charge is clamped at 2 g — so the engine cannot be flooded and
    /// the whole hot-start problem the Priming panel exists for cannot arise.
    /// </summary>
    [Fact]
    public void TheXlsReportsWhetherAutomixtureIsInPlay()
    {
        var vars = Xls().GetVariables();
        Assert.True(vars.ContainsKey("DA40_XLS_AUTOMIXTURE"));
        Assert.Equal("AUTOMIXTURE", vars["DA40_XLS_AUTOMIXTURE"].Name);
        Assert.Equal("number", vars["DA40_XLS_AUTOMIXTURE"].Units);
        Assert.True(vars["DA40_XLS_AUTOMIXTURE"].RenderAsReadOnlyStatus);

        // It carries a Ctrl+M row: the call-out is a state change and must be mutable.
        Assert.False(vars["DA40_XLS_AUTOMIXTURE"].ExcludeFromMonitorManager);

        // Read-only: the setting lives in the simulator's assistance options, not here.
        Assert.DoesNotContain("DA40_XLS_AUTOMIXTURE",
            Xls().GetPanelControls().SelectMany(p => p.Value));
    }

    /// <summary>The NG has no such variable — it is a FADEC, there is no mixture lever.</summary>
    [Fact]
    public void TheNgHasNoAutomixtureRow()
    {
        Assert.False(Ng().GetVariables().ContainsKey("DA40_XLS_AUTOMIXTURE"));
        Assert.DoesNotContain(Ng().GetVariables().Values, d => d.Name == "AUTOMIXTURE");
    }

    /// <summary>
    /// ⚠️ THE VALUE RAMPS IN FIFTHS, so only the ends mean anything. The model adds 0.2 a
    /// tick until it reaches 1 and drops to 0 in one step; a reading in between is the
    /// ramp, not a state, and must never be announced as one.
    /// </summary>
    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "On")]
    [InlineData(1.0, "On")]
    [InlineData(0.2, "Engaging")]
    [InlineData(0.8, "Engaging")]
    public void TheAutomixtureRampReadsAsARampAndNotAsAState(double value, string expected)
        => Assert.Equal(expected, CowsDA40Definition.DescribeAutomixture(value));

    // ------------------------------------------------- Engage Starter with Mixture (XLS)

    /// <summary>
    /// The POH prints the same MFD menu for both airframes and they differ by exactly two
    /// rows: Priming Assist (which the Priming panel already owns) and this one. A
    /// variant-blind options list was offering the NG a switch its aeroplane has not got.
    /// </summary>
    [Fact]
    public void OnlyTheXlsCarriesTheMixtureStarterOption()
    {
        var xls = Xls().GetVariables();
        Assert.True(xls.ContainsKey("DA40_OPT_START_MIXTURE"));
        Assert.Equal("START_MIXTURE", xls["DA40_OPT_START_MIXTURE"].Name);

        Assert.False(Ng().GetVariables().ContainsKey("DA40_OPT_START_MIXTURE"));
    }

    // ------------------------------------------------- Emergency fuel transfer (NG)

    /// <summary>
    /// ⚠️ IN EMERGENCY THE AEROPLANE THROWS FUEL AWAY AND NOTHING SAID SO. The POH:
    /// "There is no sensor to stop the transfer of fuel. Fuel will be pushed overboard if
    /// the transfer isn't stopped." The model agrees — the cooling loop returns fuel to the
    /// main tank clamped `19.5 min`, and past that clamp it is simply gone.
    /// </summary>
    [Fact]
    public void TheNgReportsTheEmergencyTransfer()
    {
        var vars = Ng().GetVariables();
        Assert.True(vars.ContainsKey("DA40_FUEL_XFER_EMERG"));
        Assert.Equal("FUEL_TEMP_ENG_FLOW:1", vars["DA40_FUEL_XFER_EMERG"].Name);
        Assert.Contains("DA40_FUEL_XFER_EMERG", Ng().GetPanelDisplayVariables()["Fuel System"]);

        // The XLS has neither this fuel system nor this variable.
        Assert.False(Xls().GetVariables().ContainsKey("DA40_FUEL_XFER_EMERG"));
    }

    /// <summary>
    /// It is batched so the row can be composed beside the valve and the tank, and SILENT
    /// because it is a rate — non-zero whenever the propeller turns, valve or no valve.
    /// </summary>
    [Fact]
    public void TheEmergencyTransferRateIsCachedAndNeverSpokenAsANumber()
    {
        var def = Ng().GetVariables()["DA40_FUEL_XFER_EMERG"];
        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        Assert.True(def.IsAnnounced);
        Assert.Contains("DA40_FUEL_XFER_EMERG", CowsDA40Definition.SilentCachedReadoutKeys);
    }

    [Fact]
    public void TheTransferRowSaysNothingBeforeItHasReadTheValve()
        => Assert.Equal("Not available yet",
            CowsDA40Definition.DescribeEmergencyTransfer(null, 0.0125, 10));

    [Theory]
    [InlineData(0)]   // Main
    [InlineData(2)]   // Off
    public void NothingTransfersUnlessTheValveIsAtEmergency(double valve)
        => Assert.Equal("Not transferring",
            CowsDA40Definition.DescribeEmergencyTransfer(valve, 0.0125, 10));

    /// <summary>
    /// The rate is the model's own per-SECOND figure. 0.0125 gal/s is what
    /// `PROP RPM / 2300 × 45 / 3600` gives at 2300 rpm — the POH's "around 0.7 gal/min".
    /// </summary>
    [Fact]
    public void TheTransferRateIsReportedPerMinute()
    {
        string text = CowsDA40Definition.DescribeEmergencyTransfer(1, 0.0125, 10);
        Assert.Contains("0.8 gallons per minute", text);
        Assert.DoesNotContain("overboard", text);
    }

    /// <summary>
    /// The one that matters: against the 19.5 gallon stop, everything the loop pushes in
    /// from here is lost, and there is no sensor and no gauge that can show it (the NG's
    /// fuel indication saturates at 14 gallons, well below the stop).
    /// </summary>
    [Fact]
    public void AFullMainTankMeansTheTransferIsGoingOverboard()
    {
        string text = CowsDA40Definition.DescribeEmergencyTransfer(1, 0.0125, 19.5);
        Assert.Contains("overboard", text);
    }

    /// <summary>
    /// ⚠️ THE DA40 CARRIES NO PANEL HELP TEXT, ON EITHER VARIANT, AND MUST NOT REGAIN ANY.
    /// It had 78 of them. They were redundant - the pilot is expected to know the aeroplane,
    /// the same as a sighted pilot does, and a control that has to explain itself is a
    /// control that is named wrong. Every fact those lines carried lives in docs/da40.md,
    /// which is where a pilot who wants the background goes. No other aircraft in this app
    /// leans on them either.
    /// </summary>
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoDA40VariableCarriesHelpText(DA40Variant variant)
    {
        var withHelp = new CowsDA40Definition(variant).GetVariables()
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.HelpText))
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(withHelp.Count == 0,
            $"{variant}: help text is not used on this aircraft - {string.Join(", ", withHelp)}");
    }
    // ------------------------------------------------- Indication failures, per airframe

    /// <summary>
    /// ⚠️ THE TWO AIRFRAMES HAVE DIFFERENT INDICATION FAILURES AND MSFSBA CARRIED THE NG's
    /// SET ON BOTH. Grepped from each model's own XML: they share six, the NG adds four
    /// (fuel temperature per tank, gearbox, coolant) and the XLS adds eleven (tachometer,
    /// manifold pressure, fuel flow, fuel pressure, and CHT and EGT per cylinder).
    ///
    /// It matters more here than anywhere else on this aeroplane because of the DISP_ rule:
    /// a failed indication ZEROES the variable MSFSBA reads while the engine runs on
    /// perfectly. Verified live on the XLS — `FAILURES_DISP_CHT:1 = 1` took `DISP_CHT:1` to
    /// 0 while cylinder 2 still read 353 °F — so without a row the pilot sees a cylinder at
    /// zero with nothing to tell that from a real reading.
    /// </summary>
    [Theory]
    [InlineData("DA40_FAIL_DISP_OP")]
    [InlineData("DA40_FAIL_DISP_OT")]
    [InlineData("DA40_FAIL_DISP_AMPS")]
    [InlineData("DA40_FAIL_DISP_VOLT")]
    [InlineData("DA40_FAIL_DISP_FUEL_1")]
    [InlineData("DA40_FAIL_DISP_FUEL_2")]
    public void BothAirframesShareSixIndicationFailures(string key)
    {
        Assert.Contains(key, Ng().GetPanelControls()["Indication Failures"]);
        Assert.Contains(key, Xls().GetPanelControls()["Indication Failures"]);
    }

    /// <summary>
    /// The XLS's own eleven. The first four are the numbers its cruise tables and its
    /// mixture are set by; the eight after them are what the lean assist is read from.
    /// </summary>
    [Theory]
    [InlineData("DA40_FAIL_DISP_RPM", "FAILURES_DISP_RPM")]
    [InlineData("DA40_FAIL_DISP_MAP", "FAILURES_DISP_MAP")]
    [InlineData("DA40_FAIL_DISP_FF", "FAILURES_DISP_FF")]
    [InlineData("DA40_FAIL_DISP_FP", "FAILURES_DISP_FP")]
    [InlineData("DA40_FAIL_DISP_CHT_1", "FAILURES_DISP_CHT:1")]
    [InlineData("DA40_FAIL_DISP_CHT_4", "FAILURES_DISP_CHT:4")]
    [InlineData("DA40_FAIL_DISP_EGT_1", "FAILURES_DISP_EGT:1")]
    [InlineData("DA40_FAIL_DISP_EGT_4", "FAILURES_DISP_EGT:4")]
    public void TheXlsCarriesItsOwnIndicationFailures(string key, string lvar)
    {
        var xls = Xls();
        Assert.True(xls.GetVariables().ContainsKey(key), $"{key} is not defined on the XLS");
        Assert.Equal(lvar, xls.GetVariables()[key].Name);
        Assert.Contains(key, xls.GetPanelControls()["Indication Failures"]);

        // And never on the NG, whose model has none of them.
        Assert.False(Ng().GetVariables().ContainsKey(key), $"{key} must not exist on the NG");
    }

    /// <summary>
    /// ⚠️ A ROW FOR A FAILURE THE AIRFRAME CANNOT HAVE IS WORSE THAN NO ROW — a pilot scans
    /// it and is reassured about a system nothing watches. The XLS has no gearbox, no
    /// coolant and no fuel-temperature indication, and none of those four variables exists
    /// in its package.
    /// </summary>
    [Theory]
    [InlineData("DA40_FAIL_DISP_FUEL_T1")]
    [InlineData("DA40_FAIL_DISP_FUEL_T2")]
    [InlineData("DA40_FAIL_DISP_GT")]
    [InlineData("DA40_FAIL_DISP_WT")]
    public void TheXlsDoesNotCarryTheAustrosIndicationFailures(string key)
    {
        Assert.Contains(key, Ng().GetPanelControls()["Indication Failures"]);

        Assert.False(Xls().GetVariables().ContainsKey(key), $"{key} is a phantom on the XLS");
        Assert.DoesNotContain(key, Xls().GetPanelControls()["Indication Failures"]);
    }
}
