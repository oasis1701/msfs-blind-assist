namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// A position control whose state var is one of TFDi's read-only EXPORTS (<c>export_vars</c> in the
/// control map) has no walkable axis in this app: its wheel moves the VALUE the export reports —
/// the FCP mode knobs (<c>MD11_CGS_HDG_KB</c> reads <c>MD11_AP_HDG_TRK</c>, <c>SPD_KB</c> reads
/// <c>MD11_AP_IAS_MACH</c>, <c>VS_KB</c> reads <c>MD11_AP_VS_FPA</c>) turn the heading, speed and
/// vertical-speed windows, and the EFIS minimums caps (<c>MD11_LECP/RECP_MINIMUMS_CAP</c> read
/// <c>MD11_CAP/FO_MINIMUMS</c>) turn the minimums — never the var the row would name. A combo on
/// such a row could only stall its walk, and the direct-write fallback then raw-wrote the export
/// (<c>MD11_CAP_MINIMUMS</c> ← 0 zeroed the captain's minimums silently). So the row renders
/// read-only and a set is refused; the mode is switched by its own button beside the row
/// (<c>MD11_CGS_HDGTRK_BT</c> / <c>IASMACH_BT</c> / <c>VS_FPA_BT</c>, <c>Md11Minimums.ModeSwitch</c>).
///
/// Pure so it can be pinned without a sim; the definition applies it in <c>BuildControlVariable</c>
/// and at the top of <c>SetControl</c>. Momentary buttons are deliberately exempt: a press is the
/// actuator, whatever var describes its state.
/// </summary>
public static class Md11ExportBacked
{
    /// <summary>True when <paramref name="control"/> is a position-kind control reading a read-only export.</summary>
    public static bool IsReadOnly(Md11Control control, IReadOnlySet<string> exportVars)
    {
        if (control == null || exportVars == null) return false;
        if (string.IsNullOrEmpty(control.StateVar)) return false;
        if (!IsPositional(control.Kind)) return false;
        return exportVars.Contains(control.StateVar);
    }

    /// <summary>
    /// The kinds with an operable, readable POSITION: the ones <c>SetControl</c> walks and
    /// <c>BuildControlVariable</c> registers OnRequest. THE one spelling — both of those switches
    /// guard a single case on this predicate (<c>case string positional when …</c>) rather than
    /// writing the six labels out again, because C# will happily compile a group that has quietly
    /// lost one: dropping <c>Md11Kinds.Handle</c> from SetControl's alone made the three engine
    /// fire handles unwalkable with no error, and the tripwire test — which reflects over
    /// <c>Md11Kinds</c> membership — only catches a kind being ADDED.
    /// </summary>
    public static bool IsPositional(string kind) => kind is Md11Kinds.Switch or Md11Kinds.Knob
        or Md11Kinds.KnobPush or Md11Kinds.KnobPushPull or Md11Kinds.Lever or Md11Kinds.Handle;
}
