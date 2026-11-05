// Characterization tests for FenixChannelPfd, the decoder of the captain's PFD from the RemoteData message of the Fenix
// display channel (the gRPC service on 127.0.0.1:18083), and for PfdPushes, the push builder the other PFD tests reuse.
// Pure logic: every message is built here with FenixChannelProtoWriter and the field numbers written out literally, never
// read from FenixChannelFields and never from a capture, so a wrong number in the app's table fails a test.
//
// The contract: each field is read in the wire shape the contract gives it, a string as valid UTF-8, and a field in
// another shape is a wrong shape named by its path ("28.5", "46.2.1", "1.14"), reported before any missing content; the
// air data (28), the air data flags (46) and an EFIS entry must be there, and the first EFIS entry is the captain's; a
// value left out reads as protobuf-net leaves it (the air data, the trend and the track 0, a text "", a flag false, the
// QNH 1013) except a speed and the speed target, which are NaN, and a deviation: the channel writes NaN for a scale it
// does not draw, so a deviation left out of a part that is sent is a real 0.0 (centered), and only with the whole
// part left out are both NaN; the baro text (105) is never read; the heading is the frame's; the first format letter
// the app does not know in a PFD text makes the section an unknown code that names the text.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelPfdDecoderTests
{
    private static FenixChannelSection<FenixChannelPfd> Decode(PfdPushes.Push push) => FenixChannelPfd.Decode(push.Remote());

    private static FenixChannelPfd Decoded(PfdPushes.Push push)
    {
        FenixChannelSection<FenixChannelPfd> section = Decode(push);
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    private static void AssertNotDrawn(params double[] values)
    {
        foreach (double value in values)
        {
            Assert.True(double.IsNaN(value), $"expected NaN, got {value}");
        }
    }

    // ---- Values -----------------------------------------------------------------------------

    [Fact]
    public void A_cruise_message_decodes_every_pfd_value()
    {
        FenixChannelPfd pfd = Decoded(new PfdPushes.Push());

        Assert.Equal(3.1028, pfd.Pitch);
        Assert.Equal(-0.0465, pfd.Bank);
        Assert.Equal(34010.8607, pfd.Altitude);
        Assert.Equal(268.2704, pfd.Airspeed);
        Assert.Equal(0, pfd.VerticalSpeed);

        Assert.True(pfd.AttitudeShown);
        Assert.True(pfd.HeadingShown);
        Assert.True(pfd.SpeedShown);
        Assert.True(pfd.AltitudeShown);
        Assert.True(pfd.VerticalSpeedShown);

        Assert.Equal(283.7129, pfd.VMax);
        Assert.Equal(232.4935, pfd.Vls);
        Assert.Equal(196.6389, pfd.AlphaProt);
        Assert.Equal(237.4975, pfd.GreenDot);
        AssertNotDrawn(pfd.Vsw, pfd.V1, pfd.V2, pfd.Vr, pfd.F, pfd.S, pfd.VfeNext);

        Assert.Equal(268.1442, pfd.TargetIas);
        Assert.True(pfd.SpeedManaged);
        Assert.True(pfd.BugsVisible);
        Assert.Equal(0, pfd.SpeedTrend);

        Assert.Equal("g.775", pfd.MachText);
        Assert.Equal("", pfd.RadioAltitudeText);
        Assert.Equal("", pfd.LsIdent);
        Assert.Equal("", pfd.LsFrequency);
        Assert.Equal("", pfd.LsDme);
        Assert.Equal("", pfd.ApproachCenter);
        Assert.Equal("", pfd.ApproachLoc);
        Assert.Equal("", pfd.ApproachGs);
        Assert.Equal("", pfd.MarkerText);
        Assert.Equal("", pfd.LsFlagText);
        AssertNotDrawn(pfd.LocDots, pfd.GsDots);
        Assert.False(pfd.MetricAltitude);
        Assert.False(pfd.DhMessage);

        Assert.False(pfd.Ls);
        Assert.Equal(new FenixPfdBaro(Std: true, Inches: false, Qfe: false, QnhHpa: 1016, QnhInHg: 30.0025f), pfd.Baro);
        Assert.Equal(318.0504, pfd.MagneticTrack);
        Assert.Equal(0, pfd.MagHeading);
    }

    [Fact]
    public void Every_value_is_read_from_its_own_field()
    {
        // No two values are alike, so a number crossed with another shows.
        FenixChannelPfd pfd = Decoded(new PfdPushes.Push
        {
            Pitch = 1.5, Bank = 2.5, Altitude = 3.5, Airspeed = 4.5, VerticalSpeed = 5.5,
            VMax = 11.5, Vls = 12.5, AlphaProt = 13.5, Vsw = 14.5,
            V1 = 21.5, V2 = 22.5, Vr = 23.5, F = 24.5, S = 25.5, GreenDot = 26.5, VfeNext = 27.5,
            TargetIas = 31.5, SpeedTrend = 41.5, LocDots = 61.5, GsDots = 62.5, MagneticTrack = 71.5,
            MachText = "mach", RadioAltitudeText = "radio", LsIdent = "ident", LsFrequency = "freq", LsDme = "dme",
            ApproachCenter = "center", ApproachLoc = "loc", ApproachGs = "gs", MarkerText = "marker", LsFlagText = "flag"
        });

        Assert.Equal(new[] { 1.5, 2.5, 3.5, 4.5, 5.5 },
            new[] { pfd.Pitch, pfd.Bank, pfd.Altitude, pfd.Airspeed, pfd.VerticalSpeed });
        Assert.Equal(new[] { 11.5, 12.5, 13.5, 14.5 }, new[] { pfd.VMax, pfd.Vls, pfd.AlphaProt, pfd.Vsw });
        Assert.Equal(new[] { 21.5, 22.5, 23.5, 24.5, 25.5, 26.5, 27.5 },
            new[] { pfd.V1, pfd.V2, pfd.Vr, pfd.F, pfd.S, pfd.GreenDot, pfd.VfeNext });
        Assert.Equal(new[] { 31.5, 41.5, 61.5, 62.5, 71.5 },
            new[] { pfd.TargetIas, pfd.SpeedTrend, pfd.LocDots, pfd.GsDots, pfd.MagneticTrack });
        Assert.Equal(new[] { "mach", "radio", "ident", "freq", "dme", "center", "loc", "gs", "marker", "flag" },
            new[]
            {
                pfd.MachText, pfd.RadioAltitudeText, pfd.LsIdent, pfd.LsFrequency, pfd.LsDme, pfd.ApproachCenter,
                pfd.ApproachLoc, pfd.ApproachGs, pfd.MarkerText, pfd.LsFlagText
            });
    }

    // One flag at a time, with every other flag of the message false: only that one member reads true.
    private static readonly Dictionary<string, Action<PfdPushes.Push, bool>> FlagSetters = new()
    {
        ["AttitudeShown"] = (push, on) => push.AttitudeShown = on,
        ["HeadingShown"] = (push, on) => push.HeadingShown = on,
        ["SpeedShown"] = (push, on) => push.SpeedShown = on,
        ["AltitudeShown"] = (push, on) => push.AltitudeShown = on,
        ["VerticalSpeedShown"] = (push, on) => push.VerticalSpeedShown = on,
        ["SpeedManaged"] = (push, on) => push.SpeedManaged = on,
        ["BugsVisible"] = (push, on) => push.BugsVisible = on,
        ["Std"] = (push, on) => push.Efis.QnhStd = on,
        ["Inches"] = (push, on) => push.Efis.QnhInches = on,
        ["Qfe"] = (push, on) => push.Efis.Qfe = on,
        ["Ls"] = (push, on) => push.Efis.Ls = on,
        ["MetricAltitude"] = (push, on) => push.MetricAltitude = on,
        ["AltitudeAmber"] = (push, on) => push.AltitudeAmber = on,
        ["DhMessage"] = (push, on) => push.DhMessage = on
    };

    public static IEnumerable<object[]> FlagNames() => FlagSetters.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(FlagNames))]
    public void Each_flag_is_read_from_its_own_field(string flag)
    {
        var push = new PfdPushes.Push();
        foreach (Action<PfdPushes.Push, bool> set in FlagSetters.Values)
        {
            set(push, false);
        }

        FlagSetters[flag](push, true);
        FenixChannelPfd pfd = Decoded(push);

        var read = new Dictionary<string, bool>
        {
            ["AttitudeShown"] = pfd.AttitudeShown,
            ["HeadingShown"] = pfd.HeadingShown,
            ["SpeedShown"] = pfd.SpeedShown,
            ["AltitudeShown"] = pfd.AltitudeShown,
            ["VerticalSpeedShown"] = pfd.VerticalSpeedShown,
            ["SpeedManaged"] = pfd.SpeedManaged,
            ["BugsVisible"] = pfd.BugsVisible,
            ["Std"] = pfd.Baro.Std,
            ["Inches"] = pfd.Baro.Inches,
            ["Qfe"] = pfd.Baro.Qfe,
            ["Ls"] = pfd.Ls,
            ["MetricAltitude"] = pfd.MetricAltitude,
            ["AltitudeAmber"] = pfd.AltitudeAmber,
            ["DhMessage"] = pfd.DhMessage
        };
        Assert.Equal(flag, Assert.Single(read.Where(entry => entry.Value)).Key);
    }

    // ---- What a message leaves out ----------------------------------------------------------

    [Fact]
    public void An_absent_speed_is_not_drawn_and_absent_air_data_is_zero()
    {
        // The parts left out whole.
        FenixChannelPfd noParts = Decoded(new PfdPushes.Push { SpeedTrend = 12.5, MagneticTrack = 90 }
            .Omit("125", "127", "113", "74", "82", "100", "117", "194", "98"));
        AssertNotDrawn(noParts.VMax, noParts.Vls, noParts.AlphaProt, noParts.Vsw, noParts.V1, noParts.V2, noParts.Vr,
            noParts.F, noParts.S, noParts.GreenDot, noParts.VfeNext, noParts.TargetIas, noParts.LocDots, noParts.GsDots);
        Assert.False(noParts.SpeedManaged);
        Assert.False(noParts.BugsVisible);
        Assert.Equal(new[] { "", "", "", "", "", "", "" },
            new[] { noParts.LsIdent, noParts.LsFrequency, noParts.LsDme, noParts.ApproachCenter, noParts.ApproachLoc,
                noParts.ApproachGs, noParts.MarkerText });
        Assert.Equal(0, noParts.SpeedTrend);
        Assert.Equal(0, noParts.MagneticTrack);

        // The same parts there, with their fields left out one by one; the air data parts too, which may be empty.
        FenixChannelPfd noFields = Decoded(new PfdPushes.Push { SpeedTrend = 12.5, MagneticTrack = 90 }
            .Omit("28.1", "28.2", "28.4", "28.5", "28.6", "125.1", "125.2", "125.3", "125.5", "127.1", "127.6",
                "113.4", "113.9", "98", "194.2", "107"));
        Assert.Equal(new[] { 0.0, 0.0, 0.0, 0.0, 0.0 },
            new[] { noFields.Pitch, noFields.Bank, noFields.Altitude, noFields.Airspeed, noFields.VerticalSpeed });
        AssertNotDrawn(noFields.VMax, noFields.Vls, noFields.AlphaProt, noFields.Vsw, noFields.V1, noFields.GreenDot,
            noFields.TargetIas);
        Assert.False(noFields.SpeedManaged);
        Assert.Equal(0, noFields.SpeedTrend);
        Assert.Equal(0, noFields.MagneticTrack);
        Assert.Equal("", noFields.MachText);
    }

    [Fact]
    public void An_absent_deviation_in_a_present_part_is_centered()
    {
        // The channel sends NaN for a scale it does not draw, so a deviation the part leaves out is a real 0.0 that
        // protobuf-net left out. The cruise has the part, with both deviations NaN.
        FenixChannelPfd bothLeftOut = Decoded(new PfdPushes.Push().Omit("82.1", "82.2"));
        Assert.Equal(new[] { 0.0, 0.0 }, new[] { bothLeftOut.LocDots, bothLeftOut.GsDots });

        // One of them 0 and the other NaN, as the builder writes them: only the NaN is sent.
        FenixChannelPfd onlyLocalizer = Decoded(new PfdPushes.Push { LocDots = 0, GsDots = double.NaN });
        Assert.Equal(0, onlyLocalizer.LocDots);
        AssertNotDrawn(onlyLocalizer.GsDots);

        FenixChannelPfd onlyGlideslope = Decoded(new PfdPushes.Push { LocDots = double.NaN, GsDots = 0 });
        AssertNotDrawn(onlyGlideslope.LocDots);
        Assert.Equal(0, onlyGlideslope.GsDots);

        // A number sent is that number, and the part sent with nothing in it centers both.
        FenixChannelPfd sent = Decoded(new PfdPushes.Push { LocDots = -0.5, GsDots = 1.25 });
        Assert.Equal(new[] { -0.5, 1.25 }, new[] { sent.LocDots, sent.GsDots });
        FenixChannelPfd empty = Decoded(new PfdPushes.Push { LocDots = 0, GsDots = 0 });
        Assert.Equal(new[] { 0.0, 0.0 }, new[] { empty.LocDots, empty.GsDots });

        // With the part itself left out there is no scale: both stay NaN.
        FenixChannelPfd noPart = Decoded(new PfdPushes.Push { LocDots = 0.5, GsDots = 0.5 }.Omit("82"));
        AssertNotDrawn(noPart.LocDots, noPart.GsDots);
    }

    [Fact]
    public void An_absent_qnh_is_1013()
    {
        // The Fenix leaves 1013 out, as it does every value at its contract default.
        var push = new PfdPushes.Push();
        push.Efis.Qnh = 1013f;
        FenixPfdBaro atDefault = Decoded(push).Baro;
        Assert.Equal(1013, atDefault.QnhHpa);
        Assert.Equal(30.0025f, atDefault.QnhInHg);

        FenixPfdBaro none = Decoded(new PfdPushes.Push().Omit("1.14", "1.15")).Baro;
        Assert.Equal(1013, none.QnhHpa);
        Assert.True(double.IsNaN(none.QnhInHg));

        Assert.Equal(1029, Decoded(new PfdPushes.Push { Efis = { Qnh = 1029f } }).Baro.QnhHpa);
    }

    [Fact]
    public void The_metric_flag_and_the_dh_message_are_false_until_the_message_sets_them()
    {
        Assert.False(Decoded(new PfdPushes.Push()).MetricAltitude);
        Assert.False(Decoded(new PfdPushes.Push()).DhMessage);

        // Set, and then the parts and fields left out: a flag the message does not carry is false.
        Assert.True(Decoded(new PfdPushes.Push { MetricAltitude = true }).MetricAltitude);
        Assert.True(Decoded(new PfdPushes.Push { DhMessage = true }).DhMessage);
        Assert.False(Decoded(new PfdPushes.Push { MetricAltitude = true }.Omit("122")).MetricAltitude);
        Assert.False(Decoded(new PfdPushes.Push { MetricAltitude = true }.Omit("122.3")).MetricAltitude);
        Assert.False(Decoded(new PfdPushes.Push { DhMessage = true }.Omit("205")).DhMessage);
    }

    [Fact]
    public void A_pfd_with_no_baro_set_reads_the_absent_defaults()
    {
        // The default of a record built by hand is the one an EFIS entry that sends nothing decodes to.
        Assert.Equal(new FenixPfdBaro(Std: false, Inches: false, Qfe: false, QnhHpa: 1013, QnhInHg: double.NaN),
            new FenixChannelPfd().Baro);
    }

    // ---- Wrong shapes -----------------------------------------------------------------------

    // Every field the decoder reads, with a wire shape it does not have: a varint for most, a fixed64 for the flags and
    // the EFIS settings, and the bytes of an invalid UTF-8 string for a text.
    public static IEnumerable<object[]> WrongShapes()
    {
        (string Path, string Shape)[] reshapes =
        [
            ("28", "varint"), ("28.1", "varint"), ("28.2", "varint"), ("28.4", "varint"), ("28.5", "varint"),
            ("28.6", "varint"),
            ("46", "varint"), ("46.1", "varint"), ("46.2", "varint"), ("46.1.1", "fixed64"), ("46.1.2", "fixed64"),
            ("46.2.1", "fixed64"), ("46.2.2", "fixed64"), ("46.8", "fixed64"),
            ("1", "varint"), ("1.6", "fixed64"), ("1.7", "fixed64"), ("1.10", "fixed64"), ("1.11", "fixed64"),
            ("1.14", "fixed64"), ("1.15", "fixed64"),
            ("74", "varint"), ("74.1", "utf8"), ("74.2", "varint"), ("74.3", "varint"),
            ("82", "varint"), ("82.1", "varint"), ("82.2", "varint"),
            ("98", "varint"),
            ("100", "varint"), ("100.1", "varint"), ("100.2", "varint"), ("100.3", "varint"),
            ("107", "varint"), ("107", "utf8"),
            ("113", "varint"), ("113.4", "varint"), ("113.5", "fixed64"), ("113.9", "fixed64"),
            ("117", "varint"), ("117.1", "varint"), ("117.3", "utf8"),
            ("122", "varint"), ("122.3", "fixed64"), ("122.8", "fixed64"),
            ("125", "varint"), ("125.1", "varint"), ("125.2", "varint"), ("125.3", "varint"), ("125.5", "varint"),
            ("127", "varint"), ("127.1", "varint"), ("127.2", "varint"), ("127.3", "varint"), ("127.4", "varint"),
            ("127.5", "varint"), ("127.6", "varint"), ("127.7", "varint"),
            ("157", "varint"), ("157", "utf8"),
            ("194", "varint"), ("194.2", "varint"),
            ("205", "fixed64")
        ];

        return reshapes.Select(reshape => new object[] { reshape.Path, reshape.Shape });
    }

    private static void Reshape(PfdPushes.Push push, string path, string shape)
    {
        switch (shape)
        {
            case "varint": push.AsVarint(path); break;
            case "fixed64": push.AsFixed64(path); break;
            case "utf8": push.AsInvalidUtf8(path); break;
            default: throw new ArgumentException($"no wrong shape called {shape}", nameof(shape));
        }
    }

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public void Each_wrong_shape_is_named_by_its_path(string path, string shape)
    {
        var push = new PfdPushes.Push();
        Reshape(push, path, shape);

        FenixChannelSection<FenixChannelPfd> section = Decode(push);

        Assert.False(section.IsOk);
        Assert.Null(section.Value);
        Assert.Equal($"field {path} has the wrong shape", section.Problem);
    }

    [Theory]
    [InlineData("28")]
    [InlineData("46")]
    [InlineData("1")]
    public void A_required_part_in_the_wrong_shape_is_a_wrong_shape_and_not_missing(string path)
    {
        // A damaged part is what hides the content, so the damage is what the section says.
        FenixChannelSection<FenixChannelPfd> section = Decode(new PfdPushes.Push().AsVarint(path));

        Assert.Equal($"field {path} has the wrong shape", section.Problem);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_path_is_written_the_same_in_every_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            FenixChannelSection<FenixChannelPfd> section = Decode(new PfdPushes.Push().AsFixed64("46.2.1"));
            Assert.Equal("field 46.2.1 has the wrong shape", section.Problem);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- Missing content --------------------------------------------------------------------

    [Theory]
    [InlineData("28", "no captain air data")]
    [InlineData("46", "no air data flags")]
    [InlineData("1", "no EFIS")]
    public void Missing_air_data_flags_or_efis_is_missing_content(string omitted, string problem)
    {
        FenixChannelSection<FenixChannelPfd> section = Decode(new PfdPushes.Push().Omit(omitted));

        Assert.False(section.IsOk);
        Assert.Null(section.Value);
        Assert.Equal(problem, section.Problem);
    }

    [Fact]
    public void A_message_with_nothing_in_it_has_no_captain_air_data()
    {
        FenixChannelPush empty = FenixChannelPush.From(Array.Empty<byte>())!;

        Assert.Equal("no captain air data", FenixChannelPfd.Decode(empty).Problem);
    }

    // ---- The EFIS ---------------------------------------------------------------------------

    [Fact]
    public void The_first_efis_entry_is_the_captains()
    {
        // The push writes the first officer's entry after the captain's, and a singular field reads the last one sent.
        var push = new PfdPushes.Push
        {
            Efis = { QnhStd = false, QnhInches = true, Ls = true, Qnh = 1029f, QnhInch = 30.39f },
            OfficerEfis = { QnhStd = true, QnhInches = false, Ls = false, Qnh = 1008f, QnhInch = 29.77f }
        };

        FenixChannelPfd pfd = Decoded(push);

        Assert.True(pfd.Ls);
        Assert.Equal(new FenixPfdBaro(Std: false, Inches: true, Qfe: false, QnhHpa: 1029, QnhInHg: 30.39f), pfd.Baro);
    }

    [Fact]
    public void A_lone_efis_entry_is_the_captains()
    {
        FenixChannelPfd pfd = Decoded(new PfdPushes.Push { SendsOfficerEfis = false });

        Assert.True(pfd.Baro.Std);
        Assert.Equal(1016, pfd.Baro.QnhHpa);
    }

    [Fact]
    public void The_qnh_text_is_never_read()
    {
        // The baro is the EFIS's: this text is never read, so even a letter the app does not know in it costs nothing.
        Assert.Contains('x', FenixChannelText.Parse("xQNH c1029").UnknownCodes);
        var atQnh = new PfdPushes.Push { QnhText = "xQNH c1029", Efis = { QnhStd = false, Qnh = 1029f, QnhInch = 30.39f } };

        FenixChannelPfd pfd = Decoded(atQnh);
        Assert.Equal(new FenixPfdBaro(Std: false, Inches: false, Qfe: false, QnhHpa: 1029, QnhInHg: 30.39f), pfd.Baro);
        Assert.True(FenixChannelPfd.From(atQnh.Snapshot()).IsOk);

        // And in a shape it does not have: a field that is not looked at has no wrong shape.
        var reshaped = new PfdPushes.Push().AsFixed64("105");
        Assert.True(Decode(reshaped).IsOk);
        Assert.True(Decoded(reshaped).Baro.Std);
        Assert.True(FenixChannelPfd.From(reshaped.Snapshot()).IsOk);
    }

    // ---- From a snapshot --------------------------------------------------------------------

    [Fact]
    public void A_snapshot_with_no_message_or_no_frame_has_no_data()
    {
        FenixChannelSnapshot whole = new PfdPushes.Push().Snapshot();

        Assert.True(FenixChannelPfd.From(whole).IsOk);
        Assert.Equal("no data", FenixChannelPfd.From(whole with { Push = null }).Problem);
        Assert.Equal("no data", FenixChannelPfd.From(whole with { Frame = null }).Problem);
    }

    [Fact]
    public void The_heading_is_the_frames_and_is_never_decoded_again()
    {
        FenixChannelSnapshot snapshot = new PfdPushes.Push { MagHeading = 313.2 }.Snapshot();

        Assert.Equal(313.2, FenixChannelPfd.From(snapshot).Value!.MagHeading);

        // The frame is what the trust gate judged, so its heading is the one read even when the message differs.
        FenixChannelSnapshot other = snapshot with { Frame = snapshot.Frame! with { MagHeading = 90 } };
        Assert.Equal(90, FenixChannelPfd.From(other).Value!.MagHeading);

        FenixChannelSnapshot unreadable = snapshot with { Frame = snapshot.Frame! with { MagHeading = double.NaN } };
        Assert.True(double.IsNaN(FenixChannelPfd.From(unreadable).Value!.MagHeading));
    }

    [Fact]
    public void A_problem_of_the_message_is_the_snapshots()
    {
        FenixChannelSnapshot snapshot = new PfdPushes.Push().AsVarint("28.5").Snapshot();

        Assert.Equal("field 28.5 has the wrong shape", FenixChannelPfd.From(snapshot).Problem);
    }

    // ---- Format letters ---------------------------------------------------------------------

    private static void SetText(PfdPushes.Push push, string text, string value)
    {
        switch (text)
        {
            case "Mach": push.MachText = value; break;
            case "RadioAltitude": push.RadioAltitudeText = value; break;
            case "LsIdent": push.LsIdent = value; break;
            case "LsFrequency": push.LsFrequency = value; break;
            case "LsDme": push.LsDme = value; break;
            case "ApproachCenter": push.ApproachCenter = value; break;
            case "ApproachLoc": push.ApproachLoc = value; break;
            case "ApproachGs": push.ApproachGs = value; break;
            case "Marker": push.MarkerText = value; break;
            case "LsFlag": push.LsFlagText = value; break;
            default: throw new ArgumentException($"no text called {text}", nameof(text));
        }
    }

    [Theory]
    [InlineData("Mach", "Mach line")]
    [InlineData("RadioAltitude", "radio altitude")]
    [InlineData("LsIdent", "LS information")]
    [InlineData("LsFrequency", "LS information")]
    [InlineData("LsDme", "LS information")]
    [InlineData("ApproachCenter", "approach labels")]
    [InlineData("ApproachLoc", "approach labels")]
    [InlineData("ApproachGs", "approach labels")]
    [InlineData("Marker", "marker")]
    [InlineData("LsFlag", "LS flag")]
    public void An_unknown_letter_in_a_pfd_text_names_its_place(string text, string place)
    {
        // k is a letter the app does not know, and stays one: p and d are known now. The text is otherwise one the PFD
        // draws: a code and a number.
        var push = new PfdPushes.Push();
        SetText(push, text, "k350");

        FenixChannelSection<FenixChannelPfd> section = FenixChannelPfd.From(push.Snapshot());

        Assert.False(section.IsOk);
        Assert.Null(section.Value);
        Assert.Equal($"unknown code 'k' in the {place}", section.Problem);
    }

    [Fact]
    public void The_first_unknown_letter_in_the_order_of_the_texts_is_the_one_named()
    {
        var push = new PfdPushes.Push { MachText = "g.775", RadioAltitudeText = "jk350", MarkerText = "x", LsFlagText = "z" };
        Assert.Equal("unknown code 'j' in the radio altitude", FenixChannelPfd.From(push.Snapshot()).Problem);

        push.MachText = "k.775";
        Assert.Equal("unknown code 'k' in the Mach line", FenixChannelPfd.From(push.Snapshot()).Problem);
    }

    [Fact]
    public void The_letters_the_app_knows_are_not_unknown_codes()
    {
        var push = new PfdPushes.Push
        {
            MachText = "g.775", RadioAltitudeText = "gsl350", LsIdent = "cIMT", LsFrequency = "c109.30", LsDme = "gcsDME 4.2",
            ApproachCenter = "wufILS", ApproachLoc = "aLOC", ApproachGs = "rG/S", MarkerText = "mOM", LsFlagText = "yLS"
        };

        Assert.True(FenixChannelPfd.From(push.Snapshot()).IsOk);
    }
}

