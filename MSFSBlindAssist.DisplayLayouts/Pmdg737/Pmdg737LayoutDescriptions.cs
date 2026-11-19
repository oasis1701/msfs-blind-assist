using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The derived layouts as log lines, in the invariant culture: the FMA, the engine displays, the navigation
/// display and the primary flight display. Pure: only the layout records and the base library.
/// </summary>
public static class Pmdg737LayoutDescriptions
{
    // ---- the FMA ----

    /// <summary>The derived layout as log lines: its file, anchor, globals and offsets, then each column's words.</summary>
    public static IReadOnlyList<string> Describe(Pmdg737FmaLayout l) => new[]
    {
        Invariant($"layout from {l.SourceFile}: anchor 0x{l.AnchorAddress:x} ({l.Anchor.Length} bytes); ")
            + Invariant($"instance global 0x{l.InstanceGlobal:x}, object {l.ObjectSize} bytes, display global 0x{l.DisplayGlobal:x}; ")
            + Invariant($"offsets FMA_AP {l.FmaAp}, FMA_AT {l.FmaAt}, FMAPitch {l.Pitch}, FMAPitchArmed {l.PitchArmed}, ")
            + Invariant($"FMARoll {l.Roll}, FMARollArmed {l.RollArmed}, FD1 {l.Fd1}, FD2 {l.Fd2}, MCPDialAlt {l.McpAlt}, MCPDialHdg_C {l.McpHdg}"),
        DescribeWords("A/T", l.AtWords),
        DescribeWords("Roll", l.RollWords),
        DescribeWords("Pitch", l.PitchWords),
        DescribeWords("AFDS", l.StatusWords),
    };

    private static string DescribeWords(string column, FmaColumnWords w) =>
        Invariant($"  {column} words from value {w.FirstValue}: {string.Join(" | ", w.Words)}")
        + (w.ValueOneWord is { } one ? $"; value 1: {one}" : "");

    // ---- the engine displays ----

    /// <summary>The engine gauge fields by name, in the order the log lists them.</summary>
    public static IEnumerable<(string Name, GlassField Field)> NamedGaugeFields(EngineGaugeFields f) => new[]
    {
        ("TAT", f.Tat), ("N1", f.N1), ("N1 reference", f.N1Reference), ("N1 red line", f.N1RedLine),
        ("EGT", f.Egt), ("EGT red line", f.EgtRedLine), ("EGT maximum continuous", f.EgtMaxContinuous),
        ("N2", f.N2), ("N2 maximum", f.N2Max),
        ("fuel flow", f.FuelFlow), ("fuel used", f.FuelUsed), ("show fuel used", f.ShowFuelUsed),
        ("oil pressure", f.OilPressure), ("oil pressure red", f.OilPressureRed), ("oil pressure amber", f.OilPressureAmber),
        ("oil temperature", f.OilTemperature), ("oil temperature red", f.OilTemperatureRed), ("oil temperature amber", f.OilTemperatureAmber),
        ("oil quantity", f.OilQuantity), ("oil quantity LO", f.OilQuantityLow),
        ("vibration", f.Vibration), ("vibration limit", f.VibrationLimit), ("running", f.Running),
        ("thrust mode", f.ThrustMode), ("assumed temperature shown", f.AssumedTempShown),
        ("assumed temperature", f.AssumedTemperature), ("N1 SET", f.N1SetSelector),
        ("N1 latch", f.N1RedLatch), ("EGT latch", f.EgtRedLatch), ("N2 latch", f.N2RedLatch),
    };

