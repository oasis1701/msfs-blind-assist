namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The singletons the Captain's PFD reads (each a global and size).</summary>
public sealed record PfdObjects(GlassObject Glass, GlassObject Efis, GlassObject Sim, GlassObject Options);

/// <summary>A display unit that, showing <see cref="Page"/> (the PFD page or a classic EFIS page), draws the
/// Captain's side: always, or only while the lower DU selector holds <see cref="WhenSelector"/>.</summary>
public readonly record struct PfdCaptainUnit(int Unit, int Page, int? WhenSelector);

/// <summary>
/// The EFIS object's display units (<see cref="UnitCount"/> units <see cref="UnitStride"/> bytes apart, the page
/// value at <see cref="PageOffset"/>, the unit index at <see cref="IndexOffset"/>); the page value that draws
/// the glass PFD, the classic EFIS pages that draw an attitude indicator, the pages that leave
/// a unit dark; which units draw the Captain's side on those pages, as PMDG's display unit decides; the lower
/// DU selector (on the EFIS object, element <see cref="SelectorSide"/>); the unit's EFIS panel field
/// <see cref="UnitPanelOffset"/>, which the unit passes to the PFD page it draws; and the PFD page's fields that
/// keep the side (<see cref="PfdSideField"/>) and that panel (<see cref="PfdPanelField"/>), by which PMDG indexes
/// the glass data. Equality compares the
/// lists by content.
/// </summary>
public sealed record PfdUnitFields(
    int UnitCount,
    int UnitStride,
    int PageOffset,
    int IndexOffset,
    int PfdPage,
    IReadOnlyList<int> ClassicPages,
    IReadOnlyList<int> DarkPages,
    IReadOnlyList<PfdCaptainUnit> CaptainUnits,
    GlassField LowerSelector,
    int SelectorSide,
    int UnitPanelOffset,
    int PfdSideField,
    int PfdPanelField)
{
    public bool Equals(PfdUnitFields? other) =>
        other is not null
        && UnitCount == other.UnitCount && UnitStride == other.UnitStride && PageOffset == other.PageOffset
        && IndexOffset == other.IndexOffset && PfdPage == other.PfdPage && ClassicPages.SequenceEqual(other.ClassicPages)
        && DarkPages.SequenceEqual(other.DarkPages) && CaptainUnits.SequenceEqual(other.CaptainUnits)
        && LowerSelector == other.LowerSelector && SelectorSide == other.SelectorSide && UnitPanelOffset == other.UnitPanelOffset
        && PfdSideField == other.PfdSideField && PfdPanelField == other.PfdPanelField;

    public override int GetHashCode() => HashCode.Combine(UnitCount, UnitStride, PageOffset, PfdPage, CaptainUnits.Count, UnitPanelOffset);
}

/// <summary>
/// The EFIS control panels the PFD follows: panel k at EFIS + <see cref="PanelBase"/> + k ×
/// <see cref="PanelStride"/>; a unit's panel is its <see cref="PfdUnitFields.UnitPanelOffset"/> field, which
/// PMDG sets for the Captain's units from i[EFIS + <see cref="PanelIndexOffset"/>]. The fields are
/// relative to the panel, named as PMDG saves the panel's state.
/// </summary>
public sealed record PfdPanelFields(int PanelBase, int PanelStride, int PanelIndexOffset, GlassField Fpv, GlassField Mtrs, GlassField BaroStd, GlassField Powered);

/// <summary>Where on the PFD a row's thing sits, in screen order; the part names live in one table (PfdParts).</summary>
public enum PfdPlace { Fma, AboveSpeedTape, SpeedTape, BelowSpeedTape, Attitude, AboveAltitudeTape, AltitudeTape, BelowAltitudeTape, VerticalSpeed, Heading, Message }

/// <summary>A text PMDG prints while <see cref="Shown"/> holds: a flag in the place of the instrument it replaces,
/// or a message (place <see cref="PfdPlace.Message"/>); <see cref="Spoken"/> false for what the aircraft calls out.</summary>
public sealed record PfdMessage(string Text, PfdPlace Place, PfdExpr Shown, bool ReplacesInstrument, bool Spoken);

