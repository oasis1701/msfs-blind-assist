// Characterization tests for FenixCabinNames and FenixCabinLayout: the Fenix data-service names behind
// the Cabin window, and which doors, slides and lavatories the loaded airframe has. The expected values
// are the FAP bundle's own (the data-name table, mainDoors$, slides$, getLavatoryDesignations) and the
// EFB's per-type ground-service door lists.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinLayoutTests
{
    [Theory]
    [InlineData(FenixCabinNames.CabinReady, "aircraft.communication.cids.cabinReady")]
    [InlineData(FenixCabinNames.CabinReadyAvailable, "aircraft.communication.cids.cabinReady.available")]
    [InlineData(FenixCabinNames.CabinLights, "aircraft.communication.cids.cabinLights")]
    [InlineData(FenixCabinNames.CidsOperational, "aircraft.communication.cids.operational")]
    [InlineData(FenixCabinNames.CidsReady, "aircraft.communication.cids.ready")]
    [InlineData(FenixCabinNames.CidsPowerUp, "aircraft.communication.cids.powerUp")]
    [InlineData(FenixCabinNames.FapOperational, "aircraft.communication.cids.fap.operational")]
    [InlineData(FenixCabinNames.FapReady, "aircraft.communication.cids.fap.ready")]
    [InlineData(FenixCabinNames.FapPowerUp, "aircraft.communication.cids.fap.powerUp")]
    [InlineData(FenixCabinNames.FapSpeakerVolume, "aircraft.communication.cids.fap.speakerVolume")]
    [InlineData(FenixCabinNames.TempFwdAir, "aircraft.cabin.fwd.temp.air")]
    [InlineData(FenixCabinNames.TempFwdSelected, "aircraft.cabin.fwd.temp.selected")]
    [InlineData(FenixCabinNames.TempFwdOffset, "aircraft.cabin.fwd.temp.offset")]
    [InlineData(FenixCabinNames.TempAftAir, "aircraft.cabin.aft.temp.air")]
    [InlineData(FenixCabinNames.TempAftSelected, "aircraft.cabin.aft.temp.selected")]
    [InlineData(FenixCabinNames.TempAftOffset, "aircraft.cabin.aft.temp.offset")]
    [InlineData(FenixCabinNames.ZoneControllerOperational, "aircraft.acAndPress.zoneController.primaryOperational")]
    [InlineData(FenixCabinNames.LavGalleyFanRunning, "aircraft.airConditioning.lavGalleyExtractionFan.running")]
    [InlineData(FenixCabinNames.WaterTank, "aircraft.communication.cids.waterTank")]
    [InlineData(FenixCabinNames.WaterPreselect, "aircraft.communication.cids.waterTank.preselect")]
    [InlineData(FenixCabinNames.WasteTank, "aircraft.communication.cids.wasteTank")]
    [InlineData(FenixCabinNames.WasteTankFull, "aircraft.communication.cids.wasteTank.full")]
    [InlineData(FenixCabinNames.VacuumGeneratorOperational, "aircraft.communication.cids.vacuumGenerator.operational")]
    [InlineData(FenixCabinNames.VacuumSystemPowered, "aircraft.waste.vacuumSystemPowered")]
    [InlineData(FenixCabinNames.WaterServicePanelOpen, "aircraft.water.waterServicePanel.open")]
    [InlineData(FenixCabinNames.WasteServicePanelOpen, "aircraft.waste.wasteServicePanel.open")]
    [InlineData(FenixCabinNames.CargoForward, "doors.cargo.forward")]
    [InlineData(FenixCabinNames.CargoAft, "doors.cargo.aft")]
    [InlineData(FenixCabinNames.CargoBulk, "doors.cargo.bulk")]
    [InlineData(FenixCabinNames.CargoFwdMoving, "fenix.doors.cargo.fwd.movement")]
    [InlineData(FenixCabinNames.CargoAftMoving, "fenix.doors.cargo.aft.movement")]
    [InlineData(FenixCabinNames.AltitudeAboveGround, "aircraft.altitude.aboveGround")]
    public void Fixed_names_are_the_ones_the_FAP_and_EFB_use(string actual, string expected)
        => Assert.Equal(expected, actual);

    [Fact]
    public void Composed_names_follow_the_FAP_templates()
    {
        Assert.Equal("aircraft.communication.cids.door.d1l", FenixCabinNames.Door("d1l"));
        Assert.Equal("aircraft.communication.cids.doorPressure.d4r", FenixCabinNames.DoorPressure("d4r"));
        Assert.Equal("aircraft.communication.cids.slide.owl", FenixCabinNames.Slide("owl"));
        Assert.Equal("aircraft.communication.cids.slidePressure.d2r", FenixCabinNames.SlidePressure("d2r"));
        Assert.Equal("doors.control.d3l", FenixCabinNames.DoorControl("d3l"));
        Assert.Equal("aircraft.communication.cids.lav.m.flush.operational", FenixCabinNames.LavatoryFlush("M"));
    }

    [Fact]
    public void The_key_lists_are_the_FAP_designations_in_the_FAP_order()
    {
        Assert.Equal(new[] { "d1l", "d1r", "d2l", "d2r", "d3l", "d3r", "d4l", "d4r" }, FenixCabinNames.DoorKeys);
        Assert.Equal(new[] { "d1l", "d1r", "d2l", "d2r", "owl", "owr", "d3l", "d3r", "d4l", "d4r" }, FenixCabinNames.SlideKeys);
        Assert.Equal(new[] { "A", "D", "E", "M" }, FenixCabinNames.LavatoryLetters);
    }

    [Fact]
    public void All_holds_every_name_once()
    {
        Assert.Equal(80, FenixCabinNames.All.Count);
        Assert.Equal(80, FenixCabinNames.All.Distinct().Count());
        Assert.Contains(FenixCabinNames.CabinReady, FenixCabinNames.All);
        Assert.Contains(FenixCabinNames.CargoBulk, FenixCabinNames.All);
        Assert.Contains(FenixCabinNames.FapSpeakerVolume, FenixCabinNames.All);
        Assert.Contains(FenixCabinNames.LavatoryFlush("E"), FenixCabinNames.All);
        Assert.Contains(FenixCabinNames.DoorControl("d4r"), FenixCabinNames.All);
        // The height the EFB guards its door and cargo toggles on.
        Assert.Contains(FenixCabinNames.AltitudeAboveGround, FenixCabinNames.All);
    }

    [Fact]
    public void All_holds_the_composed_names_of_every_key()
    {
        foreach (string key in FenixCabinNames.DoorKeys)
        {
            Assert.Contains(FenixCabinNames.Door(key), FenixCabinNames.All);
            Assert.Contains(FenixCabinNames.DoorPressure(key), FenixCabinNames.All);
            Assert.Contains(FenixCabinNames.DoorControl(key), FenixCabinNames.All);
        }
        foreach (string key in FenixCabinNames.SlideKeys)
        {
            Assert.Contains(FenixCabinNames.Slide(key), FenixCabinNames.All);
            Assert.Contains(FenixCabinNames.SlidePressure(key), FenixCabinNames.All);
        }
        foreach (string letter in FenixCabinNames.LavatoryLetters)
            Assert.Contains(FenixCabinNames.LavatoryFlush(letter), FenixCabinNames.All);
    }

    [Fact]
    public void A321_double_fullsize()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A321, "DoubleFullsize", "TwoClass", "SlideDisarmed");

        Assert.Equal(FenixAircraftType.A321, layout.Type);
        Assert.Equal(FenixExitsType.DoubleFullsize, layout.Exits);
        Assert.Equal(new[] { "1L", "1R", "2L", "2R", "3L", "3R", "4L", "4R" }, layout.MainDoors);
        Assert.Equal(new[] { "1L", "1R", "2L", "2R", "3L", "3R", "4L", "4R" }, layout.Slides);
        Assert.Equal(new[] { "A", "M", "D", "E" }, layout.Lavatories);
        Assert.Equal(new[] { "d1l", "d1r", "d2l", "d2r", "d3l", "d3r", "d4l", "d4r" }, layout.GroundDoors);
        Assert.True(layout.HasBulkCargo);
        Assert.False(layout.SlideInPark);
    }

    [Fact]
    public void A320_double_overwing()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A320, "DoubleOverwing", "SingleClass", "SlideInPark");

        Assert.Equal(FenixExitsType.DoubleOverwing, layout.Exits);
        Assert.Equal(new[] { "1L", "1R", "4L", "4R" }, layout.MainDoors);
        Assert.Equal(new[] { "1L", "1R", "OWL", "OWR", "4L", "4R" }, layout.Slides);
        Assert.Equal(new[] { "A", "D", "E" }, layout.Lavatories);
        Assert.Equal(new[] { "d1l", "d1r", "d4l", "d4r" }, layout.GroundDoors);
        Assert.True(layout.HasBulkCargo);
        Assert.True(layout.SlideInPark);
    }

    [Fact]
    public void A319_falls_back_by_type()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A319, null, null, null);

        Assert.Equal(FenixExitsType.SingleOverwing, layout.Exits);
        Assert.Equal(new[] { "1L", "1R", "4L", "4R" }, layout.MainDoors);
        Assert.Equal(new[] { "1L", "1R", "OWL", "OWR", "4L", "4R" }, layout.Slides);
        Assert.Equal(new[] { "A", "D" }, layout.Lavatories);
        Assert.Equal(new[] { "d1l", "d1r", "d4l", "d4r" }, layout.GroundDoors);
        Assert.False(layout.HasBulkCargo);
        Assert.False(layout.SlideInPark);
    }

    [Fact]
    public void A321_falls_back_by_type()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A321, null, null, null);

        Assert.Equal(FenixExitsType.DoubleFullsize, layout.Exits);
        Assert.Equal(new[] { "A", "M", "D", "E" }, layout.Lavatories);
        Assert.Equal(new[] { "d1l", "d1r", "d2l", "d2r", "d3l", "d3r", "d4l", "d4r" }, layout.GroundDoors);
        Assert.True(layout.HasBulkCargo);
    }

    [Fact]
    public void Settings_win_over_type()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A321, "SingleOverwingSingleFullsize", "TwoClass", null);

        Assert.Equal(FenixExitsType.SingleOverwingSingleFullsize, layout.Exits);
        Assert.Equal(new[] { "1L", "1R", "3L", "3R", "4L", "4R" }, layout.MainDoors);
        Assert.Equal(new[] { "1L", "1R", "OWL", "OWR", "3L", "3R", "4L", "4R" }, layout.Slides);
        Assert.Equal(new[] { "d1l", "d1r", "d3l", "d3r", "d4l", "d4r" }, layout.GroundDoors);
    }

    [Theory]
    [InlineData(FenixAircraftType.A319, "SingleClass", "A,D,E")]
    [InlineData(FenixAircraftType.A320, "HighDensity", "A,D")]
    [InlineData(FenixAircraftType.A321, "SingleClass", "A,D,E")]
    [InlineData(FenixAircraftType.A319, "TwoClass", "A,M,D,E")]
    public void The_aircraft_variant_setting_picks_the_lavatories(FenixAircraftType type, string variant, string expected)
        => Assert.Equal(expected, string.Join(",", FenixCabinLayout.Resolve(type, null, variant, null).Lavatories));

    [Theory]
    [InlineData("SingleOverwing", "1L,1R,4L,4R", "1L,1R,OWL,OWR,4L,4R")]
    [InlineData("DoubleOverwing", "1L,1R,4L,4R", "1L,1R,OWL,OWR,4L,4R")]
    [InlineData("DoubleFullsize", "1L,1R,2L,2R,3L,3R,4L,4R", "1L,1R,2L,2R,3L,3R,4L,4R")]
    [InlineData("SingleOverwingSingleFullsize", "1L,1R,3L,3R,4L,4R", "1L,1R,OWL,OWR,3L,3R,4L,4R")]
    [InlineData("DoubleOverwingSingleFullsize", "1L,1R,3L,3R,4L,4R", "1L,1R,OWL,OWR,3L,3R,4L,4R")]
    public void Doors_and_slides_follow_the_exits_type(string exits, string mainDoors, string slides)
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A320, exits, null, null);

        Assert.Equal(Enum.Parse<FenixExitsType>(exits), layout.Exits);
        Assert.Equal(mainDoors, string.Join(",", layout.MainDoors));
        Assert.Equal(slides, string.Join(",", layout.Slides));
    }

    [Fact]
    public void Unknown_type_and_unknown_settings_use_the_A320_layout()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.Unknown, "Bogus", "", null);

        Assert.Equal(FenixAircraftType.Unknown, layout.Type);
        Assert.Equal(FenixExitsType.DoubleOverwing, layout.Exits);
        Assert.Equal(new[] { "A", "D", "E" }, layout.Lavatories);
        Assert.Equal(new[] { "d1l", "d1r", "d4l", "d4r" }, layout.GroundDoors);
        Assert.True(layout.HasBulkCargo);
    }

    [Fact]
    public void The_A318_has_the_A320_layout()
    {
        var layout = FenixCabinLayout.Resolve(FenixAircraftType.A318, null, null, null);

        Assert.Equal(FenixExitsType.DoubleOverwing, layout.Exits);
        Assert.Equal(new[] { "A", "D", "E" }, layout.Lavatories);
        Assert.True(layout.HasBulkCargo);
    }

    // The setting must be the enum name spelled exactly: no case folding, no trimming, no numbers.
    [Theory]
    [InlineData("doubleoverwing")]
    [InlineData("DOUBLEFULLSIZE")]
    [InlineData(" DoubleFullsize")]
    [InlineData("1")]
    [InlineData("DoubleFullsize,SingleOverwing")]
    public void An_exits_setting_that_is_not_spelled_exactly_falls_back_to_the_type(string setting)
    {
        Assert.Equal(FenixExitsType.SingleOverwing, FenixCabinLayout.Resolve(FenixAircraftType.A319, setting, null, null).Exits);
        Assert.Equal(FenixExitsType.DoubleOverwing, FenixCabinLayout.Resolve(FenixAircraftType.A320, setting, null, null).Exits);
    }

    [Theory]
    [InlineData("SlideInPark", true)]
    [InlineData("SlideDisarmed", false)]
    [InlineData("slideinpark", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Slide_in_park_is_the_exact_SlideInPark_message(string? message, bool expected)
        => Assert.Equal(expected, FenixCabinLayout.Resolve(FenixAircraftType.A320, null, null, message).SlideInPark);

    [Theory]
    [InlineData("1L", "d1l")]
    [InlineData("4R", "d4r")]
    [InlineData("OWL", "owl")]
    [InlineData("OWR", "owr")]
    [InlineData("2R", "d2r")]
    public void DataKey_maps_designations(string designation, string expected)
        => Assert.Equal(expected, FenixCabinLayout.DataKey(designation));

    [Fact]
    public void Every_resolved_designation_has_a_data_key_the_names_know()
    {
        foreach (FenixAircraftType type in Enum.GetValues<FenixAircraftType>())
        {
            foreach (FenixExitsType exits in Enum.GetValues<FenixExitsType>())
            {
                var layout = FenixCabinLayout.Resolve(type, exits.ToString(), null, null);
                foreach (string door in layout.MainDoors)
                    Assert.Contains(FenixCabinLayout.DataKey(door), FenixCabinNames.DoorKeys);
                foreach (string slide in layout.Slides)
                    Assert.Contains(FenixCabinLayout.DataKey(slide), FenixCabinNames.SlideKeys);
                foreach (string key in layout.GroundDoors)
                    Assert.Contains(key, FenixCabinNames.DoorKeys);
            }
        }
    }
}