/// <summary>
/// Builds the RemoteData message of the captain's PFD, and the snapshot around it, for the PFD tests. A
/// <see cref="Push"/> starts as a cruise at 34,000 feet (Mach .775 at standard pressure, 268 knots, VMAX, a green dot,
/// VLS and alpha prot drawn, both LS deviations NaN) and a test changes what it needs: it sets a value, leaves a field
/// out by its path (<see cref="Push.Omit"/>), or writes it in another wire shape (<see cref="Push.AsVarint"/>,
/// <see cref="Push.AsFixed64"/>, <see cref="Push.AsInvalidUtf8"/>). A path is the field numbers joined by dots, from
/// RemoteData down: "28.5", "46.2.1", "1.14". Every field number is written out literally here, as
/// <see cref="FenixChannelProtoWriter.GoodPush"/> does, so a wrong number in the app's table fails a test.
/// </summary>
/// <remarks>
/// A value at its default is left out of the message, as protobuf-net leaves it out: a double of 0, a flag that is
/// false, an empty text, a Qnh of 1013. A NaN is written, since the Fenix sends one for a speed it does not draw.
/// </remarks>
internal static class PfdPushes
{
    /// <summary>The settings of one EFIS panel.</summary>
    internal sealed class Panel
    {
        public bool QnhStd { get; set; }
        public bool QnhInches { get; set; }
        public bool ShowQnh { get; set; }
        public bool Qfe { get; set; }
        public bool Meters { get; set; }
        public bool Ls { get; set; }
        public float Qnh { get; set; } = 1013f;
        public float? QnhInch { get; set; }
    }