    /// <summary>The derived layout as log lines (invariant culture): objects, every field as offset/stride,
    /// the units and pages, the SYS page, the options, each page's rows.</summary>
    public static IReadOnlyList<string> Describe(Pmdg737EngineDisplayLayout l)
    {
        static string F(GlassField f) => Invariant($"{f.Offset}/{f.Stride}");
        static string O(GlassObject o) => Invariant($"0x{o.Global:x} ({o.Size} bytes)");
        string Fields(IEnumerable<(string Name, GlassField Field)> fields) => string.Join(", ", fields.Select(f => $"{f.Name} {F(f.Field)}"));
        var a = l.Alerts;
        var p = l.Pages;
        var s = l.Sys;
        var o = l.Options;
        var r = l.Gauges.ThrustRules;
        string Choice(bool option, bool assumed) => string.Join(" ", Enumerable.Range(0, r.ModeLimit).Select(mode => r.Word(mode, option, assumed)));
        return new[]
        {
            Invariant($"engine displays from {l.SourceFile}: anchor 0x{l.AnchorAddress:x} ({l.Anchor.Length} bytes); ")
                + $"engine {O(l.Engine)}, glass {O(l.Glass)}, CEFIS {O(p.Efis)}, COptions {O(o.Options)}, CAircraft {O(l.Aircraft.Aircraft)} initialised {F(l.Aircraft.Initialised)}",
            "  gauges: " + Fields(NamedGaugeFields(l.Gauges)) + Invariant($"; vibration shown above {l.Gauges.VibrationShownAbove}"),
            "  thrust words: " + string.Join(" | ", l.Gauges.ThrustWords)
                + Invariant($"; AUTO {r.N1SetAuto}, no mode {r.NoMode}, drawn below {r.ModeLimit}, derate option {F(r.DerateOption)}; ")
                + $"words by mode: {Choice(false, false)} / assumed {Choice(false, true)} / option {Choice(true, false)} / option assumed {Choice(true, true)}",
            "  alerts: " + Fields(new[]
            {
                ("START VALVE", a.StartValve), ("OIL FILTER", a.OilFilterBypass), ("LOW OIL", a.LowOilPressure),
                ("fuel", a.FuelQuantity), ("fuel warning", a.FuelWarning), ("REV", a.ReverserShown), ("REV colour", a.ReverserSecond),
                ("TAI", a.TaiOn), ("TAI amber", a.TaiAmber), ("ENG FAIL", a.EngineFail), ("X-BLD START", a.CrossBleedStart),
                ("A/T LIM", a.AutothrottleLimit), ("autoland", a.AutolandAdvisory), ("ATC", a.AtcMemo), ("COMM", a.CommMemo),
            }) + Invariant($"; LOW {a.FuelLowValue}, CONFIG {a.FuelConfigValue}, IMBAL {a.FuelImbalanceValue}, NO LAND 3 {a.NoLand3Value}, NO AUTOLAND {a.NoAutolandValue}"),
            Invariant($"  units: {p.UnitCount} x {p.UnitStride} bytes, page {p.PageOffset}, index {p.IndexOffset}, upper {p.UpperUnit}, lower {p.LowerUnit}, manual N1 {F(p.ManualN1)}; pages ")
                + string.Join(", ", p.Pages.OrderBy(kv => kv.Key).Select(kv => Invariant($"{kv.Key} {kv.Value}"))),
            "  SYS: " + Fields(new[]
            {
                ("hydraulic quantity", s.HydQuantity), ("pressure", s.HydPressure), ("RF", s.Refill), ("AIL L", s.AileronLeft),
                ("SPLR L", s.SpoilerLeft), ("ELEV", s.Elevator), ("SPLR R", s.SpoilerRight), ("AIL R", s.AileronRight),
                ("RUDDER", s.Rudder), ("brake temperature", s.BrakeTemperature), ("brake status", s.BrakeStatus),
            }) + Invariant($"; pressure shown above {s.HydPressureShownAbove}"),
            Invariant($"  options: fuel in KG {F(o.FuelInKg)}, oil in percent {F(o.OilInPercent)} (x {o.PercentMultiplier}), ")
                + Invariant($"in litres {F(o.OilInLitres)} (x {o.LitresMultiplier:0.####}), else quarts (x {o.QuartsMultiplier:0.####}); oil LO indication {F(o.OilLowIndication)}"),
            "  rows: " + string.Join("; ", l.PageItems.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}")),
        };
    }

    // ---- the navigation display ----

    /// <summary>The derived layout as log lines, in the invariant culture: the anchor, then each part with
    /// every field by name (a field as offset/stride, an object as its global and size).</summary>
    public static IReadOnlyList<string> Describe(Pmdg737NavDisplayLayout l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return new[]
        {
            Invariant($"navigation display from {l.SourceFile}: anchor 0x{l.AnchorAddress:x} ({l.Anchor.Length} bytes)"),
            "  objects: " + NdMembers(l.Objects), "  panel: " + NdMembers(l.Panel), "  units: " + NdMembers(l.Units),
            "  orientation: " + NdMembers(l.Orientation), "  route: " + NdMembers(l.Route), "  map validity: " + NdMembers(l.MapValidity),
            "  geometry: " + NdMembers(l.Geometry),
            "  corners: " + NdMembers(l.Corners), "  messages: " + NdMembers(l.Messages), "  XTK: " + NdMembers(l.Xtk), "  lists: " + NdMembers(l.Lists),
            "  tuned stations: " + NdMembers(l.Tuned), "  receivers: " + NdMembers(l.Receivers), "  traffic: " + NdMembers(l.Traffic),
            "  symbols: " + NdMembers(l.Symbols), "  CDU FIX page: " + NdMembers(l.FixPage),
            "  approach: " + (l.Approach is { } approach ? NdMembers(approach) : "not derived: " + l.ApproachFailure),
            "  pointers: " + (l.Pointers is { } pointers ? NdMembers(pointers) : "not derived: " + l.PointersFailure),
            "  details: " + (l.Details is { } details ? NdMembers(details) : "none"),
        };
    }

