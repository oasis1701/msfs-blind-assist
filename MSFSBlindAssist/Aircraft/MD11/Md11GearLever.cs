namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The gear lever, whose var is NOT the boolean the control map claims.
///
/// <c>MD11_MIP_GEAR_SW</c> is the lever's 0-25 TRAVEL, and the aircraft's own tooltip
/// (CenterInstrument.xml) tests <c>(L:MD11_MIP_GEAR_SW) 20 &gt;=</c> for Down. The generated map's
/// value_map {0 Up, 1 Down} is the generator mis-reading that <c>%{if}</c>: the COMPARISON yields
/// the boolean, not the var (pinned by Md11ReadoutTests). A <c>&gt; 0.5</c> test happens to agree at
/// either end and is WRONG mid-travel — it calls a lever at 10 "down" while the aircraft says up.
///
/// Two readers share this one rule so they cannot drift: the gear hotkey read-out
/// (<see cref="Describe"/>, over a fresh read) and the Landing Gear panel's combo, keyed on the map's 0/1
/// (<see cref="DescriptionKey"/>, wired through <c>SimVarDefinition.ValueToDescriptionKey</c> —
/// MainForm's combo lookup is an exact key match, so the parked lever's 25 selected nothing).
/// The pick still writes the map key, which is what the walker's two-position toggle expects.
/// </summary>
public static class Md11GearLever
{
    /// <summary>The control-map node (and variable key) of the lever.</summary>
    public const string Key = "MD11_MIP_GEAR_SW";

    /// <summary>
    /// The travel (0-25) at or above which the aircraft calls the lever DOWN. TFDi's own threshold,
    /// from the Gear Lever tooltip in CenterInstrument.xml — not a guess.
    /// </summary>
    public const double DownThreshold = 20;

    /// <summary>The map's keys: what the combo's items are keyed on and what a pick writes.</summary>
    public const double UpKey = 0, DownKey = 1;

    /// <summary>TFDi's rule, verbatim: <c>(L:MD11_MIP_GEAR_SW) 20 &gt;=</c>.</summary>
    public static bool IsDown(double travel) => travel >= DownThreshold;

    /// <summary>The ValueDescriptions key a travel value classifies onto (the map's 0 Up / 1 Down).</summary>
    public static double DescriptionKey(double travel) => IsDown(travel) ? DownKey : UpKey;

    /// <summary>
    /// The gear key's sentence for a FRESH read of the lever: "Gear down" / "Gear up" by TFDi's
    /// threshold, and "Gear position unavailable" only when nothing was delivered
    /// (<paramref name="travel"/> null) — never a cached value, which this OnRequest var holds
    /// only while the Landing Gear panel is open.
    /// </summary>
    public static string Describe(double? travel)
        => travel is not double t ? "Gear position unavailable" : IsDown(t) ? "Gear down" : "Gear up";
}
