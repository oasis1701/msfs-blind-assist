using System.Globalization;
using G = MSFSBlindAssist.Services.PMDG.Pmdg737PfdGeometry;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The PFD's part names, in screen order: the one table every row starts with.</summary>
public static class PfdParts
{
    public const string Fma = "FMA: ";
    public const string AboveSpeedTape = "Above the speed tape: ";
    public const string SpeedTape = "Speed tape: ";
    public const string BelowSpeedTape = "Below the speed tape: ";
    public const string Attitude = "Attitude: ";
    public const string AboveAltitudeTape = "Above the altitude tape: ";
    public const string AltitudeTape = "Altitude tape: ";
    public const string BelowAltitudeTape = "Below the altitude tape: ";
    public const string VerticalSpeed = "Vertical speed: ";
    public const string Heading = "Heading: ";
    public const string Message = "Message: ";

    public static string Of(PfdPlace place) => place switch
    {
        PfdPlace.Fma => Fma,
        PfdPlace.AboveSpeedTape => AboveSpeedTape,
        PfdPlace.SpeedTape => SpeedTape,
        PfdPlace.BelowSpeedTape => BelowSpeedTape,
        PfdPlace.Attitude => Attitude,
        PfdPlace.AboveAltitudeTape => AboveAltitudeTape,
        PfdPlace.AltitudeTape => AltitudeTape,
        PfdPlace.BelowAltitudeTape => BelowAltitudeTape,
        PfdPlace.VerticalSpeed => VerticalSpeed,
        PfdPlace.Heading => Heading,
        PfdPlace.Message => Message,
        _ => throw new ArgumentOutOfRangeException(nameof(place), place, null),
    };
}

/// <summary>
/// The Captain's PFD as list rows, in screen order: the FMA, above the speed tape, the speed tape (highest to lowest,
/// the current-value box in its place), below it, the attitude, above the altitude tape, the altitude tape, below it,
/// the vertical speed, the heading, then the messages. Each row starts with its part name (<see cref="PfdParts"/>), but
/// the FMA's columns and extras, which name themselves, and
/// carries PMDG's own printed text (labels, flags, formats); a value is read only while PMDG draws it. Items PMDG draws
/// without a label are named only where the layout itself singles them out (the selected speed bug is the one PMDG
/// parks; VREF plus 20 is REF's value plus a constant; the 80 or 100 knot bug a choice of two constants; V2 plus 15 the
/// bug whose gate the flap bugs' choice reads; the speed reference bug the one gated by the WT readout's selector), never
/// by their place in a list: another reads "bug". A flag that replaces an instrument is a row in its place. With no full
/// reading (the window just opened) only the FMA, the flags and the messages are rows. Invariant culture. Pure.
/// </summary>
public static class Pmdg737PfdText
{
    public const string Off = "Captain PFD: off";
    public const string NotOnAnyDisplay = "Captain PFD: not on any display";
    public const string Classic = "Captain PFD: classic EFIS display, not read";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<string> Rows(PfdSample s, Pmdg737PfdLayout l, IReadOnlyList<string>? fmaColumns, string fmaReadout)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(l);
        if (!Pmdg737PfdCrossCheck.DisplaysLit(s, l)) return new[] { Off };
        switch (Pmdg737PfdCrossCheck.CaptainPfd(s, l.Units))
        {
            case PfdShown.NotOnAnyDisplay: return new[] { NotOnAnyDisplay };
            case PfdShown.Classic: return new[] { Classic };
        }

        var up = new MessagesUp(s, l);
        var rows = new List<string>();
        // The FMA's columns name themselves ("A/T: N1") and its extras stand under them, so neither takes "FMA: " (owner,
        // 2026-10-30); a reader state not naming the FMA still does.
        if (fmaColumns is not null) rows.AddRange(WithPfdArmedRoll(fmaColumns, s, l));
        else rows.Add(fmaReadout.StartsWith("FMA", StringComparison.Ordinal) ? fmaReadout : PfdParts.Fma + fmaReadout);
        rows.AddRange(l.FmaStrip.Extras.Where(up.Is).Select(m => m.Text));