/// <summary>A tape's scale: pixels per unit (knot or foot); the visible half height in pixels around the box (the
/// slice is the current value ± HalfHeight / PixelsPerUnit); the lowest value its box shows (0 for none).</summary>
public sealed record PfdTapeGeometry(double PixelsPerUnit, double HalfHeightPixels, double Floor);

/// <summary>A bug on a tape: PMDG's printed label (null when it prints none), its value, when PMDG draws it (its
/// window around the current value included), and whether PMDG holds it at the tape's edge when out of view.</summary>
public sealed record PfdBug(string? Label, PfdExpr Value, PfdExpr Shown, bool ParksAtEdge);

/// <summary>A band on a tape: its words, where it starts, its other end (null: it runs off the tape), which way it
/// runs, and when PMDG draws it.</summary>
public sealed record PfdBand(string Words, PfdExpr From, PfdExpr? To, bool Upward, PfdExpr Shown);

/// <summary>A number PMDG prints at a fixed place: its label (empty for none), the format as printed, the value (and
/// a second for "%d/%d"), and when it is printed.</summary>
public sealed record PfdReadout(PfdPlace Place, string Label, string Format, PfdExpr Value, PfdExpr? Second, PfdExpr Shown);

/// <summary>
/// The flap manoeuvring bugs past the first: their labels by index (PMDG's label table), their speeds (an int per
/// index), the window around the current speed, when the section is drawn, and which indexes PMDG draws for each
/// case of what its choice reads (<see cref="Inputs"/>). Equality
/// compares the lists by content.
/// </summary>
public sealed record PfdFlapBugs(IReadOnlyList<string> Labels, PfdField Speeds, double Window, PfdExpr Shown,
    IReadOnlyList<PfdFlapInput> Inputs, IReadOnlyList<PfdFlapCase> Cases)
{
    public bool Equals(PfdFlapBugs? other) =>
        other is not null && Labels.SequenceEqual(other.Labels) && Speeds == other.Speeds && Window == other.Window && Shown == other.Shown
        && Inputs.SequenceEqual(other.Inputs) && Cases.SequenceEqual(other.Cases);

    public override int GetHashCode() => HashCode.Combine(Labels.Count, Speeds, Window, Cases.Count);

    /// <summary>The indexes PMDG draws for these input values: each matched to the case value it compares the same
    /// way with every constant PMDG tests it against (exactly, for an input PMDG counts from); null when no case
    /// matches (a combination the layout's cases do not cover: not read, never "none drawn").</summary>
    public IReadOnlyList<int>? Drawn(IReadOnlyList<double> values)
    {
        var key = new double[Inputs.Count];
        for (int i = 0; i < Inputs.Count; i++) key[i] = Inputs[i].Representative(values[i]);
        return Cases.FirstOrDefault(c => c.Values.SequenceEqual(key))?.Drawn;
    }
}

/// <summary>One input of the flap bugs' choice: its value, the constants PMDG compares it with, and the values the
/// cases were run for (every value from -1 to the label count for an input PMDG counts from).</summary>
public sealed record PfdFlapInput(PfdExpr Value, IReadOnlyList<double> Compared, IReadOnlyList<double> Candidates, bool Counted)
{
    public bool Equals(PfdFlapInput? other) =>
        other is not null && Value == other.Value && Compared.SequenceEqual(other.Compared) && Candidates.SequenceEqual(other.Candidates) && Counted == other.Counted;

    public override int GetHashCode() => HashCode.Combine(Value, Compared.Count, Candidates.Count, Counted);

    /// <summary>The candidate standing for <paramref name="value"/>: itself when run, else the nearest candidate
    /// on the same side of every compared constant.</summary>
    public double Representative(double value)
    {
        if (Candidates.Contains(value)) return value;
        int[] Signs(double v) => Compared.Select(c => Math.Sign(v - c)).ToArray();
        var signs = Signs(value);
        return Candidates.Where(c => Signs(c).SequenceEqual(signs)).OrderBy(c => Math.Abs(c - value)).Cast<double?>().FirstOrDefault()
            ?? Candidates.OrderBy(c => Math.Abs(c - value)).First();
    }
}

