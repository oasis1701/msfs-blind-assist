using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins GSX's REAL billing and receipt shapes.
///
/// The previous receipt test asserted against a hand-written literal that
/// invented a <c>total</c> and a <c>lines</c> array. GSX publishes neither: the
/// live <c>/receipt</c> frame carries exactly <c>canPrint</c>, <c>html</c>,
/// <c>logo</c>, <c>operator</c>, <c>printPreview</c> and <c>printer</c>. The
/// invented members parsed to 0.00 and empty against the real wire, so every
/// invoice announced "Total 0.00" over a genuine 1761.42 charge — with a green
/// test. Nothing in this file may assert a member the wire does not carry.
/// </summary>
public class GsxBillingTests
{
    // The exact key set of a live /receipt frame. Values are placeholders (the
    // raw capture is not committed — html and logo are large blobs carrying
    // user and financial data), but the KEYS are the whole point of the test:
    // no total, no lines.
    private const string LiveShapedReceipt = """
        {"canPrint":true,"html":"<html>…</html>","logo":"data:image/png;base64,AAAA",
         "operator":"OneJet","printPreview":false,"printer":"Default"}
        """;

    private static JsonElement Fixture(string name)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Parses_the_live_billing_timers()
    {
        var b = GsxBilling.Parse(Fixture("gsx-billing.json"));
        Assert.NotEmpty(b.Timers);
        var jetway = b.Timers.First();
        Assert.Equal("Jetway", jetway.SubService);
        Assert.False(string.IsNullOrEmpty(jetway.Friendly));
        Assert.True(jetway.Running);
        Assert.True(b.AnyRunning);
    }

    [Fact]
    public void Parses_the_live_billing_builders_where_the_money_actually_is()
    {
        var b = GsxBilling.Parse(Fixture("gsx-billing.json"));

        var builder = Assert.Single(b.Builders);
        Assert.Equal("Ground Handling", builder.Friendly);
        Assert.Equal(1761.42, builder.Subtotal);

        var line = Assert.Single(builder.Lines);
        Assert.Equal("Passenger deboarding", line.Description);
        Assert.Equal(1761.42, line.Amount);

        Assert.True(b.HasBuilders);
        Assert.Equal(1761.42, b.BuildersTotal);
    }

    [Fact]
    public void Builders_total_sums_every_section()
    {
        var b = GsxBilling.Parse(Json("""
            {"builders":[{"friendly":"Ground Handling","subtotal":1761.42,"lines":[]},
                         {"friendly":"Fuel","subtotal":238.58,"lines":[]}]}
            """));
        Assert.Equal(2000.00, b.BuildersTotal, 2);
    }

    [Fact]
    public void Timers_and_builders_are_parsed_independently()
    {
        // A frame carrying only one half must not discard the other.
        var timersOnly = GsxBilling.Parse(Json("""{"timers":[{"subService":"GPU","running":true}]}"""));
        Assert.Single(timersOnly.Timers);
        Assert.Empty(timersOnly.Builders);

        var buildersOnly = GsxBilling.Parse(Json("""{"builders":[{"friendly":"Ground Handling","subtotal":10.0}]}"""));
        Assert.Empty(buildersOnly.Timers);
        Assert.Single(buildersOnly.Builders);
        Assert.Equal(10.0, buildersOnly.BuildersTotal);
    }

    [Fact]
    public void No_timers_means_nothing_running()
    {
        var b = GsxBilling.Parse(Json("""{"timers":[]}"""));
        Assert.Empty(b.Timers);
        Assert.False(b.AnyRunning);
    }

    [Fact]
    public void Malformed_billing_does_not_throw()
    {
        Assert.Empty(GsxBilling.Parse(Json("{}")).Timers);
        Assert.Empty(GsxBilling.Parse(Json("{}")).Builders);
        Assert.Empty(GsxBilling.Parse(Json("[]")).Timers);
        Assert.Empty(GsxBilling.Parse(Json("""{"builders":"nonsense"}""")).Builders);
        Assert.Empty(GsxBilling.Parse(Json("""{"builders":[1,2,3]}""")).Builders);
        Assert.False(GsxBilling.Parse(Json("""{"builders":[{}]}""")).BuildersTotal > 0);
    }

    [Fact]
    public void Null_receipt_parses_to_null()
    {
        Assert.Null(GsxReceipt.Parse(Json("null")));
        Assert.Null(GsxReceipt.Parse(default));
    }

