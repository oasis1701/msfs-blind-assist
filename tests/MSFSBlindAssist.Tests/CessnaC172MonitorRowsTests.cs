// The Cessna 172's Ctrl+M window lists exactly what the aircraft speaks in the background. Oil
// temperature, EGT, fuel flow, battery voltage, oil pressure, vacuum and the fuel quantities are
// Continuous + IsAnnounced so they ride the batch for the status displays and hotkeys, but
// ProcessSimVarUpdate swallows every update (the low oil, vacuum and fuel warnings are the G1000's
// CAS messages, C172-17), so a row for one would mute nothing (VAR-9). So do the autopilot modes the
// FMA speaks. The RPM redline, the stall warning, the autopilot master and flight director (their
// OFF), the covers and the audio panel keys are spoken, so each keeps its row. Both directions are
// pinned.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class CessnaC172MonitorRowsTests
{
    private static HashSet<string> RowKeys() =>
        MonitorRowBuilder.Build(new CessnaC172G1000Definition().GetVariables()).Select(r => r.Key).ToHashSet();

    [Fact]
    public void Silent_readouts_have_no_row()
    {
        var keys = RowKeys();

        Assert.DoesNotContain("C172_OIL_TEMP", keys);
        Assert.DoesNotContain("C172_EGT", keys);
        Assert.DoesNotContain("C172_FUEL_FLOW", keys);
        Assert.DoesNotContain("C172_BATTERY_VOLTAGE", keys);
        Assert.DoesNotContain("C172_OIL_PRESSURE", keys);
        Assert.DoesNotContain("C172_VACUUM", keys);
        Assert.DoesNotContain("C172_FUEL_LEFT_QTY", keys);
        Assert.DoesNotContain("C172_FUEL_RIGHT_QTY", keys);
    }

    [Fact]
    public void The_modes_the_fma_speaks_have_no_row()
    {
        var keys = RowKeys();

        foreach (var key in new[]
        {
            "C172_AP_HDG_STATE", "C172_AP_ALT_STATE", "C172_AP_NAV_STATE", "C172_AP_APR_STATE",
            "C172_AP_BC_STATE", "C172_AP_VS_STATE", "C172G_AP_FLC_STATE",
        })
        {
            Assert.DoesNotContain(key, keys);
        }
    }

    [Fact]
    public void Every_spoken_variable_keeps_its_row()
    {
        var keys = RowKeys();

        foreach (var key in new[]
        {
            "C172_ENGINE_RPM", "C172_STALL_WARNING",
            "C172_AP_MASTER_STATE", "C172G_AP_FD_STATE",
            "C172G_COVER_ENGINE", "C172G_COVER_PITOT", "C172G_COVER_STATIC",
            "C172G_AUDIO_TRANSMIT", "C172G_AUDIO_COM1", "C172G_AUDIO_COM2", "C172G_AUDIO_NAV1",
            "C172G_AUDIO_NAV2", "C172G_AUDIO_DME", "C172G_AUDIO_ADF", "C172G_AUDIO_MKR_MUTE",
            "C172G_AUDIO_HI_SENS", "C172G_AUDIO_SPKR",
        })
        {
            Assert.Contains(key, keys);
        }
    }

    // The intercom's two SimVars are cached for the audio panel's status list and never spoken.
    [Fact]
    public void The_intercom_simvars_have_no_row()
    {
        var keys = RowKeys();

        Assert.DoesNotContain(CessnaC172G1000Definition.IntercomActiveKey, keys);
        Assert.DoesNotContain(CessnaC172G1000Definition.IntercomModeKey, keys);
    }
}
