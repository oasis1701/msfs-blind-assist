// Characterization tests for MSFSBlindAssist.Services.TaxiAugment.AptDatParser.
//
// No dedicated probe exists; row shapes are documented in the source's own header
// comment (verified against an OMDB fixture 2026-06-22) and confirmed here by running
// the tests. This is characterization, not spec verification: if a literal ever
// disagrees with actual output, the test must be corrected to match real output, not
// the other way around.

using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class AptDatParserTests
{
    [Fact]
    public void Parse_emits_a_taxiway_edge_from_a_1201_node_pair_and_a_1202_row()
    {
        string text =
            "1201 25.2500 55.3600 both 100 node_a\n" +
            "1201 25.2510 55.3610 both 101 node_b\n" +
            "1202 100 101 twoway taxiway_D A1\n";

        var data = AptDatParser.Parse(text);

        Assert.Equal("aptdat", data.Source);
        var seg = Assert.Single(data.Taxiways);
        Assert.Equal("A1", seg.Name);
        Assert.Equal(25.2500, seg.Lat1);
        Assert.Equal(55.3600, seg.Lon1);
        Assert.Equal(25.2510, seg.Lat2);
        Assert.Equal(55.3610, seg.Lon2);
    }

    [Fact]
    public void Parse_joins_a_multi_word_taxiway_name()
    {
        string text =
            "1201 1.0 2.0 both 1 a\n" +
            "1201 1.1 2.1 both 2 b\n" +
            "1202 1 2 twoway taxiway_E Inner Perimeter Road\n";

        var data = AptDatParser.Parse(text);

        Assert.Equal("Inner Perimeter Road", Assert.Single(data.Taxiways).Name);
    }

    [Fact]
    public void A_1202_row_referencing_an_unknown_node_id_is_skipped()
    {
        string text =
            "1201 1.0 2.0 both 1 a\n" +
            "1202 1 999 twoway taxiway_D A1\n"; // node 999 was never defined by a 1201 row

        var data = AptDatParser.Parse(text);

        Assert.Empty(data.Taxiways);
    }

    [Fact]
    public void A_1202_runway_type_row_is_not_emitted_as_a_taxiway()
    {
        string text =
            "1201 1.0 2.0 both 1 a\n" +
            "1201 1.1 2.1 both 2 b\n" +
            "1202 1 2 twoway runway 09/27\n";

        var data = AptDatParser.Parse(text);

        Assert.Empty(data.Taxiways);
    }

    [Fact]
    public void A_1202_row_with_no_name_is_skipped()
    {
        string text =
            "1201 1.0 2.0 both 1 a\n" +
            "1201 1.1 2.1 both 2 b\n" +
            "1202 1 2 twoway taxiway_D\n"; // no trailing name field at all
        var data = AptDatParser.Parse(text);

        Assert.Empty(data.Taxiways);
    }

    [Fact]
    public void A_malformed_1201_row_missing_fields_is_tolerated_not_thrown()
    {
        string text =
            "1201 1.0 2.0\n" + // missing usage/node id
            "1201 1.1 2.1 both 2 b\n" +
            "1202 1 2 twoway taxiway_D A1\n"; // references the never-registered node 1

        var data = AptDatParser.Parse(text); // must not throw

        Assert.Empty(data.Taxiways); // node 1 never registered -> edge skipped
    }

    [Fact]
    public void A_non_numeric_1201_coordinate_is_tolerated_not_thrown()
    {
        string text =
            "1201 NOTANUMBER 2.0 both 1 a\n" +
            "1201 1.1 2.1 both 2 b\n" +
            "1202 1 2 twoway taxiway_D A1\n";

        var data = AptDatParser.Parse(text);

        Assert.Empty(data.Taxiways);
    }

    [Fact]
    public void Parse_emits_a_parking_spot_from_a_1300_row()
    {
        string text = "1300 25.25 55.36 090.0 gate 000 Gate A1\n";

        var data = AptDatParser.Parse(text);

        var spot = Assert.Single(data.Parking);
        Assert.Equal("Gate A1", spot.Name);
        Assert.Equal(25.25, spot.Lat);
        Assert.Equal(55.36, spot.Lon);
    }

    [Fact]
    public void A_1300_row_with_no_name_is_skipped()
    {
        string text = "1300 25.25 55.36 090.0 gate 000\n"; // nothing after "airlines"

        var data = AptDatParser.Parse(text);

        Assert.Empty(data.Parking);
    }

    [Fact]
    public void Blank_and_unrelated_lines_are_ignored()
    {
        string text =
            "I\n1000 version\n\n   \nSOME COMMENT LINE\n" +
            "1201 1.0 2.0 both 1 a\n" +
            "1201 1.1 2.1 both 2 b\n" +
            "1202 1 2 twoway taxiway_D A1\n";

        var data = AptDatParser.Parse(text);

        Assert.Single(data.Taxiways);
    }

    [Fact]
    public void Parse_tolerates_CRLF_line_endings()
    {
        string text =
            "1201 1.0 2.0 both 1 a\r\n" +
            "1201 1.1 2.1 both 2 b\r\n" +
            "1202 1 2 twoway taxiway_D A1\r\n";

        var data = AptDatParser.Parse(text);

        Assert.Single(data.Taxiways);
    }
}
