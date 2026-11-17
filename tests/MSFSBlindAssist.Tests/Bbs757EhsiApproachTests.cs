// The 757 EHSI's VOR and ILS picture (plan 28): read from the nd, the PFD's glideslope, the DME and the VOR panels
// through the layout's Approach part, listed as BlueBird draws it, its LOC and G/S flags spoken. The values are plan 28's
// live measurements at the KSEA gate (SEA on 116.80, course 187; ISNQ on 110.30, course 164), placed through the test
// layout.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiApproachTests
{
    internal static readonly Bbs757EhsiLayout P = Bbs757EhsiLayoutTests.SampleWithParts();
    private static Bbs757EhsiApproach A => P.Approach!;

    // Where this test's memory puts the approach's objects (the scene's own end below 0x1B000).
    internal const uint Pfd = 0x1B000, Dme = 0x1C000, IlsStation = 0x1D000, SeaStation = 0x1D100, PaeStation = 0x1D200,
        VorL = 0x1E000, VorR = 0x1E800, Receiver = 0x1F000, Signal = 0x20000, Adf = 0x21000, AdfLeft = 0x21800,
        AdfRight = 0x21900, IlsPanelStation = 0x1D300;

    /// <summary>A navaid record: ident, kind byte, position, frequency (MHz × 100).</summary>
    internal sealed record Station(string Ident, int Kind, double Lat, double Lon, int Frequency = 0);

    internal static readonly Station Sea = new("SEA", 2, 47.43538, -122.30978, 11680);
    internal static readonly Station Isnq = new("ISNQ", 5, 47.43418, -122.31085, 11030);
    internal static readonly Station Pae = new("PAE", 2, 47.91983, -122.27778, 11040);

    /// <summary>The approach's memory as BlueBird keeps it, written over a <see cref="Scene"/> through its Tweak.</summary>
    internal sealed class Approach
    {
        public double VorCourse = 130, VorDeviation, VorDme, IlsCourse = 215, LocalizerDeviation, IlsFrequency = -1,
            Glideslope, DmeDistance, RmiLeft, RmiRight;
        public bool VorBar, VorDmeValid, LocalizerBar, Dots = true, IlsTest, LocFlag, GlideslopeFlag, GlideslopeScale,
            GlideslopePointer, GlideslopeTest, HasPfd = true, HasDme = true, DmePowered = true, DmeFailed, Received,
            HasReceiver = true, HasSignal = true;
        public int ToFrom, Side = 6, ManualWhole = 116, ManualFraction = 80;
        public Station? DmeStation, VorLStation, VorRStation, VorLIlsStation;
        public (double Lat, double Lon)? AdfLeftPosition, AdfRightPosition;

        public void Write(byte[] b)
        {
            F64(b, Nd + A.VorCourse, VorCourse);
            F64(b, Nd + A.VorDeviation, VorDeviation);
            Flag(b, Nd + A.VorDeviationValid, VorBar);
            I32(b, Nd + A.ToFrom, ToFrom);
            F64(b, Nd + A.VorDme, VorDme);
            Flag(b, Nd + A.VorDmeValid, VorDmeValid);
            F64(b, Nd + A.IlsCourse, IlsCourse);
            F64(b, Nd + A.LocalizerDeviation, LocalizerDeviation);
            Flag(b, Nd + A.LocalizerBar, LocalizerBar);
            Flag(b, Nd + A.IlsDots, Dots);
            Flag(b, Nd + A.IlsTest, IlsTest);
            F64(b, Nd + A.IlsFrequency, IlsFrequency);
            Flag(b, Nd + A.LocFlag, LocFlag);
            Flag(b, Nd + A.GlideslopeFlag, GlideslopeFlag);
            I32(b, Nd + A.DisplaySide, Side);

            I32(b, Nd + A.NdToPfd, HasPfd ? Pfd : 0);
            F64(b, Pfd + A.PfdToIls + A.GlideslopeDeviation, Glideslope);
            Flag(b, Pfd + A.PfdToIls + A.GlideslopeScale, GlideslopeScale);
            Flag(b, Pfd + A.PfdToIls + A.GlideslopePointer, GlideslopePointer);
            Flag(b, Pfd + A.PfdToIls + A.GlideslopeTest, GlideslopeTest);

            I32(b, Nd + A.NdToDme, HasDme ? Dme : 0);
            Flag(b, Dme + A.DmePowered, DmePowered);
            Flag(b, Dme + A.DmeFailed, DmeFailed);
            I32(b, Dme + A.DmeStation, DmeStation is null ? 0 : IlsStation);
            F64(b, Dme + A.DmeDistance, DmeDistance);
            if (DmeStation is { } dme) PutStation(b, IlsStation, dme);

            I32(b, Source + A.SourceToIlsReceiver, HasReceiver ? Receiver : 0);
            I32(b, Receiver + A.ReceiverToSignal, HasSignal ? Signal : 0);
            Flag(b, Signal + A.SignalBlock + A.LocalizerReceived, Received);

            F64(b, A.RmiLeftHandle + A.HandleValue, RmiLeft);
            F64(b, A.RmiRightHandle + A.HandleValue, RmiRight);
            I32(b, Nd + A.NdToVorLeft, VorL);
            I32(b, Nd + A.NdToVorRight, VorR);
            I32(b, VorL + A.PanelStation, VorLStation is null ? 0 : SeaStation);
            I32(b, VorR + A.PanelStation, VorRStation is null ? 0 : PaeStation);
            if (VorLStation is { } l) PutStation(b, SeaStation, l);
            if (VorRStation is { } r) PutStation(b, PaeStation, r);

            I32(b, Nd + A.NdToAdf, Adf);
            I32(b, Adf + AdfLeftAt, AdfLeftPosition is null ? 0 : AdfLeft);
            I32(b, Adf + AdfRightAt, AdfRightPosition is null ? 0 : AdfRight);
            if (AdfLeftPosition is { } lp) PutPosition(b, AdfLeft, lp);
            if (AdfRightPosition is { } rp) PutPosition(b, AdfRight, rp);

            // ILS mode's VOR station and the panel's manual frequency (VOR L only: VOR R's stays 0).
            I32(b, VorL + A.PanelIlsStation, VorLIlsStation is null ? 0 : IlsPanelStation);
            if (VorLIlsStation is { } ils) PutStation(b, IlsPanelStation, ils);
            I32(b, VorL + A.PanelManualWhole, ManualWhole);
            I32(b, VorL + A.PanelManualFraction, ManualFraction);
        }

        private static void PutStation(byte[] b, uint at, Station s)
        {
            for (int i = 0; i < A.StationIdentLength; i++) I32(b, at + A.StationIdent + 4 * i, i < s.Ident.Length ? s.Ident[i] : 0);
            b[at + A.StationKind] = (byte)s.Kind;
            b[at + A.StationFrequency] = (byte)(s.Frequency & 0xFF);
            b[at + A.StationFrequency + 1] = (byte)(s.Frequency >> 8);
            F64(b, at + A.StationLongitude, s.Lon);
            F64(b, at + A.StationLatitude, s.Lat);
        }

        /// <summary>An ADF station's position at the layout's longitude and latitude.</summary>
        private static void PutPosition(byte[] b, uint at, (double Lat, double Lon) p)
        {
            F64(b, at + A.AdfLongitude, p.Lon);
            F64(b, at + A.AdfLatitude, p.Lat);
        }
    }

    /// <summary>The ADF receivers' pointer fields in the ADF panel, for the layout that has their positions.</summary>
    private const int AdfLeftAt = 0x40, AdfRightAt = 0x44;

    private static readonly Bbs757EhsiLayout WithAdfPositions =
        P with { Approach = A with { AdfPositions = true, AdfLeft = AdfLeftAt, AdfRight = AdfRightAt } };

    /// <summary>The SEA-tuned VOR of the measurements: VOR L on 116.80, course 187, the aircraft on SEA's 010 radial.</summary>
    internal static Approach VorSea() => new()
    {
        VorCourse = 187, VorBar = true, VorDeviation = 0.255, ToFrom = 1, VorDmeValid = true, VorDme = 0.54, VorLStation = Sea,
    };

    /// <summary>The ISNQ-tuned ILS of the measurements: 110.30, course 164, pinned on the ramp.</summary>
    internal static Approach IlsIsnq() => new()
    {
        IlsCourse = 164, LocalizerBar = true, LocalizerDeviation = 1.0, IlsFrequency = 110.30, Received = true,
        GlideslopeScale = true, GlideslopePointer = true, Glideslope = 2.2, DmeStation = Isnq, DmeDistance = 0.56,
    };

    internal static Bbs757EhsiReading Read(Approach approach, int mode, bool full = false, Bbs757EhsiLayout? layout = null,
        Action<Scene>? scene = null)
    {
        var s = new Scene { Mode = mode, Tweak = approach.Write };
        scene?.Invoke(s);
        var reading = Bbs757EhsiReaderTests.Read(s, full, layout ?? P);
        Assert.False(reading.Corrupt);
        return reading;
    }

    /// <summary>The rows without the parts later tasks read (their "not read" rows).</summary>
    internal static string[] Rows(Bbs757EhsiReading r) =>
        Bbs757EhsiText.Rows(r).Where(row => !row.Contains(": not read: ", StringComparison.Ordinal)).ToArray();

    private static string[] Texts(IReadOnlyList<Bbs757SpokenLine> lines) => lines.Select(l => l.Text).ToArray();

    /// <summary>The scene's top right ETA as plan 26's MAP top line prints it (the same clock, leg and ground speed).</summary>
    private static string EtaRow() =>
        Bbs757EhsiText.Rows(Read(new Approach(), mode: 2)).Single(r => r.StartsWith("Top right: ", StringComparison.Ordinal));

    /// <summary>The scene's heading bug row (plan 26's): the bug 147 against the heading 119.</summary>
    private const string BugRow = "Heading bug: 147, 28° right";

    /// <summary>The scene's wind (plan 28's map extras): its show flag clear in memory, so the screen's dashes, at the
    /// bottom left (0.048 W, 0.895 H): after the rose's rows, before TO/FROM at the same height further right (0.835 W),
    /// the LOC flag (0.9 H) and the receiver's label and ILS frequency 31 px lower.</summary>
    private const string NoWindRow = "Bottom left: wind ---";

    // ---- the rows ----

    [Fact]
    public void Vor_mode_reads_to_from()
    {
        var r = Read(VorSea(), mode: 0);
        Assert.Equal(new[]
        {
            "EHSI: VOR, range 160",
            "Top: HDG 119 M",
            "Top left: DME 0.5",
            EtaRow(),
            "Center: course pointer 187, 68° right",
            "Center: deviation 0.5 dots right",
            NoWindRow,
            "Bottom right: TO",
            "Bottom left: VOR L",
            "Bearing pointer 1: VOR L, SEA, 74° right",
            BugRow,
        }, Rows(r));

        // FROM, the bar left of the course, the First Officer's side's label.
        var from = VorSea();
        from.ToFrom = -1;
        from.VorDeviation = -0.3;
        from.Side = 7;
        var rows = Rows(Read(from, mode: 0));
        Assert.Contains("Bottom right: FROM", rows);
        Assert.Contains("Center: deviation 0.6 dots left", rows);
        Assert.Contains("Bottom left: VOR R", rows);
        // Pinned, and centered under 0.05 dot.
        var pinned = VorSea();
        pinned.VorDeviation = 1.0;
        Assert.Contains("Center: deviation at full scale, right", Rows(Read(pinned, mode: 0)));
        var centered = VorSea();
        centered.VorDeviation = 0.02;
        Assert.Contains("Center: deviation centered", Rows(Read(centered, mode: 0)));
        // A DME of 100 NM or more is the integer, truncated.
        var far = VorSea();
        far.VorDme = 123.7;
        Assert.Contains("Top left: DME 123", Rows(Read(far, mode: 0)));
    }

    [Fact]
    public void Ils_mode_reads_course_deviation_and_dme()
    {
        var r = Read(IlsIsnq(), mode: 1);
        Assert.Equal(new[]
        {
            "EHSI: ILS, range 160",
            "Top: HDG 119 M",
            "Top left: DME 0.6",
            EtaRow(),
            "Center: course pointer 164, 45° right",
            "Center: deviation at full scale, right",
            "Right side: glideslope at full scale, above",
            NoWindRow,
            "Bottom left: ILS",
            "Bottom right: 110.30",
            BugRow,
        }, Rows(r));

        // Unpinned: the localizer in dots of 2 a unit, the glideslope already in dots, positive up.
        var off = IlsIsnq();
        off.LocalizerDeviation = -0.6;
        off.Glideslope = -1.24;
        var rows = Rows(Read(off, mode: 1));
        Assert.Contains("Center: deviation 1.2 dots left", rows);
        Assert.Contains("Right side: glideslope 1.2 dots below", rows);

        // The DME follows the ILS: a VOR station in the DME, a failed or unpowered DME, a distance that is not a number
        // or negative all print the dashes.
        foreach (var change in new Action<Approach>[]
                 {
                     a => a.DmeStation = Sea, a => a.DmeFailed = true, a => a.DmePowered = false, a => a.HasDme = false,
                     a => a.DmeDistance = double.NaN, a => a.DmeDistance = -1, a => a.DmeStation = null,
                 })
        {
            var a = IlsIsnq();
            change(a);
            Assert.Contains("Top left: DME ---", Rows(Read(a, mode: 1)));
        }
    }

    [Fact]
    public void A_mode_and_frequency_mismatch_reads_as_drawn()
    {
        // ILS with no frequency (parked): the label, the dashes, the dots, no bar, no glideslope scale.
        Assert.Equal(new[]
        {
            "EHSI: ILS, range 160",
            "Top: HDG 119 M",
            "Top left: DME ---",
            EtaRow(),
            "Center: course pointer 215, 96° right",
            "Center: no deviation bar",
            NoWindRow,
            "Bottom left: ILS",
            "Bottom right: ------",
            BugRow,
        }, Rows(Read(new Approach(), mode: 1)));

        // A frequency with no station (110.50): the dashes too, the scale up with no pointer.
        var noStation = new Approach { IlsFrequency = 110.50, GlideslopeScale = true };
        var rows = Rows(Read(noStation, mode: 1));
        Assert.Contains("Bottom right: ------", rows);
        Assert.Contains("Right side: no glideslope pointer", rows);
        // No receiver or no signal block selected: not received.
        Assert.Contains("Bottom right: ------", Rows(Read(new Approach { IlsFrequency = 110.30, Received = true, HasSignal = false }, mode: 1)));
        Assert.Contains("Bottom right: ------", Rows(Read(new Approach { IlsFrequency = 110.30, Received = true, HasReceiver = false }, mode: 1)));

        // VOR with no station (117.40): DME dashes, the course pointer and the dots, no bar, no TO/FROM.
        Assert.Equal(new[]
        {
            "EHSI: VOR, range 160",
            "Top: HDG 119 M",
            "Top left: DME ---",
            EtaRow(),
            "Center: course pointer 130, 11° right",
            "Center: no deviation bar",
            NoWindRow,
            "Bottom left: VOR L",
            BugRow,
        }, Rows(Read(new Approach(), mode: 0)));
    }

    // Plan 26's rows the screen also draws in VOR and ILS: the top right ETA while not centered (expanded), never in the
    // full rose (centered); the heading bug in every mode while valid.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Vor_and_ils_list_the_eta_when_expanded_and_the_heading_bug(int mode)
    {
        var expanded = Rows(Read(VorSea(), mode));
        Assert.Equal(EtaRow(), expanded[3]);
        Assert.StartsWith("Top left: DME ", expanded[2], StringComparison.Ordinal);
        Assert.Equal(BugRow, expanded[^1]);
        Assert.DoesNotContain(expanded, r => r.StartsWith("Top left: ", StringComparison.Ordinal) && r.EndsWith(" NM", StringComparison.Ordinal));

        var centered = Rows(Read(VorSea(), mode, scene: s => s.Centered = true));
        Assert.DoesNotContain(centered, r => r.StartsWith("Top right: ", StringComparison.Ordinal));
        Assert.Equal(BugRow, centered[^1]);

        // The ETA's own dashes as plan 26 lists them: under the minimum ground speed.
        Assert.Contains("Top right: no ETA", Rows(Read(VorSea(), mode, scene: s => s.GroundSpeed = 50)));
        // No heading bug while the IRS is not valid (plan 26's).
        Assert.DoesNotContain(Rows(Read(VorSea(), mode, scene: s => s.Valid = false)), r => r.StartsWith("Heading bug", StringComparison.Ordinal));
    }

    // The ILS deviation row follows the bar byte alone; the dots byte clear is its own row.
    [Fact]
    public void Ils_without_its_dots_still_shows_the_bar()
    {
        var noDots = IlsIsnq();
        noDots.Dots = false;
        noDots.LocalizerDeviation = -0.6;
        var rows = Rows(Read(noDots, mode: 1));
        int bar = Array.IndexOf(rows, "Center: deviation 1.2 dots left");
        Assert.True(bar > 0);
        Assert.Equal("Center: no deviation scale", rows[bar + 1]);

        noDots.LocalizerBar = false;
        rows = Rows(Read(noDots, mode: 1));
        bar = Array.IndexOf(rows, "Center: no deviation bar");
        Assert.True(bar > 0);
        Assert.Equal("Center: no deviation scale", rows[bar + 1]);

        // VOR mode has no ILS dots: never the row.
        var vor = VorSea();
        vor.Dots = false;
        Assert.DoesNotContain("Center: no deviation scale", Rows(Read(vor, mode: 0)));
    }

    // Review Focus: losing the signal draws no flag, only the bar and the glideslope pointer go; a failed receiver with a
    // frequency set draws LOC and G/S, and they are spoken once.
    [Fact]
    public void A_lost_signal_shows_the_flag_not_the_pointer()
    {
        // The signal lost on 110.30: the stale deviations stay in memory, the screen draws no bar and no pointer.
        var lost = IlsIsnq();
        lost.LocalizerBar = false;
        lost.GlideslopePointer = false;
        lost.Received = false;
        lost.LocalizerDeviation = 0.7;
        lost.Glideslope = 1.5;
        var lostReading = Read(lost, mode: 1);
        var rows = Rows(lostReading);
        Assert.Contains("Center: no deviation bar", rows);
        Assert.Contains("Right side: no glideslope pointer", rows);
        Assert.DoesNotContain(rows, r => r.Contains("dots", StringComparison.Ordinal) || r.Contains("flag", StringComparison.Ordinal));
        Assert.Empty(Bbs757EhsiText.Changes(Read(IlsIsnq(), mode: 1), lostReading, true));

        // The receiver failed with the frequency set: no dots, no scale, the two flags in place.
        var failed = new Approach { IlsFrequency = 110.30, Dots = false, LocFlag = true, GlideslopeFlag = true };
        var failedReading = Read(failed, mode: 1);
        Assert.Equal(new[]
        {
            "EHSI: ILS, range 160",
            "Top: HDG 119 M",
            "Top left: DME ---",
            EtaRow(),
            "Center: course pointer 215, 96° right",
            "Center: no deviation bar",
            "Center: no deviation scale",
            "Right side: G/S flag",
            NoWindRow,
            "Bottom: LOC flag",
            "Bottom left: ILS",
            "Bottom right: ------",
            BugRow,
        }, Rows(failedReading));
        var lines = Bbs757EhsiText.Changes(lostReading, failedReading, true);
        Assert.All(lines, l => Assert.Equal(Bbs757EhsiDisplay.MuteKey, l.MuteKey));
        Assert.Equal(new[] { "EHSI flag: LOC, G/S" }, Texts(lines));
        Assert.Equal(new[] { "EHSI flag gone: LOC, G/S" }, Texts(Bbs757EhsiText.Changes(failedReading, lostReading, true)));
        Assert.Empty(Bbs757EhsiText.Changes(failedReading, failedReading, true));

        // The flags are ILS mode's: the pilot turning to VOR or MAP takes them off the screen and nothing is said.
        var inVor = Read(failed, mode: 0);
        Assert.DoesNotContain(Rows(inVor), r => r.Contains("flag", StringComparison.Ordinal));
        Assert.Empty(Bbs757EhsiText.Changes(failedReading, inVor, true));
        Assert.Empty(Bbs757EhsiText.Changes(inVor, failedReading, true));
    }

    // Re-review finding 1: the approach part at its tear limit is null for a while in ILS mode, then reads again. LOC and
    // G/S neither go nor come back with it; a real change while it is read in both is still spoken once.
    [Fact]
    public void An_approach_part_dropping_out_and_back_speaks_nothing()
    {
        var flagged = Read(new Approach { IlsFrequency = 110.30, Dots = false, LocFlag = true, GlideslopeFlag = true }, mode: 1);
        Assert.Contains("Bottom: LOC flag", Rows(flagged));
        var dropped = flagged with { Approach = null, ApproachMissing = Bbs757EhsiReader.UnexpectedValues };
        Assert.Empty(Bbs757EhsiText.Changes(flagged, dropped, true));
        Assert.Empty(Bbs757EhsiText.Changes(dropped, flagged, true));
        var clear = Read(IlsIsnq(), mode: 1);
        Assert.Empty(Bbs757EhsiText.Changes(dropped, clear, true));
        Assert.Empty(Bbs757EhsiText.Changes(clear, dropped, true));
        Assert.Equal(new[] { "EHSI flag: LOC, G/S" }, Texts(Bbs757EhsiText.Changes(clear, flagged, true)));
        Assert.Equal(new[] { "EHSI flag gone: LOC, G/S" }, Texts(Bbs757EhsiText.Changes(flagged, clear, true)));
    }

    // ---- the bearing pointers ----

    [Fact]
    public void A_vor_bearing_pointer_is_placed_from_the_map()
    {
        // Both selectors on VOR: VOR L's station SEA, VOR R's PAE; placed from the nd's center and rotation (134.43).
        var both = VorSea();
        both.VorRStation = Pae;
        // The angle only, as the screen draws it (an arrow, no distance).
        var rows = Rows(Read(both, mode: 0));
        Assert.Contains("Bearing pointer 1: VOR L, SEA, 74° right", rows);
        // PAE north (bearing 002 true): 132° left of the map's up.
        Assert.Contains("Bearing pointer 2: VOR R, PAE, 132° left", rows);
        // In MAP too, after the map rows and before the wind and the heading bug.
        var map = Rows(Read(both, mode: 2, full: true));
        int pointer = Array.IndexOf(map, "Bearing pointer 1: VOR L, SEA, 74° right");
        Assert.True(pointer > 0);
        Assert.Equal("Bearing pointer 2: VOR R, PAE, 132° left", map[pointer + 1]);
        Assert.Equal(NoWindRow, map[pointer + 2]);
        Assert.StartsWith("Heading bug: ", map[pointer + 3], StringComparison.Ordinal);

        // Straight ahead and straight behind: the place's words, without the distance.
        var aheadBehind = VorSea();
        var (aheadLat, aheadLon) = Bbs757EhsiTcasTests.At(20, 0);
        var (behindLat, behindLon) = Bbs757EhsiTcasTests.At(20, 180);
        aheadBehind.VorLStation = Sea with { Lat = aheadLat, Lon = aheadLon };
        aheadBehind.VorRStation = Pae with { Lat = behindLat, Lon = behindLon };
        rows = Rows(Read(aheadBehind, mode: 0));
        Assert.Contains("Bearing pointer 1: VOR L, SEA, ahead", rows);
        Assert.Contains("Bearing pointer 2: VOR R, PAE, behind", rows);
    }

    // In ILS mode the VOR pointer's station is the panel's ILS-mode one, drawn only while its frequency is the panel's
    // manual one; in every mode only a station of a VOR kind (the layout's) draws a pointer.
    [Fact]
    public void Ils_mode_points_at_the_panels_ils_station_on_the_manual_frequency()
    {
        // ISNQ (110.30, kind 5) as VOR L's ILS-mode station, the manual frequency 110.30: the pointer, not SEA's.
        var tuned = VorSea();
        tuned.VorLIlsStation = Isnq;
        tuned.ManualWhole = 110;
        tuned.ManualFraction = 30;
        var rows = Rows(Read(tuned, mode: 1));
        Assert.Contains(rows, r => r.StartsWith("Bearing pointer 1: VOR L, ISNQ, ", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Contains("SEA", StringComparison.Ordinal));
        // The same panel in VOR mode: SEA (the panel's station) as before.
        Assert.Contains("Bearing pointer 1: VOR L, SEA, 74° right", Rows(Read(tuned, mode: 0)));
        // An ILS-mode station outside the module is torn.
        var torn = new Scene { Mode = 1, Tweak = b => { tuned.Write(b); I32(b, VorL + A.PanelIlsStation, Outside); } };
        Assert.True(Bbs757EhsiReaderTests.Read(torn, false, P).Corrupt);

        // Another manual frequency (116.80): no pointer in ILS mode.
        var other = VorSea();
        other.VorLIlsStation = Isnq;
        Assert.DoesNotContain(Rows(Read(other, mode: 1)), r => r.StartsWith("Bearing pointer 1", StringComparison.Ordinal));
        // No ILS-mode station: none (SEA at the panel's station is not used in ILS mode).
        Assert.DoesNotContain(Rows(Read(VorSea(), mode: 1)), r => r.StartsWith("Bearing pointer 1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(3)]   // TACAN
    [InlineData(6)]   // DME
    public void A_station_of_no_vor_kind_draws_no_pointer(int kind)
    {
        var a = VorSea();
        a.VorLStation = Sea with { Kind = kind };
        a.VorRStation = Pae;
        var rows = Rows(Read(a, mode: 0));
        Assert.DoesNotContain(rows, r => r.StartsWith("Bearing pointer 1", StringComparison.Ordinal));
        Assert.Contains("Bearing pointer 2: VOR R, PAE, 132° left", rows);
        // In ILS mode too, with its frequency matching.
        var ils = VorSea();
        ils.VorLIlsStation = Isnq with { Kind = kind };
        ils.ManualWhole = 110;
        ils.ManualFraction = 30;
        Assert.DoesNotContain(Rows(Read(ils, mode: 1)), r => r.StartsWith("Bearing pointer 1", StringComparison.Ordinal));
    }

    [Fact]
    public void An_adf_bearing_pointer_is_named_and_placed_only_where_the_layout_gives_its_position()
    {
        var adf = VorSea();
        adf.RmiLeft = 1;
        adf.RmiRight = 1;
        adf.AdfLeftPosition = (47.5, -122.2);   // north-east of the gate: left of the heading's 134
        // Without the layout's ADF positions: named, no place, whether a station is tuned or not.
        var rows = Rows(Read(adf, mode: 0));
        Assert.Contains("Bearing pointer 1: ADF L", rows);
        Assert.Contains("Bearing pointer 2: ADF R", rows);

        // With them: placed (each receiver its own position); an ADF with no receiver draws no pointer.
        adf.AdfRightPosition = (47.4, -122.4);   // south-west: right of the heading
        rows = Rows(Read(adf, mode: 0, layout: WithAdfPositions));
        Assert.Contains(rows, r => r.StartsWith("Bearing pointer 1: ADF L, ", StringComparison.Ordinal) && r.EndsWith(" left", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.StartsWith("Bearing pointer 2: ADF R, ", StringComparison.Ordinal) && r.EndsWith(" right", StringComparison.Ordinal));
        adf.AdfRightPosition = null;
        Assert.DoesNotContain(Rows(Read(adf, mode: 0, layout: WithAdfPositions)), r => r.StartsWith("Bearing pointer 2", StringComparison.Ordinal));
    }

    [Fact]
    public void No_bearing_pointer_in_plan_without_a_station_or_without_the_irs()
    {
        static bool Pointer(string r) => r.StartsWith("Bearing pointer", StringComparison.Ordinal);
        Assert.DoesNotContain(Rows(Read(VorSea(), mode: 3, full: true)), Pointer);
        Assert.DoesNotContain(Rows(Read(new Approach(), mode: 0)), Pointer);
        Assert.DoesNotContain(Rows(Read(VorSea(), mode: 0, scene: s => s.Valid = false)), Pointer);
        // A selector on neither VOR nor ADF: none.
        var other = VorSea();
        other.RmiLeft = 2;
        Assert.DoesNotContain(Rows(Read(other, mode: 0)), Pointer);
    }

    // ---- all or nothing ----

    [Fact]
    public void A_torn_approach_read_is_corrupt()
    {
        foreach (var tweak in new Action<byte[]>[]
                 {
                     b => I32(b, Nd + A.NdToPfd, Outside),
                     b => I32(b, Nd + A.NdToDme, Outside),
                     b => I32(b, Dme + A.DmeStation, Outside),
                     b => I32(b, VorL + A.PanelStation, Outside),
                     b => I32(b, Nd + A.NdToVorLeft, Outside),
                     b => F64(b, SeaStation + A.StationLatitude, 91),
                     b => I32(b, SeaStation + A.StationIdent, 0xD800),
                     b => I32(b, Source + A.SourceToIlsReceiver, Outside),
                 })
        {
            var a = VorSea();
            a.IlsFrequency = 110.30;
            a.DmeStation = Isnq;
            var s = new Scene { Mode = 0, Tweak = b => { a.Write(b); tweak(b); } };
            var reading = Bbs757EhsiReaderTests.Read(s, false, P);
            Assert.True(reading.Corrupt);
            Assert.Null(reading.Approach);
        }
    }

    // The ILS self-test's amber: the deviation scale's dots while the ILS test byte is set, the glideslope scale while
    // the PFD's test byte is. Listed by the scale they color, never spoken.
    [Fact]
    public void The_ils_self_test_lists_its_amber_scales()
    {
        var plain = IlsIsnq();
        var tested = IlsIsnq();
        tested.IlsTest = true;
        tested.GlideslopeTest = true;
        var rows = Rows(Read(tested, mode: 1));
        int deviation = Array.IndexOf(rows, "Center: deviation at full scale, right");
        Assert.Equal("Center: deviation scale amber", rows[deviation + 1]);
        int glideslope = Array.IndexOf(rows, "Right side: glideslope at full scale, above");
        Assert.Equal("Right side: glideslope scale amber", rows[glideslope + 1]);
        Assert.DoesNotContain(Rows(Read(plain, mode: 1)), r => r.Contains("amber", StringComparison.Ordinal));
        Assert.Empty(Bbs757EhsiText.Changes(Read(plain, mode: 1), Read(tested, mode: 1), true));

        // No dots drawn (the scale's own row says so), or no glideslope scale: nothing amber to list.
        tested.Dots = false;
        tested.GlideslopeScale = false;
        Assert.DoesNotContain(Rows(Read(tested, mode: 1)), r => r.Contains("amber", StringComparison.Ordinal));
        // VOR mode draws neither scale's test.
        Assert.DoesNotContain(Rows(Read(tested, mode: 0)), r => r.Contains("amber", StringComparison.Ordinal));
    }

    [Fact]
    public void The_reading_carries_the_approach_as_stored()
    {
        var tested = IlsIsnq();
        tested.IlsTest = true;
        tested.GlideslopeTest = true;
        var r = Read(tested, mode: 1).Approach!;
        Assert.Equal(164, r.IlsCourse);
        Assert.True(r.IlsTest);
        Assert.True(r.GlideslopeTest);
        Assert.Equal(110.30, r.IlsFrequency);
        Assert.True(r.IlsDmeValid);
        Assert.Equal(0.56, r.IlsDme);
        Assert.Equal(new Bbs757EhsiApproachWords("VOR L", "VOR R", "ILS", "TO", "FROM", "DME", "------", "  ---", "LOC", "G/S"), r.Words);
        Assert.Equal((1.0, 2.0, 2.2), (r.DeviationFullScale, r.DotsPerUnit, r.GlideslopePin));
        Assert.Equal(Bbs757EhsiToFrom.To, Read(VorSea(), mode: 0).Approach!.ToFrom);
    }
}
