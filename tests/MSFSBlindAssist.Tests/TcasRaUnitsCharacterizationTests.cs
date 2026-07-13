// Characterization tests pinning the TCAS RA-guidance variable registrations on both
// FBW jets (Aircraft/FlyByWireA320Definition.cs, Aircraft/FlyByWireA380Definition.cs).
//
// The fly-to/avoid V/S band L:vars and the rate-to-maintain are written by FBW UNITLESS
// but already in feet per minute (RA_VARIANTS green/red = MAX_VS/MIN_VS = ±6000 fpm;
// rateToMaintain = 0/±1500/±2500). Registering them with a velocity unit makes the
// SimConnect data-def read assume the raw value is m/s and multiply by 196.85 — a
// 1500-fpm climb target was spoken as "295276". They MUST stay Units="number" (raw);
// the "feet per minute" label is hardcoded in TcasRaGuidance.Compose + the :1 display
// overrides. (CLAUDE.md invariant; docs/troubleshooting-playbook.md has the full story.)
//
// Also pins the A380 PRIM-FE flight-path-angle format: FPA lives in the ±0.5–4° range,
// so the readout must keep tenth-degree precision ("0.0"), matching the FCU FPA readout.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class TcasRaUnitsCharacterizationTests
{
    private static readonly string[] TcasRawFpmVars =
    {
        "A32NX_TCAS_VSPEED_GREEN:1",
        "A32NX_TCAS_VSPEED_GREEN:2",
        "A32NX_TCAS_VSPEED_RED:1",
        "A32NX_TCAS_VSPEED_RED:2",
        "A32NX_TCAS_RA_RATE_TO_MAINTAIN",
    };

    [Fact]
    public void A320_tcas_ra_band_and_rate_vars_are_registered_as_raw_number()
    {
        var vars = new FlyByWireA320Definition().GetVariables();
        foreach (var key in TcasRawFpmVars)
        {
            Assert.True(vars.ContainsKey(key), $"missing TCAS var {key}");
            Assert.Equal("number", vars[key].Units);
        }
    }

    [Fact]
    public void A380_tcas_ra_band_and_rate_vars_are_registered_as_raw_number()
    {
        var vars = new FlyByWireA380Definition().GetVariables();
        foreach (var key in TcasRawFpmVars)
        {
            Assert.True(vars.ContainsKey(key), $"missing TCAS var {key}");
            Assert.Equal("number", vars[key].Units);
        }
    }

    [Theory]
    [InlineData("PFD_GAMMA_A")]
    [InlineData("PFD_GAMMA_T")]
    public void A380_prim_flight_path_angles_keep_tenth_degree_precision(string key)
    {
        var vars = new FlyByWireA380Definition().GetVariables();
        Assert.True(vars.ContainsKey(key), $"missing PFD var {key}");
        Assert.True(vars[key].IsArinc429);
        Assert.Equal("degrees", vars[key].Arinc429Unit);
        Assert.Equal("0.0", vars[key].Arinc429Format);
    }
}
