using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the SD window reads of a push before any page: the bottom line (field 178), and the format letters the app
/// does not know in the SD's channel text. The trust gate decodes neither: the bottom line and the STATUS text are the
/// SD's alone, so the window checks them itself, from the RemoteData message of the snapshot it was given ([FNX-22]).
/// </summary>
internal static class FenixChannelSdFrame
{
    // The STATUS page, the one page that draws the STATUS text (175, 176): FenixChannelSdPages numbers it 14 too, where
    // its constant is private to the page dispatch.
    private const int StatusPage = 14;

    /// <summary>
    /// The bottom line's nine elements, in order, each as coded text ("" for an element without its string). None at all
    /// is missing, "no bottom line"; a count other than nine is "field 178 has the wrong shape", as an FMA of another
    /// count is; an element of another shape is named by its path ("field 178.1 has the wrong shape").
    /// </summary>
    public static FenixChannelSection<IReadOnlyList<string>> BottomLine(FenixChannelPush remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        // Reading the strings reports nothing: a problem of their shape stops the window, and the window reports it.
        FenixChannelSdReader reader = FenixChannelSdReader.Of(remote, _ => { });
        IReadOnlyList<string> elements = reader.Strings(FenixChannelFields.Remote.EcamBottom);

        // The reader words its problems as the factories do; this one is the bottom line's.
        if (reader.Problem is { } problem) return new FenixChannelSection<IReadOnlyList<string>>(null, problem);
        if (elements.Count == 0) return FenixChannelSection<IReadOnlyList<string>>.Missing("bottom line");
        if (elements.Count != FenixChannelFields.EcamBottom.Count)
        {
            return FenixChannelSection<IReadOnlyList<string>>.WrongShape(
                FenixChannelFields.Remote.EcamBottom.ToString(CultureInfo.InvariantCulture));
        }

        return FenixChannelSection<IReadOnlyList<string>>.Ok(elements);
    }

    /// <summary>
    /// The first format letter <see cref="FenixChannelText"/> does not know in the SD's channel text, in the trust gate's
    /// words: "unknown code 'k' in the bottom line", or, while <paramref name="page"/> is the STATUS page, "unknown code
    /// 'k' in the STATUS text" (its left column, then its right). Null when there is none. Every element of the bottom
    /// line is looked at, the four the window does not read too: a letter the app does not know means the SD draws its
    /// line in a way the app cannot tell ([FNX-24]). An element that cannot be read as text is skipped here: its shape is
    /// the bottom line's problem, or the STATUS page's, found when they are read, so only a letter decides this.
    /// </summary>
    public static string? UnknownCode(FenixChannelPush remote, int page)
    {
        ArgumentNullException.ThrowIfNull(remote);

        string? unknown = FirstUnknown(remote, FenixChannelFields.Remote.EcamBottom, "bottom line");
        if (unknown is null && page == StatusPage)
        {
            unknown = FirstUnknown(remote, FenixChannelFields.Remote.StatusLeft, "STATUS text")
                ?? FirstUnknown(remote, FenixChannelFields.Remote.StatusRight, "STATUS text");
        }

        return unknown;
    }

    // The first unknown letter in the elements of one repeated NullWrapped string, worded for where.
    private static string? FirstUnknown(FenixChannelPush remote, int field, string where)
    {
        foreach (FenixChannelField element in remote.All(field))
        {
            if (TextOf(element) is not { } text) continue;

            FenixTextLine line = FenixChannelText.Parse(text);
            if (line.UnknownCodes.Count > 0)
            {
                return FenixChannelSection<IReadOnlyList<string>>.UnknownCode(line.UnknownCodes[0], where).Problem;
            }
        }

        return null;
    }

    // The coded text of one NullWrapped element: its field 1, "" without one, and null when the element is not a message
    // or its field 1 is not a string.
    private static string? TextOf(FenixChannelField element)
    {
        if (element.AsMessage() is not { } wrapped) return null;

        return wrapped.TryGet(FenixChannelFields.NullWrapped.Value, out FenixChannelField value) ? value.AsString() : "";
    }
}
