using System.Linq;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Turns a <see cref="GsxGateSelectResult"/> into the phrase (if any) a blind pilot needs to
/// hear about a <c>gate.select</c> outcome that no on-screen readout conveys. Pure and
/// stateless — no dependency on <c>ScreenReaderAnnouncer</c> — so the mapping is directly
/// unit-testable; the caller (<c>Forms.TaxiAssistForm.SelectGsxGateAsync</c>) owns actually
/// speaking the result, always via the QUEUED announcer (<c>Announce</c>, never
/// <c>AnnounceImmediate</c> — this fires from a background GSX round trip well after the
/// Calculate click, not a direct UI interaction the screen reader already announces).
///
/// Exactly four situations are announced, per the Spec 2 wiring task:
/// <list type="bullet">
/// <item>a <c>too_small</c> warning on an otherwise-successful selection — GSX's own verdict
/// that the stand does not fit the airframe, and there is no other route to that information;</item>
/// <item>an occupied stand (<see cref="GsxGateSelectOutcome.AssignedToOther"/>) — and this never
/// offers to force it, matching <see cref="GsxRemoteGateSelector"/>'s own never-auto-force rule;</item>
/// <item>an ambiguous identifier (<see cref="GsxGateSelectOutcome.Ambiguous"/>) — surfaces that
/// GSX would not guess, rather than silently doing nothing;</item>
/// <item>a successful revoke-and-reprepare (<see cref="GsxGateSelectResult.WasRevokedAndReprepared"/>)
/// — so the pilot knows the previous stand's setup was torn down.</item>
/// </list>
/// Every other outcome is silent by design — see the switch below for why each one is left
/// out. Silence there means <see cref="Describe"/> returns null and the caller announces
/// nothing; it is deliberate, not an oversight. That default is also what makes adding a new
/// <see cref="GsxGateSelectOutcome"/> member safe: the switch names its cases explicitly and
/// has no <c>default:</c> arm, so a new member falls straight through and speaks nothing
/// until someone deliberately gives it a case.
///
/// ONE phrase here is deliberately not part of that mapping —
/// <see cref="GateSelectUnsupportedMessage"/>, the "you need GSX 4.0.8" line — because it
/// must be spoken once per taxi dialog rather than once per result, and this class must stay
/// stateless. See its own doc comment.
/// </summary>
public static class GsxGateSelectAnnouncer
{
    private const string TooSmallWarning = "too_small";

    /// <summary>Names beyond this many candidates collapse into "...and N more", so an
    /// ambiguous-match phrase stays a sentence rather than a recital of the whole list.</summary>
    private const int MaxAmbiguousNames = 3;

    /// <summary>
    /// What to say when GSX is connected but its capability list has no <c>gate</c> token —
    /// i.e. a 4.0.1-4.0.7 build, where the Remote API exists but <c>gate.select</c> does not
    /// (<see cref="GsxGateSelectOutcome.GateSelectUnsupported"/>).
    ///
    /// DELIBERATELY NOT returned by <see cref="Describe"/>, and that is not an oversight:
    /// this must be spoken ONCE per taxi-dialog instance, and this class is pure and
    /// stateless so its mapping stays unit-testable. The once-only latch therefore lives on
    /// the caller — <c>Forms.TaxiAssistForm.SelectGsxGateAsync</c> — which reads the outcome
    /// and speaks this constant itself. Without a latch the pilot would hear it on EVERY
    /// gate-destination Calculate, which on an older GSX is every single flight.
    ///
    /// It states the capability fact and the pilot's next move, and claims nothing about
    /// GSX being broken or absent — on this path GSX is running and answering; only this one
    /// verb is missing, and Access GSX's menus, services and settings all work normally.
    /// 4.0.8 is the version named (not 4.0.1, which shipped the Remote API itself) because
    /// 4.0.8 is where <c>gate.select</c> arrived: a pilot sent to 4.0.1 would land on a build
    /// where gate selection still silently does nothing.
    /// </summary>
    public const string GateSelectUnsupportedMessage =
        "Automatic gate selection needs GSX 4.0.8 or newer. This GSX build does not offer it, " +
        "so select the stand in the GSX menu yourself.";

