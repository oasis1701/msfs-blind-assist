using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>A point on BlueBird's EFB, in the tablet's own pixels (its gauge is 1397 wide).</summary>
public sealed record EfbPoint(int X, int Y);

/// <summary>
/// What the 757 EFB code keeps of BlueBird's EFB tablet ("BBOS", release 0.1.0) that no read of the tablet gives it: the chocks'
/// L:var, the names of the weight units the load sheet speaks, the rules for a SimBrief ID, and how a click sequence goes to the
/// agent (<see cref="CoherentBbs757EfbClient"/>). Every control is found where the tablet reader sees it
/// (<see cref="Bbs757EfbScreen"/>) and pressed through <see cref="Bbs757EfbPressRunner"/>, so no table of measured positions is
/// left to go stale when BlueBird moves a button. The one exception is the window's own Home button: the reader offers no control
/// for the home icon, so it is tapped at a point measured once (<see cref="Bbs757EfbScreen.HomeButton"/>).
/// Pure.
/// </summary>
public static class Bbs757EfbLayout
{
    /// <summary>The chocks' L:var (0 removed, 1 in place, measured), also the Status display's "Chocks" row.</summary>
    public const string ChocksVar = "BBS757_vis_chocks";

    // ---- weight units ----------------------------------------------------------

    /// <summary>"pounds" or "kilograms": how the window names a unit system.</summary>
    public static string UnitsName(Bbs757WeightUnits units) =>
        units == Bbs757WeightUnits.Pounds ? "pounds" : "kilograms";

    // ---- SimBrief ID ---------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="text"/> is a SimBrief ID BlueBird can store: ASCII digits only (no
    /// sign, space or separator), 1 to int.MaxValue. Never parsed as a number in the PC's culture;
    /// the digits are read as they are.
    /// </summary>
    public static bool IsSimBriefId(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 10) return false;
        long value = 0;
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c)) return false;
            value = value * 10 + (c - '0');
        }
        return value is >= 1 and <= int.MaxValue;
    }

    /// <summary>The value of a SimBrief ID <see cref="IsSimBriefId"/> accepted.</summary>
    public static long SimBriefIdValue(string digits)
    {
        if (!IsSimBriefId(digits)) throw new ArgumentException("Not a SimBrief ID.", nameof(digits));
        long value = 0;
        foreach (char c in digits) value = value * 10 + (c - '0');
        return value;
    }

    // ---- sequences -------------------------------------------------------------

    /// <summary>
    /// A sequence as the agent's <c>clickSeq</c> takes it: <c>[[x,y,delayAfterMs],…]</c>, integers
    /// written invariant.
    /// </summary>
    public static string ToJson(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < seq.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var (p, delay) = seq[i];
            sb.Append('[')
              .Append(p.X.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(p.Y.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(delay.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>The whole time a sequence takes, every click's delay after it included.</summary>
    public static int TotalMs(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq) => seq.Sum(s => s.DelayAfterMs);
}
