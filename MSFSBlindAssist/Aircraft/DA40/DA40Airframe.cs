namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Which DA40 profile the aircraft in the sim needs. MSFSBA keeps the profile the pilot last
/// picked, and one DA40 profile on the other airframe reads that airframe's variables as 0 —
/// measured: the XLS profile on a loaded NG spoke "Engine variations not generated, the engine
/// cannot start", because the XLS's spread-pressure variable does not exist on the NG. The two are
/// one family in the Aircraft menu and the sim has already settled which one is flying, so
/// MainForm swaps between them by itself. It never switches into or out of the DA40.
/// </summary>
internal static class DA40Airframe
{
    public const string NgCode = "COWS_DA40NG";
    public const string XlsCode = "COWS_DA40XLS";

    /// <summary>
    /// The profile code for either the AircraftLoaded file path
    /// (<c>…\simobjects\airplanes\cows_da40ng\aircraft.cfg</c>, lower case as the sim sends it) or
    /// the aircraft TITLE the connect reports ("DA40-NG White", "DA40-XLS Ribbon 2", possibly
    /// followed by the ATC identification). Null for anything that is not a COWS DA40.
    /// </summary>
    public static string? CodeFor(string? titleOrFile)
    {
        if (string.IsNullOrWhiteSpace(titleOrFile)) return null;
        string s = titleOrFile.Trim();

        if (s.Contains(@"\cows_da40ng\", StringComparison.OrdinalIgnoreCase)) return NgCode;
        if (s.Contains(@"\cows_da40xls\", StringComparison.OrdinalIgnoreCase)) return XlsCode;

        if (s.StartsWith("DA40-NG", StringComparison.OrdinalIgnoreCase)) return NgCode;
        if (s.StartsWith("DA40-XLS", StringComparison.OrdinalIgnoreCase)) return XlsCode;
        return null;
    }

    /// <summary>
    /// The profile to swap to, or null to stay. Only from one DA40 profile to the other: a DA40
    /// airframe under another aircraft's profile is the pilot's choice and is left alone.
    /// </summary>
    public static string? SwapTarget(string currentCode, string? titleOrFile)
    {
        if (currentCode != NgCode && currentCode != XlsCode) return null;
        string? wanted = CodeFor(titleOrFile);
        return wanted != null && wanted != currentCode ? wanted : null;
    }
}
