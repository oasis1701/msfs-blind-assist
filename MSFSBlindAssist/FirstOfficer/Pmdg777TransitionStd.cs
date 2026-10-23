namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777 phase monitor's STD pushes at the transition crossings (FlightPhaseMonitor). The
/// EFIS STD buttons are TOGGLES and the SDK has no latched STD flag (<c>EFIS_BaroSTD_Sw_Pushed</c> is
/// a momentary push), so the one reading is the Captain's altimeter setting at 29.92 inHg
/// (<see cref="AircraftStateEvaluator.IsBaroStdCapt"/>). That reading cannot tell STD from a QNH of
/// 29.92, the US standard day and MSFS's preset weather: read as STD, the climb said "Altimeters set
/// to standard" over a QNH of 29.92 it never pushed, and the descent pushed both sides INTO standard
/// while saying "set to local QNH".
///
/// So the monitor keeps its own record. The climb pushes STD only from a reading that is definitely
/// NOT 29.92, and remembers it. The descent pushes again only to take back that push, and only while
/// the Captain still reads 29.92; any other descent pushes nothing and speaks the reminder the PMDG
/// 737 and iFly descents speak. The First Officer's side has no reading of its own, so it is pushed
/// with the Captain's: a side set to STD by hand first is not seen. Pure logic, for the tests.
/// </summary>
public sealed class Pmdg777TransitionStd
{
    public const string StandardSetText = "Transition altitude. Altimeters set to standard.";
    public const string AlreadyStandardText =
        "Transition altitude. The captain's altimeter already reads standard, nothing pushed.";
    public const string BackToQnhText = "Transition level. Altimeters set to local QNH. Set local pressure now.";

    /// <summary>What a crossing does: push both STD buttons or not, and what is spoken.</summary>
    public readonly record struct Decision(bool PushBoth, string Speech);

    /// <summary>True from this monitor's own climb push until the next transition-level crossing or
    /// <see cref="Reset"/>.</summary>
    public bool PushedStandard { get; private set; }

    /// <summary>Climbing through the transition altitude. <paramref name="captainReads2992"/> is
    /// <see cref="AircraftStateEvaluator.IsBaroStdCapt"/>: null while the setting is unread.</summary>
    public Decision OnClimb(bool? captainReads2992)
    {
        switch (captainReads2992)
        {
            case false:
                PushedStandard = true;
                return new Decision(true, StandardSetText);
            case true:
                // STD, or a QNH of 29.92: a push would take the first out of standard.
                return new Decision(false, AlreadyStandardText);
            default:
                return new Decision(false, TransitionAltimeters.AltitudeReminder);
        }
    }

    /// <summary>Descending through the transition level. The push from the climb is forgotten either
    /// way: from here the pilot sets the local pressure, and a later push must never act on it.</summary>
    public Decision OnDescent(bool? captainReads2992)
    {
        bool takeBack = PushedStandard && captainReads2992 == true;
        PushedStandard = false;
        return takeBack
            ? new Decision(true, BackToQnhText)
            : new Decision(false, TransitionAltimeters.LevelReminder);
    }

    /// <summary>Forgets the climb push: a new flight, or a transition-level crossing with the
    /// altimeter setting off (FO-23), which pushes nothing.</summary>
    public void Reset() => PushedStandard = false;
}