/// <summary>One run of the flap bugs' choice: the input values and the indexes drawn, in drawing order.</summary>
public sealed record PfdFlapCase(IReadOnlyList<double> Values, IReadOnlyList<int> Drawn)
{
    public bool Equals(PfdFlapCase? other) => other is not null && Values.SequenceEqual(other.Values) && Drawn.SequenceEqual(other.Drawn);

    public override int GetHashCode() => HashCode.Combine(Values.Count, Drawn.Count);
}

/// <summary>
/// The speed tape: the current speed (<see cref="Ias"/>, its box showing <see cref="Box"/>), when the tape is drawn
/// (else its flag), the trend arrow's tip in knots and when drawn, the bugs, PMDG's flap bugs, the bands, the
/// readouts around the tape, the messages PMDG prints on it, and the scale. Equality compares the lists by content.
/// </summary>
public sealed record PfdSpeedFields(PfdExpr Ias, PfdExpr Box, PfdExpr TapeShown, PfdExpr Trend, PfdExpr TrendShown,
    IReadOnlyList<PfdBug> Bugs, PfdFlapBugs FlapBugs, IReadOnlyList<PfdBand> Bands, IReadOnlyList<PfdReadout> Readouts,
    IReadOnlyList<PfdMessage> Messages, PfdTapeGeometry Geometry)
{
    public bool Equals(PfdSpeedFields? other) =>
        other is not null && Ias == other.Ias && Box == other.Box && TapeShown == other.TapeShown && Trend == other.Trend
        && TrendShown == other.TrendShown && Bugs.SequenceEqual(other.Bugs) && FlapBugs == other.FlapBugs && Bands.SequenceEqual(other.Bands)
        && Readouts.SequenceEqual(other.Readouts) && Messages.SequenceEqual(other.Messages) && Geometry == other.Geometry;

    public override int GetHashCode() => HashCode.Combine(Ias, Bugs.Count, Bands.Count, Readouts.Count, Messages.Count, Geometry);
}

/// <summary>A position of the speed reference selector where its inner knob (EVT_MPM_SPEED_REFERENCE_CONTROL) changes
/// a value: the value, its step per click, and its limits.</summary>
public sealed record PfdKnobPosition(int Position, PfdField Value, double Step, double Min, double Max);

/// <summary>The speed reference selector (its position) and the positions where the inner knob sets a value.
/// Equality compares the list by content.</summary>
public sealed record PfdSpeedReferenceFields(PfdField Selector, IReadOnlyList<PfdKnobPosition> Positions)
{
    public bool Equals(PfdSpeedReferenceFields? other) => other is not null && Selector == other.Selector && Positions.SequenceEqual(other.Positions);

    public override int GetHashCode() => HashCode.Combine(Selector, Positions.Count);
}

/// <summary>
/// The flight director's split-axis bars (PMDG's option): each bar's place as PMDG draws it, its clamp included (the
/// pitch bar in degrees above the attitude's centre, the roll bar in steps right of it; worked out only where the bar
/// is drawn), the commands PMDG works them out from (raw, for later use), the pixels per degree and per step, the roll
/// bar's farthest place either way, and when each bar is drawn; and when the single cue PMDG draws otherwise is drawn
/// (where it points is not read).
/// </summary>
public sealed record PfdFlightDirectorFields(PfdExpr PitchShown, PfdExpr RollShown, PfdExpr PitchBar, PfdExpr RollBar,
    PfdField PitchCommand, PfdField RollCommand, double PitchBarPixelsPerDegree, double RollBarPixelsPerUnit, double RollBarFullScalePixels,
    PfdExpr SingleCueShown);

