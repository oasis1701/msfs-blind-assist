using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757's squawk entry: a typed four-digit code in the TCAS and transponder panel, sent as the
/// stock <c>K:XPNDR_SET</c> in BCD (measured: 0x4743 set 4743; a decimal 4743 set 1207) and read
/// back from the stock <c>TRANSPONDER CODE:1</c> (BCO16). The code is spoken as one number, "Squawk
/// 4743" (the owner's rule on every aircraft). Pure.
/// </summary>
public static class Bbs757Squawk
{
    /// <summary>The panel's text field: a key containing "_SET" is what MainForm renders as a box that Enter applies.</summary>
    public const string SetKey = "BBS757_SQUAWK_SET";

    /// <summary>The read-back row, <c>TRANSPONDER CODE:1</c> in BCO16, shown in the panel's status display.</summary>
    public const string CodeKey = "BBS757_SQUAWK";

    public const string InvalidMessage = "Squawk must be four digits, each 0 to 7.";
    public const string EmptyMessage = "Type a four-digit squawk first.";

    /// <summary>
    /// The squawk box's own text, the one squawk validator (the definition takes it before MainForm parses it): one to four ASCII
    /// digits 0 to 7, padded to four ("421" is 0421). An empty box says to type a code first; anything
    /// else (letters, a sign, a decimal, a fifth digit, an 8 or a 9) gets the digits message.
    /// </summary>
    public static bool TryParseText(string? typed, out string code, out string error)
    {
        code = "";
        error = "";
        string text = (typed ?? "").Trim();
        if (text.Length == 0) { error = EmptyMessage; return false; }
        if (text.Length > 4 || !text.All(c => c is >= '0' and <= '7'))
        {
            error = InvalidMessage;
            return false;
        }
        code = text.PadLeft(4, '0');
        return true;
    }

    /// <summary>The code as BCD, one digit per nibble: "4743" → 0x4743.</summary>
    public static int Bcd(string code)
    {
        int bcd = 0;
        foreach (char c in code) bcd = (bcd << 4) | (c - '0');
        return bcd;
    }

    /// <summary>The calculator string that sets the transponder: "18243 (>K:XPNDR_SET)" for 4743.</summary>
    public static string Write(string code) => $"{Bcd(code).ToString(CultureInfo.InvariantCulture)} (>K:XPNDR_SET)";

    /// <summary><c>TRANSPONDER CODE:1</c> in BCO16, one octal digit per nibble: 0x4743 → "4743".</summary>
    public static string Decode(double bco16)
    {
        int w = (int)Math.Round(bco16);
        return $"{(w >> 12) & 0xF}{(w >> 8) & 0xF}{(w >> 4) & 0xF}{w & 0xF}";
    }

    /// <summary>
    /// What a set says once the transponder was read back: "Squawk 4743"; "Squawk 4743 entered, the
    /// transponder reads 1200" when it holds another code; "Squawk 4743 sent, the transponder did not
    /// report back" when it could not be read.
    /// </summary>
    public static string Confirmation(string requested, double? readBack)
    {
        if (readBack is not double v || !double.IsFinite(v)) return $"Squawk {requested} sent, the transponder did not report back";
        string now = Decode(v);
        return now == requested ? $"Squawk {requested}" : $"Squawk {requested} entered, the transponder reads {now}";
    }
}