    /// <summary>The phrase to speak for <paramref name="result"/>, or null when nothing needs
    /// to be said. Never throws — every accessor it reads is already a defended, defaulted
    /// member of <see cref="GsxGateSelectResult"/>.</summary>
    public static string? Describe(GsxGateSelectResult result)
    {
        var parts = new List<string>();

        // Checked independent of Outcome's own switch below: a services_active retry that
        // resolves to Prepared carries BOTH this and (possibly) a too_small warning on the
        // very same result, and the pilot needs to hear both facts, not just the first one
        // that matched.
        if (result.WasRevokedAndReprepared)
            parts.Add("GSX released the previous stand's services.");

        switch (result.Outcome)
        {
            case GsxGateSelectOutcome.Prepared:
                // Warnings is only ever populated alongside Prepared (see
                // GsxGateSelectResult's own doc comment on the property) -- every failure
                // path leaves it at its default empty list -- so this is the one branch
                // where a too_small warning can appear.
                if (result.Warnings.Contains(TooSmallWarning, StringComparer.Ordinal))
                    parts.Add($"GSX warns {GateName(result, "This stand")} may be too small for this aircraft.");
                break;

            case GsxGateSelectOutcome.AssignedToOther:
                // Never offers to force it -- matches GsxRemoteGateSelector's own rule that
                // force:true is never sent automatically (spec ruling: silently overriding
                // an AI-occupied stand would put a blind pilot nose-to-nose with an aircraft
                // they cannot see).
                parts.Add($"{GateName(result, "The requested stand")} is occupied by another aircraft. GSX did not select it.");
                break;

            case GsxGateSelectOutcome.Ambiguous:
                parts.Add(DescribeAmbiguous(result.Candidates));
                break;

                // Every other outcome is silent by design, not an omission:
                //   AlreadyThere     -- the spec calls already_parked/already_selected
                //                       "nothing to do"; the pilot asked for a stand GSX had
                //                       already prepared or parked at.
                //   NotFound/BadArgs/
                //   NoAirport        -- not among the four outcomes this task's spec lists to
                //                       speak; each is logged to gsx-gate-select.log (the
                //                       documented first stop for "gate not found") rather
                //                       than interrupting the pilot on every Calculate click.
                //   ServicesActive   -- only reachable here if the ONE automatic retry also
                //                       came back services_active; GsxRemoteGateSelector never
                //                       retries a second time, and the spec doesn't ask for
                //                       this double-busy case to be spoken.
                //   Unavailable      -- GSX sent a code this build doesn't recognise, or its
                //                       capabilities aren't known yet (Remote API not
                //                       connected). Nothing here is actionable in flight, and
                //                       "GSX is unreachable" already has its own surface --
                //                       GsxService.UnavailableReason, spoken on an explicit
                //                       Access GSX action, never as background speech.
                //   GsxNotRunning /
                //   AuthRequired     -- likewise: named so gsx-gate-select.log tells the truth
                //                       about WHICH failure happened (Part C), not so they can
                //                       be spoken. GSX being down is already covered by
                //                       UnavailableReason, and auth_required cannot occur on
                //                       localhost (authRequired: false on every capture) --
                //                       there is no action a pilot could take mid-flight for
                //                       either.
                //   GateSelectUnsupported
                //                    -- the 4.0.8 fact IS spoken, but ONCE per taxi dialog,
                //                       and a once-only latch cannot live in a stateless
                //                       mapper. TaxiAssistForm owns the latch and speaks
                //                       GateSelectUnsupportedMessage above; returning it here
                //                       would repeat it on every Calculate.
                //   TransportFailure -- the request never reached GSX at all (not connected,
                //                       send failed, timed out) -- same reasoning as
                //                       Unavailable: routine and not worth interrupting for.
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>GSX's own name for the resolved stand, or <paramref name="fallback"/> when
    /// GSX's response omitted or malformed the field.</summary>
    private static string GateName(GsxGateSelectResult result, string fallback) =>
        result.ResolvedGate?.UiName is { Length: > 0 } name ? name : fallback;

    /// <summary>Names up to <see cref="MaxAmbiguousNames"/> candidates, then a residual
    /// count, so the phrase stays a sentence instead of a recital of the whole match list —
    /// the same cap SayIntentions' route announcements use for the same reason.</summary>
    private static string DescribeAmbiguous(IReadOnlyList<GsxGateSelectCandidate> candidates)
    {
        var names = candidates
            .Select(c => c.UiName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(MaxAmbiguousNames)
            .ToList();

        if (names.Count == 0)
            return "GSX found more than one matching stand and did not select one.";

        string list = string.Join(", ", names);
        int residual = candidates.Count - names.Count;
        string suffix = residual > 0 ? $", and {residual} more" : "";
        return $"GSX found more than one matching stand: {list}{suffix}. Please choose a more specific one.";
    }
}
