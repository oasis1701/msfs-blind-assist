using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

public class AircraftSizeClassTests
{
    [Fact]
    public void A388_is_code_F_with_its_wingspan_and_a_heavy_touchdown_speed()
    {
        var p = AircraftSizeClass.Resolve("A388", "Airbus A380-800", 500);

        Assert.Equal(IcaoCodeLetter.F, p.CodeLetter);
        Assert.Equal(79.75, p.WingspanMetres!.Value, 2);
        Assert.Equal(140.0, p.TouchdownSpeedKts);
        Assert.False(p.IsFreighter);
        Assert.Equal("Airbus A380-800", p.DisplayName);
        Assert.Equal(25.0, AircraftSizeClass.MinTaxiwayWidthMetres(p.CodeLetter));
    }

    [Fact]
    public void B738_is_code_C()
    {
        var p = AircraftSizeClass.Resolve("b738", "Boeing 737-800", 189);
        Assert.Equal(IcaoCodeLetter.C, p.CodeLetter);
        Assert.Equal("B738", p.TypeCode);
        Assert.Equal(130.0, p.TouchdownSpeedKts);
        Assert.Equal(15.0, AircraftSizeClass.MinTaxiwayWidthMetres(IcaoCodeLetter.C));
    }

    [Fact]
    public void A_Cessna_152_is_code_A_with_a_GA_touchdown_speed()
    {
        // The review's KSEA sweep had a C152 briefed as "size class unknown ... aircraft type not recognised".
        var p = AircraftSizeClass.Resolve("C152", "Cessna 152", 1);
        Assert.Equal(IcaoCodeLetter.A, p.CodeLetter);
        Assert.Equal(10.2, p.WingspanMetres!.Value, 1);
        Assert.Equal(70.0, p.TouchdownSpeedKts);
    }

    [Theory]
    [InlineData("C182", 11.0)]
    [InlineData("PA28", 10.7)]
    [InlineData("SR22", 11.7)]
    [InlineData("DA40", 11.9)]
    [InlineData("DA42", 13.4)]
    [InlineData("DA62", 14.6)]
    [InlineData("BE58", 11.5)]
    public void Common_GA_types_are_code_A(string code, double wingspanMetres)
    {
        var p = AircraftSizeClass.Resolve(code, "", null);
        Assert.Equal(wingspanMetres, p.WingspanMetres!.Value, 1);
        Assert.Equal(IcaoCodeLetter.A, p.CodeLetter);
    }

    [Theory]
    [InlineData("MD1F", "MD-11F", 0)]
    [InlineData("B77L", "Boeing 777F", 0)]
    [InlineData("B748", "Boeing 747-8F", null)]
    [InlineData("MD11", "McDonnell Douglas MD-11F", null)]
    [InlineData("B738", "Boeing 737-800BCF", null)]
    [InlineData("B752", "Boeing 757-200PF", null)]
    [InlineData("A332", "Airbus A330-200F", null)]
    [InlineData("B763", "Boeing 767-300 Freighter", null)]
    [InlineData("A320", "Airbus A320", 0)]
    public void Freighters_are_recognised(string code, string name, int? maxPax)
        => Assert.True(AircraftSizeClass.LooksLikeFreighter(code, name, maxPax));

    [Theory]
    [InlineData("B77L", "Boeing 777-200LR", 301)]
    [InlineData("A320", "Airbus A320-200", 180)]
    [InlineData("F100", "Fokker 100", 100)]
    [InlineData("B738", "Boeing 737-800", null)]
    public void Passenger_aircraft_are_not_freighters(string code, string name, int? maxPax)
        => Assert.False(AircraftSizeClass.LooksLikeFreighter(code, name, maxPax));

