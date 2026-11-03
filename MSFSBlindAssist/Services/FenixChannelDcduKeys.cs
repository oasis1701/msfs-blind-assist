using Keys = System.Windows.Forms.Keys;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The nine keys of the captain's DCDU: its four soft keys, named by position, then the page, message and print keys.
/// </summary>
internal enum FenixDcduKey
{
    LeftKey1,
    LeftKey2,
    RightKey1,
    RightKey2,
    PageMinus,
    PagePlus,
    MessageMinus,
    MessagePlus,
    Print
}

/// <summary>
/// What a key press comes to: the variable to press, or why nothing is pressed. Exactly one is set.
/// </summary>
/// <param name="VariableKey">The key of the Fenix button variable to press; null when the press is refused.</param>
/// <param name="Refusal">What the window says when the press is refused; null when the key presses.</param>
internal readonly record struct FenixDcduDecision(string? VariableKey, string? Refusal);

/// <summary>
/// The DCDU window's keys: which chord is which key, which Fenix button each one presses, and whether a press goes
/// ahead. Pure logic over <see cref="FenixChannelDcdu.Read"/>; the window sends what <see cref="Decide"/> says and nothing
/// else ([FNX-21], [A320-3]).
/// </summary>
/// <remarks>
/// <para>The chords are the A32NX DCDU window's. The four soft keys are Ctrl+1 and Ctrl+2 (left) and Alt+1 and Alt+2
/// (right), or F1 and F2 and F7 and F8 when the alternate soft keys are on, and then the other set maps to nothing.
/// PageDown and PageUp are page plus and minus and Ctrl+PageDown and Ctrl+PageUp message plus and minus (DOWN is
/// forward), Ctrl+P prints. A chord must match exactly, so the same key with another modifier is nothing.</para>
/// <para>A soft key acts on the label it has now, so <see cref="Decide"/> is given the snapshot taken at the press and
/// keeps nothing between presses: a message arriving after the last refresh relabels a slot. The page, message and print
/// keys never depend on what the app reads, so they press whatever the DCDU shows.</para>
/// </remarks>
internal static class FenixChannelDcduKeys
{
    /// <summary>What the window says for a soft key whose label is blank: nothing is pressed.</summary>
    public const string NoActionMessage = "No action on that key.";

    /// <summary>What the window says for a soft key while the DCDU cannot be read: nothing is pressed.</summary>
    public const string NotAvailableMessage = "DCDU not available";

    /// <summary>
    /// The key <paramref name="keyData"/> (the key with its modifiers, as a key event gives it) is, or null when it is no
    /// DCDU key. <paramref name="alternateLskKeys"/> is the shared alternate soft keys setting.
    /// </summary>
    public static FenixDcduKey? Map(Keys keyData, bool alternateLskKeys)
    {
        if (alternateLskKeys)
        {
            switch (keyData)
            {
                case Keys.F1: return FenixDcduKey.LeftKey1;
                case Keys.F2: return FenixDcduKey.LeftKey2;
                case Keys.F7: return FenixDcduKey.RightKey1;
                case Keys.F8: return FenixDcduKey.RightKey2;
            }
        }
        else
        {
            switch (keyData)
            {
                case Keys.Control | Keys.D1: return FenixDcduKey.LeftKey1;
                case Keys.Control | Keys.D2: return FenixDcduKey.LeftKey2;
                case Keys.Alt | Keys.D1: return FenixDcduKey.RightKey1;
                case Keys.Alt | Keys.D2: return FenixDcduKey.RightKey2;
            }
        }

        return keyData switch
        {
            Keys.PageDown => FenixDcduKey.PagePlus,
            Keys.PageUp => FenixDcduKey.PageMinus,
            Keys.Control | Keys.PageDown => FenixDcduKey.MessagePlus,
            Keys.Control | Keys.PageUp => FenixDcduKey.MessageMinus,
            Keys.Control | Keys.P => FenixDcduKey.Print,
            _ => null
        };
    }

    /// <summary>
    /// The Fenix button variable <paramref name="key"/> presses: each soft key by the position of its label's field, and
    /// the page, message and print keys by the part of the cockpit they are (the variable names are the model's, in which
    /// "PGDN" is page plus and "MSGUP" message plus).
    /// </summary>
    public static string VariableOf(FenixDcduKey key) => key switch
    {
        FenixDcduKey.LeftKey1 => "S_DCDU1_LSK1L",
        FenixDcduKey.LeftKey2 => "S_DCDU1_LSK2L",
        FenixDcduKey.RightKey1 => "S_DCDU1_LSK1R",
        FenixDcduKey.RightKey2 => "S_DCDU1_LSK2R",
        FenixDcduKey.PagePlus => "S_DCDU1_PGDN",
        FenixDcduKey.PageMinus => "S_DCDU1_PGUP",
        FenixDcduKey.MessagePlus => "S_DCDU1_MSGUP",
        FenixDcduKey.MessageMinus => "S_DCDU1_MSGDWN",
        FenixDcduKey.Print => "S_DCDU1_PRINT",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };

    /// <summary>Whether <paramref name="key"/> is one of the four soft keys, whose press depends on its label.</summary>
    public static bool IsSoftKey(FenixDcduKey key)
        => key is FenixDcduKey.LeftKey1 or FenixDcduKey.LeftKey2 or FenixDcduKey.RightKey1 or FenixDcduKey.RightKey2;

    /// <summary>Whether <paramref name="key"/> is a page or a message key, the keys that move through the messages.</summary>
    public static bool IsNavigation(FenixDcduKey key)
        => key is FenixDcduKey.PageMinus or FenixDcduKey.PagePlus or FenixDcduKey.MessageMinus or FenixDcduKey.MessagePlus;

    /// <summary>
    /// Whether the press of <paramref name="key"/> goes ahead, judged on <paramref name="s"/>, the snapshot taken at the
    /// press. A soft key presses its variable when the DCDU reads and the key's label speaks something; it is refused
    /// with <see cref="NotAvailableMessage"/> when the DCDU cannot be read (the display is not trusted, the content is
    /// missing or in another shape, or a line has a format letter the app does not know) and with
    /// <see cref="NoActionMessage"/> when the label is blank. The page, message and print keys always press.
    /// </summary>
    public static FenixDcduDecision Decide(FenixDcduKey key, FenixChannelSnapshot s)
    {
        if (!IsSoftKey(key)) return new FenixDcduDecision(VariableOf(key), null);

        FenixChannelSection<FenixDcduScreen> section = FenixChannelDcdu.Read(s);
        if (!section.IsOk) return new FenixDcduDecision(null, NotAvailableMessage);

        FenixDcduScreen screen = section.Value!;
        string label = key switch
        {
            FenixDcduKey.LeftKey1 => screen.LeftKey1,
            FenixDcduKey.LeftKey2 => screen.LeftKey2,
            FenixDcduKey.RightKey1 => screen.RightKey1,
            FenixDcduKey.RightKey2 => screen.RightKey2,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

        return FenixChannelSpeech.Line(label) is null
            ? new FenixDcduDecision(null, NoActionMessage)
            : new FenixDcduDecision(VariableOf(key), null);
    }
}
