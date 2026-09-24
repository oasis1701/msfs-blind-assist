namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The one read-compare-toggle decision the A380's stock-event toggle combos share.
///
/// `HandleUIVariableSet` grew eight hand-rolled copies of "read the live value, compare it to
/// the pick, fire a toggle event only if they differ", each re-deciding the cache-miss default
/// independently — and a copy that diverges by one token is invisible at review. It shipped
/// twice: the 2026-07 wing anti-ice change, and its 2026-09 fix, which restored the branch with
/// `?? 0` where the code it restored had `?? (desiredOn ? 0.0 : 1.0)`.
///
/// ⚠️ A null <paramref name="current"/> means the live value is UNKNOWN (cold cache — the batch
/// cache is cleared on every aircraft switch and reconnect and stays empty for up to a batch
/// period), NOT "off". It must FIRE, matching the convention every other toggle-if-differs
/// branch uses, because reading unknown as 0 makes "Off" permanently unsendable while "On"
/// toggles an already-on control back OFF — which for wing anti-ice means switching the system
/// off in icing in answer to the pilot selecting it on. Same rule, same reason, as
/// <see cref="A380EfisCpControls"/>'s null-current handling.
/// </summary>
public static class A380ToggleCommand
{
    /// <summary>
    /// Whether the toggle event must be fired to move the control to <paramref name="desired"/>,
    /// given the caller's best view of the live value (null = unknown, which always fires).
    /// The caller must force-read the key when this returns false, or the combo latches on a
    /// position the aircraft never took.
    /// </summary>
    public static bool ShouldFire(double desired, double? current)
    {
        bool want = desired > 0.5;
        bool have = (current ?? (want ? 0.0 : 1.0)) > 0.5;
        return want != have;
    }
}
