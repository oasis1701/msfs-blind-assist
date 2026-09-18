namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// One altimeter STD toggle: the row a pilot presses (<c>Key</c>); the altimeter knob whose PUSH it
/// presses (<c>Knob</c>) — on the MD-11 that push IS the STD toggle; the altimeter export that push
/// changes (<c>AltimeterVar</c>); the side as a read-back speaks it (<c>Side</c>); whether the
/// definition reads the press back (<c>ReadsBack</c> — false where an announcer already speaks every
/// change of that altimeter); and the EFIS panel an MSFSBA row is placed on (<c>PanelName</c>, null
/// for a map row the layout table already places).
/// </summary>
public sealed record Md11StdTarget(string Key, string Knob, string AltimeterVar, string Side, bool ReadsBack, string? PanelName);

/// <summary>
/// The MD-11's three altimeter STD toggles (review round 2, A20).
///
/// STD is a MODE the baro knob's PUSH toggles, and its indication lives on the WASM-rendered PFD —
/// unreadable; the stock <c>KOHLSMAN SETTING STD:1</c> is never driven (docs/md11.md, "The altimeter
/// read-out"). So a press is confirmed by the altimeter SETTING it changes, never by an STD flag.
///
///   • Captain and First Officer: two rows of this app's own ("Captain Altimeter STD", "First
///     Officer Altimeter STD") press <c>MD11_LECP_BAROSET_CAP</c> / <c>MD11_RECP_BAROSET_CAP</c>'s
///     push. The knobs' own rows stay READ-ONLY — their state var is the altimeter export
///     (<see cref="Md11ExportBacked"/>) — so these are separate rows, like the COM rows, and the push
///     goes out as the knob's CEVENT pair, never a write.
///   • Standby: TFDi's <c>MD11_MIP_ISFD_STD_BT</c> is the animation node the standby baro knob's push
///     drives (<c>MD11_MIP_ISFD_BARO_KB</c>: ANIM_CODE_PUSH on that L:var) and carries NO events of
///     its own, so pressing its row did nothing; it is redirected to that knob's push.
///
/// The captain's press is confirmed by <see cref="Md11AltimeterAnnouncer"/>, which already speaks
/// every settled change of <c>MD11_CAP_ALTIMETER</c>; the other two have no announcer, so the
/// definition reads them back once, in <see cref="ReadBackSentence"/>'s words.
/// </summary>
public static class Md11StdToggles
{
    /// <summary>The captain's STD row — an MSFSBA row, not a map control.</summary>
    public const string CaptainKey = "MD11_MSFSBA_CAP_BARO_STD";

    /// <summary>The first officer's STD row — an MSFSBA row, not a map control.</summary>
    public const string FirstOfficerKey = "MD11_MSFSBA_FO_BARO_STD";

    /// <summary>TFDi's own standby-display STD button: a map row with no events, redirected to the standby baro knob's push.</summary>
    public const string StandbyKey = "MD11_MIP_ISFD_STD_BT";

    public const string CaptainLabel = "Captain Altimeter STD";
    public const string FirstOfficerLabel = "First Officer Altimeter STD";

    public static readonly Md11StdTarget Captain =
        new(CaptainKey, Md11Fcp.BaroKnob, Md11Fcp.ReadCaptainBaro, "Captain", ReadsBack: false, PanelName: "EFIS Captain");

    public static readonly Md11StdTarget FirstOfficer =
        new(FirstOfficerKey, "MD11_RECP_BAROSET_CAP", Md11Fcp.ReadFoBaro, "First Officer", ReadsBack: true, PanelName: "EFIS First Officer");

    public static readonly Md11StdTarget Standby =
        new(StandbyKey, "MD11_MIP_ISFD_BARO_KB", Md11Fcp.ReadStandbyBaro, "Standby", ReadsBack: true, PanelName: null);

    /// <summary>Captain, first officer, standby.</summary>
    public static readonly Md11StdTarget[] Targets = { Captain, FirstOfficer, Standby };

    /// <summary>The STD toggle a panel key presses, if it is one.</summary>
    public static bool TryGet(string key, out Md11StdTarget target)
    {
        foreach (var t in Targets)
        {
            if (string.Equals(t.Key, key, StringComparison.Ordinal)) { target = t; return true; }
        }
        target = Captain;
        return false;
    }

    /// <summary>
    /// The read-back after an STD press, in the B key's words (<see cref="Md11AltimeterAnnouncer.Sentence"/>)
    /// prefixed with the side: "First Officer altimeter standard", "Standby altimeter: 1020, 30.12".
    /// Null when nothing was delivered or the export reads 0 (a missing export reads a flat 0) — no
    /// evidence, so nothing is said.
    /// </summary>
    public static string? ReadBackSentence(string side, double? reading)
    {
        if (reading is not double r || r <= 0) return null;
        return Md11Fcp.IsStandard(r)
            ? $"{side} altimeter standard"
            : $"{side} altimeter: {Md11Fcp.DescribeAltimeter(r)}";
    }
}
