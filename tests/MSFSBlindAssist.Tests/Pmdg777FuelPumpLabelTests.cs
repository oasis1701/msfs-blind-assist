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

    public static TheoryData<string, string, string, string> Pumps() => new()
    {
        { "FUEL_FwdPump_1", "FUEL_PumpFwd_Sw_0", "Left Forward Pump",   "FUEL_annunLOWPRESS_Fwd_1" },
        { "FUEL_FwdPump_2", "FUEL_PumpFwd_Sw_1", "Right Forward Pump",  "FUEL_annunLOWPRESS_Fwd_2" },
        { "FUEL_AftPump_1", "FUEL_PumpAft_Sw_0", "Left Aft Pump",       "FUEL_annunLOWPRESS_Aft_1" },
        { "FUEL_AftPump_2", "FUEL_PumpAft_Sw_1", "Right Aft Pump",      "FUEL_annunLOWPRESS_Aft_2" },
        { "FUEL_CtrPump_1", "FUEL_PumpCtr_Sw_0", "Center Left Pump",    "FUEL_annunLOWPRESS_Ctr_1" },
        { "FUEL_CtrPump_2", "FUEL_PumpCtr_Sw_1", "Center Right Pump",   "FUEL_annunLOWPRESS_Ctr_2" },
    };

    [Theory]
    [MemberData(nameof(Pumps))]
    public void Fuel_pump_pins_its_struct_field_and_spoken_label(
        string varKey, string structField, string label, string lightKey)
    {
        _ = lightKey;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(varKey), $"missing fuel pump var {varKey}");
        Assert.Equal(structField, vars[varKey].Name);
        Assert.Equal(label, vars[varKey].DisplayName);
    }

    [Theory]
    [MemberData(nameof(Pumps))]
    public void Low_press_light_label_is_derived_from_its_pump_label(
        string varKey, string structField, string label, string lightKey)
    {
        _ = structField;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(lightKey), $"missing LOW PRESS light var {lightKey}");
        Assert.Equal(label + LowPressSuffix, vars[lightKey].DisplayName);
        Assert.Equal(vars[varKey].DisplayName + LowPressSuffix, vars[lightKey].DisplayName);
    }

    /// <summary>
    /// Every one of these six serves a named side, so none may fall back to a bare
    /// index - the same regression Pmdg777HydraulicPumpLabelTests exists to prevent.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pumps))]
    public void Fuel_pump_names_a_side_never_a_bare_index(
        string varKey, string structField, string label, string lightKey)
    {
        _ = structField;
        _ = lightKey;
        var vars = new PMDG777Definition().GetVariables();
        string spoken = vars[varKey].DisplayName;

        Assert.True(spoken.Contains("Left") || spoken.Contains("Right"),
            $"{varKey} serves a named side, so its label must say which: got \"{spoken}\"");
        Assert.False(spoken.EndsWith(" 1", StringComparison.Ordinal)
                  || spoken.EndsWith(" 2", StringComparison.Ordinal),
            $"{varKey} must name its side, not a bare index: got \"{spoken}\"");
        _ = label;
    }
}