    /// <summary>One RemoteData message of the captain's PFD, a cruise until a test changes it.</summary>
    internal sealed class Push
    {
        private readonly HashSet<string> _omitted = [];
        private readonly Dictionary<string, Action<FenixChannelProtoWriter, int>> _reshaped = [];

        /// <summary>The captain's EFIS panel, the first entry of field 1: STD, with a QNH of 1016.</summary>
        public Panel Efis { get; } = new() { QnhStd = true, Qnh = 1016f, QnhInch = 30.0025f };

        /// <summary>
        /// The first officer's EFIS panel, the second entry of field 1: unlike the captain's on every setting, so that a
        /// read of the wrong entry shows.
        /// </summary>
        public Panel OfficerEfis { get; } = new()
        {
            QnhInches = true, ShowQnh = true, Qfe = true, Meters = true, Ls = true, Qnh = 1029f, QnhInch = 29.8f
        };

        /// <summary>False leaves the first officer's entry out, so that the captain's is the only one.</summary>
        public bool SendsOfficerEfis { get; set; } = true;

        // 28 the captain's air data
        public double Pitch { get; set; } = 3.1028;
        public double Bank { get; set; } = -0.0465;
        public double Altitude { get; set; } = 34010.8607;
        public double Airspeed { get; set; } = 268.2704;
        public double VerticalSpeed { get; set; }

