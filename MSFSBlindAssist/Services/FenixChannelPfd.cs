using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The baro settings of the captain's EFIS panel, as the PFD's baro box and altitude tape follow them.
/// </summary>
/// <param name="Std">The baro is at standard (QnhStd).</param>
/// <param name="Inches">The reference is shown in inches of mercury (QnhInches); false is hectopascals.</param>
/// <param name="Qfe">The QFE mode is on (QfeMode).</param>
/// <param name="QnhHpa">The QNH in hectopascals (Qnh): 1013 when the field is absent, its contract default.</param>
/// <param name="QnhInHg">The QNH in inches of mercury as the Fenix itself shows it (QnhInch): NaN when absent.</param>
internal readonly record struct FenixPfdBaro(bool Std, bool Inches, bool Qfe, double QnhHpa, double QnhInHg);

/// <summary>
/// What the captain's PFD draws, read from the RemoteData message of a push: the air data, which of it the PFD shows,
/// the speed tape's limits and marks, the FCU speed target, the Mach, radio altitude, LS and marker texts, the
/// deviations, the altitude readout's metric flag, the DH message, the EFIS settings and the magnetic track. The
/// heading is the frame's.
/// </summary>
/// <remarks>
/// <para>Everything here is the captain's side: the first EFIS entry is the captain's panel, and the first officer's
/// air data and texts are not read. The baro reference comes from the EFIS entry and never from the baro text (field
/// 105): the entry holds the setting as numbers, and the text only draws it.</para>
/// <para>A value the push left out reads as protobuf-net leaves it: the air data, the trend and the track are 0, a
/// text is "", a flag is false, and the QNH is 1013. A speed or the speed target that is absent is NaN, the same as one
/// the PFD does not draw, since 0 is none of them. A deviation is different: the channel sends NaN for a scale the PFD
/// does not draw, so one that is absent from the deviations part is a real 0.0, a centered scale. With the part itself
/// absent both deviations are NaN.</para>
/// </remarks>
internal sealed record FenixChannelPfd
{
    /// <summary>The pitch (28.1); 0 when absent.</summary>
    public double Pitch { get; init; }

    /// <summary>The bank (28.2); 0 when absent.</summary>
    public double Bank { get; init; }

    /// <summary>The PFD's altitude, which follows the baro setting (28.4); 0 when absent.</summary>
    public double Altitude { get; init; }

    /// <summary>The indicated airspeed (28.5); 0 when absent.</summary>
    public double Airspeed { get; init; }

    /// <summary>The vertical speed (28.6), in the unit the Fenix sends; 0 when absent.</summary>
    public double VerticalSpeed { get; init; }

    /// <summary>The PFD draws the attitude (46.1.1); false is the ATT flag.</summary>
    public bool AttitudeShown { get; init; }

    /// <summary>The PFD draws the heading (46.1.2); false is the HDG flag.</summary>
    public bool HeadingShown { get; init; }

    /// <summary>The PFD draws the airspeed (46.2.1); false is the SPD flag.</summary>
    public bool SpeedShown { get; init; }

    /// <summary>The PFD draws the altitude (46.2.2); false is the ALT flag.</summary>
    public bool AltitudeShown { get; init; }

    /// <summary>The PFD draws the vertical speed (46.8); false is the V/S flag.</summary>
    public bool VerticalSpeedShown { get; init; }

    /// <summary>VMAX (125.1); NaN when not drawn.</summary>
    public double VMax { get; init; } = double.NaN;

    /// <summary>VLS (125.2); NaN when not drawn.</summary>
    public double Vls { get; init; } = double.NaN;

    /// <summary>Alpha prot (125.3); NaN when not drawn.</summary>
    public double AlphaProt { get; init; } = double.NaN;

    /// <summary>VSW (125.5), a number in alternate law; NaN when not drawn.</summary>
    public double Vsw { get; init; } = double.NaN;

