// Characterization tests for FenixCabinData and FenixCabinLines: the value cache behind the Fenix Cabin
// window, the labelled lines of its five tabs (one ListBox line per value), the FAP's water and waste
// cautions, the door-pressure messages and the connection status. The rules are the FAP bundle's own
// (its lavatory, water and waste cautions, door message, door and slide names, temperature store,
// power-up screens and Set-Up store); "live" is the IAE A321 read of 2026-10-21. An unknown value ("" from
// the data service) is never a state or a caution (FNX-1).

using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixCabinLinesTests
{
    private const string AfterTakeoffId = "bc69d460-bfb0-4945-ac49-4da6d695d962";
    private const string SafetyBriefingId = "ab9c425f-4b60-483b-9679-bab553460bcb";

    private static readonly FenixCabinLayout A321 =
        FenixCabinLayout.Resolve(FenixAircraftType.A321, "DoubleFullsize", "TwoClass", "SlideDisarmed");

    private static readonly FenixCabinLayout A320InPark =
        FenixCabinLayout.Resolve(FenixAircraftType.A320, "DoubleOverwing", "SingleClass", "SlideInPark");

    // The live pram state: music off, one channel, volume 5, nothing playing, memo empty.
    private static readonly FenixCabinPramState LivePram = new(
        MusicOn: false,
        Channels: [new FenixCabinItem("f5957b3b-9ba4-4933-822f-806acf7f0cb7", "Boarding Music")],
        SelectedChannel: 0,
        Volume: 5,
        NowAnnouncing: null,
        Memo: [],
        PlayingAllInMemo: false,
        Announcements: [new FenixCabinItem(AfterTakeoffId, "After Takeoff"), new FenixCabinItem(SafetyBriefingId, "Safety Briefing")],
        PreferredVariant: 0,
        MaxVariant: 0);

    private static FenixCabinData Data(params (string Name, JToken Value)[] values)
    {
        var data = new FenixCabinData();
        foreach (var (name, value) in values)
            data.Apply(name, FenixDataValue.FromJson(value));
        return data;
    }

    // The live data read of 2026-10-21, then the overrides in order. The loudspeaker
    // volume 1 (= 100 percent) is the live value.
    private static FenixCabinData Live(params (string Name, JToken Value)[] overrides)
    {
        var values = new List<(string Name, JToken Value)>
        {
            (FenixCabinNames.CabinLights, "Bright"),
            (FenixCabinNames.CabinReady, false),
            (FenixCabinNames.CabinReadyAvailable, false),
            (FenixCabinNames.CidsOperational, true),
            (FenixCabinNames.CidsReady, true),
            (FenixCabinNames.CidsPowerUp, 100),
            (FenixCabinNames.FapOperational, true),
            (FenixCabinNames.FapReady, true),
            (FenixCabinNames.FapPowerUp, 100),
            (FenixCabinNames.FapSpeakerVolume, 1),
            (FenixCabinNames.TempFwdAir, 24.000536617538728),
            (FenixCabinNames.TempFwdSelected, 24),
            (FenixCabinNames.TempFwdOffset, 0),
            (FenixCabinNames.TempAftAir, 24.000544906158098),
            (FenixCabinNames.TempAftSelected, 24),
            (FenixCabinNames.TempAftOffset, 0),
            (FenixCabinNames.WaterTank, 85.00000000000001),
            (FenixCabinNames.WaterPreselect, 100),
            (FenixCabinNames.WasteTank, 5),
            (FenixCabinNames.WasteTankFull, false),
            (FenixCabinNames.CargoForward, false),
            (FenixCabinNames.CargoAft, false),
            (FenixCabinNames.CargoBulk, false),
            (FenixCabinNames.CargoFwdMoving, false),
            (FenixCabinNames.CargoAftMoving, false),
            ("aircraft.communication.cids.lav.b.flush.operational", "")
        };
        foreach (string key in FenixCabinNames.DoorKeys)
        {
            values.Add((FenixCabinNames.Door(key), false));
            values.Add((FenixCabinNames.DoorPressure(key), 2000));
            values.Add((FenixCabinNames.DoorControl(key), 0));
        }
        foreach (string key in FenixCabinNames.SlideKeys)
        {
            values.Add((FenixCabinNames.Slide(key), true));
            values.Add((FenixCabinNames.SlidePressure(key), key is "owl" or "owr" ? (JToken)"" : 3500));
        }
        foreach (string letter in new[] { "A", "M", "D", "E" })
            values.Add((FenixCabinNames.LavatoryFlush(letter), true));
        values.AddRange(overrides);
        return Data(values.ToArray());
    }

    private static IReadOnlyList<FenixCabinLine> Section(IReadOnlyList<FenixCabinLine> lines, FenixCabinSection section)
        => lines.Where(l => l.Section == section).ToList();

    private static string[] Texts(IReadOnlyList<FenixCabinLine> lines, FenixCabinSection section)
        => Section(lines, section).Select(l => l.Text).ToArray();

    private static string Line(IReadOnlyList<FenixCabinLine> lines, string key)
        => Assert.Single(lines, l => l.Key == key).Text;

    // ---- The value cache ------------------------------------------------------------------------

    [Fact]
    public void Data_keeps_the_last_value_and_reads_only_its_own_kind()
    {
        var data = new FenixCabinData();
        Assert.False(data.AnyKnown);
        Assert.Equal(FenixDataValue.Unknown, data.Get(FenixCabinNames.CabinReady));
        Assert.False(data.Apply(FenixCabinNames.CabinReady, FenixDataValue.Unknown));

        Assert.True(data.Apply(FenixCabinNames.CabinReady, FenixDataValue.FromJson(false)));
        Assert.False(data.Apply(FenixCabinNames.CabinReady, FenixDataValue.FromJson(false)));
        Assert.True(data.Apply(FenixCabinNames.CabinReady, FenixDataValue.FromJson(true)));
        Assert.True(data.AnyKnown);
        Assert.Equal(true, data.Bool(FenixCabinNames.CabinReady));
        Assert.Null(data.Number(FenixCabinNames.CabinReady));
        Assert.Null(data.Text(FenixCabinNames.CabinReady));

        data.Apply(FenixCabinNames.WaterTank, FenixDataValue.FromJson(85.5));
        Assert.Equal(85.5, data.Number(FenixCabinNames.WaterTank));
        Assert.Null(data.Bool(FenixCabinNames.WaterTank));

        data.Apply(FenixCabinNames.CabinLights, FenixDataValue.FromJson("Dim1"));
        Assert.Equal("Dim1", data.Text(FenixCabinNames.CabinLights));
        Assert.Null(data.Number(FenixCabinNames.CabinLights));

        // An unknown name reads "" (FNX-1): stored, but none of the typed reads gives a value.
        Assert.True(data.Apply(FenixCabinNames.WaterTank, FenixDataValue.FromJson("")));
        Assert.Null(data.Number(FenixCabinNames.WaterTank));
        Assert.Null(data.Text(FenixCabinNames.WaterTank));
    }

    [Fact]
    public void Data_with_only_unknown_or_null_values_knows_nothing()
    {
        var data = Data(
            (FenixCabinNames.CabinReady, ""),
            (FenixCabinNames.WaterTank, JValue.CreateNull()));

        Assert.False(data.AnyKnown);
        Assert.Null(data.Bool(FenixCabinNames.CabinReady));
        Assert.Null(data.Number(FenixCabinNames.WaterTank));
    }

    // ---- Formatting -----------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Formatting_is_invariant_under_comma_cultures(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            Assert.Equal("24.0", FenixCabinLines.Degrees(24.000536617538728));
            Assert.Equal("24.5", FenixCabinLines.Degrees(24.5));
            Assert.Equal("85 percent", FenixCabinLines.Percent(85.00000000000001));
            Assert.Equal("13 percent", FenixCabinLines.Percent(12.5));
            Assert.Equal("no offset", FenixCabinLines.Offset(0));
            Assert.Equal("offset plus 0.5", FenixCabinLines.Offset(0.5));
            Assert.Equal("offset minus 1.0", FenixCabinLines.Offset(-1));
            Assert.Equal("offset minus 2.5", FenixCabinLines.Offset(-2.5));
            return 0;
        });
    }

    [Fact]
    public void Formatting_never_says_minus_zero()
    {
        Assert.Equal("0.0", FenixCabinLines.Degrees(-0.04));
        Assert.Equal("0 percent", FenixCabinLines.Percent(-0.3));
        Assert.Equal("no offset", FenixCabinLines.Offset(-0.01));
    }

    [Theory]
    [InlineData("1L", "Door 1 left")]
    [InlineData("1R", "Door 1 right")]
    [InlineData("4L", "Door 4 left")]
    [InlineData("3R", "Door 3 right")]
    [InlineData("OWL", "Overwing slide left")]
    [InlineData("OWR", "Overwing slide right")]
    public void DoorName_follows_the_FAP_numbering(string designation, string name)
    {
        Assert.Equal(name, FenixCabinLines.DoorName(designation));
    }

    // ---- Status ---------------------------------------------------------------------------------

    [Fact]
    public void Status_lines_for_the_live_A321()
    {
        var lines = TestCulture.Run("sv-SE", () => FenixCabinLines.Build(Live(), LivePram, A321));

        Assert.Equal(
            new[]
            {
                "Cabin ready: no, not available yet",
                "Cabin lights: Bright",
                "Boarding music: off",
                "Announcement playing: none",
                "Doors: all closed",
                "Slides: all armed",
                "Cargo doors: all closed",
                "Cabin temperature: forward 24.0, aft 24.0 degrees",
                "Water 85 percent, waste 5 percent",
                "Cautions: none"
            },
            Texts(lines, FenixCabinSection.Status));
        Assert.Equal(
            new[]
            {
                "status.cabinReady", "status.lights", "status.music", "status.announcing", "status.doors",
                "status.slides", "status.cargo", "status.temperature", "status.water", "status.cautions"
            },
            Section(lines, FenixCabinSection.Status).Select(l => l.Key));

        Assert.Equal("Cabin ready: yes",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.CabinReady, true)), LivePram, A321), "status.cabinReady"));
        Assert.Equal("Boarding music: on, Boarding Music, volume 5",
            Line(FenixCabinLines.Build(Live(), LivePram with { MusicOn = true }, A321), "status.music"));
        Assert.Equal("Doors open: 1 left, 4 right",
            Line(FenixCabinLines.Build(
                Live((FenixCabinNames.Door("d1l"), true), (FenixCabinNames.Door("d4r"), true)), LivePram, A321), "status.doors"));
    }

    [Fact]
    public void Status_cabin_ready_and_lights_say_only_what_is_known()
    {
        string Ready(params (string, JToken)[] overrides)
            => Line(FenixCabinLines.Build(Live(overrides), LivePram, A321), "status.cabinReady");

        Assert.Equal("Cabin ready: no", Ready((FenixCabinNames.CabinReadyAvailable, true)));
        Assert.Equal("Cabin ready: no", Ready((FenixCabinNames.CabinReadyAvailable, "")));
        Assert.Equal("Cabin ready: no data", Ready((FenixCabinNames.CabinReady, "")));

        string Lights(JToken value)
            => Line(FenixCabinLines.Build(Live((FenixCabinNames.CabinLights, value)), LivePram, A321), "status.lights");

        Assert.Equal("Cabin lights: Dim 1", Lights("Dim1"));
        Assert.Equal("Cabin lights: Dim 2", Lights("Dim2"));
        Assert.Equal("Cabin lights: Off", Lights("Off"));
        Assert.Equal("Cabin lights: no data", Lights(""));
        Assert.Equal("Cabin lights: no data", Lights(3));
    }

    [Fact]
    public void Status_summaries_claim_only_what_the_data_proves()
    {
        string Status(string key, FenixCabinData data, FenixCabinLayout? layout = null)
            => Line(FenixCabinLines.Build(data, LivePram, layout ?? A321), key);

        // Doors: one unknown and none open is not "all closed"; an open door is still reported.
        Assert.Equal("Doors: no data", Status("status.doors", Live((FenixCabinNames.Door("d2l"), ""))));
        Assert.Equal("Doors open: 1 left",
            Status("status.doors", Live((FenixCabinNames.Door("d2l"), ""), (FenixCabinNames.Door("d1l"), true))));

        // Slides: the summary counts only the layout's slides (the live A321 reads owl/owr too).
        Assert.Equal("Slides: all armed", Status("status.slides", Live((FenixCabinNames.Slide("owl"), false))));
        Assert.Equal("Slides disarmed: 1 right, 4 left",
            Status("status.slides", Live((FenixCabinNames.Slide("d1r"), false), (FenixCabinNames.Slide("d4l"), false))));
        Assert.Equal("Slides disarmed: overwing left",
            Status("status.slides", Live((FenixCabinNames.Slide("owl"), false)), A320InPark));
        Assert.Equal("Slides: no data", Status("status.slides", Live((FenixCabinNames.Slide("d3r"), ""))));

        // Doors the layout does not list are not counted: the A320 reads d2l open, it has no 2L.
        Assert.Equal("Doors: all closed", Status("status.doors", Live((FenixCabinNames.Door("d2l"), true)), A320InPark));

        // Cargo: the same rule as the cargo door lines (moving, else open or closed).
        Assert.Equal("Cargo doors: forward open", Status("status.cargo", Live((FenixCabinNames.CargoForward, true))));
        Assert.Equal("Cargo doors: forward open, aft moving",
            Status("status.cargo", Live((FenixCabinNames.CargoForward, true), (FenixCabinNames.CargoAftMoving, true))));
        Assert.Equal("Cargo doors: no data", Status("status.cargo", Live((FenixCabinNames.CargoBulk, ""))));
        Assert.Equal("Cargo doors: all closed",
            Status("status.cargo", Live((FenixCabinNames.CargoBulk, true)),
                FenixCabinLayout.Resolve(FenixAircraftType.A319, null, null, null)));

        // Temperature and water: the known half is said, the other half is "no data".
        Assert.Equal("Cabin temperature: forward no data, aft 24.0 degrees",
            Status("status.temperature", Live((FenixCabinNames.TempFwdAir, ""))));
        Assert.Equal("Cabin temperature: no data",
            Status("status.temperature", Live((FenixCabinNames.TempFwdAir, ""), (FenixCabinNames.TempAftAir, ""))));
        Assert.Equal("Water 85 percent, waste no data", Status("status.water", Live((FenixCabinNames.WasteTank, ""))));
        Assert.Equal("Water and waste: no data",
            Status("status.water", Live((FenixCabinNames.WaterTank, ""), (FenixCabinNames.WasteTank, ""))));

        // Music and announcing come from the pram state.
        Assert.Equal("Announcement playing: Safety Briefing",
            Line(FenixCabinLines.Build(Live(), LivePram with { NowAnnouncing = "Safety Briefing" }, A321), "status.announcing"));
        Assert.Equal("Boarding music: no data", Line(FenixCabinLines.Build(Live(), null, A321), "status.music"));
        Assert.Equal("Announcement playing: no data", Line(FenixCabinLines.Build(Live(), null, A321), "status.announcing"));
    }

    // ---- Audio ----------------------------------------------------------------------------------

    [Fact]
    public void Audio_lines_from_the_pram_state()
    {
        var lines = FenixCabinLines.Build(Live(), LivePram, A321);

        Assert.Equal(
            new[]
            {
                "Boarding music: off",
                "Music channel: Boarding Music, 1 of 1",
                "Volume: 5",
                "Now announcing: nothing",
                "Memo: empty",
                "Playing all memo announcements: no",
                "FAP loudspeaker volume: 100 percent"
            },
            Texts(lines, FenixCabinSection.Audio));
        Assert.Equal(
            new[] { "audio.music", "audio.channel", "audio.volume", "audio.announcing", "audio.memo", "audio.playAll", "audio.speakerVolume" },
            Section(lines, FenixCabinSection.Audio).Select(l => l.Key));
        Assert.DoesNotContain(lines, l => l.Key == "audio.set");
        Assert.DoesNotContain(lines, l => l.Key == "audio.unavailable");

        var withSets = FenixCabinLines.Build(Live(), LivePram with { MaxVariant = 2, PreferredVariant = 1 }, A321);
        Assert.Equal("Announcement set: 1 (0 to 2)", Line(withSets, "audio.set"));

        // The loudspeaker line comes from the data, so it stays when the pram state is missing.
        var noPram = FenixCabinLines.Build(Live(), null, A321);
        Assert.Equal(new[] { "Audio: no data from the Fenix", "FAP loudspeaker volume: 100 percent" }, Texts(noPram, FenixCabinSection.Audio));
        Assert.Equal(new[] { "audio.unavailable", "audio.speakerVolume" }, Section(noPram, FenixCabinSection.Audio).Select(l => l.Key));
    }

    [Fact]
    public void Audio_lines_follow_the_pram_lists()
    {
        var playing = LivePram with
        {
            MusicOn = true,
            Channels = [new FenixCabinItem("c1", "Jazz"), new FenixCabinItem("c2", "Classic")],
            SelectedChannel = 1,
            NowAnnouncing = "After Takeoff",
            Memo = [new FenixCabinItem(AfterTakeoffId, "After Takeoff"), new FenixCabinItem(SafetyBriefingId, "Safety Briefing")],
            PlayingAllInMemo = true
        };
        var lines = FenixCabinLines.Build(Live(), playing, A321);

        Assert.Equal("Boarding music: on", Line(lines, "audio.music"));
        Assert.Equal("Music channel: Classic, 2 of 2", Line(lines, "audio.channel"));
        Assert.Equal("Now announcing: After Takeoff", Line(lines, "audio.announcing"));
        Assert.Equal("Memo: After Takeoff, Safety Briefing", Line(lines, "audio.memo"));
        Assert.Equal("Playing all memo announcements: yes", Line(lines, "audio.playAll"));
        Assert.Equal("Boarding music: on, Classic, volume 5", Line(lines, "status.music"));

        Assert.Equal("Music channel: none",
            Line(FenixCabinLines.Build(Live(), LivePram with { Channels = [] }, A321), "audio.channel"));
        Assert.Equal("Music channel: no data",
            Line(FenixCabinLines.Build(Live(), LivePram with { SelectedChannel = 3 }, A321), "audio.channel"));
    }

    [Theory]
    [InlineData(0.83, "FAP loudspeaker volume: 85 percent")]
    [InlineData(0.8, "FAP loudspeaker volume: 80 percent")]
    [InlineData(1.2, "FAP loudspeaker volume: 100 percent")]
    [InlineData(-0.1, "FAP loudspeaker volume: 0 percent")]
    public void Loudspeaker_volume_reads_as_the_FAP_set_up_page_shows_it(double fraction, string text)
    {
        string line = TestCulture.Run("de-DE",
            () => Line(FenixCabinLines.Build(Live((FenixCabinNames.FapSpeakerVolume, fraction)), LivePram, A321), "audio.speakerVolume"));
        Assert.Equal(text, line);
    }

    [Fact]
    public void Unknown_loudspeaker_volume_is_no_data()
    {
        Assert.Equal("FAP loudspeaker volume: no data",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.FapSpeakerVolume, "")), LivePram, A321), "audio.speakerVolume"));
    }

    // ---- Temperature ----------------------------------------------------------------------------

    [Fact]
    public void Temperature_lines_add_the_offset_to_the_target()
    {
        var data = Live(
            (FenixCabinNames.TempFwdAir, 24.4), (FenixCabinNames.TempFwdSelected, 24), (FenixCabinNames.TempFwdOffset, 0.5),
            (FenixCabinNames.TempAftAir, 23.96), (FenixCabinNames.TempAftSelected, 24), (FenixCabinNames.TempAftOffset, 0));
        var lines = TestCulture.Run("de-DE", () => FenixCabinLines.Build(data, LivePram, A321));

        Assert.Equal(
            new[]
            {
                "Forward zone: 24.4 degrees, target 24.5 degrees, offset plus 0.5",
                "Aft zone: 24.0 degrees, target 24.0 degrees, no offset"
            },
            Texts(lines, FenixCabinSection.Temperature));
        Assert.Equal(new[] { "temp.forward", "temp.aft" }, Section(lines, FenixCabinSection.Temperature).Select(l => l.Key));
        Assert.DoesNotContain(lines, l => l.Key == "temp.available");
    }

    [Theory]
    [InlineData(FenixCabinNames.ZoneControllerOperational)]
    [InlineData(FenixCabinNames.LavGalleyFanRunning)]
    [InlineData(FenixCabinNames.CidsOperational)]
    public void Temperature_control_is_not_available_when_the_FAP_would_grey_it(string name)
    {
        var lines = FenixCabinLines.Build(Live((name, false)), LivePram, A321);
        var temperature = Section(lines, FenixCabinSection.Temperature);

        Assert.Equal("temp.available", temperature[0].Key);
        Assert.Equal("Temperature control: not available", temperature[0].Text);
        Assert.Equal(new[] { "temp.available", "temp.forward", "temp.aft" }, temperature.Select(l => l.Key));
    }

    [Fact]
    public void Temperature_zone_says_no_data_for_what_it_lacks()
    {
        Assert.Equal("Forward zone: 24.0 degrees, target no data, offset no data",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.TempFwdOffset, "")), LivePram, A321), "temp.forward"));
        Assert.Equal("Aft zone: no data, target 24.0 degrees, no offset",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.TempAftAir, "")), LivePram, A321), "temp.aft"));
    }

    // ---- Doors ----------------------------------------------------------------------------------

    [Fact]
    public void Door_lines_follow_the_layout_and_the_FAP_messages()
    {
        var data = Data(
            (FenixCabinNames.Door("d1l"), false), (FenixCabinNames.Slide("d1l"), true),
            (FenixCabinNames.DoorPressure("d1l"), 2000), (FenixCabinNames.SlidePressure("d1l"), 3500),
            (FenixCabinNames.Door("d1r"), false), (FenixCabinNames.Slide("d1r"), false),
            (FenixCabinNames.DoorPressure("d1r"), 2000), (FenixCabinNames.SlidePressure("d1r"), 3500),
            (FenixCabinNames.Door("d4l"), true), (FenixCabinNames.Slide("d4l"), false),
            (FenixCabinNames.DoorPressure("d4l"), 2000), (FenixCabinNames.SlidePressure("d4l"), 3500),
            (FenixCabinNames.Door("d4r"), false), (FenixCabinNames.Slide("d4r"), true),
            (FenixCabinNames.DoorPressure("d4r"), 2000), (FenixCabinNames.SlidePressure("d4r"), 2900),
            (FenixCabinNames.Slide("owl"), true),
            (FenixCabinNames.CargoForward, true),
            (FenixCabinNames.CargoAftMoving, true),
            (FenixCabinNames.Door("d2l"), false));
        var lines = FenixCabinLines.Build(data, null, A320InPark);

        Assert.Equal("Door 1 left: closed, slide armed", Line(lines, "door.d1l"));
        Assert.Equal("Door 1 right: closed, slide in park", Line(lines, "door.d1r"));
        Assert.Equal("Door 4 left: open, slide disarmed", Line(lines, "door.d4l"));
        Assert.Equal("Door 4 right: closed, slide armed, check slide pressure", Line(lines, "door.d4r"));
        Assert.Equal("Overwing slide left: armed", Line(lines, "slide.owl"));
        Assert.Equal("Overwing slide right: no data", Line(lines, "slide.owr"));
        Assert.Equal("Forward cargo door: open", Line(lines, "cargo.forward"));
        Assert.Equal("Aft cargo door: moving", Line(lines, "cargo.aft"));
        Assert.Equal("Bulk cargo door: no data", Line(lines, "cargo.bulk"));
        Assert.DoesNotContain(lines, l => l.Key == "door.d2l");

        // The FAP's own front-to-back order: its slide list (1L 1R OWL OWR 4L 4R), then the cargo doors.
        Assert.Equal(
            new[] { "door.d1l", "door.d1r", "slide.owl", "slide.owr", "door.d4l", "door.d4r", "cargo.forward", "cargo.aft", "cargo.bulk" },
            Section(lines, FenixCabinSection.Doors).Select(l => l.Key));
    }

    [Fact]
    public void Door_lines_of_the_live_A321_list_eight_doors_and_no_overwing_slide()
    {
        var lines = FenixCabinLines.Build(Live(), LivePram, A321);

        Assert.Equal(
            new[]
            {
                "door.d1l", "door.d1r", "door.d2l", "door.d2r", "door.d3l", "door.d3r", "door.d4l", "door.d4r",
                "cargo.forward", "cargo.aft", "cargo.bulk"
            },
            Section(lines, FenixCabinSection.Doors).Select(l => l.Key));
        Assert.Equal("Door 1 left: closed, slide armed", Line(lines, "door.d1l"));
        Assert.Equal("Forward cargo door: closed", Line(lines, "cargo.forward"));
        Assert.Equal("Bulk cargo door: closed", Line(lines, "cargo.bulk"));

        // A closed door with a disarmed slide says "slide disarmed" unless the setting is SlideInPark.
        Assert.Equal("Door 2 right: closed, slide disarmed",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.Slide("d2r"), false)), LivePram, A321), "door.d2r"));

        // The A319 lists no bulk cargo door.
        var a319 = FenixCabinLayout.Resolve(FenixAircraftType.A319, null, null, null);
        Assert.DoesNotContain(FenixCabinLines.Build(Live(), LivePram, a319), l => l.Key == "cargo.bulk");
    }

    [Fact]
    public void Door_lines_say_no_data_for_the_half_they_lack()
    {
        Assert.Equal("Door 1 left: closed, slide no data",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.Slide("d1l"), "")), LivePram, A321), "door.d1l"));
        Assert.Equal("Door 1 left: no data, slide armed",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.Door("d1l"), "")), LivePram, A321), "door.d1l"));
        Assert.Equal("Door 1 left: no status, check door pressure",
            Line(FenixCabinLines.Build(
                Data((FenixCabinNames.DoorPressure("d1l"), 900), (FenixCabinNames.SlidePressure("d1l"), 3500)), null, A321), "door.d1l"));
    }

    [Fact]
    public void The_cargo_lines_come_from_the_one_cargo_table_the_Doors_button_reads()
    {
        // The Cabin window's Doors button finds a cargo line's door and flags in this same table.
        Assert.Equal(new[] { "cargo.forward", "cargo.aft", "cargo.bulk" }, FenixCabinLines.CargoDoors.Select(c => c.Key));
        Assert.Equal(new[] { FenixCargoDoor.Forward, FenixCargoDoor.Aft, FenixCargoDoor.Bulk }, FenixCabinLines.CargoDoors.Select(c => c.Door));
        Assert.Equal(new[] { FenixCabinNames.CargoForward, FenixCabinNames.CargoAft, FenixCabinNames.CargoBulk },
            FenixCabinLines.CargoDoors.Select(c => c.OpenFlag));
        Assert.Equal(new[] { FenixCabinNames.CargoFwdMoving, FenixCabinNames.CargoAftMoving, null },
            FenixCabinLines.CargoDoors.Select(c => c.MovingFlag));

        var lines = FenixCabinLines.Build(Live(), LivePram, A321);
        Assert.Equal(FenixCabinLines.CargoDoors.Select(c => $"{c.Name}: closed"),
            Section(lines, FenixCabinSection.Doors).Where(l => l.Key.StartsWith("cargo.", StringComparison.Ordinal)).Select(l => l.Text));
    }

    // ---- The part of a line a press changes -----------------------------------------------------

    private static string? State(IReadOnlyList<FenixCabinLine> lines, string key) => Assert.Single(lines, l => l.Key == key).State;

    [Fact]
    public void Door_and_cargo_lines_carry_the_state_a_press_changes()
        => TestCulture.Run("de-DE", () =>
        {
            // A door's own word, whatever its slide and pressure say; a cargo door open or closed, none while
            // it moves or is unknown.
            var data = Data(
                (FenixCabinNames.Door("d1l"), false), (FenixCabinNames.Slide("d1l"), true),
                (FenixCabinNames.Door("d1r"), true), (FenixCabinNames.Slide("d1r"), false),
                (FenixCabinNames.Door("d4l"), ""), (FenixCabinNames.Slide("d4l"), true),
                (FenixCabinNames.DoorPressure("d4r"), 900), (FenixCabinNames.Door("d4r"), false),
                (FenixCabinNames.Slide("owl"), true),
                (FenixCabinNames.CargoForward, true), (FenixCabinNames.CargoFwdMoving, false),
                (FenixCabinNames.CargoAft, false), (FenixCabinNames.CargoAftMoving, true),
                (FenixCabinNames.CargoBulk, false));
            var lines = FenixCabinLines.Build(data, null, A320InPark);

            Assert.Equal("closed", State(lines, "door.d1l"));
            Assert.Equal("open", State(lines, "door.d1r"));
            Assert.Null(State(lines, "door.d4l"));
            Assert.Equal("closed", State(lines, "door.d4r"));
            Assert.Equal("Door 4 right: closed, slide no data, check door pressure", Line(lines, "door.d4r"));
            Assert.Null(State(lines, "slide.owl"));
            Assert.Equal("open", State(lines, "cargo.forward"));
            Assert.Equal("Aft cargo door: moving", Line(lines, "cargo.aft"));
            Assert.Null(State(lines, "cargo.aft"));
            Assert.Equal("closed", State(lines, "cargo.bulk"));

            // Unknown cargo doors have none either.
            var unknown = FenixCabinLines.Build(Data(), null, A320InPark);
            Assert.Null(State(unknown, "cargo.forward"));
            Assert.Null(State(unknown, "cargo.bulk"));
            Assert.Null(State(unknown, "door.d1l"));
            return 0;
        });

    [Fact]
    public void Temperature_lines_carry_the_target_and_offset_and_no_other_line_has_a_state()
        => TestCulture.Run("de-DE", () =>
        {
            var data = Live(
                (FenixCabinNames.TempFwdAir, 24.4), (FenixCabinNames.TempFwdSelected, 24), (FenixCabinNames.TempFwdOffset, 0.5),
                (FenixCabinNames.TempAftAir, 23.96), (FenixCabinNames.TempAftSelected, 22), (FenixCabinNames.TempAftOffset, -1.5));
            var lines = FenixCabinLines.Build(data, LivePram, A321);

            // Invariant, so a comma-decimal PC compares the same state: "24.5|0.5", never "24,5|0,5".
            Assert.Equal("24.5|0.5", State(lines, "temp.forward"));
            Assert.Equal("20.5|-1.5", State(lines, "temp.aft"));
            // The measured air temperature is not part of it.
            var warmer = FenixCabinLines.Build(Live(
                (FenixCabinNames.TempFwdAir, 25.1), (FenixCabinNames.TempFwdSelected, 24), (FenixCabinNames.TempFwdOffset, 0.5)), LivePram, A321);
            Assert.Equal("24.5|0.5", State(warmer, "temp.forward"));
            Assert.Equal("24.0|0.0", State(FenixCabinLines.Build(Live(), LivePram, A321), "temp.aft"));

            // Unknown selected temperature or offset: none.
            Assert.Null(State(FenixCabinLines.Build(Live((FenixCabinNames.TempFwdOffset, "")), LivePram, A321), "temp.forward"));
            Assert.Null(State(FenixCabinLines.Build(Live((FenixCabinNames.TempAftSelected, "")), LivePram, A321), "temp.aft"));

            // Every other line leaves it null.
            Assert.All(lines.Where(l => !l.Key.StartsWith("door.", StringComparison.Ordinal)
                                        && !l.Key.StartsWith("cargo.", StringComparison.Ordinal)
                                        && l.Key is not ("temp.forward" or "temp.aft")),
                l => Assert.Null(l.State));
            return 0;
        });

    [Theory]
    [InlineData(2000.0, 2900.0, "check slide pressure")]
    [InlineData(900.0, 2900.0, "check slide pressure")]
    [InlineData(900.0, 3500.0, "check door pressure")]
    [InlineData(2000.0, 3500.0, null)]
    [InlineData(1000.0, 3000.0, null)]
    [InlineData(null, null, null)]
    [InlineData(900.0, null, "check door pressure")]
    [InlineData(null, 2900.0, "check slide pressure")]
    public void PressureMessage_follows_the_FAP_order(double? door, double? slide, string? message)
    {
        Assert.Equal(message, FenixCabinLines.PressureMessage(door, slide));
    }

    // ---- Cautions -------------------------------------------------------------------------------

    [Fact]
    public void Cautions_follow_the_FAP_rules()
    {
        IReadOnlyList<string> Of(params (string, JToken)[] values) => FenixCabinLines.Cautions(Data(values), A321);

        Assert.Contains("Water service door open", Of((FenixCabinNames.WaterServicePanelOpen, true)));
        Assert.Contains("Vacuum system disabled on ground",
            Of((FenixCabinNames.VacuumSystemPowered, false), (FenixCabinNames.VacuumGeneratorOperational, true)));
        Assert.Contains("Vacuum system disabled on ground",
            Of((FenixCabinNames.VacuumSystemPowered, true), (FenixCabinNames.VacuumGeneratorOperational, false)));
        Assert.Empty(Of((FenixCabinNames.VacuumSystemPowered, true), (FenixCabinNames.VacuumGeneratorOperational, true)));
        Assert.Contains("Vacuum system disabled: ground servicing", Of((FenixCabinNames.WasteServicePanelOpen, true)));
        Assert.Contains("Check lavatory power supply: LAV A", Of((FenixCabinNames.LavatoryFlush("A"), false)));
        Assert.Contains("Check lavatory power supply: LAV M, LAV E",
            Of((FenixCabinNames.LavatoryFlush("M"), false), (FenixCabinNames.LavatoryFlush("E"), false)));
        Assert.Contains("Vacuum system disabled: waste tank full", Of((FenixCabinNames.WasteTankFull, true)));
        Assert.Contains("Waste tank not empty, check level: ground service required", Of((FenixCabinNames.WasteTank, 31)));
        Assert.DoesNotContain("Waste tank not empty, check level: ground service required", Of((FenixCabinNames.WasteTank, 30)));

        // Every one at once, in the FAP's order.
        Assert.Equal(
            new[]
            {
                "Water service door open",
                "Vacuum system disabled on ground",
                "Vacuum system disabled: ground servicing",
                "Check lavatory power supply: LAV A",
                "Vacuum system disabled: waste tank full",
                "Waste tank not empty, check level: ground service required"
            },
            Of(
                (FenixCabinNames.WaterServicePanelOpen, true),
                (FenixCabinNames.VacuumSystemPowered, false), (FenixCabinNames.VacuumGeneratorOperational, true),
                (FenixCabinNames.WasteServicePanelOpen, true),
                (FenixCabinNames.LavatoryFlush("A"), false),
                (FenixCabinNames.WasteTankFull, true),
                (FenixCabinNames.WasteTank, 31)));
    }

    [Fact]
    public void Cautions_fire_on_what_known_values_prove_and_the_layout_lavatories()
    {
        // The FAP fires the vacuum caution on !powered || !generator, so one known false proves it.
        // That false is real data from the Fenix, not the FAP store's starting default
        // (vacuumSystemPowered:!1), which shows the caution before any data arrives.
        const string Vacuum = "Vacuum system disabled on ground";
        Assert.Equal(new[] { Vacuum }, FenixCabinLines.Cautions(Data((FenixCabinNames.VacuumSystemPowered, false)), A321));
        Assert.Equal(new[] { Vacuum }, FenixCabinLines.Cautions(Data((FenixCabinNames.VacuumGeneratorOperational, false)), A321));
        Assert.Equal(new[] { Vacuum }, FenixCabinLines.Cautions(
            Data((FenixCabinNames.VacuumSystemPowered, false), (FenixCabinNames.VacuumGeneratorOperational, "")), A321));

        // An unknown value alone never fires it (FNX-1).
        Assert.Empty(FenixCabinLines.Cautions(new FenixCabinData(), A321));
        Assert.Empty(FenixCabinLines.Cautions(
            Data((FenixCabinNames.VacuumSystemPowered, ""), (FenixCabinNames.VacuumGeneratorOperational, "")), A321));
        Assert.Empty(FenixCabinLines.Cautions(
            Data((FenixCabinNames.VacuumSystemPowered, true), (FenixCabinNames.VacuumGeneratorOperational, "")), A321));
        Assert.Empty(FenixCabinLines.Cautions(
            Data((FenixCabinNames.VacuumSystemPowered, ""), (FenixCabinNames.VacuumGeneratorOperational, true)), A321));

        // The A319 (HighDensity) has lavatories A and D only: a false E is not its caution.
        var a319 = FenixCabinLayout.Resolve(FenixAircraftType.A319, null, null, null);
        Assert.Empty(FenixCabinLines.Cautions(Data((FenixCabinNames.LavatoryFlush("E"), false)), a319));

        // The live A321 has no caution.
        Assert.Empty(FenixCabinLines.Cautions(Live(), A321));
    }

    [Fact]
    public void Cautions_show_in_the_water_tab_and_the_status_summary()
    {
        var data = Live(
            (FenixCabinNames.WaterServicePanelOpen, true),
            (FenixCabinNames.LavatoryFlush("A"), false),
            (FenixCabinNames.SlidePressure("d4r"), 2900));
        var lines = FenixCabinLines.Build(data, LivePram, A321);

        Assert.Equal("Caution: Water service door open", Line(lines, "water.caution.1"));
        Assert.Equal("Caution: Check lavatory power supply: LAV A", Line(lines, "water.caution.2"));
        Assert.DoesNotContain(lines, l => l.Key == "water.caution.3");
        Assert.Equal(
            "Cautions: Water service door open; Check lavatory power supply: LAV A; Door 4 right: check slide pressure",
            Line(lines, "status.cautions"));
        Assert.Equal("Door 4 right: closed, slide armed, check slide pressure", Line(lines, "door.d4r"));
    }

    // ---- Water ----------------------------------------------------------------------------------

    [Fact]
    public void Water_lines_for_the_live_A321()
    {
        var lines = TestCulture.Run("de-DE", () => FenixCabinLines.Build(Live(), LivePram, A321));

        Assert.Equal(
            new[]
            {
                "Water: 85 percent",
                "Water preselect: 100 percent",
                "Waste: 5 percent",
                "Waste tank full: no",
                "Water service panel: no data",
                "Waste service panel: no data",
                "Vacuum system: no data",
                "Lavatory A: flush operational",
                "Lavatory M: flush operational",
                "Lavatory D: flush operational",
                "Lavatory E: flush operational"
            },
            Texts(lines, FenixCabinSection.Water));
        Assert.Equal(
            new[]
            {
                "water.water", "water.preselect", "water.waste", "water.wasteFull", "water.waterPanel", "water.wastePanel",
                "water.vacuum", "water.lav.A", "water.lav.M", "water.lav.D", "water.lav.E"
            },
            Section(lines, FenixCabinSection.Water).Select(l => l.Key));
    }

    [Fact]
    public void Water_lines_read_the_panels_vacuum_and_lavatories()
    {
        var lines = FenixCabinLines.Build(
            Live(
                (FenixCabinNames.WasteTankFull, true),
                (FenixCabinNames.WaterServicePanelOpen, false),
                (FenixCabinNames.WasteServicePanelOpen, true),
                (FenixCabinNames.VacuumSystemPowered, true),
                (FenixCabinNames.VacuumGeneratorOperational, false),
                (FenixCabinNames.LavatoryFlush("D"), false)),
            LivePram, A321);

        Assert.Equal("Waste tank full: yes", Line(lines, "water.wasteFull"));
        Assert.Equal("Water service panel: closed", Line(lines, "water.waterPanel"));
        Assert.Equal("Waste service panel: open", Line(lines, "water.wastePanel"));
        Assert.Equal("Vacuum system: powered, generator not operational", Line(lines, "water.vacuum"));
        Assert.Equal("Lavatory D: flush not operational", Line(lines, "water.lav.D"));

        Assert.Equal("Vacuum system: not powered, generator no data",
            Line(FenixCabinLines.Build(Live((FenixCabinNames.VacuumSystemPowered, false)), LivePram, A321), "water.vacuum"));
    }

    // ---- Unknown values -------------------------------------------------------------------------

    [Fact]
    public void Unknown_values_never_become_states_or_cautions()
    {
        var empty = new FenixCabinData();
        var lines = FenixCabinLines.Build(empty, null, A321);

        Assert.Empty(FenixCabinLines.Cautions(empty, A321));
        Assert.Equal("Cabin ready: no data", Line(lines, "status.cabinReady"));
        Assert.Equal("Door 1 left: no status", Line(lines, "door.d1l"));
        Assert.Equal("Water: no data", Line(lines, "water.water"));
        Assert.Equal("Cautions: no data", Line(lines, "status.cautions"));
        Assert.DoesNotContain(lines, l => l.Key == "temp.available");
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("Temperature control", StringComparison.Ordinal));
        Assert.Equal("Forward zone: no data", Line(lines, "temp.forward"));
        Assert.DoesNotContain(lines, l => l.Key.StartsWith("water.caution.", StringComparison.Ordinal));

        // Every line says it has nothing: no closed, disarmed, off, 0 percent or caution anywhere.
        Assert.All(lines, l => Assert.Matches(new Regex("^[^:]+: (no data|no status|no data from the Fenix)$"), l.Text));
    }

    [Fact]
    public void Unknown_values_inside_known_data_stay_unknown()
    {
        // Known data elsewhere, but every cabin name "" (FNX-1): still no state and no caution, and with
        // no caution input known the cautions summary cannot say "none" either.
        var data = Data((FenixCabinNames.FapOperational, true));
        foreach (string name in FenixCabinNames.All.Where(n => n != FenixCabinNames.FapOperational))
            data.Apply(name, FenixDataValue.FromJson(""));
        var lines = FenixCabinLines.Build(data, null, A321);

        Assert.Empty(FenixCabinLines.Cautions(data, A321));
        Assert.Equal("Cautions: no data", Line(lines, "status.cautions"));
        Assert.Equal("Doors: no data", Line(lines, "status.doors"));
        Assert.Equal("Slides: no data", Line(lines, "status.slides"));
        Assert.Equal("Cargo doors: no data", Line(lines, "status.cargo"));
        Assert.All(lines, l => Assert.Matches(new Regex("^[^:]+: (no data|no status|no data from the Fenix)$"), l.Text));
    }

    // "Cautions: none" needs one known caution input with nothing firing. Each row knows exactly one
    // input (besides FapOperational, which no caution reads) at a value that fires nothing; the value is JSON.
    public static TheoryData<string, string> CautionInputs => new()
    {
        { FenixCabinNames.WaterTank, "85" },
        { FenixCabinNames.WasteTank, "5" },
        { FenixCabinNames.WasteTankFull, "false" },
        { FenixCabinNames.LavatoryFlush("M"), "true" },
        { FenixCabinNames.VacuumSystemPowered, "true" },
        { FenixCabinNames.VacuumGeneratorOperational, "true" },
        { FenixCabinNames.WaterServicePanelOpen, "false" },
        { FenixCabinNames.WasteServicePanelOpen, "false" },
        { FenixCabinNames.DoorPressure("d3r"), "2000" },
        { FenixCabinNames.SlidePressure("d3r"), "3500" }
    };

    [Theory]
    [MemberData(nameof(CautionInputs))]
    public void Cautions_none_needs_a_known_caution_input(string name, string json)
    {
        var data = Data((FenixCabinNames.FapOperational, true), (name, JToken.Parse(json)));
        Assert.Equal("Cautions: none", Line(FenixCabinLines.Build(data, null, A321), "status.cautions"));
    }

    [Fact]
    public void Cautions_summary_counts_only_inputs_of_the_layout_and_of_their_kind()
    {
        string Summary(FenixCabinLayout layout, params (string, JToken)[] values)
            => Line(FenixCabinLines.Build(Data(values), null, layout), "status.cautions");

        var a319 = FenixCabinLayout.Resolve(FenixAircraftType.A319, null, null, null);

        // Values that are known but feed no caution of this airframe.
        Assert.Equal("Cautions: no data", Summary(A321, (FenixCabinNames.CabinReady, false)));
        Assert.Equal("Cautions: no data", Summary(a319, (FenixCabinNames.LavatoryFlush("E"), true)));
        Assert.Equal("Cautions: no data", Summary(A320InPark, (FenixCabinNames.DoorPressure("d2l"), 2000)));
        Assert.Equal("Cautions: no data", Summary(A320InPark, (FenixCabinNames.SlidePressure("owl"), 3500)));

        // A caution input of the wrong kind is not known.
        Assert.Equal("Cautions: no data", Summary(A321, (FenixCabinNames.WaterTank, "full")));
        Assert.Equal("Cautions: no data", Summary(A321, (FenixCabinNames.WasteTankFull, 0)));

        // A firing caution is listed as before.
        Assert.Equal("Cautions: Vacuum system disabled on ground", Summary(A321, (FenixCabinNames.VacuumSystemPowered, false)));
        Assert.Equal("Cautions: Door 1 left: check door pressure", Summary(A321, (FenixCabinNames.DoorPressure("d1l"), 900)));
    }

    // ---- Connection status ----------------------------------------------------------------------

    [Fact]
    public void Connection_status_follows_the_FAP_screens()
    {
        Assert.Equal("The Fenix is not answering. Load the Fenix and wait for it to start.",
            FenixCabinLines.ConnectionStatus(false, new FenixCabinData()));
        Assert.Equal("The Fenix is not answering. Load the Fenix and wait for it to start.",
            FenixCabinLines.ConnectionStatus(false, Live()));

        Assert.Equal("The flight attendant panel is off.",
            FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.FapOperational, false))));
        Assert.Equal("Flight attendant panel starting: 40 percent.",
            FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.FapReady, false), (FenixCabinNames.FapPowerUp, 40))));
        Assert.Equal("Cabin system starting: 60 percent.",
            FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.CidsReady, false), (FenixCabinNames.CidsPowerUp, 60))));
        Assert.Equal("Cabin system ready.", FenixCabinLines.ConnectionStatus(true, Live()));
    }

    [Fact]
    public void Connection_status_never_claims_ready_without_the_data()
    {
        // The FAP shows its power-up screen in this order: off, FAP power-up, CIDS power-up.
        Assert.Equal("The flight attendant panel is off.",
            FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.FapOperational, false), (FenixCabinNames.FapReady, false))));
        Assert.Equal("Flight attendant panel starting: 40 percent.",
            FenixCabinLines.ConnectionStatus(true, Live(
                (FenixCabinNames.FapReady, false), (FenixCabinNames.FapPowerUp, 40), (FenixCabinNames.CidsReady, false))));

        // A progress the Fenix does not give is left out, never read as 0 percent.
        Assert.Equal("Flight attendant panel starting.",
            FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.FapReady, false), (FenixCabinNames.FapPowerUp, ""))));
        Assert.Equal("Cabin system starting.",
            FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.CidsReady, false), (FenixCabinNames.CidsPowerUp, ""))));

        // Answering, but a ready flag unknown: not "ready".
        Assert.Equal("Cabin system: no data.", FenixCabinLines.ConnectionStatus(true, Live((FenixCabinNames.CidsReady, ""))));
        Assert.Equal("Cabin system: no data.", FenixCabinLines.ConnectionStatus(true, new FenixCabinData()));
    }
}