        // 46 which of it the PFD draws
        public bool AttitudeShown { get; set; } = true;
        public bool HeadingShown { get; set; } = true;
        public bool SpeedShown { get; set; } = true;
        public bool AltitudeShown { get; set; } = true;
        public bool VerticalSpeedShown { get; set; } = true;

        // 125 and 127 the speed limits and the marks
        public double VMax { get; set; } = 283.7129;
        public double Vls { get; set; } = 232.4935;
        public double AlphaProt { get; set; } = 196.6389;
        public double Vsw { get; set; } = double.NaN;
        public double V1 { get; set; } = double.NaN;
        public double V2 { get; set; } = double.NaN;
        public double Vr { get; set; } = double.NaN;
        public double F { get; set; } = double.NaN;
        public double S { get; set; } = double.NaN;
        public double GreenDot { get; set; } = 237.4975;
        public double VfeNext { get; set; } = double.NaN;

        // 113 the speed target, 98 the trend
        public double TargetIas { get; set; } = 268.1442;
        public bool BugsVisible { get; set; } = true;
        public bool SpeedManaged { get; set; } = true;
        public double SpeedTrend { get; set; }

        // The texts: 107 the Mach line, 157 the radio altitude, 74 the LS information, 100 the approach labels, 117 the
        // marker and the LS flag, and 105 the baro text, which the decoder never reads
        public string MachText { get; set; } = "g.775";
        public string RadioAltitudeText { get; set; } = "";
        public string LsIdent { get; set; } = "";
        public string LsFrequency { get; set; } = "";
        public string LsDme { get; set; } = "";
        public string ApproachCenter { get; set; } = "";
        public string ApproachLoc { get; set; } = "";
        public string ApproachGs { get; set; } = "";
        public string MarkerText { get; set; } = "";
        public string LsFlagText { get; set; } = "";
        public string QnhText { get; set; } = "";

