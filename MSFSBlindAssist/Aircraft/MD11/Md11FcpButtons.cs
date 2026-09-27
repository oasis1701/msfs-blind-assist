namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The Flight Control Panel window's button captions, with their Alt accelerators.
///
/// Deliberately NOT in the form, for the same reason <see cref="Md11McduKeys.PageButtons"/> is
/// not: two buttons sharing an Alt+letter is not a compile error and not a crash. WinForms
/// searches for the mnemonic from the control AFTER the focused one and wraps to the first when it
/// reaches the end, so the shadowed claimant wins exactly when focus is on the OTHER claimant —
/// and on this window one of the claimants was the V/S wheel, which ENGAGES the pitch mode. With
/// focus on the Altitude Unit button (where the screen reader has just read "Alt+U" out to the
/// pilot) Alt+U clicked the wheel instead of toggling feet/metres, silently; with focus on PROF,
/// Alt+P pressed Approach / Land. Md11FcpButtonsTests pins every caption here unique.
///
/// The Ctrl+H/S/A/V type-in dialogs carry their own captions on
/// <c>Aircraft\TFDiMD11Definition.Fcp.cs</c>. Each dialog is its own accelerator scope and none of
/// them collides; they are NOT shared with this table on purpose — a letter chosen for the window
/// could then collide with a dialog-only caption ("P&amp;ush knob", "Pu&amp;ll knob",
/// "&amp;Track / Heading") that no test over the window can see. Where a button exists in both
/// places the letter may differ (the wheel); that asymmetry is accepted rather than moving a
/// binding a pilot already uses.
/// </summary>
public static class Md11FcpButtons
{
    // ---- Autoflight ----
    public const string Autoflight = "&Autoflight";
    public const string Prof = "&PROF";
    public const string Nav = "&NAV";
    // Alt+O: Alt+P belongs to PROF, which is what the chord reached from the window's landing
    // focus before this letter existed. (The PMDG and iFly windows put Approach on Alt+P; here
    // that would have displaced a binding already in use.)
    public const string ApproachLand = "Appr&oach / Land";
    public const string FmsSpeed = "&FMS Speed";
    public const string GoAround = "&Go Around";

    // ---- Mode select ----
    public const string IasMach = "&IAS / Mach";
    public const string HeadingTrack = "&Heading / Track";
    public const string VsFpa = "&VS / FPA";
    public const string AltitudeUnit = "Altitude &Unit";

    // ---- Vertical speed wheel ----
    // Alt+W: Alt+U belongs to Altitude Unit. The Ctrl+V dialog keeps its own "Wheel &up" (Alt+U)
    // — a different scope with no collision, and a binding the pilot already uses.
    public const string WheelUp = "&Wheel up";
    public const string WheelDown = "Wheel &down";

    // ---- Autothrust ----
    public const string AtsDisconnectLeft = "Disconnect &Left";
    public const string AtsDisconnectRight = "Disconnect &Right";

    public const string Close = "&Close";

    /// <summary>
    /// Every caption on the window that carries an accelerator. The window is ONE mnemonic scope,
    /// so every letter in this list must be unique. The knob Push/Pull buttons carry none (they
    /// are reached by Tab) and are not listed.
    /// </summary>
    public static readonly string[] WindowCaptions =
    {
        Autoflight, Prof, Nav, ApproachLand, FmsSpeed, GoAround,
        IasMach, HeadingTrack, VsFpa, AltitudeUnit,
        WheelUp, WheelDown,
        AtsDisconnectLeft, AtsDisconnectRight,
        Close,
    };
}
