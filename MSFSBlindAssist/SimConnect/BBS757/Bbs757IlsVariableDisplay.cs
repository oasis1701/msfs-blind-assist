using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>The ILS control panel's display as the 757's definition reads it (a fake in the tests).</summary>
public interface IBbs757IlsDisplay : IDisposable
{
    /// <summary>
    /// What the display shows now; null when it could not be read or the read was canceled. Never throws.
    /// </summary>
    Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct);
}

/// <summary>
/// Reads the BlueBird 757's ILS control panel display from BlueBird's own variables: <c>L:BBS_757_ILS_FREQ</c>
/// is -1 while the panel is parked and its frequency otherwise, following each click at once, and
/// <c>L:BBS_ILS_COURSE</c> is the course (measured 2026-11-05, [B757-10]). Both are read fresh
/// (<paramref name="readFresh"/>, by variable key), and <see cref="Bbs757IlsPanel.ReadingFrom"/> says what they show.
/// </summary>
public sealed class Bbs757IlsVariableDisplay(Func<string, CancellationToken, Task<double?>> readFresh) : IBbs757IlsDisplay
{
    public async Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct)
    {
        try
        {
            double? frequency = await readFresh(Bbs757NavRad.IlsFrequencyKey, ct);
            double? course = await readFresh(Bbs757NavRad.IlsCourseKey, ct);
            var reading = Bbs757IlsPanel.ReadingFrom(frequency, course);
            if (reading == null && frequency is double v && double.IsFinite(v) && Math.Abs(v) >= 0.0005 && Math.Abs(v + 1) >= 0.0005)
                Log.Debug("BBS757", $"ILS frequency variable read {v.ToString("0.######", CultureInfo.InvariantCulture)} (invariant), not an ILS channel");
            return reading;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;   // never throws: a read the sim did not answer is no reading
        }
    }

    public void Dispose()
    {
    }
}
