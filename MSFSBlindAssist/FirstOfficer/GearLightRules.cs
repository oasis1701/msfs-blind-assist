using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The two landing-gear checks a crew makes — "gear up, lights out" and "three green" —
/// never from the lever alone (owner decisions 2026-09-22). Aircraft-neutral: each First
/// Officer profile maps its own lever encoding and gear-light fields onto these rules
/// (PMDG 737 <c>GearConfirmation</c>, iFly <c>IFly737GearConfirmation</c>, Fenix
/// <c>FenixGearConfirmation</c>).
/// </summary>
public static class GearLightRules
{
    /// <summary>UP — "gear up, lights out": the lever reads up AND every gear light is out.</summary>
    public static bool IsUp(bool leverUp, IEnumerable<bool> lightsOn)
        => leverUp && !lightsOn.Any(on => on);

    /// <summary>DOWN — "three green": the lever reads down AND every green is on (at least one
    /// supplied) AND no red is on. A red means a gear is in transit or disagrees with the
    /// lever, and a light test lights the reds too, so neither can read as "down".</summary>
    public static bool IsDown(bool leverDown, IEnumerable<bool> greensOn, IEnumerable<bool> redsOn)
    {
        var greens = greensOn.ToList();
        return leverDown && greens.Count > 0 && greens.All(on => on) && !redsOn.Any(on => on);
    }

    /// <summary>A gear verdict as a synthetic state-field value: NaN when any of the
    /// <paramref name="readings"/> it rests on is unknown — ChecklistManager then neither
    /// ticks nor reverts, and a flow wait keeps waiting — otherwise 1 when
    /// <paramref name="confirmed"/>, else 0.</summary>
    public static double AsField(IEnumerable<double> readings, bool confirmed)
        => readings.Any(double.IsNaN) ? double.NaN : (confirmed ? 1 : 0);
}
