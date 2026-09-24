using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the six PMDG 777 fuel boost pumps. The array index names a SIDE, not a
/// slot - PMDG_777X_SDK.h annotates every one of these pairs:
///
///   FUEL_PumpFwd_Sw[2];        // left fwd / right fwd
///   FUEL_PumpAft_Sw[2];        // left aft / right aft
///   FUEL_PumpCtr_Sw[2];        // ctr left / ctr right
///   FUEL_annunLOWPRESS_Fwd[2]; // left fwd / right fwd
///   FUEL_annunLOWPRESS_Aft[2]; // left aft / right aft
///   FUEL_annunLOWPRESS_Ctr[2]; // ctr left / ctr right
///
/// "Forward Pump 1" told a blind pilot nothing about which tank they had just
/// switched off, and a LOW PRESS warning named no side at all.
/// </summary>
public class Pmdg777FuelPumpLabelTests
{
    private const string LowPressSuffix = " LOW PRESS Light";

    /// <summary>varKey, PMDG struct field, spoken label, LOW PRESS light varKey, LOW PRESS light struct field.</summary>
    public static TheoryData<string, string, string, string, string> Pumps() => new()
    {
        { "FUEL_FwdPump_1", "FUEL_PumpFwd_Sw_0", "Left Forward Pump",   "FUEL_annunLOWPRESS_Fwd_1", "FUEL_annunLOWPRESS_Fwd_0" },
        { "FUEL_FwdPump_2", "FUEL_PumpFwd_Sw_1", "Right Forward Pump",  "FUEL_annunLOWPRESS_Fwd_2", "FUEL_annunLOWPRESS_Fwd_1" },
        { "FUEL_AftPump_1", "FUEL_PumpAft_Sw_0", "Left Aft Pump",       "FUEL_annunLOWPRESS_Aft_1", "FUEL_annunLOWPRESS_Aft_0" },
        { "FUEL_AftPump_2", "FUEL_PumpAft_Sw_1", "Right Aft Pump",      "FUEL_annunLOWPRESS_Aft_2", "FUEL_annunLOWPRESS_Aft_1" },
        { "FUEL_CtrPump_1", "FUEL_PumpCtr_Sw_0", "Center Left Pump",    "FUEL_annunLOWPRESS_Ctr_1", "FUEL_annunLOWPRESS_Ctr_0" },
        { "FUEL_CtrPump_2", "FUEL_PumpCtr_Sw_1", "Center Right Pump",   "FUEL_annunLOWPRESS_Ctr_2", "FUEL_annunLOWPRESS_Ctr_1" },
    };

    [Theory]
    [MemberData(nameof(Pumps))]
    public void Fuel_pump_pins_its_struct_field_and_spoken_label(
        string varKey, string structField, string label, string lightKey, string lightField)
    {
        _ = lightKey;
        _ = lightField;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(varKey), $"missing fuel pump var {varKey}");
        Assert.Equal(structField, vars[varKey].Name);
        PmdgStructFields.AssertResolves777(structField, varKey);
        Assert.Equal(label, vars[varKey].DisplayName);
    }

    /// <summary>
    /// The light's own array slot as well as its label: without the slot pin, binding a
    /// LOW PRESS light to the wrong index leaves every label assertion green while "Left
    /// Forward Pump LOW PRESS Light" reports the RIGHT tank's pressure loss.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pumps))]
    public void Low_press_light_label_is_derived_from_its_pump_label(
        string varKey, string structField, string label, string lightKey, string lightField)
    {
        _ = structField;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(lightKey), $"missing LOW PRESS light var {lightKey}");
        Assert.Equal(lightField, vars[lightKey].Name);
        PmdgStructFields.AssertResolves777(lightField, lightKey);
        Assert.Equal(label + LowPressSuffix, vars[lightKey].DisplayName);
        Assert.Equal(vars[varKey].DisplayName + LowPressSuffix, vars[lightKey].DisplayName);
    }
}