        // 122.3 the altitude readout also drawn in meters, 122.8 drawn amber, 205 the DH message
        public bool MetricAltitude { get; set; }
        public bool AltitudeAmber { get; set; }
        public bool DhMessage { get; set; }

        // 82 the deviations, NaN (written) while the PFD draws no LS scale; a 0 is left out of the part like any default
        public double LocDots { get; set; } = double.NaN;
        public double GsDots { get; set; } = double.NaN;

        // 194 the heading; the magnetic heading is also the frame's in a snapshot
        public double MagHeading { get; set; } = 313.2;
        public double MagneticTrack { get; set; } = 318.0504;

        /// <summary>Leaves each of these fields out of the message; omitting a part leaves out everything in it.</summary>
        public Push Omit(params string[] paths)
        {
            foreach (string path in paths)
            {
                _omitted.Add(path);
            }

            return this;
        }

        /// <summary>Writes the field as a varint of 1, whatever shape it has.</summary>
        public Push AsVarint(string path) => Reshape(path, (writer, field) => writer.Varint(field, 1));

        /// <summary>Writes the field as a fixed64 of 1, whatever shape it has.</summary>
        public Push AsFixed64(string path) => Reshape(path, (writer, field) => writer.Double(field, 1));

        /// <summary>Writes the field as a length-delimited value whose two bytes are not valid UTF-8.</summary>
        public Push AsInvalidUtf8(string path)
            => Reshape(path, (writer, field) => writer.Tag(field, 2).Raw(2, 0xC3, 0x28));

