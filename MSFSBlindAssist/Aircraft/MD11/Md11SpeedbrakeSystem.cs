namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11 speedbrake lever, which is THREE variables, not one (TFDi's own tooltip composes
/// them, read from <c>FootPedestalLower.xml</c> and confirmed live 2026-09-06):
///
///   • <c>MD11_SPDBRK_RNG</c>    — the lever's travel: 0 retracted, 17.5 one third, 25 two thirds,
///                                32.5 fully extended. The WHEEL_UP/WHEEL_DOWN events step it,
///                                and only while the lever is not pulled (armed).
///   • <c>MD11_SPDBRK_HANDLE</c> — the pull: 0 down, 1 pulled up with the lever retracted = GROUND
///                                SPOILERS ARMED, 2 = the ground spoilers have auto-extended on
///                                landing. The lever's single click (LEFT_BUTTON_DOWN) toggles it.
///   • <c>MD11_SPDBRK_LATCH</c>  — a visual latch animation only.
///
/// The generated map hung the travel detents on the HANDLE var, so the panel's Spoilers combo
/// could neither read the detent nor reach one (the walk read a var that never moved), and had
/// no notion of "armed" at all. Here the lever row reads the TRAVEL var, streamed so a hardware
/// lever's detent is spoken live, and a second row, Ground spoilers, reads the pull.
/// </summary>
public static class Md11SpeedbrakeSystem
{
    /// <summary>The map control (the panel's Spoilers row); its definition reads <see cref="TravelVar"/>.</summary>
    public const string LeverKey = "MD11_SPDBRK_HANDLE";
    public const string TravelVar = "MD11_SPDBRK_RNG";

    /// <summary>The Ground spoilers row: a definition of this app's own, reading <see cref="ArmVar"/>.</summary>
    public const string ArmKey = "MD11_SPDBRK_ARM";
    public const string ArmVar = "MD11_SPDBRK_HANDLE";

    /// <summary>The row's spoken name — its read-back sentences and its undeliverable refusal share it.</summary>
    public const string ArmName = "Ground spoilers";

    public const double NotArmed = 0, Armed = 1, Extended = 2;

    /// <summary>How close to a detent the travel value must sit to be named; a sweeping lever in between is silent.</summary>
    public const double DetentTolerance = 2.0;

    public static readonly (double Value, string Name)[] Detents =
    {
        (0, "Retracted"), (17.5, "1/3 extended"), (25, "2/3 extended"), (32.5, "Fully extended"),
    };

    public static Dictionary<double, string> TravelValues
        => Detents.ToDictionary(d => d.Value, d => d.Name);

    public static readonly Dictionary<double, string> ArmValues = new()
    {
        [NotArmed] = "Not armed", [Armed] = "Armed", [Extended] = "Extended",
    };

    /// <summary>The detent a travel value sits in, or null between detents.</summary>
    public static string? DescribeTravel(double rng)
    {
        int i = DetentIndex(rng);
        return i < 0 ? null : Detents[i].Name;
    }

    /// <summary>
    /// The <see cref="TravelValues"/> KEY a travel value describes — the classifier behind the
    /// Spoilers combo (<c>SimVarDefinition.ValueToDescriptionKey</c>). The travel streams every
    /// frame and rests wherever the lever stopped, a little off its detent (26.4 for "2/3
    /// extended"), so the combo's exact-key lookup matched nothing: it opened with NO selection,
    /// and in a DropDownList the first Down-arrow selects row 0 and COMMITS it — "Retracted".
    /// The rule is the read-out's own (<see cref="DescribeTravel"/>), so the detent the combo opens
    /// on is the one the read-out names. Between detents it returns the travel itself, which is
    /// never a key (every detent is more than <see cref="DetentTolerance"/> away) — the flap
    /// handle's convention (<c>Md11FlapSystem.LeverDetentKey</c>): the combo opens with nothing
    /// selected rather than a detent the lever is not in.
    ///
    /// Only the OPENING is classified. MainForm applies this when it builds the panel; its live
    /// re-sync never sees this var, because ProcessSimVarUpdate consumes every delivery of it. So
    /// once built, the combo keeps the detent it opened on, or the pilot's last pick, while the
    /// lever moves on — into a gap or to another detent — and only the spoken detents follow it.
    /// </summary>
    public static double TravelDescriptionKey(double travel)
    {
        int i = DetentIndex(travel);
        return i < 0 ? travel : Detents[i].Value;
    }

    /// <summary>
    /// The detent within <see cref="DetentTolerance"/> of a travel value, or -1 between detents —
    /// the one rule the read-out and the combo share. The detents sit 7.5 or more apart and the
    /// tolerance is 2, so at most one qualifies, and it is the nearest.
    /// </summary>
    private static int DetentIndex(double travel)
    {
        for (int i = 0; i < Detents.Length; i++)
            if (Math.Abs(travel - Detents[i].Value) <= DetentTolerance) return i;
        return -1;
    }

    /// <summary>What the lever row shows: the detent, or the fact that it is between two.</summary>
    public static string DisplayTravel(double rng) => DescribeTravel(rng) ?? "between detents";

    /// <summary>The announcement for a change of the pull var; null for a value the aircraft never produces.</summary>
    public static string? DescribeArm(double handle) => (int)Math.Round(handle) switch
    {
        0 => "Ground spoilers disarmed",
        1 => "Ground spoilers armed",
        2 => "Ground spoilers extended",
        _ => null,
    };

    /// <summary>
    /// The read-back after the lever's click: null when the DELIVERED pull is the target, or when
    /// nothing was delivered — a verdict rests on a delivery; with no delivery nothing is spoken
    /// and the Ground spoilers row shows the state — else "did not arm" / "did not disarm".
    ///
    /// The old read slept a fixed time and then read the CACHE, which for this batch-covered var
    /// still holds the pre-click pull whenever the next 1 Hz delivery has not landed: "did not
    /// arm" was spoken over a click the aircraft had taken. And because the pilot's own combo pick
    /// is echo-suppressed for three seconds (MainForm's UiSetEchoSuppressMs), that false failure
    /// was normally the only thing they heard.
    /// </summary>
    public static string? ArmReadBack(double target, double? delivered)
    {
        if (delivered is not double h) return null;
        int want = (int)Math.Round(target);
        if ((int)Math.Round(h) == want) return null;
        return want == 1 ? $"{ArmName} did not arm." : $"{ArmName} did not disarm.";
    }

    /// <summary>
    /// Why a Ground spoilers selection is refused before anything is sent, or null when it may go.
    /// The click only toggles the pull, so "Extended" is not a choice, arming needs the lever
    /// retracted (the aircraft ignores the pull otherwise), and disarming an auto-extended set
    /// is done by retracting the lever.
    /// </summary>
    public static string? RefuseArm(double target, double handle, double travel)
    {
        int want = (int)Math.Round(target), have = (int)Math.Round(handle);
        if (want == 2) return "Ground spoilers extend by themselves on landing; arm them instead.";
        if (want == have) return null;                        // nothing to do, and nothing to say
        if (want == 1 && (DescribeTravel(travel) != "Retracted")) return "Retract the spoilers before arming them.";
        if (want == 0 && have == 2) return "The ground spoilers are extended; retract the lever instead.";
        return null;
    }

    /// <summary>
    /// Why a Spoilers selection must be refused before anything is sent, or null when it may go.
    /// md11.md (Speedbrake) records that the lever template gates the wheel events on the pull
    /// being DOWN, so while it is up a walk could only end in a generic "did not move"; each pulled
    /// state gets its own reason and what to do instead. A FULL retraction is never refused. Armed
    /// (1): every other selection is an extension — arming requires the lever retracted — and is
    /// refused. Auto-extended on landing (2): a partial detent is refused like an extension,
    /// whichever way it would move the lever, and only the full retraction may go, the stow
    /// <see cref="RefuseArm"/> points the pilot to for the same state. That same gate suggests even
    /// the stow may be ignored at 2; that is unmeasured, and a stow the aircraft ignores still
    /// reports "did not move". SetControl hands this to DebouncedWalk, which asks it only for the
    /// selection its debounce settles on, with the pull as it reads at that moment.
    /// </summary>
    public static string? RefuseTravel(double targetTravel, double handle)
    {
        if (targetTravel <= DetentTolerance) return null;
        return (int)Math.Round(handle) switch
        {
            1 => "Disarm the ground spoilers before extending the spoilers.",
            2 => "The ground spoilers are extended; select Retracted to stow them.",
            _ => null,
        };
    }
}
