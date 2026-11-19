using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The parking brake when BlueBird's PARKING BRAKE MODE is REALISTIC ("needs full braking"; its setting <c>parking_brake_realistic</c>).
/// Measured 2026-10-28 (FS2020, the 757-200 RR, powered and unpowered): the lever's own toggle, which the map
/// writes and which works in EASY mode, neither sets nor releases the brake there; full braking, <c>K:BRAKES</c>
/// and the lever's new value in ONE calculator string do both, and the brake keeps its new state once the pedals
/// are let go. So a caller writes the plain toggle first, and retries with full braking only when the brake did not
/// move AND BlueBird's own setting (<c>parking_brake_realistic</c> in its work folder) does not say EASY
/// (<see cref="ShouldRetry"/>): in EASY mode the retry never runs. With the setting unreadable a toggle that did not
/// move the brake is taken as REALISTIC, since in EASY mode the toggle moves it.
/// </summary>
public static class Bbs757ParkingBrake
{
    /// <summary>The map control (its state is <c>L:B757_FC_PARK_BRAKE</c>).</summary>
    public const string Node = "BBS757_PEDESTAL_Park_Brake_Lever";

    /// <summary>Full braking and the lever at <paramref name="set"/> in one frame.</summary>
    public static string FullBrakingCode(bool set) =>
        "16383 (>K:AXIS_LEFT_BRAKE_SET) 16383 (>K:AXIS_RIGHT_BRAKE_SET) (>K:BRAKES) "
        + (set ? "1" : "0") + " (>L:B757_FC_PARK_BRAKE,bool)";

    /// <summary>The pedals let go (a sim with no pedal hardware would otherwise keep the brakes held).</summary>
    public const string PedalsReleaseCode = "-16383 (>K:AXIS_LEFT_BRAKE_SET) -16383 (>K:AXIS_RIGHT_BRAKE_SET)";

    /// <summary>The pilot's PARKING BRAKE MODE from BlueBird's work folder: true REALISTIC, false EASY, null unknown.</summary>
    public static bool? ReadRealisticMode() =>
        MSFSBlindAssist.SimConnect.BBS757.Bbs757WorkFolder.Find() is string folder
            ? MSFSBlindAssist.SimConnect.BBS757.Bbs757WorkFolder.ReadParkingBrakeRealistic(folder)
            : null;

    /// <summary>
    /// Whether a toggle that left the brake at <paramref name="after"/> earns the full-braking retry toward
    /// <paramref name="set"/>: only when the brake did not move, the read is known, and the mode is not EASY.
    /// </summary>
    public static bool ShouldRetry(double? after, bool set, bool? realistic)
        => after is double && !Reads(after, set) && realistic != false;

    /// <summary>True when <paramref name="value"/> reads the brake at <paramref name="set"/>.</summary>
    public static bool Reads(double? value, bool set) => value is double v && (v > 0.5) == set;

    /// <summary>
    /// After a plain toggle left the brake where it was: full braking with the lever at <paramref name="set"/>,
    /// the pedals always let go afterwards, then a fresh read. True when the brake reads <paramref name="set"/>.
    /// </summary>
    public static async Task<bool> RetryWithFullBrakingAsync(Bbs757SimIo io, bool set, int readBackDelayMs, CancellationToken ct)
    {
        Log.Debug("BBS757", $"Parking brake: the lever alone did not {(set ? "set" : "release")} it; retrying with full braking (REALISTIC mode).");
        io.Write(FullBrakingCode(set));
        try { await io.Delay(readBackDelayMs, ct); }
        finally { io.Write(PedalsReleaseCode); }
        double? after = await io.ReadFresh(Node, ct);
        if (!Reads(after, set))
            Log.Info("BBS757", $"Parking brake: not {(set ? "set" : "released")} with full braking either, reads " +
                $"{after?.ToString(CultureInfo.InvariantCulture) ?? "nothing"}.");
        return Reads(after, set);
    }
}
