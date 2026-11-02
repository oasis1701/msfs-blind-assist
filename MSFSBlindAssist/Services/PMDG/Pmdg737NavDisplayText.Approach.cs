using G = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayGeometry;

namespace MSFSBlindAssist.Services.PMDG;

// The APP and VOR modes' rows.
public static partial class Pmdg737NavDisplayText
{
    /// <summary>The Captain's side, which picks the FMC label ("FMC L").</summary>
    private const int CaptainSide = 0;

    /// <summary>The rows APP and VOR add to the ones they share with MAP, in screen order: the ILS/VOR block at
    /// the top right, TO/FROM; without the layout's APP/VOR part, one row saying the mode's data is not read and
    /// why (the technical reason is in the log).</summary>
    private static IReadOnlyList<string> ApproachRows(Context c)
    {
        bool app = c.S.Panel.Mode == c.L.Geometry.Modes.App;
        // The track line, drawn with the compass before the approach picture.
        var track = TrackLine(c) is { } trackLine ? new[] { "Centre: " + trackLine } : Array.Empty<string>();
        if (c.L.Approach is not { } part || c.S.Approach is not { } a)
            return new[] { (app ? "ILS" : "VOR") + " data: not read: " + FmaReasons.FileNotRecognised }.Concat(track).Concat(PointerRows(c, "Centre: ")).ToList();
        var b = part.Block;
        var rows = new List<string> { "Top right: " + string.Join(", ", BlockParts(c, b, a, app)) };
        rows.AddRange(track);
        // The course pointer and CDI, only while the readout is valid.
        if (c.S.ReadoutValid)
        {
            rows.Add("Centre: course pointer " + CoursePointer(c, a));
            if (DeviationBar(b, part.Cdi, a, app) is { } bar) rows.Add("Centre: deviation bar " + bar);
            // The heading bug, in every mode but PLN, on the compass of MAP's picture with the same CTR
            // (PMDG's screen centre and compass radius do not depend on the mode).
            var compass = G.Viewport(c.S.Panel.Centered ? NdView.CentredMap : NdView.ExpandedMap, c.L.Geometry);
            if (HeadingBug(c, compass) is { } bug) rows.Add("Centre: heading bug, " + bug);
            if (HeadingLine(c) is { } line) rows.Add("Centre: dashed line to the heading bug, " + line);
        }
        // The bearing pointers, not gated by the readout.
        rows.AddRange(PointerRows(c, "Centre: "));
        // The glideslope, APP only, at the right side.
        if (app) rows.Add("Right side: " + Glideslope(b, part.Glideslope, a));
        // The ILS/VOR block's TO/FROM, in VOR, below and right of the aircraft symbol (730, 875).
        if (!app && !a.VorFailed && (a.ToFrom == b.ToValue || a.ToFrom == b.FromValue))
            rows.Add("Centre: " + (a.ToFrom == b.ToValue ? b.ToLabel : b.FromLabel));
        return rows;
    }

    /// <summary>The traffic in APP and VOR while the ND draws it there (not centred), on expanded MAP's picture,
    /// rows "Centre:"; OFFSCALE as in MAP.</summary>
    private static MapResult ApproachTraffic(Context c)
    {
        if (c.L.Approach is not { Messages.Traffic: true } || c.S.Panel.Centered) return new MapResult(Array.Empty<string>(), false, false);
        var (rows, offscale) = TrafficRows(c, NdView.ExpandedMap, "Centre: ");
        if (TcasRing(c) is { } ring) rows.Insert(0, "Centre: " + ring);
        return new MapResult(rows, false, offscale);
    }

    /// <summary>PMDG's APP and VOR failure flag texts: the mode's receiver flag while that receiver has failed (by
    /// the receiver in the full format), and the disagree text while the receiver's frequency is the wrong kind
    /// for the mode.</summary>
    private static IEnumerable<string> ApproachMessages(Context c)
    {
        var modes = c.L.Geometry.Modes;
        int mode = c.S.Panel.Mode;
        if ((mode != modes.App && mode != modes.Vor) || c.L.Approach is not { } part || c.S.Approach is not { } a) yield break;
        var m = part.Messages;
        bool app = mode == modes.App;
        var flag = app ? m.LocFlag : m.VorFlag;
        if (app ? a.LocFailed : a.VorFailed) yield return c.Full ? flag.FullLabels.For(a.NavIndex) : flag.HalfLabel;
        if (part.Block.IsIls(a.Frequency) == (app ? m.DisagreeInAppWhenIls : m.DisagreeInVorWhenIls)) yield return m.Disagree;
    }

