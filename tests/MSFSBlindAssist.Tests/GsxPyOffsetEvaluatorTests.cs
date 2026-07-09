// Characterization tests for MSFSBlindAssist.Services.Gsx.GsxPyOffsetEvaluator.
//
// Cases derived by reading Evaluate (the four dispatch idioms: ByIdMajor,
// HandleAircraftOffsets, IcaoAircraftOffsets, ByGroup) and the GsxOffset.Zero
// strict-no-op degradation on any resolver miss, then confirmed by running the tests
// against synthetic .py fixture text built with the real GsxPyProfileReader parser
// (syntax patterns mirrored from tools/GsxOffsetProbe/Program.cs's live-profile
// golden cases). This is characterization, not spec verification: if a literal ever
// disagrees with actual output, the test must be corrected to match real output, not
// the other way around.

using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GsxPyOffsetEvaluatorTests
{
    private static GsxAircraftId Ac(string icao, int idMajor, int idMinor, string group = "", string arc = "")
        => new(icao, idMajor, idMinor, group, arc);

    // --- Null / miss degradation to GsxOffset.Zero ------------------------------

    [Fact]
    public void Null_profile_degrades_to_Zero()
    {
        var off = GsxPyOffsetEvaluator.Evaluate(null!, 66, "", Ac("B77W", 777, 300));
        Assert.Equal(GsxOffset.Zero, off);
    }

    // Gate-map entries must each sit on their OWN line ("66 : (Cat, func, ),") -- the
    // GsxPyProfileReader.GateEntryRegex is anchored per-line and does not match a
    // "parkings = { 66 : (...), }" one-liner (mirrors real GSX .py formatting).
    private const string GateOnlyPy =
        "parkings = {\n" +
        "66 : (Cat, offset66, ),\n" +
        "}\n" +
        "def offset66(ad):\n" +
        "    return Distance()\n";

    [Fact]
    public void Null_aircraft_id_degrades_to_Zero()
    {
        var profile = GsxPyProfileReader.FromText(GateOnlyPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 66, "", null!);
        Assert.Equal(GsxOffset.Zero, off);
    }

    [Fact]
    public void Unmapped_gate_number_degrades_to_Zero()
    {
        var profile = GsxPyProfileReader.FromText(GateOnlyPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 999, "", Ac("B77W", 777, 300));
        Assert.Equal(GsxOffset.Zero, off);
    }

    [Fact]
    public void Null_function_degrades_to_Zero()
    {
        var off = GsxPyOffsetEvaluator.Evaluate((GsxPyProfileReader.OffsetFunction)null!, Ac("B77W", 777, 300));
        Assert.Equal(GsxOffset.Zero, off);
    }

    // --- ByIdMajor idiom ---------------------------------------------------------

    private const string ByIdMajorPy =
        "parkings = {\n" +
        "10 : (Cat, offsetIdMajor, ),\n" +
        "}\n" +
        "def offsetIdMajor(ad):\n" +
        "    table = {737: 1.5, 777: (2.5, -1.0)}\n" +
        "    return Distance.fromMeters(table.get(aircraftData.idMajor, 0))\n";

    [Fact]
    public void ByIdMajor_scalar_table_value_maps_to_longitudinal_only()
    {
        var profile = GsxPyProfileReader.FromText(ByIdMajorPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 10, "", Ac("B738", 737, 800));

        Assert.Equal(1.5, off.LongitudinalMetres);
        Assert.Equal(0.0, off.LateralMetres);
    }

    [Fact]
    public void ByIdMajor_tuple_table_value_maps_longitudinal_and_lateral()
    {
        var profile = GsxPyProfileReader.FromText(ByIdMajorPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 10, "", Ac("B77W", 777, 300));

        Assert.Equal(2.5, off.LongitudinalMetres);
        Assert.Equal(-1.0, off.LateralMetres);
    }

    [Fact]
    public void ByIdMajor_no_table_entry_falls_to_Zero()
    {
        var profile = GsxPyProfileReader.FromText(ByIdMajorPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 10, "", Ac("A320", 320, 0));

        Assert.Equal(GsxOffset.Zero, off);
    }

    // --- HandleAircraftOffsets idiom ---------------------------------------------

    private const string HandlePy =
        "parkings = {\n" +
        "20 : (Cat, offsetHandle, ),\n" +
        "}\n" +
        "def offsetHandle(ad):\n" +
        "    table737 = {800: 1.5, 900: (2.0, -0.5)}\n" +
        "    table = {320: 3.0}\n" +
        "    return Distance.fromMeters(HandleAircraftOffsets(ad, {737: (table737, 0)}, table))\n";

    [Fact]
    public void HandleAircraftOffsets_hits_the_specific_idMinor_subtable_entry()
    {
        var profile = GsxPyProfileReader.FromText(HandlePy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 20, "", Ac("B738", 737, 800));

        Assert.Equal(1.5, off.LongitudinalMetres);
    }

    [Fact]
    public void HandleAircraftOffsets_tuple_subtable_entry_carries_lateral()
    {
        var profile = GsxPyProfileReader.FromText(HandlePy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 20, "", Ac("B739", 737, 900));

        Assert.Equal(2.0, off.LongitudinalMetres);
        Assert.Equal(-0.5, off.LateralMetres);
    }

    [Fact]
    public void HandleAircraftOffsets_idMajor_present_but_idMinor_and_fallback_both_miss_degrades_to_Zero()
    {
        // idMajor 737 IS in specificTables, but idMinor 999 misses AND the fallback key (0)
        // isn't in table737 either -> Python's None -> Distance.fromMeters(None) -> 0 (base).
        var profile = GsxPyProfileReader.FromText(HandlePy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 20, "", Ac("B737", 737, 999));

        Assert.Equal(GsxOffset.Zero, off);
    }

    [Fact]
    public void HandleAircraftOffsets_idMajor_not_in_specificTables_falls_to_the_generic_table()
    {
        var profile = GsxPyProfileReader.FromText(HandlePy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 20, "", Ac("A320", 320, 0));

        Assert.Equal(3.0, off.LongitudinalMetres);
    }

    // --- IcaoAircraftOffsets idiom ------------------------------------------------

    private const string IcaoPy =
        "parkings = {\n" +
        "29 : (Cat, offsetIcao, ),\n" +
        "}\n" +
        "def offsetIcao(ad):\n" +
        "    TableIcao = {\"B77W\": 5.0}\n" +
        "    aircraftValues = {777: 2.0}\n" +
        "    TableGroup = {\"ARC-E\": 1.0}\n" +
        "    return Distance.fromMeters(TableIcao.get(aircraftData.icaoTypeDesignator, aircraftValues.get(aircraftData.idMajor, TableGroup.get(aircraftData.aircraftGroup, 0))))\n";

    [Fact]
    public void IcaoAircraftOffsets_ICAO_table_hit_takes_priority()
    {
        var profile = GsxPyProfileReader.FromText(IcaoPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 29, "", Ac("B77W", 999, 999, "Heavy", "ARC-E"));

        Assert.Equal(5.0, off.LongitudinalMetres);
    }

    [Fact]
    public void IcaoAircraftOffsets_falls_to_idMajor_table_when_ICAO_misses()
    {
        var profile = GsxPyProfileReader.FromText(IcaoPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 29, "", Ac("ZZZZ", 777, 300));

        Assert.Equal(2.0, off.LongitudinalMetres);
    }

    [Fact]
    public void IcaoAircraftOffsets_falls_to_group_table_when_ICAO_and_idMajor_both_miss()
    {
        var profile = GsxPyProfileReader.FromText(IcaoPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 29, "", Ac("ZZZZ", 999, 0, "Heavy", "ARC-E"));

        Assert.Equal(1.0, off.LongitudinalMetres);
    }

    [Fact]
    public void IcaoAircraftOffsets_all_three_miss_degrades_to_Zero()
    {
        var profile = GsxPyProfileReader.FromText(IcaoPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 29, "", Ac("ZZZZ", 999, 0, "Medium", "ARC-C"));

        Assert.Equal(GsxOffset.Zero, off);
    }

    // --- ByGroup idiom -------------------------------------------------------------

    private const string GroupPy =
        "parkings = {\n" +
        "40 : (Cat, offsetGroup, ),\n" +
        "}\n" +
        "def offsetGroup(ad):\n" +
        "    TableGroup = {\"ARC-E\": 6.1, \"Heavy\": 2.0}\n" +
        "    return Distance.fromMeters(TableGroup.get(aircraftData.aircraftGroup, 0))\n";

    [Fact]
    public void ByGroup_matches_the_ARC_code_as_written()
    {
        var profile = GsxPyProfileReader.FromText(GroupPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 40, "", Ac("B77W", 777, 300, "Heavy", "ARC-E"));

        Assert.Equal(6.1, off.LongitudinalMetres);
    }

    [Fact]
    public void ByGroup_falls_back_to_the_broad_category_bucket_when_ARC_misses()
    {
        var profile = GsxPyProfileReader.FromText(GroupPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 40, "", Ac("XXXX", 0, 0, "Heavy", "ARC-X"));

        Assert.Equal(2.0, off.LongitudinalMetres);
    }

    [Fact]
    public void ByGroup_both_ARC_and_category_miss_degrades_to_Zero()
    {
        var profile = GsxPyProfileReader.FromText(GroupPy);
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 40, "", Ac("XXXX", 0, 0, "Medium", "ARC-C"));

        Assert.Equal(GsxOffset.Zero, off);
    }

    // --- Zero / unclassified idiom --------------------------------------------------

    [Fact]
    public void Distance_with_no_args_classifies_Zero_regardless_of_aircraft()
    {
        var profile = GsxPyProfileReader.FromText(
            "parkings = {\n50 : (Cat, offsetZero, ),\n}\ndef offsetZero(ad):\n    return Distance()\n");
        var off = GsxPyOffsetEvaluator.Evaluate(profile, 50, "", Ac("B77W", 777, 300));

        Assert.Equal(GsxOffset.Zero, off);
    }
}
