// Characterization tests for FenixChannelIsis, the decoder of the ISIS from the RemoteData message of the Fenix display
// channel (the gRPC service on 127.0.0.1:18083). Pure logic: every message is built here with FenixChannelProtoWriter and
// the field numbers written out literally (182 for the ISIS, 1 to 23 for its members, 1 and 2 for a bug), never read from
// FenixChannelFields and never from a capture, so a wrong number in the app's table fails a test.
//
// The contract (IsisState, read from FenixDisplay.exe 3.0.0.7613 on 2026-11-04): 1 to 9 bools (varint), 10 to 15 doubles
// (fixed64), 16 and 17 Nullable<int32> (a varint while it has a value, absent while null), 18 to 20 doubles, 21 an int32
// (varint), 22 repeated BugState (nested: 1 Enabled, a varint bool; 2 Value, a fixed64 double), 23 a double. 1 is the
// gate's and 15 (Brightness) is not read. A member left out is its default (false, 0, null): protobuf-net writes no
// default, and NaN arrives as a value. A 182 that is absent is missing content; a 182 that is not a message, or a member
// in another wire type, is a wrong shape named by its path; a powered ISIS that carries neither a baro setting above 0 nor
// a countdown is "no ISIS data". A shape problem is reported before missing content.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelIsisDecoderTests
{
    // The RemoteData message of a push whose field 182 is the given part.
    private static FenixChannelPush Remote(FenixChannelProtoWriter isis)
        => FenixChannelPush.From(new FenixChannelProtoWriter().Message(182, isis).ToArray())!;

    private static FenixChannelSection<FenixIsisState> Decode(FenixChannelProtoWriter isis)
        => FenixChannelIsis.Decode(Remote(isis));

    private static FenixIsisState Decoded(FenixChannelProtoWriter isis)
    {
        FenixChannelSection<FenixIsisState> section = Decode(isis);
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    private static FenixChannelProtoWriter Bug(bool enabled, double value)
    {
        var bug = new FenixChannelProtoWriter();
        if (enabled) bug.Bool(1, true);
        if (value != 0) bug.Double(2, value);
        return bug;
    }

    // A powered ISIS with a baro setting, the least that decodes.
    private static FenixChannelProtoWriter Powered() => new FenixChannelProtoWriter().Bool(1, true).Double(18, 1013);

    // ---- Values -----------------------------------------------------------------------------

    [Fact]
    public void A_cruise_isis_decodes_every_value()
    {
        var isis = new FenixChannelProtoWriter()
            .Bool(1, true).Bool(3, true).Bool(6, true)
            .Double(10, 2.6).Double(11, -1.2).Double(12, 251.4).Double(13, 31012.2).Double(14, 0.746)
            .Double(18, 1013).Double(19, double.NaN).Double(20, double.NaN)
            .Message(22, Bug(false, 30)).Message(22, Bug(true, 140))
            .Double(23, -0.004);

        FenixIsisState state = Decoded(isis);

        Assert.False(state.Initializing);
        Assert.True(state.Standard);
        Assert.False(state.LsShown);
        Assert.False(state.InBugsPage);
        Assert.True(state.HectoPascal);
        Assert.False(state.LocFailed);
        Assert.False(state.GsFailed);
        Assert.False(state.NewStyle);
        Assert.Equal(2.6, state.Pitch);
        Assert.Equal(-1.2, state.Bank);
        Assert.Equal(251.4, state.Airspeed);
        Assert.Equal(31012.2, state.Altitude);
        Assert.Equal(0.746, state.Mach);
        Assert.Null(state.AlignSeconds);
        Assert.Null(state.RealignSeconds);
        Assert.Equal(1013, state.Qnh);
        Assert.True(double.IsNaN(state.LocDots));
        Assert.True(double.IsNaN(state.GsDots));
        Assert.Equal(0, state.SelectedBug);
        Assert.Equal(new[] { new FenixIsisBug(false, 30), new FenixIsisBug(true, 140) }, state.Bugs.ToArray());
        Assert.Equal(-0.004, state.Slip);
    }

    [Fact]
    public void Each_flag_lands_in_its_place()
    {
        // One flag at a time, so a number crossed with another shows.
        (int Field, Func<FenixIsisState, bool> Read)[] flags =
        [
            (2, s => s.Initializing), (3, s => s.Standard), (4, s => s.LsShown), (5, s => s.InBugsPage),
            (6, s => s.HectoPascal), (7, s => s.LocFailed), (8, s => s.GsFailed), (9, s => s.NewStyle),
        ];

        foreach ((int field, Func<FenixIsisState, bool> read) in flags)
        {
            FenixIsisState state = Decoded(Powered().Bool(field, true));
            Assert.True(read(state), $"field {field}");
            Assert.Equal(1, flags.Count(f => f.Read(state)));
        }
    }

    [Fact]
    public void Each_number_lands_in_its_place()
    {
        FenixIsisState state = Decoded(new FenixChannelProtoWriter().Bool(1, true)
            .Double(10, 10.5).Double(11, 11.5).Double(12, 12.5).Double(13, 13.5).Double(14, 14.5)
            .Double(18, 18.5).Double(19, 19.5).Double(20, 20.5).Varint(21, 3).Double(23, 23.5));

        Assert.Equal(10.5, state.Pitch);
        Assert.Equal(11.5, state.Bank);
        Assert.Equal(12.5, state.Airspeed);
        Assert.Equal(13.5, state.Altitude);
        Assert.Equal(14.5, state.Mach);
        Assert.Equal(18.5, state.Qnh);
        Assert.Equal(19.5, state.LocDots);
        Assert.Equal(20.5, state.GsDots);
        Assert.Equal(3, state.SelectedBug);
        Assert.Equal(23.5, state.Slip);
    }

    [Fact]
    public void An_absent_member_reads_its_default()
    {
        FenixIsisState state = Decoded(Powered());

        Assert.False(state.Initializing || state.Standard || state.LsShown || state.InBugsPage || state.HectoPascal
            || state.LocFailed || state.GsFailed || state.NewStyle);
        Assert.Equal(0, state.Pitch);
        Assert.Equal(0, state.Bank);
        Assert.Equal(0, state.Airspeed);
        Assert.Equal(0, state.Altitude);
        Assert.Equal(0, state.Mach);
        Assert.Null(state.AlignSeconds);
        Assert.Null(state.RealignSeconds);
        Assert.Equal(0, state.LocDots);
        Assert.Equal(0, state.GsDots);
        Assert.Equal(0, state.SelectedBug);
        Assert.Empty(state.Bugs);
        Assert.Equal(0, state.Slip);
    }

    [Fact]
    public void A_countdown_reads_its_seconds()
    {
        Assert.Equal(85, Decoded(Powered().Varint(16, 85)).AlignSeconds);
        Assert.Equal(8, Decoded(Powered().Varint(17, 8)).RealignSeconds);
        Assert.Null(Decoded(Powered().Varint(16, 85)).RealignSeconds);

        // A countdown at zero is a value, not null: protobuf-net writes a Nullable that has a value, even 0.
        Assert.Equal(0, Decoded(Powered().Varint(16, 0)).AlignSeconds);
    }

    [Fact]
    public void A_negative_int32_reads_back_negative()
    {
        // An int32 below zero goes on the wire sign-extended to ten bytes.
        Assert.Equal(-1, Decoded(Powered().Varint(21, unchecked((ulong)(long)-1))).SelectedBug);
    }

    [Fact]
    public void Bugs_come_back_in_order()
    {
        FenixIsisState state = Decoded(Powered()
            .Message(22, Bug(false, 30)).Message(22, Bug(true, 140)).Message(22, Bug(false, 6000)));

        Assert.Equal(
            new[] { new FenixIsisBug(false, 30), new FenixIsisBug(true, 140), new FenixIsisBug(false, 6000) },
            state.Bugs.ToArray());
    }

    [Fact]
    public void An_empty_bug_is_off_at_zero()
    {
        // As Robin's ground push of 2026-11-01 sent all six: each an empty BugState.
        var isis = Powered();
        for (int i = 0; i < 6; i++) isis.Message(22, new FenixChannelProtoWriter());

        FenixIsisState state = Decoded(isis);

        Assert.Equal(6, state.Bugs.Count);
        Assert.All(state.Bugs, bug => Assert.Equal(new FenixIsisBug(false, 0), bug));
    }

    // ---- Shape and content ------------------------------------------------------------------

    public static TheoryData<string, byte[]> WrongShapes() => new()
    {
        { "182", new FenixChannelProtoWriter().Varint(182, 1).ToArray() },
        { "182.10", new FenixChannelProtoWriter().Message(182, Powered().Varint(10, 2)).ToArray() },
        { "182.16", new FenixChannelProtoWriter().Message(182, Powered().Double(16, 85)).ToArray() },
        { "182.22", new FenixChannelProtoWriter().Message(182, Powered().Varint(22, 1)).ToArray() },
        { "182.22.2", new FenixChannelProtoWriter().Message(182, Powered().Message(22,
            new FenixChannelProtoWriter().Varint(2, 140))).ToArray() },
        { "182.3", new FenixChannelProtoWriter().Message(182, Powered().Double(3, 1)).ToArray() },
        { "182.21", new FenixChannelProtoWriter().Message(182, Powered().Double(21, 1)).ToArray() },
        { "182.22.1", new FenixChannelProtoWriter().Message(182, Powered().Message(22,
            new FenixChannelProtoWriter().Double(1, 1))).ToArray() },
    };

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public void A_wrong_shape_names_its_field(string path, byte[] remote)
    {
        FenixChannelSection<FenixIsisState> section = FenixChannelIsis.Decode(FenixChannelPush.From(remote)!);

        Assert.False(section.IsOk);
        Assert.Equal($"field {path} has the wrong shape", section.Problem);
    }

    [Fact]
    public void A_wrong_shape_is_reported_before_missing_content()
    {
        // No baro setting and no countdown, and a member in another shape: the shape is what hides the content.
        FenixChannelSection<FenixIsisState> section = Decode(new FenixChannelProtoWriter().Bool(1, true).Varint(12, 250));

        Assert.Equal("field 182.12 has the wrong shape", section.Problem);
    }

    [Fact]
    public void A_powered_isis_with_no_content_is_not_available()
    {
        Assert.Equal("no ISIS data", Decode(new FenixChannelProtoWriter().Bool(1, true)).Problem);
        Assert.True(Decode(new FenixChannelProtoWriter().Bool(1, true).Varint(16, 90)).IsOk);
        Assert.True(Decode(new FenixChannelProtoWriter().Bool(1, true).Varint(17, 8)).IsOk);
        Assert.True(Decode(new FenixChannelProtoWriter().Bool(1, true).Double(18, 1013)).IsOk);

        // A baro of 0 or NaN is no baro setting.
        Assert.Equal("no ISIS data", Decode(new FenixChannelProtoWriter().Bool(1, true).Double(18, 0)).Problem);
        Assert.Equal("no ISIS data", Decode(new FenixChannelProtoWriter().Bool(1, true).Double(18, double.NaN)).Problem);
    }

    [Fact]
    public void No_isis_field_is_missing_not_a_wrong_shape()
    {
        FenixChannelSection<FenixIsisState> section =
            FenixChannelIsis.Decode(FenixChannelPush.From(new FenixChannelProtoWriter().Double(197, 1000).ToArray())!);

        Assert.False(section.IsOk);
        Assert.Equal("no ISIS data", section.Problem);
    }

    [Fact]
    public void The_measured_ground_isis_decodes()
    {
        // Robin's ground push of 2026-11-01 (KATL), field 182 as the recorder printed it: powered, hPa, new style, the
        // Mach and both deviations NaN, the brightness 255 (not read), QNH 1013, and six empty bugs.
        var isis = new FenixChannelProtoWriter()
            .Bool(1, true).Bool(6, true).Bool(9, true)
            .Double(10, 0.1933068962081263).Double(11, -0.10153514154718231).Double(12, 9.297889709472656)
            .Double(13, 372.9428689316385).Double(14, double.NaN).Double(15, 255)
            .Double(18, 1013).Double(19, double.NaN).Double(20, double.NaN);
        for (int i = 0; i < 6; i++) isis.Message(22, new FenixChannelProtoWriter());

        FenixIsisState state = Decoded(isis);

        Assert.True(state.HectoPascal);
        Assert.True(state.NewStyle);
        Assert.False(state.Standard);
        Assert.Equal(372.9428689316385, state.Altitude);
        Assert.True(double.IsNaN(state.Mach));
        Assert.Equal(1013, state.Qnh);
        Assert.Equal(6, state.Bugs.Count);
    }
}
