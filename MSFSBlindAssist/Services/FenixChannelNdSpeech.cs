namespace MSFSBlindAssist.Services;

/// <summary>
/// The ND's own speech for channel text: a color is spoken only where it tells the pilot something (the owner's
/// decision, sitting 3). Amber and red are always spoken; any other color is spoken only when it is not one of the
/// usual colors of the line's field. A field with no measured usual set (<c>null</c>) speaks every color, as
/// <see cref="FenixChannelSpeech"/> does, and yellow follows that class: it has no name yet, so it is never spoken.
/// Plan 01's <see cref="FenixChannelSpeech"/> is shared with the other windows and is not changed.
/// </summary>
/// <remarks>
/// <para>The usual sets were measured over every push of sittings 1 and 2 (about 8,000); a stretch with no color
/// letter counts as white, as plan 01's rule reads leading colorless text.</para>
/// <para>Parsing is plan 01's: <see cref="FenixChannelText.Parse"/>, adjacent same-color stretches merged, empty
/// stretches skipped, <c>!</c> a space, sizes, box and flash never spoken. A line with a letter the parser does not
/// know is never spoken (null). Stretches whose color is not spoken are joined with one space, spaces collapsed and
/// trimmed, no space before "°". A stretch whose color is spoken is "words color", separated from its neighbors by
/// ", "; a line that is one such stretch reads "words, color".</para>
/// </remarks>
internal static class FenixChannelNdSpeech
{
    private static IReadOnlySet<FenixTextColor> Set(params FenixTextColor[] colors) => new HashSet<FenixTextColor>(colors);

    /// <summary>62, the center message: white.</summary>
    public static readonly IReadOnlySet<FenixTextColor> CenterMessage = Set(FenixTextColor.White);

    /// <summary>68.1 to 68.3, the top right waypoint box: white, green, cyan.</summary>
    public static readonly IReadOnlySet<FenixTextColor> Waypoint =
        Set(FenixTextColor.White, FenixTextColor.Green, FenixTextColor.Cyan);

    /// <summary>70.1 to 70.3, the ROSE VOR block: white, cyan.</summary>
    public static readonly IReadOnlySet<FenixTextColor> VorBlock = Set(FenixTextColor.White, FenixTextColor.Cyan);

    /// <summary>72.1 to 72.3, the ROSE LS block: white, magenta, cyan.</summary>
    public static readonly IReadOnlySet<FenixTextColor> LsBlock =
        Set(FenixTextColor.White, FenixTextColor.Magenta, FenixTextColor.Cyan);

    /// <summary>76.x.1 to 76.x.3, the corners: white, green, cyan.</summary>
    public static readonly IReadOnlySet<FenixTextColor> Corner =
        Set(FenixTextColor.White, FenixTextColor.Green, FenixTextColor.Cyan);

    /// <summary>102.1.x, the corner flags: none (they are red, and red is always spoken).</summary>
    public static readonly IReadOnlySet<FenixTextColor> CornerFlag = Set();

    /// <summary>102.2, the approach label: green.</summary>
    public static readonly IReadOnlySet<FenixTextColor> ApproachLabel = Set(FenixTextColor.Green);

    /// <summary>103, the listed messages: white.</summary>
    public static readonly IReadOnlySet<FenixTextColor> ListedMessage = Set(FenixTextColor.White);

    /// <summary>163.1 and 163.2, ground speed and wind: white, green.</summary>
    public static readonly IReadOnlySet<FenixTextColor> GroundWind = Set(FenixTextColor.White, FenixTextColor.Green);

    /// <summary>
    /// One coded text spoken with only the colors that tell something. <paramref name="usual"/> is the field's usual
    /// colors, or null for a field with none measured (every color spoken). Null for a null or blank text and for one
    /// with a letter the app does not know.
    /// </summary>
    public static string? Line(string? coded, IReadOnlySet<FenixTextColor>? usual)
    {
        FenixTextLine parsed = FenixChannelText.Parse(coded);
        if (parsed.UnknownCodes.Count > 0) return null;

        // A line that holds a color speaks its leading colorless text as white (plan 01's rule).
        bool holdsColor = parsed.Runs.Any(run => run.Color is not null);
        bool seenColor = false;
        var stretches = new List<(string Text, FenixTextColor? Color)>();
        foreach (FenixTextRun run in parsed.Runs)
        {
            seenColor |= run.Color is not null;
            FenixTextColor? color = holdsColor && !seenColor ? FenixTextColor.White : run.Color;
            string text = run.Text.Replace('!', ' ');
            if (text.Length == 0) continue;

            if (string.IsNullOrWhiteSpace(text))
            {
                if (stretches.Count > 0) stretches[^1] = (stretches[^1].Text + " ", stretches[^1].Color);
                continue;
            }

            if (stretches.Count > 0 && stretches[^1].Color == color)
            {
                stretches[^1] = (stretches[^1].Text + text, color);
            }
            else
            {
                stretches.Add((text, color));
            }
        }

        if (stretches.Count == 0) return null;

        // Groups: a run of stretches whose color is not spoken is one group; a spoken-color stretch is its own.
        var groups = new List<(string Words, string Name)>();
        foreach ((string text, FenixTextColor? color) in stretches)
        {
            string name = SpokenName(color, usual);
            if (name.Length == 0 && groups.Count > 0 && groups[^1].Name.Length == 0)
            {
                groups[^1] = (groups[^1].Words + " " + text, "");
            }
            else
            {
                groups.Add((text, name));
            }
        }

        if (groups.Count == 1)
        {
            string words = Tidy(groups[0].Words);
            return groups[0].Name.Length == 0 ? words : $"{words}, {groups[0].Name}";
        }

        return string.Join(", ", groups.Select(group =>
        {
            string words = Tidy(group.Words);
            return group.Name.Length == 0 ? words : $"{words} {group.Name}";
        }));
    }

    // The color's name when it is spoken, "" when it is not.
    private static string SpokenName(FenixTextColor? color, IReadOnlySet<FenixTextColor>? usual)
    {
        if (color is not { } named) return "";

        bool spoken = usual is null || named is FenixTextColor.Amber or FenixTextColor.Red || !usual.Contains(named);
        return spoken ? FenixChannelSpeech.ColorName(named) : "";
    }

    // Runs of white space become one space, the ends are trimmed, and no space stands before a degree sign.
    private static string Tidy(string text) => FenixChannelText.CollapseSpaces(text).Replace(" °", "°", StringComparison.Ordinal);
}