/// <summary>The flight path vector: where PMDG draws it from the attitude's centre in degrees, above and right (in the
/// horizon's frame: PMDG turns it with the bank), the pixels per degree each way, and when it is drawn.</summary>
public sealed record PfdFpvFields(PfdExpr Shown, PfdExpr Vertical, PfdExpr Lateral, double VerticalPixelsPerDegree, double LateralPixelsPerDegree);

/// <summary>
/// The attitude indicator: when PMDG draws it (else its flag, or nothing while the IRU aligns), the pitch (degrees,
/// nose up positive) and the bank (degrees, as PMDG turns the horizon: positive clockwise on the screen), the slip
/// indicator's value, pixels per unit and when PMDG fills it (hollow below its threshold), the pitch scale's pixels per
/// degree, the flight director, the flight path
/// vector, the radio altitude and radio minimums readouts, and the flags and alerts PMDG prints there.
/// Equality compares the list by content.
/// </summary>
public sealed record PfdAttitudeFields(PfdExpr Shown, PfdExpr Pitch, PfdExpr Bank, PfdExpr Slip, double SlipPixelsPerUnit, PfdExpr SlipFilled,
    double PitchPixelsPerDegree, PfdFlightDirectorFields FlightDirector, PfdFpvFields Fpv, PfdReadout RadioAltitude, PfdReadout RadioMinimums,
    IReadOnlyList<PfdMessage> Messages)
{
    public bool Equals(PfdAttitudeFields? other) =>
        other is not null && Shown == other.Shown && Pitch == other.Pitch && Bank == other.Bank && Slip == other.Slip
        && SlipPixelsPerUnit == other.SlipPixelsPerUnit && SlipFilled == other.SlipFilled && PitchPixelsPerDegree == other.PitchPixelsPerDegree && FlightDirector == other.FlightDirector
        && Fpv == other.Fpv && RadioAltitude == other.RadioAltitude && RadioMinimums == other.RadioMinimums && Messages.SequenceEqual(other.Messages);

    public override int GetHashCode() => HashCode.Combine(Pitch, Bank, Slip, FlightDirector, Fpv, Messages.Count);
}

/// <summary>
/// The altitude tape: the altitude (the altimeter of the unit's EFIS panel), the number its box shows (to the step its
/// finest digit drum turns in; PMDG's drum for a negative altitude is not read), when the tape is drawn (else its ALT
/// flag), the selected altitude bug (held at the tape's edge) and the BARO minimums bug, the landing altitude (its line
/// hatched below, and PMDG's optional bar above it in two parts), the readouts around the tape, its messages, and the
/// scale. Equality compares the lists by content.
/// </summary>
public sealed record PfdAltitudeFields(PfdExpr Altitude, PfdExpr Box, double ReadoutStep, PfdExpr TapeShown, PfdBug SelectedBug, PfdBug MinimumsBug,
    IReadOnlyList<PfdBand> Bands, IReadOnlyList<PfdReadout> Readouts, IReadOnlyList<PfdMessage> Messages, PfdTapeGeometry Geometry)
{
    public bool Equals(PfdAltitudeFields? other) =>
        other is not null && Altitude == other.Altitude && Box == other.Box && ReadoutStep == other.ReadoutStep && TapeShown == other.TapeShown
        && SelectedBug == other.SelectedBug && MinimumsBug == other.MinimumsBug && Bands.SequenceEqual(other.Bands)
        && Readouts.SequenceEqual(other.Readouts) && Messages.SequenceEqual(other.Messages) && Geometry == other.Geometry;

    public override int GetHashCode() => HashCode.Combine(Altitude, SelectedBug, Bands.Count, Readouts.Count, Messages.Count, Geometry);
}

