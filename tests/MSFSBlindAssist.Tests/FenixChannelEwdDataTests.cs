// Characterization tests for FenixChannelEwdData, the decoder of the E/WD's own fields from the RemoteData message of the
// Fenix display channel (the gRPC service on 127.0.0.1:18083): the two engines, the thrust limit, the flap text and the
// weight unit. Pure logic: every message is built here with FenixChannelProtoWriter and the field numbers written out
// literally, never read from FenixChannelFields and never from a capture, so a wrong number in the app's table fails a
// test.
//
// The contract: the engine message (129) and both of its engines (129.1, 129.2) must be there, or the section is missing
// "engine data"; in an engine the N1, N2, fuel flow, EPR and EGT are fixed64s, and one that is left out reads 0 while a NaN
// stays NaN; the thrust limit (129.8) is repeated NullWrapped, an element without its string is "" and keeps its place;
// the flap text (152) is a string, "" when absent; the weight unit (154) is a bool, false when absent. A field in another
// wire shape, a string that is not UTF-8 or a part that does not parse is a wrong shape named by its path, the first one
// found, and it is reported before missing content. Fields the decoder does not read are skipped whatever their shape. A
// format letter the app does not know in the thrust limit or the flap text is an unknown code that names it.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelEwdDataTests
{
    // One engine: the N1 (2), N2 (3), fuel flow (4), EPR (5) and EGT (6).
    private static FenixChannelProtoWriter Engine(double n1, double n2, double ff, double epr, double egt)
        => new FenixChannelProtoWriter().Double(2, n1).Double(3, n2).Double(4, ff).Double(5, epr).Double(6, egt);

    // The cruise values of the two engines.
    private static FenixChannelProtoWriter LeftCruise() => Engine(81.0801, 86.1461, 1320, 1.4082, 376.7972);

    private static FenixChannelProtoWriter RightCruise() => Engine(81.2561, 86.3052, 1320, 1.407, 378.4421);

    // One element of the thrust limit: a message whose field 1 is the string, or one without it for a null string.
    private static FenixChannelProtoWriter Limit(string? text)
        => text is null ? new FenixChannelProtoWriter() : new FenixChannelProtoWriter().String(1, text);

    private static FenixChannelPush Parsed(FenixChannelProtoWriter remote)
        => FenixChannelPush.From(remote.ToArray())
            ?? throw new InvalidOperationException("the test's RemoteData is not valid protobuf");

    private static FenixChannelSection<FenixEwdData> Decode(FenixChannelProtoWriter remote)
        => FenixChannelEwdData.Decode(Parsed(remote));

    private static FenixEwdData Decoded(FenixChannelProtoWriter remote)
    {
        FenixChannelSection<FenixEwdData> section = Decode(remote);
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    // RemoteData with the cruise engines, the cruise thrust limit and the weight unit. With skippable true it also carries
    // fields the decoder does not read, in shapes the contract does not give them.
    private static FenixChannelProtoWriter Cruise(bool skippable = false)
    {
        FenixChannelProtoWriter left = LeftCruise();
        FenixChannelProtoWriter engines = new FenixChannelProtoWriter();
        if (skippable) left.Float(7, 1.5f);

        engines.Message(1, left).Message(2, RightCruise());
        engines.Message(8, Limit("cCLB")).Message(8, Limit("g1.618")).Message(8, Limit(null));
        if (skippable) engines.String(4, "text");

        var remote = new FenixChannelProtoWriter().Message(129, engines).Bool(154, true);
        if (skippable) remote.String(173, "text").Float(130, 2.5f).Varint(131, 7);
        return remote;
    }

    // ---- Values -----------------------------------------------------------------------------

    [Fact]
    public void The_cruise_values_decode()
    {
        FenixEwdData ewd = Decoded(Cruise());

        Assert.Equal(new FenixEwdEngine(N1: 81.0801, N2: 86.1461, FuelFlow: 1320, Epr: 1.4082, Egt: 376.7972), ewd.Left);
        Assert.Equal(new FenixEwdEngine(N1: 81.2561, N2: 86.3052, FuelFlow: 1320, Epr: 1.407, Egt: 378.4421), ewd.Right);
        Assert.Equal(new[] { "cCLB", "g1.618", "" }, ewd.ThrustLimit);
        Assert.True(ewd.IsKg);
        Assert.Equal("", ewd.FlapText);
    }

    [Fact]
    public void The_flap_text_is_read_as_sent_with_its_codes()
    {
        FenixChannelProtoWriter remote = Cruise().String(152, "cCONF 1");

        Assert.Equal("cCONF 1", Decoded(remote).FlapText);
    }

    // ---- Missing content --------------------------------------------------------------------

    [Fact]
    public void No_engine_data_is_missing()
    {
        FenixChannelSection<FenixEwdData> noEngines = Decode(new FenixChannelProtoWriter().Bool(154, true));
        Assert.False(noEngines.IsOk);
        Assert.Null(noEngines.Value);
        Assert.Equal("no engine data", noEngines.Problem);

        FenixChannelSection<FenixEwdData> leftOnly =
            Decode(new FenixChannelProtoWriter().Message(129, new FenixChannelProtoWriter().Message(1, LeftCruise())));
        Assert.False(leftOnly.IsOk);
        Assert.Null(leftOnly.Value);
        Assert.Equal("no engine data", leftOnly.Problem);

        FenixChannelSection<FenixEwdData> rightOnly =
            Decode(new FenixChannelProtoWriter().Message(129, new FenixChannelProtoWriter().Message(2, RightCruise())));
        Assert.Equal("no engine data", rightOnly.Problem);

        FenixChannelSection<FenixEwdData> emptyMessage =
            Decode(new FenixChannelProtoWriter().Message(129, new FenixChannelProtoWriter()));
        Assert.Equal("no engine data", emptyMessage.Problem);
    }

    // ---- What a message leaves out ----------------------------------------------------------

    [Fact]
    public void An_absent_value_reads_zero_and_nan_stays_nan()
    {
        // The left engine sends nothing: protobuf-net leaves a 0 out. The right sends a NaN for two gauges, which is a
        // value and stays one, among values that are there. The thrust limit has an element with no string between two
        // with one: it reads "" and keeps its place. The weight unit and the flap text are left out.
        FenixChannelProtoWriter engines = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter())
            .Message(2, Engine(double.NaN, 86.3052, 1320, 1.407, double.NaN))
            .Message(8, Limit("cCLB"))
            .Message(8, Limit(null))
            .Message(8, Limit("g1.618"));

        FenixEwdData ewd = Decoded(new FenixChannelProtoWriter().Message(129, engines));

        Assert.Equal(new FenixEwdEngine(0, 0, 0, 0, 0), ewd.Left);
        Assert.True(double.IsNaN(ewd.Right.N1));
        Assert.Equal(86.3052, ewd.Right.N2);
        Assert.Equal(1320, ewd.Right.FuelFlow);
        Assert.Equal(1.407, ewd.Right.Epr);
        Assert.True(double.IsNaN(ewd.Right.Egt));
        Assert.Equal(new[] { "cCLB", "", "g1.618" }, ewd.ThrustLimit);
        Assert.False(ewd.IsKg);
        Assert.Equal("", ewd.FlapText);
    }

    [Fact]
    public void Fields_it_does_not_read_are_skipped()
    {
        FenixEwdData plain = Decoded(Cruise());
        FenixEwdData withExtras = Decoded(Cruise(skippable: true));

        Assert.Equal(plain.Left, withExtras.Left);
        Assert.Equal(plain.Right, withExtras.Right);
        Assert.Equal(plain.ThrustLimit, withExtras.ThrustLimit);
        Assert.Equal(plain.IsKg, withExtras.IsKg);
        Assert.Equal(plain.FlapText, withExtras.FlapText);
    }

    // ---- Wrong shapes -----------------------------------------------------------------------

    // Writes `field` into `into` in a wire shape the decoder does not accept for it.
    private static void Damage(FenixChannelProtoWriter into, int field, string shape)
    {
        switch (shape)
        {
            case "varint": into.Varint(field, 1); break;
            case "fixed64": into.Double(field, 1); break;
            case "utf8": into.Tag(field, 2).Raw(2, 0xC3, 0x28); break;     // two bytes that are not UTF-8
            case "broken": into.Tag(field, 2).Raw(1, 0x80); break;          // one byte that is not a whole tag
            default: throw new ArgumentException($"no wrong shape called {shape}", nameof(shape));
        }
    }

    // A complete RemoteData as the E/WD reads it, with each field named in `damaged` (by its path in the push) written in
    // the wire shape given with it instead of the right one.
    private static FenixChannelProtoWriter Complete(params (string Path, string Shape)[] damaged)
    {
        void Put(FenixChannelProtoWriter into, string path, int field, Action<FenixChannelProtoWriter> intact)
        {
            foreach ((string damagedPath, string shape) in damaged)
            {
                if (damagedPath != path) continue;

                Damage(into, field, shape);
                return;
            }

            intact(into);
        }

        FenixChannelProtoWriter EngineAt(string at, double n1, double n2, double ff, double epr, double egt)
        {
            var engine = new FenixChannelProtoWriter();
            Put(engine, at + ".2", 2, writer => writer.Double(2, n1));
            Put(engine, at + ".3", 3, writer => writer.Double(3, n2));
            Put(engine, at + ".4", 4, writer => writer.Double(4, ff));
            Put(engine, at + ".5", 5, writer => writer.Double(5, epr));
            Put(engine, at + ".6", 6, writer => writer.Double(6, egt));
            return engine;
        }

        var wrapped = new FenixChannelProtoWriter();
        Put(wrapped, "129.8.1", 1, writer => writer.String(1, "cCLB"));

        var engines = new FenixChannelProtoWriter();
        Put(engines, "129.1", 1, writer => writer.Message(1, EngineAt("129.1", 81.0801, 86.1461, 1320, 1.4082, 376.7972)));
        Put(engines, "129.2", 2, writer => writer.Message(2, EngineAt("129.2", 81.2561, 86.3052, 1320, 1.407, 378.4421)));
        Put(engines, "129.8", 8, writer => writer.Message(8, wrapped));

        var remote = new FenixChannelProtoWriter();
        Put(remote, "129", 129, writer => writer.Message(129, engines));
        Put(remote, "152", 152, writer => writer.String(152, "cCONF 1"));
        Put(remote, "154", 154, writer => writer.Bool(154, true));
        return remote;
    }

    // Every field the decoder reads, with a wire shape it does not have: a varint for a number, a part or a string, a
    // fixed64 for a flag, the bytes of an invalid UTF-8 string for a text, and a part whose bytes are not protobuf.
    public static IEnumerable<object[]> WrongShapes()
    {
        List<(string Path, string Shape)> reshapes =
        [
            ("129", "varint"), ("129", "utf8"), ("129", "broken"),
            ("129.1", "varint"), ("129.1", "broken"),
            ("129.2", "varint"), ("129.2", "broken"),
            ("129.8", "varint"), ("129.8", "broken"),
            ("129.8.1", "varint"), ("129.8.1", "utf8"),
            ("152", "varint"), ("152", "utf8"),
            ("154", "fixed64"), ("154", "utf8")
        ];

        foreach (string engine in new[] { "129.1", "129.2" })
        {
            foreach (int field in new[] { 2, 3, 4, 5, 6 })
            {
                reshapes.Add(($"{engine}.{field}", "varint"));
            }
        }

        return reshapes.Select(reshape => new object[] { reshape.Path, reshape.Shape });
    }

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public void A_wrong_shape_names_its_path(string path, string shape)
    {
        FenixChannelSection<FenixEwdData> section = Decode(Complete((path, shape)));

        Assert.False(section.IsOk);
        Assert.Null(section.Value);
        Assert.Equal($"field {path} has the wrong shape", section.Problem);
    }

    [Fact]
    public void The_complete_message_the_wrong_shape_cases_start_from_decodes()
    {
        Assert.True(Decode(Complete()).IsOk);
    }

    [Fact]
    public void The_first_wrong_shape_found_wins_and_comes_before_missing_data()
    {
        // In the order of a path: the engine message, the left engine and its numbers, the right engine and its numbers,
        // the thrust limit, the flap text, the weight unit.
        Assert.Equal("field 129.1.6 has the wrong shape",
            Decode(Complete(("129.2.2", "varint"), ("129.1.6", "varint"))).Problem);
        Assert.Equal("field 129.2 has the wrong shape",
            Decode(Complete(("129.8", "varint"), ("129.2", "varint"))).Problem);
        Assert.Equal("field 129.8 has the wrong shape",
            Decode(Complete(("152", "varint"), ("129.8", "varint"))).Problem);
        Assert.Equal("field 152 has the wrong shape",
            Decode(Complete(("154", "fixed64"), ("152", "varint"))).Problem);

        // A left engine that is not a message hides the right one, and the damage is what the section says.
        FenixChannelProtoWriter leftBroken = new FenixChannelProtoWriter()
            .Message(129, new FenixChannelProtoWriter().Varint(1, 1));
        Assert.Equal("field 129.1 has the wrong shape", Decode(leftBroken).Problem);
    }

    // ---- Format letters ---------------------------------------------------------------------

    [Fact]
    public void An_unknown_letter_in_the_thrust_limit_or_flap_text_is_an_unknown_code()
    {
        FenixChannelProtoWriter badLimit = new FenixChannelProtoWriter().Message(129, new FenixChannelProtoWriter()
            .Message(1, LeftCruise())
            .Message(2, RightCruise())
            .Message(8, Limit("cCLB"))
            .Message(8, Limit("q1.618")));
        FenixChannelSection<FenixEwdData> limit = Decode(badLimit);
        Assert.False(limit.IsOk);
        Assert.Null(limit.Value);
        Assert.Equal("unknown code 'q' in the thrust limit", limit.Problem);

        FenixChannelSection<FenixEwdData> flap = Decode(Cruise().String(152, "xCONF"));
        Assert.False(flap.IsOk);
        Assert.Null(flap.Value);
        Assert.Equal("unknown code 'x' in the flap text", flap.Problem);
    }
}
