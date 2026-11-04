using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>One of the ISIS's bugs (182.22): whether it is enabled, and its value.</summary>
internal readonly record struct FenixIsisBug(bool Enabled, double Value);

/// <summary>
/// What the ISIS draws, read from field 182 (IsisState) of a push's RemoteData message. Every value is as sent: a flag or
/// a number the push left out is false or 0, as protobuf-net leaves a default out, and a countdown left out is null.
/// </summary>
/// <param name="Initializing">The ISIS is initializing (182.2).</param>
/// <param name="Standard">The baro is at STD (182.3); the ISIS keeps its QNH meanwhile.</param>
/// <param name="LsShown">The LS scales are shown (182.4, App).</param>
/// <param name="InBugsPage">The bugs page is shown (182.5).</param>
/// <param name="HectoPascal">The baro is in hectopascals (182.6); false is inches.</param>
/// <param name="LocFailed">The localizer has failed (182.7).</param>
/// <param name="GsFailed">The glideslope has failed (182.8).</param>
/// <param name="NewStyle">The new-style ISIS is drawn (182.9).</param>
/// <param name="Pitch">The pitch (182.10).</param>
/// <param name="Bank">The bank (182.11).</param>
/// <param name="Airspeed">The airspeed (182.12).</param>
/// <param name="Altitude">The altitude (182.13).</param>
/// <param name="Mach">The Mach (182.14).</param>
/// <param name="AlignSeconds">The length of the alignment in seconds (182.16, 90, constant while it lasts); null when none
/// is running.</param>
/// <param name="RealignSeconds">The length of the quick realignment in seconds (182.17, 10, constant while it lasts); null
/// when none is running.</param>
/// <param name="Qnh">The QNH (182.18), always in hPa: the ISIS converts it while 182.6 is clear.</param>
/// <param name="LocDots">The localizer deviation (182.19); NaN while not drawn.</param>
/// <param name="GsDots">The glideslope deviation (182.20); NaN while not drawn.</param>
/// <param name="SelectedBug">The selected bug (182.21).</param>
/// <param name="Bugs">The bugs (182.22), in the order sent.</param>
/// <param name="Slip">The slip (182.23).</param>
internal sealed record FenixIsisState(
    bool Initializing, bool Standard, bool LsShown, bool InBugsPage, bool HectoPascal, bool LocFailed, bool GsFailed,
    bool NewStyle, double Pitch, double Bank, double Airspeed, double Altitude, double Mach, int? AlignSeconds,
    int? RealignSeconds, double Qnh, double LocDots, double GsDots, int SelectedBug, IReadOnlyList<FenixIsisBug> Bugs,
    double Slip);

/// <summary>
/// The decoder of the ISIS from the Fenix display channel. Pure: it reads a RemoteData message and nothing else.
/// </summary>
/// <remarks>
/// What stands between a wrong value and a pilot is the build check, the live condition (182.1, the trust gate's), the
/// shape of every member read here (the first in another wire type than the contract gives makes the whole section a
/// wrong shape, named by its path) and the content condition: a 182 that carries neither a baro setting above 0 nor a
/// phase length (182.16 or 182.17) is "no ISIS data". A shape problem is reported before missing content, since a damaged
/// part is what hides the content. 182.1 (the gate's) and 182.15 (Brightness) are not read here. 182.16 and 182.17 hold
/// the lengths of the alignment and the quick realignment (90 and 10), constant while each lasts, not a countdown
/// (measured 2026-11-04); the record names them AlignSeconds and RealignSeconds for that length.
/// </remarks>
internal static class FenixChannelIsis
{
    /// <summary>What the Fenix windows list says, and opens nothing, on a Fenix with the round standby gauges.</summary>
    public const string NoIsisSentence = "This Fenix has no ISIS.";

    /// <summary>
    /// Whether Alt+I may open the ISIS window on <paramref name="variant"/>: not on a Fenix with the round standby gauges
    /// (<c>HasIsis</c> false), which keeps the AI read with its round-gauge prompt. A standby not yet read leaves it to the
    /// ISIS verdict, as [FNX-11] leaves a control whose field is unread visible.
    /// </summary>
    public static bool KeyMayOpen(Aircraft.FenixVariant variant) => variant.HasIsis != false;

    /// <summary>
    /// What the windows list's ISIS entry says instead of opening the window on <paramref name="variant"/>:
    /// <see cref="NoIsisSentence"/> with the round standby gauges, else null (the entry opens the window).
    /// </summary>
    public static string? RefusalFor(Aircraft.FenixVariant variant) => variant.HasIsis == false ? NoIsisSentence : null;