/// <summary>
/// The vertical speed: the rate (feet per minute, climbing positive), when the needle is drawn (else the VERT flag), the
/// number PMDG prints from <see cref="ReadoutThreshold"/> fpm in <see cref="ReadoutStep"/> steps and whether it is
/// printed above the scale, the selected vertical speed bug (held at the scale's end), and the messages. Equality
/// compares the list by content.
/// </summary>
public sealed record PfdVerticalSpeedFields(PfdExpr Rate, PfdExpr NeedleShown, PfdReadout Number, PfdExpr NumberAbove, double ReadoutThreshold,
    double ReadoutStep, PfdBug SelectedBug, IReadOnlyList<PfdMessage> Messages)
{
    public bool Equals(PfdVerticalSpeedFields? other) =>
        other is not null && Rate == other.Rate && NeedleShown == other.NeedleShown && Number == other.Number && NumberAbove == other.NumberAbove
        && ReadoutThreshold == other.ReadoutThreshold && ReadoutStep == other.ReadoutStep && SelectedBug == other.SelectedBug
        && Messages.SequenceEqual(other.Messages);

    public override int GetHashCode() => HashCode.Combine(Rate, Number, ReadoutThreshold, ReadoutStep, SelectedBug, Messages.Count);
}

/// <summary>
/// The heading: the compass's heading (magnetic, or true with the variation added), when the compass is drawn (else
/// the HDG flag), the track and the selected heading bug (each held at the edge of the ±<see cref="HalfArcDegrees"/> the
/// compass shows), the selected heading readout, the reference labels (the true one while <see cref="TrueReference"/>
/// holds), and the messages. Equality compares the list by content.
/// </summary>
public sealed record PfdHeadingFields(PfdExpr Heading, PfdExpr Shown, PfdExpr Track, PfdExpr TrackShown, PfdBug SelectedHeading,
    double HalfArcDegrees, PfdReadout SelectedReadout, PfdExpr TrueReference, string MagneticLabel, string TrueLabel, IReadOnlyList<PfdMessage> Messages)
{
    public bool Equals(PfdHeadingFields? other) =>
        other is not null && Heading == other.Heading && Shown == other.Shown && Track == other.Track && TrackShown == other.TrackShown
        && SelectedHeading == other.SelectedHeading && HalfArcDegrees == other.HalfArcDegrees && SelectedReadout == other.SelectedReadout
        && TrueReference == other.TrueReference && MagneticLabel == other.MagneticLabel && TrueLabel == other.TrueLabel
        && Messages.SequenceEqual(other.Messages);

    public override int GetHashCode() => HashCode.Combine(Heading, Track, SelectedHeading, HalfArcDegrees, Messages.Count);
}

/// <summary>
/// The texts PMDG's FMA strip draws beyond the four autopilot columns the FMA reader speaks (place
/// <see cref="PfdPlace.Fma"/>, not spoken), and the armed roll column as PMDG draws it: the glass data's armed roll value
/// shows LNAV armed beside another mode (or alone) where the FMA reader's autopilot field does not, in literals PMDG draws in
/// the column's place for some values and a word of its table for the others. Equality compares the lists by content.
/// </summary>
public sealed record PfdFmaStripFields(IReadOnlyList<PfdMessage> Extras, IReadOnlyList<PfdFmaDraw> ArmedRoll)
{
    public bool Equals(PfdFmaStripFields? other) => other is not null && Extras.SequenceEqual(other.Extras) && ArmedRoll.SequenceEqual(other.ArmedRoll);

    public override int GetHashCode() => HashCode.Combine(Extras.Count, ArmedRoll.Count);
}

/// <summary>One text an FMA column draws, in draw order, and when: a <see cref="Literal"/>, or the word of PMDG's table at
/// <see cref="Index"/> (the table's <see cref="Words"/> read from the file, as many as the draw's own range test allows, or
/// to the table's last word where the draw has none, as in the MSFS 2020 file; a value past them is no word).
/// Equality compares the words by content.</summary>
public sealed record PfdFmaDraw(PfdExpr Shown, string? Literal, PfdExpr? Index, IReadOnlyList<string> Words)
{
    public bool Equals(PfdFmaDraw? other) => other is not null && Shown == other.Shown && Literal == other.Literal && Index == other.Index
        && Words.SequenceEqual(other.Words);

    public override int GetHashCode() => HashCode.Combine(Shown, Literal, Index, Words.Count);
}

