using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the captain's DCDU draws, read from the RemoteData message of a push: the header, five message lines, three info
/// lines and the four soft key labels. Every line is channel text as sent, with its format letters, and "" when the
/// push left it out. <see cref="FenixChannelDcdu.Decode"/> has checked that no line has a letter the app does not know
/// (the answer letter "i" is known on the DCDU), so a reader can parse each with <see cref="FenixChannelText"/>, or take
/// its words with <see cref="FenixChannelDcdu.Plain"/>; a key label's ">" is added by the reader, not here.
/// </summary>
/// <param name="HeaderLeft">The left half of the header (2).</param>
/// <param name="HeaderRight">The right half of the header (3).</param>
/// <param name="Message">The five message lines, top to bottom (4 to 8).</param>
/// <param name="Info">The three info lines, top to bottom (9 to 11).</param>
/// <param name="LeftKey1">The label of left key 1 (12).</param>
/// <param name="LeftKey2">The label of left key 2 (13).</param>
/// <param name="RightKey1">The label of right key 1 (14).</param>
/// <param name="RightKey2">The label of right key 2 (15).</param>
internal sealed record FenixDcduScreen(
    string HeaderLeft, string HeaderRight, IReadOnlyList<string> Message, IReadOnlyList<string> Info,
    string LeftKey1, string LeftKey2, string RightKey1, string RightKey2);

/// <summary>
/// Reads the captain's DCDU from the RemoteData message of a push. Never the first officer's side. The first problem
/// found is the one given: missing content, then a field in the wrong shape, then a format letter the app does not know
/// ([FNX-24]).
/// </summary>
/// <remarks>
/// A line the push left out reads as protobuf-net leaves it, "" (it writes nothing for a null string). The print frame
/// (186.1) and every other member of the part are not read.
/// </remarks>
internal static class FenixChannelDcdu
{
    /// <summary>What the window and the log call the display.</summary>
    public const string DisplayName = "DCDU";

    /// <summary>
    /// The DCDU of <paramref name="push"/>, the RemoteData message. Every line is checked for its wire shape and as valid
    /// UTF-8; the first one in another shape gives a section that names it by its path ("186.4"), and so does a field 186
    /// that is not a valid message ("186"). A push with no field 186 is missing the DCDU's content. The lines are checked
    /// last: the first format letter <see cref="FenixChannelText"/> does not know, in the header, the message, the info
    /// lines or the key labels, makes the section an unknown code that names the part.
    /// </summary>
    public static FenixChannelSection<FenixDcduScreen> Decode(FenixChannelPush push)
    {
        if (!push.TryGet(Remote.DcduCapt, out FenixChannelField field))
        {
            return FenixChannelSection<FenixDcduScreen>.Missing("DCDU content");
        }

        FenixChannelPush? content = field.AsMessage();
        if (content is null) return FenixChannelSection<FenixDcduScreen>.WrongShape(FormattableString.Invariant($"{Remote.DcduCapt}"));

        // The path of the first line found in another wire shape; null while every line read had its shape.
        string? wrong = null;

        var screen = new FenixDcduScreen(
            TextOf(DcduContent.HeaderLeft), TextOf(DcduContent.HeaderRight),
            [TextOf(DcduContent.Msg1), TextOf(DcduContent.Msg2), TextOf(DcduContent.Msg3), TextOf(DcduContent.Msg4),
                TextOf(DcduContent.Msg5)],
            [TextOf(DcduContent.Info1), TextOf(DcduContent.Info2), TextOf(DcduContent.Info3)],
            TextOf(DcduContent.Lsk1L), TextOf(DcduContent.Lsk2L), TextOf(DcduContent.Lsk1R), TextOf(DcduContent.Lsk2R));

        if (wrong is not null) return FenixChannelSection<FenixDcduScreen>.WrongShape(wrong);

        foreach ((string text, string place) in TextsOf(screen))
        {
            FenixTextLine line = FenixChannelText.Parse(text);
            if (line.UnknownCodes.Count > 0) return FenixChannelSection<FenixDcduScreen>.UnknownCode(line.UnknownCodes[0], place);
        }

        return FenixChannelSection<FenixDcduScreen>.Ok(screen);

        // A line is "" when the part did not send it, and the first one in another shape records its path and reads as "".
        string TextOf(int number)
        {
            if (!content.TryGet(number, out FenixChannelField line)) return "";

            string? text = line.AsString();
            if (text is null) wrong ??= FormattableString.Invariant($"{Remote.DcduCapt}.{number}");
            return text ?? "";
        }
    }

    /// <summary>
    /// The DCDU of <paramref name="s"/>, the snapshot the verdict and the message were taken from together: when the
    /// verdict does not trust the display, its reason is the problem; a trusted display with no message is missing
    /// the DCDU's content; otherwise the message decoded by <see cref="Decode"/>.
    /// </summary>
    public static FenixChannelSection<FenixDcduScreen> Read(FenixChannelSnapshot s)
    {
        if (!s.Verdict.Trusted) return new FenixChannelSection<FenixDcduScreen>(default, s.Verdict.Reason);
        if (s.Push is null) return FenixChannelSection<FenixDcduScreen>.Missing("DCDU content");

        return Decode(s.Push);
    }

    /// <summary>
    /// A line's text as the DCDU window and its announcements give it: the format codes dropped, with no color named, "!"
    /// a space (as <see cref="FenixChannelSpeech"/> has it) and the end trimmed. The spacing the Fenix drew inside the
    /// line, and at its start, is kept: the window lays a row out as the display does. Empty exactly when the line has
    /// nothing to read, so a blank line, a blank key label and a blank status are all "".
    /// </summary>
    public static string Plain(string? coded)
        => FenixChannelText.Parse(coded).JoinedText.Replace('!', ' ').TrimEnd();

    /// <summary>
    /// <see cref="Plain"/> for a sentence: runs of white space made one space and both ends trimmed, so that the Fenix's
    /// column padding is not heard.
    /// </summary>
    public static string Spoken(string? coded) => FenixChannelText.CollapseSpaces(Plain(coded));

    // Each line of the DCDU with the part a pilot would call it, in the order the first unknown letter is looked for.
    private static IEnumerable<(string Text, string Place)> TextsOf(FenixDcduScreen screen)
    {
        yield return (screen.HeaderLeft, "DCDU header");
        yield return (screen.HeaderRight, "DCDU header");
        foreach (string line in screen.Message)
        {
            yield return (line, "DCDU message");
        }

        foreach (string line in screen.Info)
        {
            yield return (line, "DCDU info lines");
        }

        yield return (screen.LeftKey1, "DCDU key labels");
        yield return (screen.LeftKey2, "DCDU key labels");
        yield return (screen.RightKey1, "DCDU key labels");
        yield return (screen.RightKey2, "DCDU key labels");
    }
}