    [Fact]
    public void Unknown_type_has_no_wingspan_and_the_unknown_letter()
    {
        var p = AircraftSizeClass.Resolve("ZZZZ", "", null);
        Assert.Null(p.WingspanMetres);
        Assert.Equal(IcaoCodeLetter.Unknown, p.CodeLetter);
        Assert.Equal(130.0, p.TouchdownSpeedKts);
        Assert.Equal(0.0, AircraftSizeClass.MinTaxiwayWidthMetres(IcaoCodeLetter.Unknown));
        Assert.Equal("ZZZZ", p.DisplayName);
    }

    [Fact]
    public void Blank_type_and_name_still_yield_a_profile()
    {
        var p = AircraftSizeClass.Resolve(null, null, null);
        Assert.Equal("", p.TypeCode);
        Assert.Equal("unknown aircraft", p.DisplayName);
        Assert.Equal(IcaoCodeLetter.Unknown, p.CodeLetter);
    }

    [Theory]
    [InlineData(14.99, IcaoCodeLetter.A)]
    [InlineData(15.0, IcaoCodeLetter.B)]
    [InlineData(35.99, IcaoCodeLetter.C)]
    [InlineData(36.0, IcaoCodeLetter.D)]
    [InlineData(51.99, IcaoCodeLetter.D)]
    [InlineData(52.0, IcaoCodeLetter.E)]
    [InlineData(64.99, IcaoCodeLetter.E)]
    [InlineData(65.0, IcaoCodeLetter.F)]
    [InlineData(88.4, IcaoCodeLetter.F)]
    public void Letter_boundaries_follow_annex_14(double metres, IcaoCodeLetter expected)
        => Assert.Equal(expected, AircraftSizeClass.LetterForWingspan(metres));

    [Theory]
    [InlineData(IcaoCodeLetter.A, 70.0)]
    [InlineData(IcaoCodeLetter.B, 115.0)]
    [InlineData(IcaoCodeLetter.C, 130.0)]
    [InlineData(IcaoCodeLetter.D, 135.0)]
    [InlineData(IcaoCodeLetter.E, 140.0)]
    [InlineData(IcaoCodeLetter.F, 140.0)]
    public void Touchdown_speed_per_letter(IcaoCodeLetter letter, double kts)
        => Assert.Equal(kts, AircraftSizeClass.TouchdownSpeedKts(letter));

    [Theory]
    [InlineData("P28A", 10.67)] [InlineData("P28B", 10.67)] [InlineData("P28R", 10.8)] [InlineData("P28T", 10.8)]
    [InlineData("BE36", 10.21)] [InlineData("C150", 10.16)] [InlineData("C700", 21.0)] [InlineData("DHC2", 14.63)]
    [InlineData("DHC6", 19.81)] [InlineData("E135", 20.04)] [InlineData("E145", 20.04)] [InlineData("PA24", 10.97)]
    [InlineData("PA31", 12.4)] [InlineData("PA34", 11.85)] [InlineData("PA44", 11.77)] [InlineData("P46T", 13.11)]
    [InlineData("R182", 10.92)] [InlineData("S22T", 11.68)] [InlineData("SF50", 11.79)] [InlineData("HDJT", 12.12)]
    [InlineData("PC6T", 15.87)] [InlineData("DV20", 10.87)]
    public void Common_general_aviation_and_business_types_are_known(string code, double spanMetres)
    {
        var p = AircraftSizeClass.Resolve(code, "", null);
        Assert.Equal(spanMetres, p.WingspanMetres);
        Assert.NotEqual(IcaoCodeLetter.Unknown, p.CodeLetter);
    }

    [Fact]
    public void The_code_letter_uses_the_same_wingspan_lines_as_GSX_s_ARC()
    {
        // GsxAircraftIdMap (not GsxAircraftId, the record) is the static class that carries ArcFromWingspanMetres.
        for (double m = 1.0; m <= 90.0; m += 0.25)
            Assert.Equal(MSFSBlindAssist.Services.Gsx.GsxAircraftIdMap.ArcFromWingspanMetres(m), "ARC-" + AircraftSizeClass.LetterForWingspan(m));
    }
}