    /// <summary>V1 (127.1); NaN when not drawn.</summary>
    public double V1 { get; init; } = double.NaN;

    /// <summary>V2 (127.2); NaN when not drawn.</summary>
    public double V2 { get; init; } = double.NaN;

    /// <summary>VR (127.3); NaN when not drawn.</summary>
    public double Vr { get; init; } = double.NaN;

    /// <summary>The F speed, the flap retraction speed (127.4); NaN when not drawn.</summary>
    public double F { get; init; } = double.NaN;

    /// <summary>The S speed, the slat retraction speed (127.5); NaN when not drawn.</summary>
    public double S { get; init; } = double.NaN;

    /// <summary>The green dot speed (127.6); NaN when not drawn.</summary>
    public double GreenDot { get; init; } = double.NaN;

    /// <summary>VFE next (127.7); NaN when not drawn.</summary>
    public double VfeNext { get; init; } = double.NaN;

    /// <summary>The speed target the FCU sends (113.4); NaN when absent.</summary>
    public double TargetIas { get; init; } = double.NaN;

    /// <summary>The speed target is managed (113.9).</summary>
    public bool SpeedManaged { get; init; }

    /// <summary>The speed bugs are visible (113.5).</summary>
    public bool BugsVisible { get; init; }

    /// <summary>The speed trend (98); 0 when absent.</summary>
    public double SpeedTrend { get; init; }

    /// <summary>The Mach line, channel text (107); "" when absent.</summary>
    public string MachText { get; init; } = "";

    /// <summary>The radio altitude, channel text (157); "" when absent.</summary>
    public string RadioAltitudeText { get; init; } = "";

    /// <summary>The LS identifier, channel text (74.1); "" when absent.</summary>
    public string LsIdent { get; init; } = "";

    /// <summary>The LS frequency, channel text (74.2); "" when absent.</summary>
    public string LsFrequency { get; init; } = "";

    /// <summary>The LS DME distance, channel text (74.3); "" when absent.</summary>
    public string LsDme { get; init; } = "";

    /// <summary>The approach center label, channel text (100.1); "" when absent.</summary>
    public string ApproachCenter { get; init; } = "";

    /// <summary>The approach localizer label, channel text (100.2); "" when absent.</summary>
    public string ApproachLoc { get; init; } = "";

    /// <summary>The approach glideslope label, channel text (100.3); "" when absent.</summary>
    public string ApproachGs { get; init; } = "";

    /// <summary>The marker text, channel text (117.1); "" when absent.</summary>
    public string MarkerText { get; init; } = "";

    /// <summary>The LS flag text, channel text (117.3); "" when absent.</summary>
    public string LsFlagText { get; init; } = "";

    /// <summary>The altitude readout is drawn in meters under the baro box as well (122.3); false when absent.</summary>
    public bool MetricAltitude { get; init; }

    /// <summary>
    /// The altitude readout is drawn amber (122.8): from a BARO minimum down, where it is the only sign the PFD gives, and
    /// at a RADIO minimum. False when absent.
    /// </summary>
    public bool AltitudeAmber { get; init; }

    /// <summary>The PFD draws the DH message above the radio altitude (205); false when absent.</summary>
    public bool DhMessage { get; init; }

    /// <summary>The localizer deviation (82.1); NaN when not drawn, 0 when the part leaves it out.</summary>
    public double LocDots { get; init; } = double.NaN;

    /// <summary>The glideslope deviation (82.2); NaN when not drawn, 0 when the part leaves it out.</summary>
    public double GsDots { get; init; } = double.NaN;

    /// <summary>The captain's EFIS LS pushbutton is on (1.6).</summary>
    public bool Ls { get; init; }

    /// <summary>The captain's EFIS baro settings (1); the defaults of an EFIS entry that sends none until set.</summary>
    public FenixPfdBaro Baro { get; init; } = new(false, false, false, 1013, double.NaN);

    /// <summary>The magnetic track (194.2); 0 when absent.</summary>
    public double MagneticTrack { get; init; }