    /// <summary>The ISIS of the RemoteData message <paramref name="remote"/>.</summary>
    public static FenixChannelSection<FenixIsisState> Decode(FenixChannelPush remote)
    {
        if (!remote.TryGet(Remote.Isis, out FenixChannelField isisField))
        {
            return FenixChannelSection<FenixIsisState>.Missing("ISIS data");
        }

        string root = Number(Remote.Isis);
        FenixChannelPush? isis = isisField.AsMessage();
        if (isis is null) return FenixChannelSection<FenixIsisState>.WrongShape(root);

        // The path of the first member found in another wire shape; null while every member read had its shape.
        string? wrong = null;

        var bugs = new List<FenixIsisBug>();
        foreach (FenixChannelField entry in isis.All(IsisState.Bugs))
        {
            FenixChannelPush? bug = entry.AsMessage();
            if (bug is null)
            {
                wrong ??= $"{root}.{Number(IsisState.Bugs)}";
                continue;
            }

            string bugPath = $"{root}.{Number(IsisState.Bugs)}";
            bugs.Add(new FenixIsisBug(FlagOf(bug, bugPath, BugState.Enabled), DoubleOf(bug, bugPath, BugState.Value)));
        }

        var state = new FenixIsisState(
            Initializing: FlagOf(isis, root, IsisState.Initializing),
            Standard: FlagOf(isis, root, IsisState.Standard),
            LsShown: FlagOf(isis, root, IsisState.App),
            InBugsPage: FlagOf(isis, root, IsisState.InBugsPage),
            HectoPascal: FlagOf(isis, root, IsisState.HectoPascal),
            LocFailed: FlagOf(isis, root, IsisState.LocFailed),
            GsFailed: FlagOf(isis, root, IsisState.GsFailed),
            NewStyle: FlagOf(isis, root, IsisState.NewStyle),
            Pitch: DoubleOf(isis, root, IsisState.Pitch),
            Bank: DoubleOf(isis, root, IsisState.Bank),
            Airspeed: DoubleOf(isis, root, IsisState.Airspeed),
            Altitude: DoubleOf(isis, root, IsisState.Altitude),
            Mach: DoubleOf(isis, root, IsisState.Mach),
            AlignSeconds: CountOf(isis, root, IsisState.Init),
            RealignSeconds: CountOf(isis, root, IsisState.QuickRealign),
            Qnh: DoubleOf(isis, root, IsisState.Qnh),
            LocDots: DoubleOf(isis, root, IsisState.LocDots),
            GsDots: DoubleOf(isis, root, IsisState.GsDots),
            SelectedBug: CountOf(isis, root, IsisState.SelectedBug) ?? 0,
            Bugs: bugs.AsReadOnly(),
            Slip: DoubleOf(isis, root, IsisState.Slip));

        if (wrong is not null) return FenixChannelSection<FenixIsisState>.WrongShape(wrong);

        // NaN is not above 0, so a NaN baro is no baro setting.
        bool hasContent = state.Qnh > 0 || state.AlignSeconds is not null || state.RealignSeconds is not null;
        if (!hasContent) return FenixChannelSection<FenixIsisState>.Missing("ISIS data");

        return FenixChannelSection<FenixIsisState>.Ok(state);

        // ---- The reads. A member left out answers its default; one in another shape records its path, the first
        // only, and reads as left out.

        bool FlagOf(FenixChannelPush part, string path, int number)
        {
            if (!part.TryGet(number, out FenixChannelField field)) return false;

            if (field.Type != FenixWireType.Varint)
            {
                wrong ??= $"{path}.{Number(number)}";
                return false;
            }

            return field.AsBool();
        }

        double DoubleOf(FenixChannelPush part, string path, int number)
        {
            if (!part.TryGet(number, out FenixChannelField field)) return 0;

            if (field.Type != FenixWireType.Fixed64)
            {
                wrong ??= $"{path}.{Number(number)}";
                return 0;
            }

            return field.AsDouble();
        }

        // An int32 as a varint (a negative one sign-extended to ten bytes), or null when left out: a Nullable<int32>
        // that is null, or a plain int32 at its default, which the caller reads as 0.
        int? CountOf(FenixChannelPush part, string path, int number)
        {
            if (!part.TryGet(number, out FenixChannelField field)) return null;

            if (field.Type != FenixWireType.Varint)
            {
                wrong ??= $"{path}.{Number(number)}";
                return null;
            }

            return unchecked((int)(long)field.Varint);
        }
    }

    private static string Number(int field) => field.ToString(CultureInfo.InvariantCulture);
}