    [Fact]
    public void Receipt_reads_only_the_members_gsx_actually_sends()
    {
        var r = GsxReceipt.Parse(Json(LiveShapedReceipt));
        Assert.NotNull(r);
        Assert.Equal("OneJet", r!.Operator);
        Assert.True(r.CanPrint);

        // The blobs must stay unread — they carry no screen-reader value and
        // GsxReceipt deliberately has no surface for them.
        Assert.Null(typeof(GsxReceipt).GetProperty("Html"));
        Assert.Null(typeof(GsxReceipt).GetProperty("Logo"));

        // And the two members that were invented must stay gone, so nothing can
        // quietly start speaking a figure GSX never published.
        Assert.Null(typeof(GsxReceipt).GetProperty("Total"));
        Assert.Null(typeof(GsxReceipt).GetProperty("Lines"));
    }

    [Fact]
    public void Receipt_missing_operator_degrades_to_empty_not_throw()
    {
        var r = GsxReceipt.Parse(Json("""{"canPrint":false}"""));
        Assert.NotNull(r);
        Assert.Equal("", r!.Operator);
        Assert.False(r.CanPrint);
    }

    // ── The spoken invoice line ──────────────────────────────────────────────

    [Fact]
    public void Invoice_announcement_speaks_the_total_from_billing_not_the_receipt()
    {
        var receipt = GsxReceipt.Parse(Json(LiveShapedReceipt))!;
        var billing = GsxBilling.Parse(Fixture("gsx-billing.json"));

        Assert.Equal("Invoice available from OneJet. Total 1761.42.",
                     GsxService.FormatReceiptAnnouncement(receipt, billing));
    }

    [Fact]
    public void Invoice_announcement_states_no_figure_when_billing_has_none()
    {
        var receipt = GsxReceipt.Parse(Json(LiveShapedReceipt))!;

        // The old code printed "Total 0.00" here — an authoritative-sounding
        // wrong number is worse than no number at all.
        string said = GsxService.FormatReceiptAnnouncement(receipt, GsxBilling.Empty);
        Assert.Equal("Invoice available from OneJet.", said);
        Assert.DoesNotContain("Total", said, StringComparison.Ordinal);
        Assert.DoesNotContain("0.00", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoice_announcement_without_an_operator_still_reads()
    {
        var receipt = GsxReceipt.Parse(Json("""{"canPrint":true}"""))!;
        Assert.Equal("Invoice available.", GsxService.FormatReceiptAnnouncement(receipt, GsxBilling.Empty));
        Assert.Equal("Invoice available. Total 10.00.",
                     GsxService.FormatReceiptAnnouncement(
                         receipt, GsxBilling.Parse(Json("""{"builders":[{"friendly":"X","subtotal":10.0}]}"""))));
    }

    [Theory]
    [InlineData("de-DE")]   // comma decimal separator, dot thousands
    [InlineData("fr-FR")]   // comma decimal separator, narrow-nbsp thousands
    [InlineData("en-US")]   // the CI default, kept so the invariant case is still covered
    public void Invoice_total_is_invariant_formatted(string cultureName)
    {
        // A comma decimal separator (de-DE, fr-FR, …) would make "1761,42" read as a
        // completely different number through a screen reader.
        //
        // The culture MUST be swapped for the assertion to mean anything. This test ran under
        // the ambient culture — en-US on windows-latest — where 1234.5.ToString("0.00") renders
        // identically with or without CultureInfo.InvariantCulture, so it passed whether or not
        // the production call site had it. Dropping the InvariantCulture from
        // GsxService.FormatReceiptAnnouncement left all five invoice tests green while a de-DE
        // pilot heard "Total 1761,42" — literally the failure this test names.
        // Same idiom as SayIntentionsCultureTests (the tr-TR suite).
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            var receipt = GsxReceipt.Parse(Json(LiveShapedReceipt))!;
            var billing = GsxBilling.Parse(Json("""{"builders":[{"friendly":"X","subtotal":1234.5}]}"""));
            string spoken = GsxService.FormatReceiptAnnouncement(receipt, billing);

            Assert.Contains("1234.50", spoken, StringComparison.Ordinal);
            // Pin the failure directly, not just the success: a comma separator must never
            // reach the phrase, whatever the ambient culture formats numbers like.
            Assert.DoesNotContain("1234,50", spoken, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