    private static string NdMembers(object record) => string.Join(", ", record.GetType().GetProperties()
        .Where(p => p.GetIndexParameters().Length == 0).Select(p => p.Name + " " + NdRender(p.GetValue(record))));

    /// <summary>One value of a layout part, culture-free (a record's own ToString prints doubles in the PC's culture).</summary>
    private static string NdRender(object? value) => value switch
    {
        null => "none",
        GlassField f => Invariant($"{f.Offset}/{f.Stride}"),
        GlassObject o => Invariant($"0x{o.Global:x} ({o.Size} bytes)"),
        string s => "\"" + s + "\"",
        char c => "'" + c + "'",
        bool b => b ? "yes" : "no",
        IFormattable x => x.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        System.Collections.IDictionary d => "{" + string.Join(", ", d.Keys.Cast<object>().Select(k => NdRender(k) + ": " + NdRender(d[k]))) + "}",
        System.Collections.IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(NdRender)) + "]",
        _ => "{ " + NdMembers(value) + " }",
    };

    // ---- the primary flight display ----

    /// <summary>The derived layout as log lines, in the invariant culture: the anchor, then each part with every field
    /// by name (a field as offset/stride, an object as its global and size), each optional part or why it did not derive.</summary>
    public static IReadOnlyList<string> Describe(Pmdg737PfdLayout l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var names = new Dictionary<GlassObject, string> { [l.Objects.Glass] = "G", [l.Objects.Efis] = "E", [l.Objects.Sim] = "S", [l.Objects.Options] = "O" };
        string Name(GlassObject o) => names.TryGetValue(o, out var n) ? n : Invariant($"0x{o.Global:x}");
        var lines = new List<string>
        {
            Invariant($"primary flight display from {l.SourceFile}: anchor 0x{l.AnchorAddress:x} ({l.Anchor.Length} bytes)"),
            "  objects: " + PfdMembers(l.Objects, Name), "  units: " + PfdMembers(l.Units, Name), "  panel: " + PfdMembers(l.Panel, Name),
        };
        var s = l.Speed;
        lines.Add("  speed: Ias " + PfdRender(s.Ias, Name) + ", Box " + PfdRender(s.Box, Name) + ", TapeShown " + PfdRender(s.TapeShown, Name)
            + ", Trend " + PfdRender(s.Trend, Name) + " when " + PfdRender(s.TrendShown, Name) + ", Geometry " + PfdRender(s.Geometry, Name));
        lines.AddRange(s.Bugs.Select(b => "    bug: " + PfdMembers(b, Name)));
        lines.AddRange(s.Bands.Select(b => "    band: " + PfdMembers(b, Name)));
        lines.AddRange(s.Readouts.Select(r => "    readout: " + PfdMembers(r, Name)));
        lines.AddRange(s.Messages.Select(x => "    message: " + PfdMembers(x, Name)));
        var f = s.FlapBugs;
        lines.Add("    flap bugs: Labels " + PfdRender(f.Labels, Name) + ", Speeds " + PfdRender(f.Speeds, Name) + Invariant($", Window {f.Window}")
            + ", Shown " + PfdRender(f.Shown, Name) + Invariant($", {f.Cases.Count} cases over ") + string.Join("; ", f.Inputs.Select(i => PfdMembers(i, Name))));
        lines.Add("  speed reference: " + PfdMembers(l.SpeedReference, Name));
        var a = l.Attitude;
        lines.Add("  attitude: Shown " + PfdRender(a.Shown, Name) + ", Pitch " + PfdRender(a.Pitch, Name) + Invariant($" at {a.PitchPixelsPerDegree} px/deg")
            + ", Bank " + PfdRender(a.Bank, Name) + ", Slip " + PfdRender(a.Slip, Name) + Invariant($" at {a.SlipPixelsPerUnit} px")
            + ", filled while " + PfdRender(a.SlipFilled, Name));
        lines.Add("    flight director: " + PfdMembers(a.FlightDirector, Name));
        lines.Add("    flight path vector: " + PfdMembers(a.Fpv, Name));
        lines.Add("    radio altitude: " + PfdMembers(a.RadioAltitude, Name));
        lines.Add("    radio minimums: " + PfdMembers(a.RadioMinimums, Name));
        lines.AddRange(a.Messages.Select(x => "    message: " + PfdMembers(x, Name)));
        var al = l.Altitude;
        lines.Add("  altitude: Altitude " + PfdRender(al.Altitude, Name) + ", Box " + PfdRender(al.Box, Name) + Invariant($" (step {al.ReadoutStep})")
            + ", TapeShown " + PfdRender(al.TapeShown, Name) + ", Geometry " + PfdRender(al.Geometry, Name));
        lines.Add("    selected bug: " + PfdMembers(al.SelectedBug, Name));
        lines.Add("    minimums bug: " + PfdMembers(al.MinimumsBug, Name));
        lines.AddRange(al.Bands.Select(b => "    band: " + PfdMembers(b, Name)));
        lines.AddRange(al.Readouts.Select(r => "    readout: " + PfdMembers(r, Name)));
        lines.AddRange(al.Messages.Select(x => "    message: " + PfdMembers(x, Name)));
        var v = l.VerticalSpeed;
        lines.Add("  vertical speed: Rate " + PfdRender(v.Rate, Name) + ", NeedleShown " + PfdRender(v.NeedleShown, Name) + ", NumberAbove " + PfdRender(v.NumberAbove, Name)
            + Invariant($", threshold {v.ReadoutThreshold}, step {v.ReadoutStep}"));
        lines.Add("    number: " + PfdMembers(v.Number, Name));
        lines.Add("    selected bug: " + PfdMembers(v.SelectedBug, Name));
        lines.AddRange(v.Messages.Select(x => "    message: " + PfdMembers(x, Name)));
        var h = l.Heading;
        lines.Add("  heading: Heading " + PfdRender(h.Heading, Name) + ", Shown " + PfdRender(h.Shown, Name) + ", Track " + PfdRender(h.Track, Name)
            + " when " + PfdRender(h.TrackShown, Name) + Invariant($", half arc {h.HalfArcDegrees}") + ", TrueReference " + PfdRender(h.TrueReference, Name)
            + ", labels " + PfdRender(h.MagneticLabel, Name) + "/" + PfdRender(h.TrueLabel, Name));
        lines.Add("    selected bug: " + PfdMembers(h.SelectedHeading, Name));
        lines.Add("    selected readout: " + PfdMembers(h.SelectedReadout, Name));
        lines.AddRange(h.Messages.Select(x => "    message: " + PfdMembers(x, Name)));
        lines.Add(Invariant($"  FMA strip: {l.FmaStrip.Extras.Count} extra texts"));
        lines.AddRange(l.FmaStrip.Extras.Select(x => "    extra: " + PfdMembers(x, Name)));
        lines.AddRange(l.FmaStrip.ArmedRoll.Select(x => "    armed roll: " + (x.Literal is { } literal ? $"\"{literal}\""
            : "word " + PfdRender(x.Index!, Name) + " of [" + string.Join(", ", x.Words.Select(w => $"\"{w}\"")) + "]") + " when " + PfdRender(x.Shown, Name)));
        var o = l.Optional;
        foreach (var (part, value) in new (string, object?)[]
        {
            (Pmdg737PfdParts.Approach, o.Approach), (Pmdg737PfdParts.Markers, o.Markers), (Pmdg737PfdParts.RisingRunway, o.RisingRunway),
            (Pmdg737PfdParts.Nps, o.Nps), (Pmdg737PfdParts.Tcas, o.Tcas), (Pmdg737PfdParts.Aoa, o.Aoa), (Pmdg737PfdParts.Pli, o.Pli),
        })
            lines.Add("  " + part + ": " + (value is null ? "not derived: " + o.Failures.FirstOrDefault(f => f.Part == part)?.Reason : PfdMembers(value, Name)));
        return lines;
    }

    private static string PfdMembers(object record, Func<GlassObject, string> name) => string.Join(", ", record.GetType().GetProperties()
        .Where(p => p.GetIndexParameters().Length == 0).Select(p => p.Name + " " + PfdRender(p.GetValue(record), name)));

    /// <summary>One value of a layout part, culture-free (a record's own ToString prints doubles in the PC's culture).</summary>
    private static string PfdRender(object? value, Func<GlassObject, string> name) => value switch
    {
        null => "none",
        PfdExpr e => PfdEval.Show(e, name),
        GlassField f => Invariant($"{f.Offset}/{f.Stride}"),
        GlassObject o => Invariant($"0x{o.Global:x} ({o.Size} bytes)"),
        string s => "\"" + s + "\"",
        char c => "'" + c + "'",
        bool b => b ? "yes" : "no",
        IFormattable x => x.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        System.Collections.IDictionary d => "{" + string.Join(", ", d.Keys.Cast<object>().Select(k => PfdRender(k, name) + ": " + PfdRender(d[k], name))) + "}",
        System.Collections.IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(x => PfdRender(x, name))) + "]",
        _ => "{ " + PfdMembers(value, name) + " }",
    };
}
