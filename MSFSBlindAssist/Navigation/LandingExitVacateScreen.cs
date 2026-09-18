using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Works out, for each exit on a runway, whether the taxiways it leads to actually take the
/// aircraft CLEAR of that runway — the flag the Landing Exit Planner shows as "no taxiway mapped
/// clear of the runway" and refuses to default to.
///
/// <para>ONE owner, because two places now choose an exit. The planner dialog does it while the
/// pilot is reading the list; the touchdown re-plan does it with nobody watching, at landing speed,
/// on a runway the pilot never selected — and there the fresh exits carry
/// <see cref="LandingExit.VacatesRunway"/>'s optimistic default, so nothing had ever asked the
/// question. The dialog's own reasoning applies with more force on the rollout: "better to learn
/// while choosing than at 60 knots on the rollout."</para>
///
/// <para>Marking is only a PREFERENCE — <see cref="LandingExitReplan.ChooseExit"/> still offers a
/// flagged exit when it is all there is, exactly as the dialog keeps them in its list. At some
/// airports they are the only exits mapped.</para>
/// </summary>
public static class LandingExitVacateScreen
{
    public static void Mark(TaxiGraph? graph, IReadOnlyList<LandingExit>? exits, Runway? runway)
    {
        if (graph == null || exits == null || runway == null) return;

        foreach (var exit in exits)
        {
            if (exit == null) continue;
            LandingExitDestination.Resolve(
                graph, exit, exits, runway, runway.Heading,
                out _, out double endLateralM, out _);
            exit.VacatesRunway = RunwayVacateResolver.IsOffPavement(endLateralM, runway);
        }
    }
}
