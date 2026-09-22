// MSFSBlindAssist/Aircraft/C172/Cessna172Squawk.cs
namespace MSFSBlindAssist.Aircraft.C172;

/// <summary>
/// Squawk code ↔ BCD16. TRANSPONDER CODE:1 is registered with Units "Bco16" so the sim delivers
/// 0x1200 for squawk 1200 (the HS787 finding: the raw decimal read mis-shifts), and XPNDR_SET takes
/// the same encoding. The panel entry box parses the text to a double first, so a leading zero is
/// lost ("0422" → 422) and is restored by padding to four digits.
/// </summary>
public static class Cessna172Squawk
{
    public const string InvalidMessage = "Invalid squawk code. Four digits, each 0 to 7.";

    public static bool TryToBcd(double typed, out uint bcd, out string error)
    {
        bcd = 0;
        error = "";
        if (double.IsNaN(typed) || typed < 0 || typed > 7777 || typed != Math.Floor(typed))
        {
            error = InvalidMessage;
            return false;
        }
        string digits = ((int)typed).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        foreach (char c in digits)
        {
            if (c < '0' || c > '7') { error = InvalidMessage; return false; }
            bcd = (bcd << 4) | (uint)(c - '0');
        }
        return true;
    }

    /// <summary>The four digits of a Bco16 reading.</summary>
    public static string FromBcd(double bco16)
    {
        int bcd = (int)bco16;
        int d1 = (bcd >> 12) & 0xF, d2 = (bcd >> 8) & 0xF, d3 = (bcd >> 4) & 0xF, d4 = bcd & 0xF;
        return $"{d1}{d2}{d3}{d4}";
    }
}
