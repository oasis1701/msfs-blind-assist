namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Whether a <c>TaxiGuidanceManager.LoadRoute</c> reachability refusal must roll back the
/// state it had already overwritten (<c>CaptureLoadRouteRollback</c> /
/// <c>RestoreLoadRouteRollback</c>), leaving the route currently being flown untouched.
///
/// <para>PR #238 review follow-up (Important 5). <c>LoadRoute</c>'s three refusal sites --
/// no start node in range, no buildable route, and a first leg across a runway -- each
/// independently spelled <c>if (reachability != ReachabilityClass.Unchanged)</c> around
/// their own <c>RestoreLoadRouteRollback</c> call. The defect this PR's own Task 7 fixed was
/// exactly two of those three sites having drifted from the third (a
/// <see cref="ReachabilityClass.LeavingUnconnectedPosition"/> refusal fell through with NO
/// rollback at the first two, while the runway-crossing site already restored for both
/// non-Unchanged classes) -- hand-re-establishing agreement across three independently
/// hand-typed copies leaves the exact same drift surface open, with nothing pinning it
/// against a future edit touching only one or two of the three. One predicate, called from
/// all three, is what this codebase does instead for exactly this shape -- see
/// <c>Services/RunwayIncursionWatch</c> and <c>Services/GroundTrafficSuppression</c>, both
/// written for the same reason: which of several near-identical call sites a change happens
/// to touch should not decide whether the fix took.</para>
///
/// <para>PR #238 review, Minor D correction. The THIRD site (first leg across a runway) no
/// longer calls <see cref="ShouldRestore"/> directly for its own <c>RestoreLoadRouteRollback</c>
/// call -- its OUTER guard was renamed to <see cref="RouteReachability.IsOffDestinationNetwork"/>,
/// because that outer guard answers a different question ("does the first leg need checking at
/// all") than this predicate does ("must a refusal roll back"), even though the two share the
/// exact same formula today. The rollback call stays correct without its own direct
/// <c>ShouldRestore</c> check: being inside an <c>IsOffDestinationNetwork</c> block already
/// guarantees <c>ShouldRestore</c> would agree, since both predicates read the same
/// <c>reachability</c> value against the same <see cref="ReachabilityClass.Unchanged"/>
/// baseline. <see cref="ShouldRestore"/> itself is unchanged, still called directly by the
/// first two sites, and remains the one place the "must roll back" decision lives.</para>
///
/// <para>Only <see cref="ReachabilityClass.Unchanged"/> means the aircraft was already on
/// (or headed toward) the destination's own network -- an ordinary refusal in that case (no
/// taxi path data, destination node not found, no nearby taxiway node, could not calculate a
/// route) has nothing to do with route reachability, and rolling those back too is a
/// separate, unasked-for change (CLAUDE.md / the brief's explicit scope for this PR).</para>
/// </summary>
public static class LoadRefusalRollback
{
    public static bool ShouldRestore(ReachabilityClass reachability) =>
        reachability != ReachabilityClass.Unchanged;
}
