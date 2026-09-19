namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The scope of the walk's direct-write fallback (<c>TFDiMD11Definition.TryDirectSetAsync</c>) —
/// the one place this app writes a control's own L:var instead of a CEVENT, reached only after a
/// closed-loop walk failed to move the control.
///
/// Pure, so the rule is testable without a sim. The rule has two halves:
///   • the fallback may write ONLY the var the control's own registered definition reads back —
///     the write is confirmed by re-reading that definition, so a write to any other var is
///     unverifiable by construction; and
///   • it never writes a control in <see cref="FallbackNeverWrites"/>, whose state var is not its
///     command encoding at all (an animation or travel value).
///
/// The speedbrake lever is the case that bit: its row reads the travel var MD11_SPDBRK_RNG, but the
/// generated map's state var is MD11_SPDBRK_HANDLE, the 0/1/2 ground-spoiler pull. A dropped wheel
/// click → walk gave up → the fallback wrote 17.5 into the pull; DescribeArm(17.5) returned null,
/// the Ground spoilers row went blank, and RefuseArm compared against a value the aircraft never
/// produces. A refused control returns false to the caller, which speaks the same "did not move"
/// it always did — no new speech, no widening of the gate. Add to the list, never loosen the rule.
///
/// SCOPE OF THE NAME HALF, exactly: it sees a definition RE-POINTED off the map's state var (the
/// speedbrake shape). A state var that is the WRONG QUANTITY under the right name is invisible to
/// it — the EFIS minimums caps read <c>MD11_CAP/FO_MINIMUMS</c>, the minimums VALUE, under their own
/// name, so this gate would happily let a 0/1 be written there. Such a control must be kept off the
/// walk (<c>Md11ExportBacked</c> refuses them at the top of <c>SetControl</c>, which is why they
/// never reach this fallback) or added to the list below. Nothing here can infer it.
/// </summary>
public static class Md11DirectSet
{
    /// <summary>
    /// The gear lever: its var is the lever's 0–25 travel, not a 0/1 command. Named here so the
    /// list below reads as three peers; the string itself belongs to <see cref="Md11GearLever"/>.
    /// </summary>
    public const string GearSwitchKey = Md11GearLever.Key;

    /// <summary>
    /// Controls the fallback must never write, keyed by node id, with the reason it logs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FallbackNeverWrites =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Md11SpeedbrakeSystem.LeverKey] =
                "the map's state var is the ground-spoiler pull while the row reads the travel var; a detent value written into the pull corrupts the Ground spoilers state",
            // ASSUMED, not measured: MD11_FLAP_RNG is the lever template's animation var and the
            // wasm writes it, but whether the wasm also READS it back as the handle input has never
            // been probed. Refusing is the safe direction either way — the composed "Flaps 28"
            // read-out is fed from this same var, so a write that sticks in an animation would make
            // the read-out lie to a blind pilot. docs/md11.md carries the in-sim probe that settles it.
            [Md11FlapSystem.LeverKey] =
                "its state var is assumed to be the lever's animation (unmeasured); a detent value written there may move the picture, not the flaps",
            [GearSwitchKey] =
                "its state var is the lever's 0-25 travel; the map's 0/1 is a different encoding and a write there moves the picture, not the gear",
        };

    /// <summary>
    /// Why the fallback must not write this control, or null when it may.
    /// <paramref name="registeredVar"/> is the <c>Name</c> of the control's own SimVarDefinition
    /// (what a read-back of the node id actually reads), or null when nothing is registered under
    /// the node id. Every control that reaches the fallback is registered today, so the null branch
    /// is defence, not live scope.
    /// </summary>
    public static string? Refuse(Md11Control control, string? registeredVar)
    {
        if (string.IsNullOrEmpty(control.StateVar)) return "no state var";
        if (FallbackNeverWrites.TryGetValue(control.NodeId, out var why)) return why;
        if (registeredVar == null) return "nothing registered under its node id, so the write could not be confirmed";
        if (!string.Equals(registeredVar, control.StateVar, StringComparison.OrdinalIgnoreCase))
            return $"its row reads {registeredVar}, not its state var {control.StateVar}";
        return null;
    }
}