    /// <summary>
    /// The magnetic heading. It is the frame's, set by <see cref="From"/>: <see cref="Decode"/> leaves it 0 and the
    /// message is never decoded for it again.
    /// </summary>
    public double MagHeading { get; init; }

    /// <summary>
    /// Reads the captain's PFD from the RemoteData message <paramref name="remote"/>. Every field is checked for its
    /// wire shape, and a string for valid UTF-8; the first one in another shape gives a section that names it by its
    /// path ("28.5"). A shape problem is reported before missing content, since a damaged part is what hides the
    /// content. The message must carry the captain's air data (28), the air data flags (46) and an EFIS entry; one
    /// that lacks any of them is missing that content.
    /// </summary>
    /// <remarks>
    /// The texts are returned as sent. Their format letters are checked by <see cref="From"/>, not here.
    /// </remarks>
    public static FenixChannelSection<FenixChannelPfd> Decode(FenixChannelPush remote)
    {
        // The path of the first field found in another wire shape; null while every field read had its shape.
        string? wrong = null;

        var root = new Part(remote, "");
        Part air = PartOf(root, Remote.AdirsCapt);
        Part avail = PartOf(root, Remote.AdirsAvailCapt);
        Part ir = PartOf(avail, AdirsAvailData.Ir);
        Part adr = PartOf(avail, AdirsAvailData.Adr);
        Part limits = PartOf(root, Remote.SpeedLimitsCapt);
        Part marks = PartOf(root, Remote.Speeds);
        Part fcu = PartOf(root, Remote.FcuData);
        Part lsInfo = PartOf(root, Remote.LsInfoPfdCapt);
        Part lsData = PartOf(root, Remote.LsDataCapt);
        Part labels = PartOf(root, Remote.ApproachLabelsCapt);
        Part flags = PartOf(root, Remote.PfdFlags);
        Part altStatus = PartOf(root, Remote.AltStatus);
        Part heading = PartOf(root, Remote.Heading);

        // The EFIS is repeated, the captain's panel first: the first entry, not the last as a singular field reads.
        var efis = new Part(null, Number(Remote.Efis));
        IReadOnlyList<FenixChannelField> panels = remote.All(Remote.Efis);
        if (panels.Count > 0)
        {
            efis = efis with { Message = panels[0].AsMessage() };
            if (efis.Message is null) Wrong(root, Remote.Efis);
        }

        // The channel sends NaN for a scale the PFD does not draw, so a deviation that is left out of a part that was sent
        // is a 0.0 that protobuf-net left out. With the part itself absent there is no scale.
        double deviationAbsent = lsData.Message is null ? double.NaN : 0;

        var pfd = new FenixChannelPfd
        {
            Pitch = DoubleOf(air, AdirsData.Pitch, 0),
            Bank = DoubleOf(air, AdirsData.Bank, 0),
            Altitude = DoubleOf(air, AdirsData.Altitude, 0),
            Airspeed = DoubleOf(air, AdirsData.Airspeed, 0),
            VerticalSpeed = DoubleOf(air, AdirsData.VerticalSpeed, 0),
            AttitudeShown = FlagOf(ir, IrAvailData.AttAvail),
            HeadingShown = FlagOf(ir, IrAvailData.HeadingAvail),
            SpeedShown = FlagOf(adr, AdrAvailData.SpeedAvail),
            AltitudeShown = FlagOf(adr, AdrAvailData.AltAvail),
            VerticalSpeedShown = FlagOf(avail, AdirsAvailData.VsAvail),
            VMax = DoubleOf(limits, SpeedLimitsData.VMax, double.NaN),
            Vls = DoubleOf(limits, SpeedLimitsData.Vls, double.NaN),
            AlphaProt = DoubleOf(limits, SpeedLimitsData.AlphaProt, double.NaN),
            Vsw = DoubleOf(limits, SpeedLimitsData.Vsw, double.NaN),
            V1 = DoubleOf(marks, SpeedsData.V1, double.NaN),
            V2 = DoubleOf(marks, SpeedsData.V2, double.NaN),
            Vr = DoubleOf(marks, SpeedsData.Vr, double.NaN),
            F = DoubleOf(marks, SpeedsData.FlapRetraction, double.NaN),
            S = DoubleOf(marks, SpeedsData.SlatRetraction, double.NaN),
            GreenDot = DoubleOf(marks, SpeedsData.GreenDot, double.NaN),
            VfeNext = DoubleOf(marks, SpeedsData.VfeNext, double.NaN),
            TargetIas = DoubleOf(fcu, FcuData.TargetIas, double.NaN),
            SpeedManaged = FlagOf(fcu, FcuData.SpeedManaged),
            BugsVisible = FlagOf(fcu, FcuData.BugsVisible),
            SpeedTrend = DoubleOf(root, Remote.SpeedTrend, 0),
            MachText = TextOf(root, Remote.MachCapt),
            RadioAltitudeText = TextOf(root, Remote.RadioAltString),
            LsIdent = TextOf(lsInfo, PfdLsInfoData.Ident),
            LsFrequency = TextOf(lsInfo, PfdLsInfoData.Freq),
            LsDme = TextOf(lsInfo, PfdLsInfoData.Dme),
            ApproachCenter = TextOf(labels, ApproachLabelData.Center),
            ApproachLoc = TextOf(labels, ApproachLabelData.Loc),
            ApproachGs = TextOf(labels, ApproachLabelData.Gs),
            MarkerText = TextOf(flags, PfdFlagsData.MarkerCapt),
            LsFlagText = TextOf(flags, PfdFlagsData.LsFlagCapt),
            MetricAltitude = FlagOf(altStatus, AltStatusData.Metric),
            AltitudeAmber = FlagOf(altStatus, AltStatusData.AltAmber),
            DhMessage = FlagOf(root, Remote.DhMessage),
            LocDots = DoubleOf(lsData, LsInputData.PfdHorDots, deviationAbsent),
            GsDots = DoubleOf(lsData, LsInputData.PfdVertDots, deviationAbsent),
            Ls = FlagOf(efis, Efis.Ls),
            Baro = new FenixPfdBaro(
                Std: FlagOf(efis, Efis.QnhStd),
                Inches: FlagOf(efis, Efis.QnhInches),
                Qfe: FlagOf(efis, Efis.QfeMode),
                QnhHpa: SingleOf(efis, Efis.Qnh, 1013),
                QnhInHg: SingleOf(efis, Efis.QnhInch, double.NaN)),
            MagneticTrack = DoubleOf(heading, HeadingData.MagneticTrack, 0)
        };

        if (wrong is not null) return FenixChannelSection<FenixChannelPfd>.WrongShape(wrong);
        if (air.Message is null) return FenixChannelSection<FenixChannelPfd>.Missing("captain air data");
        if (avail.Message is null) return FenixChannelSection<FenixChannelPfd>.Missing("air data flags");
        if (efis.Message is null) return FenixChannelSection<FenixChannelPfd>.Missing("EFIS");

        return FenixChannelSection<FenixChannelPfd>.Ok(pfd);

        // ---- The reads. Each takes the part it reads from, which is null when the message did not send it or sent it
        // in another shape, and then answers the value a field left out has. A field in another shape records its path,
        // the first only, and reads as left out.

        // A singular nested part. Its path is its parent's path and its number.
        Part PartOf(Part parent, int number)
        {
            string path = PathOf(parent.Path, number);
            if (parent.Message is null || !parent.Message.TryGet(number, out FenixChannelField field))
            {
                return new Part(null, path);
            }

            FenixChannelPush? message = field.AsMessage();
            if (message is null) Wrong(parent, number);
            return new Part(message, path);
        }

        bool TryDouble(Part part, int number, out double value)
        {
            value = 0;
            if (part.Message is null || !part.Message.TryGet(number, out FenixChannelField field)) return false;

            if (field.Type != FenixWireType.Fixed64)
            {
                Wrong(part, number);
                return false;
            }

            value = field.AsDouble();
            return true;
        }

        double DoubleOf(Part part, int number, double absent) => TryDouble(part, number, out double value) ? value : absent;

        // A fixed32 (a single), widened to a double.
        double SingleOf(Part part, int number, double absent)
        {
            if (part.Message is null || !part.Message.TryGet(number, out FenixChannelField field)) return absent;

            if (field.Type != FenixWireType.Fixed32)
            {
                Wrong(part, number);
                return absent;
            }

            return field.Fixed32;
        }

        bool FlagOf(Part part, int number)
        {
            if (part.Message is null || !part.Message.TryGet(number, out FenixChannelField field)) return false;

            if (field.Type != FenixWireType.Varint)
            {
                Wrong(part, number);
                return false;
            }

            return field.AsBool();
        }

        string TextOf(Part part, int number)
        {
            if (part.Message is null || !part.Message.TryGet(number, out FenixChannelField field)) return "";

            string? text = field.AsString();
            if (text is null) Wrong(part, number);
            return text ?? "";
        }

        void Wrong(Part part, int number) => wrong ??= PathOf(part.Path, number);
    }