/// <summary>The marker beacons: each text PMDG draws in its ring (OM, MM, IM) and when. Equality compares the list
/// by content.</summary>
public sealed record PfdMarkerFields(IReadOnlyList<PfdMessage> Markers)
{
    public bool Equals(PfdMarkerFields? other) => other is not null && Markers.SequenceEqual(other.Markers);

    public override int GetHashCode() => Markers.Count;
}

/// <summary>The pitch limit indicator: when PMDG draws it, and where, in degrees above the attitude's centre.</summary>
public sealed record PfdPliFields(PfdExpr Shown, PfdExpr Above, double PixelsPerDegree);

/// <summary>The rising runway: when PMDG draws it, its place across (right positive) and its height above its lowest
/// place, each as PMDG works it out before scaling (its clamps included), and the pixels each way.</summary>
public sealed record PfdRisingRunwayFields(PfdExpr Shown, PfdExpr Lateral, PfdExpr Height, double LateralPixelsPerUnit, double HeightPixelsPerUnit);

/// <summary>The angle of attack dial: when PMDG draws it, the number it prints, the needle (degrees, its clamp
/// included), and the limit mark with when it is drawn.</summary>
public sealed record PfdAoaFields(PfdExpr Shown, PfdReadout Readout, PfdExpr Needle, PfdExpr Limit, PfdExpr LimitShown);

/// <summary>The TCAS resolution advisory's pitch zones on the attitude: each zone's edge in degrees above the
/// attitude's centre, which way it runs, and when it is drawn. Equality compares the list by content.</summary>
public sealed record PfdTcasFields(IReadOnlyList<PfdBand> PitchZones)
{
    public bool Equals(PfdTcasFields? other) => other is not null && PitchZones.SequenceEqual(other.PitchZones);

    public override int GetHashCode() => PitchZones.Count;
}

/// <summary>A pointer on a deviation scale: when PMDG draws it, the deviation in dots as PMDG works it out (the field ×
/// 2), its place in pixels from the scale's centre as PMDG draws it (right or down positive, its clamp included), and
/// the place it is held at either way (the scale's edge).</summary>
public sealed record PfdDeviation(PfdExpr Shown, PfdExpr Dots, PfdExpr Place, double EdgePixels);

/// <summary>
/// One navigation performance scale: the RNP and ANP its bar is drawn from (while <see cref="BarShown"/>), the
/// deviation pointer in dots, and the pointer PMDG places at a fraction of the RNP (<see cref="Pointer"/>, the fraction ×
/// the edge in pixels, held at the edge) while <see cref="PointerShown"/>.
/// </summary>
public sealed record PfdNpsScale(PfdExpr Rnp, PfdExpr Anp, PfdExpr BarShown, PfdDeviation Deviation, PfdExpr Pointer, PfdExpr PointerShown);

/// <summary>The navigation performance scales: the lateral one below the attitude, the vertical one beside it.</summary>
public sealed record PfdNpsFields(PfdNpsScale Lateral, PfdNpsScale Vertical);

/// <summary>A C string PMDG keeps in an object's field (per side, <see cref="Stride"/> apart), at most
/// <see cref="Length"/> bytes, and when PMDG draws it.</summary>
public sealed record PfdText(GlassObject Owner, int Offset, int Stride, int Length, PfdExpr Shown);

/// <summary>
/// The approach part PMDG draws: the approach info block (the ident, a text; the readouts after it, each
/// labelled by the text PMDG draws just before it: "/" the course, "DME" the distance, the alternatives each with when
/// printed), the localizer and glideslope pointers, the approach mode line (texts, each with when drawn), and the flags
/// standing in for the pointers and the DME (LOC, G/S, DME). The FMC runway line (RW%s and its distance) is chosen by
/// PMDG's route, which is not read. Equality compares the lists by content.
/// </summary>
public sealed record PfdApproachFields(PfdText Ident, IReadOnlyList<PfdReadout> Info, PfdDeviation Localizer, PfdDeviation Glideslope,
    IReadOnlyList<PfdMessage> ModeLine, IReadOnlyList<PfdMessage> Messages)
{
    public bool Equals(PfdApproachFields? other) =>
        other is not null && Ident == other.Ident && Info.SequenceEqual(other.Info) && Localizer == other.Localizer && Glideslope == other.Glideslope
        && ModeLine.SequenceEqual(other.ModeLine) && Messages.SequenceEqual(other.Messages);

    public override int GetHashCode() => HashCode.Combine(Ident, Info.Count, Localizer, Glideslope, ModeLine.Count, Messages.Count);
}

