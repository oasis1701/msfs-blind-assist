// Characterization tests for MSFSBlindAssist.Services.Gsx.GsxOffset and
// GsxAircraftIdMap (the id/offset-derivation logic; there is no separate
// GsxAircraftIdMap.cs file -- both live under Services/Gsx).
//
// Ports the pure, no-installed-GSX-profile-needed golden cases from
// tools/GsxOffsetProbe/Program.cs (sections "Universal aircraft-id derivation" and the
// wingspan->ARC boundaries). The probe's profile-parsing sections (EDDF/SKBO/ELLX .py
// evaluation) require real installed GSX .py files on disk and are out of scope for a
// portable unit test -- this file covers only GsxOffset.Zero and the ICAO/wingspan
// derivation, which are fully self-contained.
//
// This is characterization, not spec verification: values are taken from the probe /
// derived by reasoning about the source and confirmed by running the tests; if a
// literal ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GsxOffsetTests
{
    // --- GsxOffset.Zero ------------------------------------------------------

    [Fact]
    public void Zero_is_a_strict_no_op_offset()
    {
        var zero = GsxOffset.Zero;

        Assert.Equal(0.0, zero.LongitudinalMetres);
        Assert.Equal(0.0, zero.LateralMetres);
        Assert.Equal(new GsxOffset(0.0, 0.0), zero);
    }

    // --- Universal aircraft-id derivation (never-seen designators still work) --

    [Fact]
    public void B77W_derives_777_300_even_though_it_is_not_in_the_exception_table()
    {
        GsxAircraftIdMap.TryResolve("B77W", 0, out var id);

        Assert.Equal(777, id.IdMajor);
        Assert.Equal(300, id.IdMinor);
    }

    [Fact]
    public void A359_resolves_via_the_exception_table_to_idMinor_1000()
    {
        GsxAircraftIdMap.TryResolve("A359", 64.75, out var id);

        Assert.Equal(350, id.IdMajor);
        Assert.Equal(1000, id.IdMinor);
    }

    [Fact]
    public void Invented_B79X_derives_idMajor_797_from_the_B7xx_family_pattern()
    {
        bool derived = GsxAircraftIdMap.TryDeriveFromIcao("B79X", out int idMajor, out _);

        Assert.True(derived);
        Assert.Equal(797, idMajor);
    }

    [Fact]
    public void B38M_737_MAX_resolves_via_the_exception_table_not_idMajor_zero()
    {
        // The B3xM MAX designators break the B7xx family pattern; the deriver alone
        // would give idMajor 0, so this MUST come from the exception table.
        GsxAircraftIdMap.TryResolve("B38M", 35.9, out var id);

        Assert.Equal(737, id.IdMajor);
        Assert.Equal(800, id.IdMinor);
    }

    [Fact]
    public void B39M_737_MAX_resolves_to_737_900()
    {
        GsxAircraftIdMap.TryResolve("B39M", 35.9, out var id);

        Assert.Equal(737, id.IdMajor);
        Assert.Equal(900, id.IdMinor);
    }

    [Fact]
    public void A332_derives_330_200_from_the_Airbus_widebody_pattern()
    {
        bool derived = GsxAircraftIdMap.TryDeriveFromIcao("A332", out int idMajor, out int idMinor);

        Assert.True(derived);
        Assert.Equal(330, idMajor);
        Assert.Equal(200, idMinor);
    }

    [Fact]
    public void A320_derives_the_literal_3_digit_idMajor()
    {
        bool derived = GsxAircraftIdMap.TryDeriveFromIcao("A320", out int idMajor, out _);

        Assert.True(derived);
        Assert.Equal(320, idMajor);
    }

    [Fact]
    public void E190_derives_the_literal_3_digit_idMajor()
    {
        bool derived = GsxAircraftIdMap.TryDeriveFromIcao("E190", out int idMajor, out _);

        Assert.True(derived);
        Assert.Equal(190, idMajor);
    }

    [Fact]
    public void An_unrecognized_pattern_fails_to_derive_but_still_returns_a_usable_id()
    {
        bool derived = GsxAircraftIdMap.TryDeriveFromIcao("BCS1", out int idMajor, out int idMinor);

        Assert.False(derived);
        Assert.Equal(0, idMajor);
        Assert.Equal(0, idMinor);
    }

    [Fact]
    public void TryResolve_never_throws_and_preserves_the_raw_ICAO_on_an_unresolvable_designator()
    {
        bool resolved = GsxAircraftIdMap.TryResolve("ZZZZ", 0, out var id);

        Assert.False(resolved);
        Assert.Equal("ZZZZ", id.Icao);
        Assert.Equal(0, id.IdMajor);
    }

    // --- Wingspan -> ARC code boundaries -------------------------------------

    [Theory]
    [InlineData(34.0, "ARC-C")]
    [InlineData(64.8, "ARC-E")]
    [InlineData(79.75, "ARC-F")]
    [InlineData(80.5, "ARC-F")] // >=80 also -> F
    public void ArcFromWingspanMetres_maps_documented_boundaries(double wingspan, string expected)
    {
        Assert.Equal(expected, GsxAircraftIdMap.ArcFromWingspanMetres(wingspan));
    }

    [Fact]
    public void ArcFromWingspanMetres_is_empty_for_a_non_positive_wingspan()
    {
        Assert.Equal(string.Empty, GsxAircraftIdMap.ArcFromWingspanMetres(0));
        Assert.Equal(string.Empty, GsxAircraftIdMap.ArcFromWingspanMetres(-5));
    }

    // --- Wingspan -> broad category -------------------------------------------

    [Theory]
    [InlineData(20.0, "Light")]
    [InlineData(30.0, "Medium")]
    [InlineData(50.0, "Heavy")]
    [InlineData(70.0, "Super")]
    public void CategoryFromWingspanMetres_maps_documented_bands(double wingspan, string expected)
    {
        Assert.Equal(expected, GsxAircraftIdMap.CategoryFromWingspanMetres(wingspan));
    }

    [Fact]
    public void CategoryFromWingspanMetres_is_empty_for_a_non_positive_wingspan()
    {
        Assert.Equal(string.Empty, GsxAircraftIdMap.CategoryFromWingspanMetres(0));
        Assert.Equal(string.Empty, GsxAircraftIdMap.CategoryFromWingspanMetres(-1));
    }

    // --- Resolving with vs without a wingspan --------------------------------

    [Fact]
    public void TryResolve_with_a_wingspan_populates_ArcCode_and_Group()
    {
        GsxAircraftIdMap.TryResolve("B77W", 64.8, out var id);

        Assert.Equal("ARC-E", id.ArcCode);
        Assert.NotEqual(string.Empty, id.Group);
    }

    [Fact]
    public void TryResolve_without_a_wingspan_leaves_ArcCode_empty()
    {
        GsxAircraftIdMap.TryResolve("B77W", 0, out var id);

        Assert.Equal(string.Empty, id.ArcCode);
    }
}