        /// <summary>The RemoteData message, as the decoder gets it.</summary>
        public FenixChannelPush Remote() => FenixChannelPush.From(RemoteBytes())
            ?? throw new InvalidOperationException("the test's RemoteData is not valid protobuf");

        /// <summary>
        /// A snapshot of a trusted display: the message of <see cref="Remote"/> and a frame with this push's heading and
        /// the FMA slots given (16 empty ones) and the flight director given (none drawn).
        /// </summary>
        public FenixChannelSnapshot Snapshot(IReadOnlyList<FenixFmaSlot>? fma = null, FenixFdState? fd = null)
        {
            var frame = new FenixChannelFrame(false, Array.Empty<string>(), fd ?? FenixFdState.Default,
                fma ?? Enumerable.Repeat(new FenixFmaSlot("", false), 16).ToArray(),
                Array.Empty<string>(), Array.Empty<string>(), 0, 0, MagHeading, "")
            {
                PfdLive = true
            };

            return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), frame, Remote(), null, 0, 1);
        }

        public byte[] RemoteBytes()
        {
            var remote = new FenixChannelProtoWriter();

            // 1 efis: the captain's panel, then the first officer's
            PutPart(remote, "", 1, (panel, at) => PutPanel(panel, at, Efis));
            if (SendsOfficerEfis && !_omitted.Contains("1"))
            {
                var officer = new FenixChannelProtoWriter();
                PutPanel(officer, "officer", OfficerEfis);
                remote.Message(1, officer);
            }

            PutPart(remote, "", 28, (air, at) =>
            {
                PutDouble(air, at, 1, Pitch);
                PutDouble(air, at, 2, Bank);
                PutDouble(air, at, 4, Altitude);
                PutDouble(air, at, 5, Airspeed);
                PutDouble(air, at, 6, VerticalSpeed);
            });
            PutPart(remote, "", 46, (avail, at) =>
            {
                PutPart(avail, at, 1, (ir, irAt) =>
                {
                    PutFlag(ir, irAt, 1, AttitudeShown);
                    PutFlag(ir, irAt, 2, HeadingShown);
                });
                PutPart(avail, at, 2, (adr, adrAt) =>
                {
                    PutFlag(adr, adrAt, 1, SpeedShown);
                    PutFlag(adr, adrAt, 2, AltitudeShown);
                });
                PutFlag(avail, at, 8, VerticalSpeedShown);
            });
            PutPart(remote, "", 74, (info, at) =>
            {
                PutText(info, at, 1, LsIdent);
                PutText(info, at, 2, LsFrequency);
                PutText(info, at, 3, LsDme);
            });
            PutPart(remote, "", 82, (deviations, at) =>
            {
                PutDouble(deviations, at, 1, LocDots);
                PutDouble(deviations, at, 2, GsDots);
            });
            PutDouble(remote, "", 98, SpeedTrend);
            PutPart(remote, "", 100, (labels, at) =>
            {
                PutText(labels, at, 1, ApproachCenter);
                PutText(labels, at, 2, ApproachLoc);
                PutText(labels, at, 3, ApproachGs);
            });
            PutText(remote, "", 105, QnhText);
            PutText(remote, "", 107, MachText);
            PutPart(remote, "", 113, (fcu, at) =>
            {
                PutDouble(fcu, at, 4, TargetIas);
                PutFlag(fcu, at, 5, BugsVisible);
                PutFlag(fcu, at, 9, SpeedManaged);
            });
            PutPart(remote, "", 117, (flags, at) =>
            {
                PutText(flags, at, 1, MarkerText);
                PutText(flags, at, 3, LsFlagText);
            });
            PutPart(remote, "", 122, (altStatus, at) =>
            {
                PutFlag(altStatus, at, 3, MetricAltitude);
                PutFlag(altStatus, at, 8, AltitudeAmber);
            });
            PutPart(remote, "", 125, (limits, at) =>
            {
                PutDouble(limits, at, 1, VMax);
                PutDouble(limits, at, 2, Vls);
                PutDouble(limits, at, 3, AlphaProt);
                PutDouble(limits, at, 5, Vsw);
            });
            PutPart(remote, "", 127, (speeds, at) =>
            {
                PutDouble(speeds, at, 1, V1);
                PutDouble(speeds, at, 2, V2);
                PutDouble(speeds, at, 3, Vr);
                PutDouble(speeds, at, 4, F);
                PutDouble(speeds, at, 5, S);
                PutDouble(speeds, at, 6, GreenDot);
                PutDouble(speeds, at, 7, VfeNext);
            });
            PutText(remote, "", 157, RadioAltitudeText);
            PutPart(remote, "", 194, (heading, at) =>
            {
                PutDouble(heading, at, 1, MagHeading);
                PutDouble(heading, at, 2, MagneticTrack);
            });
            PutFlag(remote, "", 205, DhMessage);

            return remote.ToArray();
        }

        private Push Reshape(string path, Action<FenixChannelProtoWriter, int> write)
        {
            _reshaped[path] = write;
            return this;
        }

        private void PutPanel(FenixChannelProtoWriter panel, string at, Panel settings)
        {
            PutFlag(panel, at, 5, settings.Meters);
            PutFlag(panel, at, 6, settings.Ls);
            PutFlag(panel, at, 7, settings.Qfe);
            PutFlag(panel, at, 10, settings.QnhStd);
            PutFlag(panel, at, 11, settings.QnhInches);
            PutFlag(panel, at, 12, settings.ShowQnh);
            PutSingle(panel, at, 14, settings.Qnh == 1013f ? null : settings.Qnh);
            PutSingle(panel, at, 15, settings.QnhInch);
        }

        private static string At(string parent, int field)
        {
            string number = field.ToString(CultureInfo.InvariantCulture);
            return parent.Length == 0 ? number : $"{parent}.{number}";
        }

        // True when the push left this field out or wrote it in another shape, so that it is not written as given.
        private bool Handled(FenixChannelProtoWriter writer, string path, int field)
        {
            if (_omitted.Contains(path)) return true;
            if (!_reshaped.TryGetValue(path, out Action<FenixChannelProtoWriter, int>? write)) return false;

            write(writer, field);
            return true;
        }

        private void PutPart(FenixChannelProtoWriter writer, string parent, int field,
            Action<FenixChannelProtoWriter, string> fill)
        {
            string at = At(parent, field);
            if (Handled(writer, at, field)) return;

            var inner = new FenixChannelProtoWriter();
            fill(inner, at);
            writer.Message(field, inner);
        }

        private void PutDouble(FenixChannelProtoWriter writer, string parent, int field, double value)
        {
            if (Handled(writer, At(parent, field), field)) return;
            if (value == 0) return;

            writer.Double(field, value);
        }

        private void PutSingle(FenixChannelProtoWriter writer, string parent, int field, float? value)
        {
            if (Handled(writer, At(parent, field), field)) return;
            if (value is null) return;

            writer.Float(field, value.Value);
        }

        private void PutFlag(FenixChannelProtoWriter writer, string parent, int field, bool value)
        {
            if (Handled(writer, At(parent, field), field)) return;
            if (!value) return;

            writer.Bool(field, true);
        }

        private void PutText(FenixChannelProtoWriter writer, string parent, int field, string value)
        {
            if (Handled(writer, At(parent, field), field)) return;
            if (value.Length == 0) return;

            writer.String(field, value);
        }
    }
}