/// <summary>An optional part missing from the layout, by name, and why.</summary>
public sealed record PfdPartFailure(string Part, string Reason);

/// <summary>
/// The PFD's optional parts: each null when the layout file has none, with the reason in <see cref="Failures"/>;
/// the required parts work without them. Equality compares the list by content.
/// </summary>
public sealed record PfdOptionalParts(PfdApproachFields? Approach, PfdMarkerFields? Markers, PfdRisingRunwayFields? RisingRunway,
    PfdNpsFields? Nps, PfdTcasFields? Tcas, PfdAoaFields? Aoa, PfdPliFields? Pli, IReadOnlyList<PfdPartFailure> Failures)
{
    public bool Equals(PfdOptionalParts? other) =>
        other is not null && Approach == other.Approach && Markers == other.Markers && RisingRunway == other.RisingRunway && Nps == other.Nps
        && Tcas == other.Tcas && Aoa == other.Aoa && Pli == other.Pli && Failures.SequenceEqual(other.Failures);

    public override int GetHashCode() => HashCode.Combine(Approach, Markers, RisingRunway, Nps, Tcas, Aoa, Pli, Failures.Count);
}

/// <summary>
/// Where one installed 737NG3.wasm keeps what the Captain's glass PFD shows, all from that file's layout file,
/// never hardcoded. The anchor locates the module's memory even when the other readers'
/// cannot. Equality compares the anchor and every part by content and ignores <see cref="SourceFile"/>, so the same
/// build installed twice gives one layout.
/// </summary>
public sealed record Pmdg737PfdLayout(string SourceFile, uint AnchorAddress, byte[] Anchor, PfdObjects Objects, PfdUnitFields Units,
    PfdPanelFields Panel, PfdSpeedFields Speed, PfdSpeedReferenceFields SpeedReference, PfdAttitudeFields Attitude, PfdAltitudeFields Altitude,
    PfdVerticalSpeedFields VerticalSpeed, PfdHeadingFields Heading, PfdFmaStripFields FmaStrip, PfdOptionalParts Optional)
{
    public bool Equals(Pmdg737PfdLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && Objects == other.Objects && Units.Equals(other.Units) && Panel == other.Panel && Speed.Equals(other.Speed)
        && SpeedReference.Equals(other.SpeedReference) && Attitude.Equals(other.Attitude) && Altitude.Equals(other.Altitude)
        && VerticalSpeed.Equals(other.VerticalSpeed) && Heading.Equals(other.Heading) && FmaStrip.Equals(other.FmaStrip) && Optional.Equals(other.Optional);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(Objects);
        h.Add(Units);
        h.Add(Speed);
        h.Add(Attitude);
        return h.ToHashCode();
    }

    /// <summary>Every message in one fixed order: the speed tape's, the attitude's, the altitude tape's, the vertical
    /// speed's, the heading's, the FMA strip's, then the approach part's when the layout has it.</summary>
    public IReadOnlyList<PfdMessage> AllMessages() =>
        Speed.Messages.Concat(Attitude.Messages).Concat(Altitude.Messages).Concat(VerticalSpeed.Messages).Concat(Heading.Messages)
            .Concat(FmaStrip.Extras).Concat(Optional.Approach?.Messages ?? Array.Empty<PfdMessage>()).ToList();
}

/// <summary>The PFD's result in a layout file: the layout, or why the file was not recognised.</summary>
public sealed record PfdDerivation(Pmdg737PfdLayout? Layout, string? Failure);
