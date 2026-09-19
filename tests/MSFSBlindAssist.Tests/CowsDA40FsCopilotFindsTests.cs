using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ FS Copilot's own COWS_DA40NG.yaml is a THIRD-PARTY INVENTORY of this airframe, and
/// diffing it against every variable this definition binds found nine readouts MSFSBA was
/// not reading.
///
/// It works as a coverage test in a way the interaction-surface audit structurally cannot:
/// that audit enumerates CLICKABLE components, and none of these nine is a control - they
/// are things the aeroplane computes and shows. A shared-cockpit sync tool has to enumerate
/// exactly that state or the two aircraft drift apart, which is what makes its list worth
/// reading.
///
/// ⚠️ Every one was READ LIVE before it was defined. A variable named in a third-party file
/// is a claim, not evidence - the same standard the stock autopilot events were held to.
/// </summary>
public class CowsDA40FsCopilotFindsTests
{
    [Theory]
    [InlineData("DA40_G1000_MINIMUMS", "COWS_MINIMUMS_ALTITUDE")]
    [InlineData("DA40_FUEL_TOTALISER_REM", "FUEL_TOTALISER_REM")]
    [InlineData("DA40_FUEL_TOTALISER_USED", "FUEL_TOTALISER_USE")]
    [InlineData("DA40_PITOT_TEMP", "PITOT_TEMP")]
    [InlineData("DA40_ELEC_BATT_ECU_CAPACITY", "ELEC_BATT_ECU_CAPACITY")]
    [InlineData("DA40_ELEC_BATT_SURF", "ELEC_BATT_SURF")]
    [InlineData("DA40_AP_POWERED", "AFCS_POWER")]
    public void EachFindIsBoundAsAnLVarReadInRawNumbers(string key, string lvar)
    {
        // ⚠️ Units MUST stay "number". An L:var registered with a converting unit makes
        // SimConnect convert from its own base unit and return garbage - this aeroplane's
        // own rule, learned on the standby subscale. The word a pilot HEARS comes from the
        // display override instead.
        var d = new CowsDA40Definition(DA40Variant.NG).GetVariables()[key];
        Assert.Equal(lvar, d.Name);
        Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.LVar, d.Type);
        Assert.Equal("number", d.Units);
    }

    [Fact]
    public void TheNumericFindsAreSilentAndEarnNoMuteRow()
    {
        // Numbers are silent by the house rule, and a row that silences nothing must not
        // earn a Ctrl+M checkbox.
        var vars = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        foreach (string key in new[]
        {
            "DA40_G1000_MINIMUMS", "DA40_FUEL_TOTALISER_REM",
            "DA40_PITOT_TEMP", "DA40_ELEC_BATT_ECU_CAPACITY", "DA40_AP_POWERED"
        })
        {
            Assert.False(vars[key].IsAnnounced, $"{key} must not announce");
            Assert.True(vars[key].ExcludeFromMonitorManager, $"{key} must not earn a mute row");
        }
    }

    [Fact]
    public void EachFindIsReachableOnAPanelOrItIsDead()
    {
        // A readout in no panel is never requested and its decoder never runs - the exact
        // shape of the dead definitions removed from the Radios panel.
        var def = new CowsDA40Definition(DA40Variant.NG);
        var all = new System.Collections.Generic.HashSet<string>();
        foreach (var p in def.GetPanelDisplayVariables()) all.UnionWith(p.Value);

        foreach (string key in new[]
        {
            "DA40_G1000_MINIMUMS", "DA40_FUEL_TOTALISER_REM", "DA40_FUEL_TOTALISER_USED",
            "DA40_PITOT_TEMP",
            "DA40_ELEC_BATT_ECU_CAPACITY", "DA40_ELEC_BATT_SURF", "DA40_AP_POWERED"
        })
            Assert.Contains(key, all);
    }

    [Fact]
    public void TheXlsDoesNotClaimTheNgOnlyReadings()
    {
        // ⚠️ The totaliser, both fuel temperatures and PITOT_TEMP were measured on the NG.
        // The XLS is a different engine with a different fuel system and its own YAML, and
        // claiming a reading exists on an airframe it was never measured on is the failure
        // this whole exercise exists to avoid. The XLS gets its own pass.
        var def = new CowsDA40Definition(DA40Variant.XLS);
        var all = new System.Collections.Generic.HashSet<string>();
        foreach (var p in def.GetPanelDisplayVariables()) all.UnionWith(p.Value);

        Assert.DoesNotContain("DA40_FUEL_TOTALISER_REM", all);
        Assert.DoesNotContain("DA40_PITOT_TEMP", all);

        // The shared halves DO appear: the main battery's surface charge, the GFC 700.
        Assert.Contains("DA40_ELEC_BATT_SURF", all);
        Assert.Contains("DA40_AP_POWERED", all);

        // ⚠️ And the ECU battery does NOT. This test used to assert it as a shared reading on
        // "the same battery model" — but the XLS has no ECU, so no ECU battery, and the
        // variable is absent from its package: the row read 0 for ever. Whole-name search of
        // each variant's own package is the evidence (CowsDA40PackagePresenceTests).
        Assert.DoesNotContain("DA40_ELEC_BATT_ECU_CAPACITY", all);
    }

    [Theory]
    // Minimums unset is a real answer and must not read as a bare "0 feet".
    [InlineData("DA40_G1000_MINIMUMS", 0, "Not set")]
    [InlineData("DA40_G1000_MINIMUMS", 250, "250 feet")]
    [InlineData("DA40_FUEL_TOTALISER_REM", 37.3, "37.3 gallons")]
    [InlineData("DA40_PITOT_TEMP", 29.9, "30 degrees C")]
    // A bool the model stores as a number, named as a state rather than a figure.
    [InlineData("DA40_AP_POWERED", 0, "Not powered")]
    [InlineData("DA40_AP_POWERED", 1, "Powered")]
    public void TheUnitAPilotHearsComesFromTheOverride(string key, double value, string expected)
    {
        var def = new CowsDA40Definition(DA40Variant.NG);
        Assert.True(def.TryGetDisplayOverride(key, value, out string text));
        Assert.Equal(expected, text);
    }
}
