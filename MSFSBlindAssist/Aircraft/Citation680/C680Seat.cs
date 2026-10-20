namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Which seat MSFSBA is flying from. The Sovereign+ has four touchscreen controllers — GTC 1 is
/// the pilot's PFD controller, 2 the left MFD controller, 3 the right MFD controller, 4 the
/// copilot's PFD controller — and two PFDs. The seat picks the default unit for each window
/// and which PFD the altimeter and minimums readouts follow. Persisted in UserSettings.C680CrewSeat.
/// </summary>
public static class C680Seat
{
    public enum Side { Pilot = 1, Copilot = 2 }

    public static int GtcIndexFor(Side seat, bool isMfd)
        => isMfd ? (seat == Side.Pilot ? 2 : 3) : (seat == Side.Pilot ? 1 : 4);

    public static int PfdIndex(Side seat) => seat == Side.Pilot ? 1 : 2;

    public static Side Other(Side seat) => seat == Side.Pilot ? Side.Copilot : Side.Pilot;

    public static Side FromSetting(int value) => value == 2 ? Side.Copilot : Side.Pilot;
}