        if (s.Reading is { } values)
        {
            var c = new Context(l, values, up);
            Part(rows, PfdParts.AboveSpeedTape, c.AboveSpeedTape);
            Part(rows, PfdParts.SpeedTape, c.SpeedTape);
            Part(rows, PfdParts.BelowSpeedTape, c.BelowSpeedTape);
            Part(rows, PfdParts.Attitude, c.Attitude);
            Part(rows, PfdParts.AboveAltitudeTape, c.AboveAltitudeTape);
            Part(rows, PfdParts.AltitudeTape, c.AltitudeTape);
            Part(rows, PfdParts.BelowAltitudeTape, c.BelowAltitudeTape);
            Part(rows, PfdParts.VerticalSpeed, c.VerticalSpeed);
            Part(rows, PfdParts.Heading, c.Heading);
        }
        else
        {
            rows.AddRange(up.All.Where(m => m.ReplacesInstrument && up.Is(m)).Select(m => PfdParts.Of(m.Place) + Flag(m)));
        }
        rows.AddRange(up.All.Where(m => m.Place == PfdPlace.Message && up.Is(m)).Select(m => PfdParts.Message + m.Text));
        return rows;
    }

    /// <summary>The minimums as <paramref name="rows"/> show them ("BARO 200", "RADIO 50"), null without a minimums row
    /// (MINS RST's read-back in the window).</summary>
    public static string? MinimumsShown(IReadOnlyList<string> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        const string prefix = PfdParts.Attitude + MinimumsRow;
        return rows.FirstOrDefault(r => r.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    /// <summary>How the attitude's minimums row starts, after the part's name.</summary>
    private const string MinimumsRow = "minimums, ";

    /// <summary>The armed roll column's text as PMDG draws it ("LNAV VOR/LOC"), "" while it draws none.</summary>
    public static string ArmedRoll(IReadOnlyList<PfdFmaDraw> draws, Func<PfdExpr, double> value)
    {
        ArgumentNullException.ThrowIfNull(draws);
        var words = new List<string>();
        foreach (var d in draws.Where(d => value(d.Shown) != 0))
        {
            double k = d.Index is { } index ? value(index) : -1;
            string word = d.Literal ?? (k >= 0 && k < d.Words.Count ? d.Words[(int)k] : "");
            if (word.Length > 0) words.Add(word);
        }
        return string.Join(" ", words);
    }

    /// <summary>The texts of the messages up whose <see cref="PfdMessage.Spoken"/> holds, in layout order.</summary>
    public static IReadOnlyList<string> SpokenMessages(PfdSample s, Pmdg737PfdLayout l)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(l);
        var up = new MessagesUp(s, l);
        return up.All.Where(m => m.Spoken && up.Is(m)).Select(m => m.Text).ToList();
    }

    /// <summary>The FMA reader's columns, the roll column's armed part replaced by the text PMDG draws there whenever it
    /// draws one (the FMA reader's autopilot field never holds LNAV armed beside another mode, nor alone); unchanged without
    /// a full reading.</summary>
    private static IReadOnlyList<string> WithPfdArmedRoll(IReadOnlyList<string> columns, PfdSample s, Pmdg737PfdLayout l)
    {
        if (s.Reading is not { } reading) return columns;
        string armed;
        try
        {
            armed = ArmedRoll(l.FmaStrip.ArmedRoll, reading.Value);
        }
        catch (InvalidOperationException)
        {
            return columns;
        }
        if (armed.Length == 0) return columns;
        string roll = Pmdg737FmaSpeech.RollLabel + ": ";
        return columns.Select(c => c.StartsWith(roll, StringComparison.Ordinal) ? Pmdg737FmaSpeech.WithArmed(c, armed) : c).ToList();
    }

    /// <summary>One part's rows, each with the part's name; a field the reading did not read, or a number PMDG's "%d"
    /// cannot print (past a long's range), makes the part one row.</summary>
    private static void Part(List<string> rows, string name, Action<List<string>> part)
    {
        var own = new List<string>();
        try
        {
            part(own);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            own = new List<string> { "not read" };
        }
        rows.AddRange(own.Select(r => name + r));
    }

    private static string Flag(PfdMessage m) => m.Text + " flag";

    /// <summary>A speed bug's name: PMDG's label, else what the layout singles it out as (the one PMDG parks: the
    /// selected speed; REF's value plus a constant: VREF plus it; a choice of two constants: that many knots; the one
    /// whose gate the flap bugs' choice reads: V2 plus 15; the one gated by the WT readout's selector: the speed
    /// reference), else "bug".</summary>
    internal static string SpeedBugName(PfdSpeedFields sp, PfdBug bug, double value)
    {
        if (bug.Label is { Length: > 0 } label) return label + " bug";
        if (bug.ParksAtEdge) return "selected speed bug";
        var refBug = sp.Bugs.FirstOrDefault(b => b.Label == "REF");
        if (refBug is not null && bug.Value is PfdBinary { Op: PfdOp.Add, Right: PfdConst plus } add && add.Left == refBug.Value)
            return "VREF plus " + Whole(plus.Value) + " bug";
        if (bug.Value is PfdSelect { IfTrue: PfdConst, IfFalse: PfdConst }) return Whole(value) + " knot bug";
        if (sp.FlapBugs.Inputs.Any(i => Contains(bug.Shown, i.Value) && Shares(i.Value, bug.Value))) return "V2 plus 15 bug";
        var wt = sp.Readouts.FirstOrDefault(r => r.Label == "WT");
        if (wt is not null && Shares(bug.Shown, wt.Shown)) return "speed reference bug";
        return "bug";
    }

    /// <summary>Which of the layout's messages the sample has up (<see cref="PfdSample.MessagesUp"/>, in
    /// <see cref="Pmdg737PfdLayout.AllMessages"/> order).</summary>
    private sealed class MessagesUp
    {
        private readonly PfdSample _s;
        private readonly Dictionary<PfdMessage, int> _index = new(ReferenceEqualityComparer.Instance);

        public MessagesUp(PfdSample s, Pmdg737PfdLayout l)
        {
            _s = s;
            All = l.AllMessages();
            for (int k = 0; k < All.Count; k++) _index.TryAdd(All[k], k);
        }

        public IReadOnlyList<PfdMessage> All { get; }

        public bool Is(PfdMessage m) => _index.TryGetValue(m, out int k) && k < _s.MessagesUp.Count && _s.MessagesUp[k];

        public List<PfdMessage> Flags(PfdPlace place) => All.Where(m => m.ReplacesInstrument && m.Place == place && Is(m)).ToList();
    }

    /// <summary>One full reading's parts, each adding its rows without the part name.</summary>
    private sealed class Context
    {
        private readonly Pmdg737PfdLayout _l;
        private readonly PfdValues _v;
        private readonly MessagesUp _up;

        public Context(Pmdg737PfdLayout l, PfdValues v, MessagesUp up)
        {
            _l = l;
            _v = v;
            _up = up;
        }

        private bool Holds(PfdExpr e) => _v.Holds(e);

        private double Value(PfdExpr e) => _v.Value(e);

        // ---- the speed tape ----

        public void AboveSpeedTape(List<string> rows) => rows.AddRange(SpeedReadouts(PfdPlace.AboveSpeedTape));

        public void BelowSpeedTape(List<string> rows) => rows.AddRange(SpeedReadouts(PfdPlace.BelowSpeedTape));

        private IEnumerable<string> SpeedReadouts(PfdPlace place) =>
            _l.Speed.Readouts.Where(r => r.Place == place && Holds(r.Shown)).Select(SpeedReadout).ToList();

        private string SpeedReadout(PfdReadout r)
        {
            string printed = Print(r);
            if (r.Label.Length > 0) return r.Label + " " + printed;
            if (r.Place == PfdPlace.AboveSpeedTape) return "selected speed " + printed;
            var refBug = _l.Speed.Bugs.FirstOrDefault(b => b.Label == "REF");
            if (refBug is not null && (Shares(r.Value, refBug.Value) || r.Second is not null && Shares(r.Second, refBug.Value))) return "REF " + printed;
            if (r.Format.StartsWith('.')) return "Mach " + printed;
            return printed;
        }

        public void SpeedTape(List<string> rows)
        {
            var sp = _l.Speed;
            var flags = _up.Flags(PfdPlace.SpeedTape);
            if (flags.Count > 0)
            {
                rows.AddRange(flags.Select(Flag));
                return;
            }
            if (!Holds(sp.TapeShown))
            {
                rows.Add("not shown");
                return;
            }
            double ias = Value(sp.Ias);
            var strip = new Strip(ias, sp.Geometry, "speed");
            string box = Whole(Value(sp.Box)) + " knots";
            if (Holds(sp.TrendShown)) box += ", trend arrow to " + Whole(Value(sp.Trend));
            strip.Box(box);
            foreach (var bug in sp.Bugs.Where(b => Holds(b.Shown)))
            {
                double value = Value(bug.Value);
                strip.Bug(SpeedBugName(sp, bug, value), value, bug.ParksAtEdge);
            }
            string? flapsNotRead = null;
            var fb = sp.FlapBugs;
            if (Holds(fb.Shown))
            {
                var drawn = fb.Drawn(fb.Inputs.Select(i => Value(i.Value)).ToList());
                if (drawn is null) flapsNotRead = "flap bugs, not read";
                else
                    foreach (int k in drawn.Where(k => k < fb.Labels.Count))
                    {
                        double speed = Value(fb.Speeds with { Index = new PfdConst(k) });
                        if (Math.Abs(speed - ias) < fb.Window) strip.Bug(fb.Labels[k] + " bug", speed, parks: false);
                    }
            }
            foreach (var band in sp.Bands.Where(b => Holds(b.Shown))) strip.Band(band.Words, Value(band.From), band.To is null ? null : Value(band.To), band.Upward, withTo: false);
            rows.AddRange(strip.Rows());
            if (flapsNotRead is not null) rows.Add(flapsNotRead);
        }

        // ---- the attitude ----

        public void Attitude(List<string> rows)
        {
            var att = _l.Attitude;
            var opt = _l.Optional;
            var approach = opt.Approach;

            if (approach is not null) ApproachInfo(rows, approach);
            else NotRead(rows, Pmdg737PfdParts.Approach);

            if (opt.Aoa is { } aoa)
            {
                if (Holds(aoa.Shown))
                {
                    string row = Holds(aoa.Readout.Shown) ? "angle of attack " + Print(aoa.Readout) : "angle of attack dial";
                    if (Holds(aoa.LimitShown)) row += ", limit " + Value(aoa.Limit).ToString("0.0", Inv);
                    rows.Add(row);
                }
            }
            else NotRead(rows, Pmdg737PfdParts.Aoa);

            if (opt.Markers is { } markers) rows.AddRange(markers.Markers.Where(m => Holds(m.Shown)).Select(m => "marker beacon, " + m.Text));
            else NotRead(rows, Pmdg737PfdParts.Markers);

            var flags = _up.Flags(PfdPlace.Attitude).Where(f => !IsApproachFlag(f)).ToList();
            if (Holds(att.Shown))
            {
                rows.Add("pitch " + PitchWords(Value(att.Pitch)) + ", " + BankWords(Value(att.Bank)));
                rows.Add("slip indicator, " + SlipWords(Value(att.Slip) * att.SlipPixelsPerUnit, Holds(att.SlipFilled)));
            }
            else if (!flags.Any(f => Shares(f.Shown, att.Shown))) rows.Add("not shown");
            rows.AddRange(flags.Select(Flag));

            if (opt.Pli is { } pli)
            {
                if (Holds(pli.Shown)) rows.Add("pitch limit indicator, " + DegreesAboveBelow(G.HalfDegrees(Value(pli.Above))));
            }
            else NotRead(rows, Pmdg737PfdParts.Pli);

            var fpv = att.Fpv;
            if (Holds(fpv.Shown))
            {
                double up = G.HalfDegrees(Value(fpv.Vertical));
                int right = G.WholeDegrees(Value(fpv.Lateral));
                var parts = new List<string>();
                if (up != 0) parts.Add(DegreesAboveBelow(up));
                if (right != 0) parts.Add(DegreesRightLeft(right));
                rows.Add("flight path vector, " + (parts.Count == 0 ? "centred" : string.Join(", ", parts)));
            }

            var fd = att.FlightDirector;
            if (Holds(fd.PitchShown))
            {
                double bar = G.FdPitchDegrees(Value(fd.PitchBar));
                rows.Add("flight director pitch bar, " + (bar == 0 ? "centred" : DegreesAboveBelow(bar)));
            }
            if (Holds(fd.RollShown))
            {
                double bar = Value(fd.RollBar);
                var roll = G.FdRoll(bar, Value(att.Bank), Value(fd.RollCommand), fd);
                rows.Add("flight director roll bar, " + (roll.AtEdge ? $"at the {(bar > 0 ? "right" : "left")} edge"
                    : roll.DegreesRight == 0 ? "centred" : DegreesRightLeft(roll.DegreesRight)));
            }
            if (Holds(fd.SingleCueShown)) rows.Add("flight director, single cue, not read");

            if (opt.Tcas is { } tcas)
                foreach (var zone in tcas.PitchZones.Where(z => Holds(z.Shown)))
                {
                    double from = G.HalfDegrees(Value(zone.From));
                    rows.Add($"{zone.Words}, from {(from == 0 ? "level" : DegreesAboveBelow(from))}, {(zone.Upward ? "upward" : "downward")}");
                }
            else NotRead(rows, Pmdg737PfdParts.Tcas);

            if (opt.RisingRunway is { } runway)
            {
                if (Holds(runway.Shown))
                {
                    int risen = (int)Math.Round((1 - Math.Clamp(Value(runway.Height), 0, 1)) * 100, MidpointRounding.AwayFromZero);
                    rows.Add($"rising runway, {risen.ToString(Inv)}% risen, {Across(Value(runway.Lateral))}");
                }
            }
            else NotRead(rows, Pmdg737PfdParts.RisingRunway);

            if (approach is not null && Holds(approach.Glideslope.Shown)) rows.Add("glideslope pointer, " + Deviation(approach.Glideslope, vertical: true));
            if (opt.Nps is { } nps) Scale(rows, "vertical", nps.Vertical, vertical: true);
            else NotRead(rows, Pmdg737PfdParts.Nps);

            if (Holds(att.RadioAltitude.Shown)) rows.Add("radio altitude " + Print(att.RadioAltitude));
            if (Holds(att.RadioMinimums.Shown)) rows.Add(MinimumsRow + Print(att.RadioMinimums));
            foreach (var r in _l.Altitude.Readouts.Where(r => r.Place == PfdPlace.Attitude && Holds(r.Shown)))
                rows.Add(MinimumsRow + (r.Label + " " + Print(r)).Trim());

            if (approach is not null && Holds(approach.Localizer.Shown)) rows.Add("localizer pointer, " + Deviation(approach.Localizer, vertical: false));
            if (opt.Nps is { } lateral) Scale(rows, "lateral", lateral.Lateral, vertical: false);

            rows.AddRange(att.Messages.Where(m => m.Place == PfdPlace.Attitude && !m.ReplacesInstrument && _up.Is(m)).Select(m => m.Text));
        }

        private bool IsApproachFlag(PfdMessage m) => _l.Optional.Approach?.Messages.Any(x => ReferenceEquals(x, m)) == true;

        private void ApproachInfo(List<string> rows, PfdApproachFields a)
        {
            string head = _v.Text(a.Ident)?.Trim() ?? "";
            var after = new List<string>();
            foreach (var info in a.Info.Where(i => Holds(i.Shown)))
            {
                string printed = Print(info);
                if (info.Label == "/") head += "/" + printed.TrimEnd('°');
                else if (info.Label == "DME") after.Add(printed.Trim('-').Length == 0 ? "DME, no distance" : "DME " + printed);
                else after.Add((info.Label + " " + printed).Trim());
            }
            if (head.Length > 0) after.Insert(0, head);
            if (after.Count > 0) rows.Add("approach info, " + string.Join(", ", after));
            rows.AddRange(a.ModeLine.Where(m => Holds(m.Shown)).Select(m => "approach mode, " + m.Text));
            rows.AddRange(a.Messages.Where(_up.Is).Select(Flag));
        }

        /// <summary>A navigation performance scale: its bar (RNP and ANP), the pointer at a fraction of the RNP, and the
        /// deviation pointer.</summary>
        private void Scale(List<string> rows, string which, PfdNpsScale scale, bool vertical)
        {
            // The vertical RNP and ANP are feet, read whole; the lateral ones nautical miles to two places.
            string Size(PfdExpr e) => vertical ? Value(e).ToString("0", Inv) + " feet" : Value(e).ToString("0.00", Inv);
            if (Holds(scale.BarShown))
                rows.Add($"{which} performance scale, RNP {Size(scale.Rnp)}, ANP {Size(scale.Anp)}");
            if (Holds(scale.PointerShown))
            {
                double place = Value(scale.Pointer);
                double fraction = Math.Abs(place) / scale.Deviation.EdgePixels;
                int percent = (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
                rows.Add($"{which} performance pointer, " + (fraction >= 1 ? "at the " + Edge(place, vertical) + " edge"
                    : percent == 0 ? "centred" : $"{percent.ToString(Inv)}% of RNP {Side(place, vertical)}"));
            }
            if (Holds(scale.Deviation.Shown)) rows.Add($"{which} deviation, " + Deviation(scale.Deviation, vertical));
        }

        /// <summary>A deviation pointer: at the scale's edge where PMDG holds it, else in tenths of a dot (right or down
        /// positive, as PMDG places it).</summary>
        private string Deviation(PfdDeviation d, bool vertical)
        {
            double place = Value(d.Place);
            if (G.AtEdge(place, d.EdgePixels)) return "at the " + Edge(place, vertical) + " edge";
            double dots = G.TenthsOfDot(Value(d.Dots));
            return dots == 0 ? "centred" : $"{Math.Abs(dots).ToString("0.0", Inv)} dots {Side(dots, vertical)}";
        }

        /// <summary>Where a pointer sits, right or down positive: below, above, right or left.</summary>
        private static string Side(double v, bool vertical) => vertical ? (v > 0 ? "below" : "above") : (v > 0 ? "right" : "left");

        /// <summary>The scale's end a pointer is held at: bottom, top, right or left.</summary>
        private static string Edge(double v, bool vertical) => vertical ? (v > 0 ? "bottom" : "top") : (v > 0 ? "right" : "left");

        private void NotRead(List<string> rows, string part)
        {
            var failure = _l.Optional.Failures.FirstOrDefault(f => f.Part == part);
            if (failure is not null) rows.Add(PartName(part) + ", not read: " + failure.Reason);
        }

        private static string PartName(string part) =>
            part == Pmdg737PfdParts.Approach ? "approach info"
            : part == Pmdg737PfdParts.Markers ? "marker beacons"
            : part;

        // ---- the altitude tape ----

        public void AboveAltitudeTape(List<string> rows)
        {
            var alt = _l.Altitude;
            foreach (var r in alt.Readouts.Where(r => r.Place == PfdPlace.AboveAltitudeTape && Holds(r.Shown)))
            {
                if (IsMetres(r)) continue;
                if (r.Label.Length == 0 && r.Value == alt.SelectedBug.Value)
                    rows.Add("selected altitude " + Print(r) + Metres(alt.SelectedBug.Value, alt.Altitude));
                else rows.Add((r.Label + " " + Print(r)).Trim());
            }
        }

        public void AltitudeTape(List<string> rows)
        {
            var alt = _l.Altitude;
            var flags = _up.Flags(PfdPlace.AltitudeTape);
            if (flags.Count > 0)
            {
                rows.AddRange(flags.Select(Flag));
                return;
            }
            if (!Holds(alt.TapeShown))
            {
                rows.Add("not shown");
                return;
            }
            double altitude = Value(alt.Altitude);
            var strip = new Strip(altitude, alt.Geometry, "altitude");
            strip.Box(Whole(Value(alt.Box)) + " feet" + Metres(alt.Altitude, alt.SelectedBug.Value));
            if (Holds(alt.SelectedBug.Shown)) strip.Bug("selected altitude bug", Value(alt.SelectedBug.Value), alt.SelectedBug.ParksAtEdge);
            if (Holds(alt.MinimumsBug.Shown)) strip.Bug("minimums bug", Value(alt.MinimumsBug.Value), alt.MinimumsBug.ParksAtEdge);
            foreach (var band in alt.Bands.Where(b => Holds(b.Shown)))
                strip.Band(band.To is null ? band.Words : band.Words.Replace("bar", "landing bar", StringComparison.Ordinal),
                    Value(band.From), band.To is null ? null : Value(band.To), band.Upward, withTo: true);
            rows.AddRange(strip.Rows());
        }

        public void BelowAltitudeTape(List<string> rows)
        {
            var shown = _l.Altitude.Readouts.Where(r => r.Place == PfdPlace.BelowAltitudeTape && Holds(r.Shown)).ToList();
            var std = shown.FirstOrDefault(r => !r.Format.Contains('%'));
            if (std is null)
            {
                rows.AddRange(shown.Select(Print));
                return;
            }
            var preselect = shown.Where(r => !ReferenceEquals(r, std)).Select(Print).ToList();
            rows.Add(Print(std) + (preselect.Count > 0 ? ", preselect " + string.Join(", ", preselect) : ""));
        }

        /// <summary>An unlabelled readout above the altitude tape working out metres from the selected altitude or the
        /// altitude (joined to their rows, never one of its own).</summary>
        private bool IsMetres(PfdReadout r)
        {
            var alt = _l.Altitude;
            return r.Label.Length == 0 && r.Value != alt.SelectedBug.Value && (Shares(r.Value, alt.SelectedBug.Value) || Shares(r.Value, alt.Altitude));
        }

        /// <summary>", 914 metres" when PMDG prints the metres of <paramref name="source"/> (a metres readout reading it and
        /// not <paramref name="other"/>), else nothing.</summary>
        private string Metres(PfdExpr source, PfdExpr other)
        {
            var m = _l.Altitude.Readouts.FirstOrDefault(r => r.Place == PfdPlace.AboveAltitudeTape && IsMetres(r)
                && Shares(r.Value, source) && !Shares(r.Value, other) && Holds(r.Shown));
            return m is null ? "" : ", " + Whole(Value(m.Value)) + " metres";
        }

        // ---- the vertical speed and the heading ----

        public void VerticalSpeed(List<string> rows)
        {
            var vs = _l.VerticalSpeed;
            var flags = _up.Flags(PfdPlace.VerticalSpeed);
            if (flags.Count > 0)
            {
                rows.AddRange(flags.Select(Flag));
                return;
            }
            if (!Holds(vs.NeedleShown))
            {
                rows.Add("not shown");
                return;
            }
            double rate = Value(vs.Rate);
            if (Holds(vs.Number.Shown)) rows.Add(Print(vs.Number) + (rate > 0 ? " up" : " down"));
            else
            {
                double needle = Math.Round(rate / vs.ReadoutStep, MidpointRounding.AwayFromZero) * vs.ReadoutStep;
                rows.Add(needle == 0 ? "needle level" : $"needle, {Whole(Math.Abs(needle))} {(needle > 0 ? "up" : "down")}");
            }
            if (Holds(vs.SelectedBug.Shown))
            {
                double selected = Value(vs.SelectedBug.Value);
                rows.Add("selected bug, " + (Math.Round(selected) == 0 ? "level" : $"{Whole(Math.Abs(selected))} {(selected > 0 ? "up" : "down")}"));
            }
        }

        public void Heading(List<string> rows)
        {
            var h = _l.Heading;
            var flags = _up.Flags(PfdPlace.Heading);
            if (flags.Count > 0)
            {
                rows.AddRange(flags.Select(Flag));
                return;
            }
            if (!Holds(h.Shown))
            {
                rows.Add("not shown");
                return;
            }
            double heading = Value(h.Heading);
            rows.Add(Three(heading) + " " + (Holds(h.TrueReference) ? h.TrueLabel : h.MagneticLabel));
            bool bug = Holds(h.SelectedHeading.Shown);
            if (bug || Holds(h.SelectedReadout.Shown))
            {
                string row = "selected heading " + Three(Value(bug && !Holds(h.SelectedReadout.Shown) ? h.SelectedHeading.Value : h.SelectedReadout.Value));
                if (bug) row += ", " + Bearing(Value(h.SelectedHeading.Value), heading, h.HalfArcDegrees);
                rows.Add(row);
            }
            if (Holds(h.TrackShown))
            {
                double track = Value(h.Track);
                rows.Add("track " + Three(track) + ", " + Bearing(track, heading, h.HalfArcDegrees));
            }
        }

        private static string Bearing(double target, double heading, double halfArc)
        {
            int offset = G.HeadingOffset(target, heading);
            if (Math.Abs(offset) > halfArc) return $"at the {(offset > 0 ? "right" : "left")} edge";
            return offset == 0 ? "centred" : DegreesRightLeft(offset);
        }

        // ---- printing ----

        private string Print(PfdReadout r)
        {
            object[] args = r.Second is null ? new object[] { Value(r.Value) } : new object[] { Value(r.Value), Value(r.Second) };
            return PmdgPrintf.Format(r.Format, args).Trim();
        }
    }

    /// <summary>A tape's strip: the current-value box and what PMDG draws on the strip, read highest to lowest, the box
    /// first among equals; a parked bug at the edge.</summary>
    private sealed class Strip
    {
        private readonly double _current;
        private readonly PfdTapeGeometry _g;
        private readonly string _unit;
        private readonly List<(double Key, string Text)> _items = new();

        public Strip(double current, PfdTapeGeometry g, string unit)
        {
            _current = current;
            _g = g;
            _unit = unit;
        }

        public void Box(string text) => _items.Add((_current, text));

        public void Bug(string name, double value, bool parks)
        {
            switch (G.Place(value, _current, _g, parks))
            {
                case PfdStripPlace.On: _items.Add((value, $"{name}, {Whole(value)}, {Offset(value)}")); break;
                case PfdStripPlace.TopEdge: _items.Add((double.PositiveInfinity, name + ", at the top edge")); break;
                case PfdStripPlace.BottomEdge: _items.Add((double.NegativeInfinity, name + ", at the bottom edge")); break;
            }
        }

        /// <summary>A band from <paramref name="from"/> up or down; with <paramref name="withTo"/> a bar with both ends
        /// reads "from … to …" (the altitude tape's landing bars), else "from … up/down" and its offset.</summary>
        public void Band(string words, double from, double? to, bool upward, bool withTo)
        {
            if (!G.BandOnStrip(from, to, upward, _current, _g)) return;
            _items.Add((from, withTo && to is { } end
                ? $"{words} from {Whole(from)} to {Whole(end)}"
                : $"{words} from {Whole(from)} {(upward ? "up" : "down")}, {Offset(from)}"));
        }

        private string Offset(double value)
        {
            int n = G.Offset(value, _current);
            return n > 0 ? $"{n.ToString(Inv)} above" : n < 0 ? $"{(-n).ToString(Inv)} below" : "at the current " + _unit;
        }

        public IEnumerable<string> Rows() => _items.OrderByDescending(x => x.Key).Select(x => x.Text);
    }

    // ---- words ----

    private static string Whole(double v) => ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(Inv);

    private static string Three(double degrees) => (((long)Math.Round(degrees, MidpointRounding.AwayFromZero) % 360 + 360) % 360).ToString("000", Inv);

    private static string Degrees(double v) => Math.Abs(v).ToString("0.#", Inv) + "°";

    private static string DegreesAboveBelow(double v) => Degrees(v) + (v > 0 ? " above" : " below");

    private static string DegreesRightLeft(int v) => Degrees(v) + (v > 0 ? " right" : " left");

    private static string PitchWords(double pitch)
    {
        double p = G.HalfDegrees(pitch);
        return p == 0 ? "level" : Degrees(p) + (p > 0 ? " up" : " down");
    }

    /// <summary>PMDG's bank is positive left (it turns the horizon clockwise).</summary>
    private static string BankWords(double bank)
    {
        int b = G.WholeDegrees(bank);
        return b == 0 ? "wings level" : "bank " + Degrees(b) + (b > 0 ? " left" : " right");
    }

    /// <summary>The slip indicator by where PMDG draws it: centred within a pixel, "slightly" to a side while PMDG draws
    /// it hollow, the side alone once PMDG fills it.</summary>
    private static string SlipWords(double pixels, bool filled)
    {
        string side = pixels > 0 ? "right" : "left";
        return filled ? side : Math.Abs(pixels) < 1 ? "centred" : "slightly " + side;
    }

    /// <summary>A place across in ±1 of full deflection, right positive.</summary>
    private static string Across(double lateral)
    {
        if (Math.Abs(lateral) >= 1) return $"at the {(lateral > 0 ? "right" : "left")} edge";
        int percent = (int)Math.Round(Math.Abs(lateral) * 100, MidpointRounding.AwayFromZero);
        return percent == 0 ? "centred" : $"{percent.ToString(Inv)}% {(lateral > 0 ? "right" : "left")}";
    }

    /// <summary>Whether two expressions read a field in common (an index ignored).</summary>
    private static bool Shares(PfdExpr a, PfdExpr b)
    {
        var keys = PfdEval.Fields(b).Select(f => (f.Owner, f.Field)).ToHashSet();
        return PfdEval.Fields(a).Any(f => keys.Contains((f.Owner, f.Field)));
    }

    /// <summary>Whether <paramref name="sub"/> occurs in <paramref name="e"/>.</summary>
    private static bool Contains(PfdExpr e, PfdExpr sub) =>
        e == sub || e switch
        {
            PfdField { Index: { } i } => Contains(i, sub),
            PfdPointed p => Contains(p.Pointer, sub),
            PfdSelect s => Contains(s.Condition, sub) || Contains(s.IfTrue, sub) || Contains(s.IfFalse, sub),
            PfdUnary u => Contains(u.Operand, sub),
            PfdBinary b => Contains(b.Left, sub) || Contains(b.Right, sub),
            _ => false,
        };
}
