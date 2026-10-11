using System.Globalization;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A32NX PFD's speed target, as the PFD draws it (FBW <c>SpeedIndicator.tsx</c>,
/// <c>onFrameUpdate</c>): the FMGC's <c>PFD_SELECTED_SPEED</c> word, MANAGED (magenta) when
/// FMGC discrete word 5 has bit 19 (auto speed control) set and bit 20 (manual speed control)
/// clear, else selected (cyan); with no valid FMGC word, the FCU's selected airspeed, selected.
///
/// Since FBW #10890 (c0421a940) this is the only place the approach, go-around and expedite
/// targets and the speed-protection limits appear: <c>L:A32NX_SPEEDS_MANAGED_PFD</c> no longer
/// carries them. The PFD picks FMGC 1 or 2 from AP/FD engagement (<c>FgBusProvider</c>); both
/// compute the same target, so this takes FMGC 1 and falls back to FMGC 2 only when FMGC 1's
/// word carries no data.
/// </summary>
public static class FlyByWirePfdSpeedTarget
{
    public static string Describe(double? fmgc1Speed, double? fmgc1Word5,
                                  double? fmgc2Speed, double? fmgc2Word5,
                                  double? fcuSelectedAirspeed)
    {
        if (TryFmgc(fmgc1Speed, fmgc1Word5, out string text)) return text;
        if (TryFmgc(fmgc2Speed, fmgc2Word5, out text)) return text;

        if (fcuSelectedAirspeed is not double fcu) return "none";
        var word = new Arinc429Word(fcu);
        if (word.IsFailureWarning) return "not available";
        if (!word.HasData) return "none";
        return Knots(word.Value) + ", selected";
    }

    private static bool TryFmgc(double? speed, double? word5, out string text)
    {
        text = "";
        if (speed is not double raw) return false;
        var target = new Arinc429Word(raw);
        if (!target.HasData) return false;

        var discrete = new Arinc429Word(word5 ?? 0);
        bool managed = discrete.BitValueOr(19, false) && !discrete.BitValueOr(20, false);
        text = Knots(target.Value) + (managed ? ", managed" : ", selected");
        return true;
    }

    // Away from zero, as the PFD's own Math.round does for a positive speed.
    private static string Knots(float value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " knots";
}