    /// <summary>The flags listed at their own place and spoken like messages: the glideslope's while PMDG draws it.</summary>
    private static IEnumerable<string> PlacedFlags(Context c)
    {
        if (c.S.Panel.Mode != c.L.Geometry.Modes.App || c.L.Approach is not { } part || c.S.Approach is not { } a) yield break;
        var gs = part.Glideslope;
        bool fmc = a.Sources[part.Block.FmcSources.ToList().IndexOf(gs.FmcSource)] == part.Block.FmcSourceValue;
        if (a.GlideslopeFailed && !fmc) yield return gs.FlagText;
    }

    /// <summary>The course pointer: the side's course as PMDG truncates it, and where its head points
    /// (the course in the readout's reference off PMDG's up direction).</summary>
    private static string CoursePointer(Context c, NdApproachSample a)
    {
        int course = (int)Math.Truncate(a.PointerCourse);
        string text = (((course % 360) + 360) % 360).ToString("000", Inv);
        return G.UpDirectionDeg(c.S, c.L) is { } up ? text + ", " + Direction(G.RelativeDeg(course + c.S.MagneticVariationDeg, up)) : text;
    }

    /// <summary>The deviation bar while PMDG draws it (the receiver's state high enough, the VOR not failed unless
    /// the FMC source is set): "at full scale" where PMDG pins it, else the dots to the tenth, "centred" at none.</summary>
    private static string? DeviationBar(NdApproachBlockFields b, NdCdiFields cdi, NdApproachSample a, bool app)
    {
        bool fmc = a.Sources[b.FmcSources.ToList().IndexOf(cdi.FmcSource)] == b.FmcSourceValue;
        if ((!fmc && a.VorFailed) || a.ReceiverState < cdi.ReceiverStateFrom) return null;
        string side = a.Deviation > 0 ? "right" : "left";
        // PMDG fills the bar in APP while it is short of the scale's end, else draws its outline.
        string look = app && Math.Abs(a.Deviation) < cdi.FilledBelow ? ", filled" : ", outlined";
        if (Math.Abs(a.Deviation) >= cdi.FullScale) return "at full scale, " + side + look;
        double dots = Math.Round(G.DeviationDots(a.Deviation, cdi), 1, MidpointRounding.AwayFromZero);
        return (dots == 0 ? "centred" : Math.Abs(dots).ToString("0.0", Inv) + " dots " + side) + look;
    }

    /// <summary>The glideslope: PMDG's amber flag (boxed) while failed without the FMC source; else the scale,
    /// with the pointer while the receiver is in its state: "at full scale" where PMDG pins it, else the dots to
    /// the tenth, below for plus (PMDG draws it down the screen), "centred" at none.</summary>
    private static string Glideslope(NdApproachBlockFields b, NdGlideslopeFields gs, NdApproachSample a)
    {
        bool fmc = a.Sources[b.FmcSources.ToList().IndexOf(gs.FmcSource)] == b.FmcSourceValue;
        if (a.GlideslopeFailed && !fmc) return FailedLabel(gs.FlagText, gs.FlagBoxed);
        if (a.ReceiverState != gs.PointerState) return "glideslope scale, no pointer";
        string side = a.Glideslope > 0 ? "below" : "above";
        if (Math.Abs(a.Glideslope) >= gs.FullScale) return "glideslope pointer at full scale, " + side;
        double dots = Math.Round(Math.Clamp(a.Glideslope, -gs.FullScale, gs.FullScale) * gs.DotsPerUnit, 1, MidpointRounding.AwayFromZero);
        return "glideslope pointer " + (dots == 0 ? "centred" : Math.Abs(dots).ToString("0.0", Inv) + " dots " + side);
    }

    /// <summary>The ILS/VOR block as printed: the label, the ident (or PMDG's dashes), in the full
    /// format the truncated course, the DME (dashes when none; the amber label, boxed, while failed).</summary>
    private static IEnumerable<string> BlockParts(Context c, NdApproachBlockFields b, NdApproachSample a, bool app)
    {
        bool fmc = a.Sources.Contains(b.FmcSourceValue), ils = b.IsIls(a.Frequency);
        yield return app ? (fmc ? b.FmcLabels.For(CaptainSide) : b.IlsLabels.For(a.NavIndex)) : b.VorLabels.For(a.NavIndex);
        string ident = app
            ? (fmc || ils ? a.ApproachIdent ?? "" : b.Dashes)
            : (!ils ? Ident(a.VorIdent ?? "", a.VorFrequency ?? "") : b.Dashes);
        if (ident.Length > 0) yield return ident;
        if (c.Full) yield return b.CourseLabel + " " + ((int)Math.Truncate(a.Course)).ToString("000", Inv);
        yield return a.DmeFailed
            ? FailedLabel(c.Full ? b.DmeFailedLabels.For(a.NavIndex) : b.DmeFailedHalfLabel, b.DmeFailedBoxed)
            : b.DmeLabel + " " + (a.DmeNm >= b.DmeShownFrom ? DmeText(a.DmeNm, b.DmeTenthsBelow) : b.DmeDashes.Trim());
    }
}
