namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The two PMDG 737 CDU keyboards. The SDK names its CDU key events after the
/// CLASSIC keyboard, and the events are POSITIONAL: on the FANS2 CDU (an aircraft
/// option, <c>L:cduOptions</c> = 1 — the BBJ2 ships with it) eight of those
/// positions carry a different key. Measured from the cockpit model
/// (<c>73X_Cockpit_Behavior.xml</c>, the <c>ngx_switch_5xx_fans</c> tooltips):
/// <code>
///   SDK event (classic key)   FANS2 key
///   548 CLB                   DEP ARR
///   549 CRZ                   ATC
///   550 DES                   VNAV
///   551 MENU                  FIX
///   553 DEP ARR               HOLD
///   554 HOLD                  FMC COMM
///   557 N1 LIMIT              MENU
///   558 FIX                   N1 LIMIT
/// </code>
/// Every other key is the same on both. Sending the classic name on a FANS2 CDU
/// opens the wrong page (DEP ARR opened HOLD, MENU opened FIX, …).
/// </summary>
public static class PMDG737CduKeyboard
{
    /// <summary>The value of <c>L:cduOptions</c> that selects the FANS2 keyboard.</summary>
    public const double Fans2OptionValue = 1;

    /// <summary>True when an <c>L:cduOptions</c> reading means the FANS2 keyboard.</summary>
    public static bool IsFans2(double? cduOptions) =>
        cduOptions is double v && Math.Abs(v - Fans2OptionValue) < 0.5;

    // FANS2 key → the SDK event suffix at its position.
    private static readonly Dictionary<string, string> Fans2KeyToSdk = new()
    {
        ["DEP_ARR"]  = "CLB",
        ["ATC"]      = "CRZ",
        ["VNAV"]     = "DES",
        ["FIX"]      = "MENU",
        ["HOLD"]     = "DEP_ARR",
        ["FMC_COMM"] = "HOLD",
        ["MENU"]     = "N1_LIMIT",
        ["N1_LIMIT"] = "FIX",
    };

    // Keys that exist on only one of the two keyboards.
    private static readonly HashSet<string> ClassicOnly = new() { "CLB", "CRZ", "DES" };
    private static readonly HashSet<string> Fans2Only = new() { "ATC", "VNAV", "FMC_COMM" };

    /// <summary>
    /// The SDK event suffix (after <c>EVT_CDU_L_</c>/<c>EVT_CDU_R_</c>) that presses
    /// the key named <paramref name="key"/> on the given keyboard, or null when that
    /// keyboard has no such key.
    /// </summary>
    public static string? SdkSuffixFor(string key, bool fans2)
    {
        if (fans2)
        {
            if (ClassicOnly.Contains(key)) return null;
            return Fans2KeyToSdk.TryGetValue(key, out var sdk) ? sdk : key;
        }
        return Fans2Only.Contains(key) ? null : key;
    }

    /// <summary>
    /// The key printed at the position of the SDK event <paramref name="sdkSuffix"/>
    /// on the given keyboard (the inverse of <see cref="SdkSuffixFor"/>).
    /// </summary>
    public static string KeyAt(string sdkSuffix, bool fans2)
    {
        if (!fans2) return sdkSuffix;
        foreach (var (key, sdk) in Fans2KeyToSdk)
            if (sdk == sdkSuffix) return key;
        return sdkSuffix;
    }

    /// <summary>The spoken/visible name of a key, e.g. "DEP_ARR" → "Dep/Arr".</summary>
    public static string DisplayName(string key) => key switch
    {
        "INIT_REF"  => "Init Ref",
        "RTE"       => "RTE",
        "CLB"       => "CLB",
        "CRZ"       => "CRZ",
        "DES"       => "DES",
        "MENU"      => "Menu",
        "LEGS"      => "Legs",
        "DEP_ARR"   => "Dep/Arr",
        "HOLD"      => "Hold",
        "PROG"      => "Prog",
        "N1_LIMIT"  => "N1 Limit",
        "FIX"       => "Fix",
        "ATC"       => "ATC",
        "VNAV"      => "VNAV",
        "FMC_COMM"  => "FMC Comm",
        _           => key,
    };

    /// <summary>The window's Alt+letter shortcut for a key, or null when it has none.</summary>
    public static char? Shortcut(string key) => key switch
    {
        "INIT_REF"  => 'I',
        "RTE"       => 'R',
        "CLB"       => 'B',
        "CRZ"       => 'Z',
        "DES"       => 'D',
        "MENU"      => 'M',
        "LEGS"      => 'G',
        "DEP_ARR"   => 'A',
        "HOLD"      => 'H',
        "PROG"      => 'P',
        "N1_LIMIT"  => 'N',
        "FIX"       => 'F',
        "EXEC"      => 'E',
        "ATC"       => 'T',
        "VNAV"      => 'V',
        "FMC_COMM"  => 'O',
        _           => null,
    };

    /// <summary>The key an Alt+letter shortcut names, or null when the letter is unused.</summary>
    public static string? KeyForShortcut(char letter) => char.ToUpperInvariant(letter) switch
    {
        'I' => "INIT_REF",
        'R' => "RTE",
        'B' => "CLB",
        'Z' => "CRZ",
        'D' => "DES",
        'M' => "MENU",
        'G' => "LEGS",
        'A' => "DEP_ARR",
        'H' => "HOLD",
        'P' => "PROG",
        'N' => "N1_LIMIT",
        'F' => "FIX",
        'E' => "EXEC",
        'T' => "ATC",
        'V' => "VNAV",
        'O' => "FMC_COMM",
        _   => null,
    };
}