    /// <summary>
    /// The captain's PFD of <paramref name="snapshot"/>: its RemoteData message decoded by <see cref="Decode"/>, with
    /// the heading of its frame. A snapshot with no message or no frame is missing "data". The text fields are checked
    /// last: the first format letter <see cref="FenixChannelText"/> does not know, in the Mach line, the radio
    /// altitude, the LS information, the approach labels, the marker or the LS flag, makes the section an unknown code
    /// that names the line ([FNX-24]). The caller has judged the snapshot's verdict.
    /// </summary>
    public static FenixChannelSection<FenixChannelPfd> From(FenixChannelSnapshot snapshot)
    {
        if (snapshot.Push is null || snapshot.Frame is null) return FenixChannelSection<FenixChannelPfd>.Missing("data");

        FenixChannelSection<FenixChannelPfd> decoded = Decode(snapshot.Push);
        if (!decoded.IsOk) return decoded;

        FenixChannelPfd pfd = decoded.Value!;
        foreach ((string text, string place) in TextsOf(pfd))
        {
            FenixTextLine line = FenixChannelText.Parse(text);
            if (line.UnknownCodes.Count > 0)
            {
                return FenixChannelSection<FenixChannelPfd>.UnknownCode(line.UnknownCodes[0], place);
            }
        }

        return FenixChannelSection<FenixChannelPfd>.Ok(pfd with { MagHeading = snapshot.Frame.MagHeading });
    }

    // Each text of the PFD with the place a pilot would call it, in the order the first unknown letter is looked for.
    private static IEnumerable<(string Text, string Place)> TextsOf(FenixChannelPfd pfd)
    {
        yield return (pfd.MachText, "Mach line");
        yield return (pfd.RadioAltitudeText, "radio altitude");
        yield return (pfd.LsIdent, "LS information");
        yield return (pfd.LsFrequency, "LS information");
        yield return (pfd.LsDme, "LS information");
        yield return (pfd.ApproachCenter, "approach labels");
        yield return (pfd.ApproachLoc, "approach labels");
        yield return (pfd.ApproachGs, "approach labels");
        yield return (pfd.MarkerText, "marker");
        yield return (pfd.LsFlagText, "LS flag");
    }

    // A message being read, with its path in the push ("" for RemoteData, "46.2" for field 2 inside field 46). The
    // message is null when it was not sent or was sent in another shape.
    private readonly record struct Part(FenixChannelPush? Message, string Path);

    private static string Number(int field) => field.ToString(CultureInfo.InvariantCulture);

    private static string PathOf(string parent, int field)
        => parent.Length == 0 ? Number(field) : $"{parent}.{Number(field)}";
}
