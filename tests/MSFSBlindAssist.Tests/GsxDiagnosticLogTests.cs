using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The gsx.log line formatters, and above all the PRIVACY TIERING they enforce: a phrase
/// MSFSBlindAssist composed from typed fields may be written down verbatim, a phrase GSX
/// authored may not. These are the tests that make the tiering a guarantee rather than an
/// intention — a regression here writes a pilot's data to a file built to be emailed to a
/// developer, and nothing else in the suite would notice.
///
/// <para>
/// A CAUTION THIS FILE PAID FOR: substring assertions are necessary but NOT sufficient here.
/// The first version of these tests asserted that an invoice total did not appear literally
/// AND that <c>chars=</c> and <c>hash=</c> did — which passed while the redaction was fully
/// reversible, because an unsalted 4-byte digest over a fixed public template with only the
/// money varying inverts to a single preimage in under a second. The test had pinned the two
/// fields that made it reversible. Redaction is now enforced by construction (a per-process
/// salt that is never written anywhere, and no length field), not by the greps below.
/// </para>
/// </summary>
public class GsxDiagnosticLogTests
{
    // ── The tier table ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(GsxSpeechSource.Service)]
    [InlineData(GsxSpeechSource.GateSelect)]
    public void MSFSBA_composed_sources_are_verbatim_safe(GsxSpeechSource source)
        => Assert.True(GsxDiagnosticLog.IsVerbatimSafe(source));

    [Theory]
    [InlineData(GsxSpeechSource.Message)]      // GSX free text, unbounded by construction
    [InlineData(GsxSpeechSource.Menu)]         // GSX previews invoices/receipts/profile paths here
    [InlineData(GsxSpeechSource.Receipt)]      // carries the invoice total
    [InlineData(GsxSpeechSource.BillingTimer)] // carries an accrued amount
    public void GSX_authored_and_money_sources_are_never_verbatim(GsxSpeechSource source)
        => Assert.False(GsxDiagnosticLog.IsVerbatimSafe(source));

    [Fact]
    public void A_service_phrase_is_written_out_in_full()
    {
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.Service, "fuel 2221 kg loaded, aircraft 5252 kg.", SpeechRoute.Background);

        Assert.Contains("src=service", line, StringComparison.Ordinal);
        Assert.Contains("phrase=\"fuel 2221 kg loaded, aircraft 5252 kg.\"", line, StringComparison.Ordinal);
        Assert.Contains("route=background", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invoice_total_never_reaches_the_line()
    {
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.Receipt, "Invoice available from OneJet. Total 1761.42.", SpeechRoute.Background);

        Assert.DoesNotContain("1761.42", line, StringComparison.Ordinal);
        Assert.DoesNotContain("OneJet", line, StringComparison.Ordinal);
        Assert.Contains("phrase=(none)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_billing_timer_amount_never_reaches_the_line()
    {
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.BillingTimer,
            "Jetway operations still running, 1 hour 6 minutes, amount 116.97.", SpeechRoute.Background);

        Assert.DoesNotContain("116.97", line, StringComparison.Ordinal);
        Assert.Contains("phrase=(none)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void GSX_authored_message_text_never_reaches_the_line()
    {
        // Modelled on GSX's real SimBrief mismatch prose, which names the pilot's
        // flight-plan endpoints — the concrete reason the message slot is metadata-only.
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.Message,
            "The loaded flight plan from EDDF doesn't match the one on SimBrief, from EGLL to KJFK",
            SpeechRoute.Background);

        Assert.DoesNotContain("EDDF", line, StringComparison.Ordinal);
        Assert.DoesNotContain("SimBrief", line, StringComparison.Ordinal);
        Assert.Contains("phrase=(none)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_redacted_line_carries_no_length_field()
    {
        // The length is what collapses a brute-force preimage search from "a template" to
        // "one candidate", so it is not written at all. See the class remark: an earlier
        // version of this test asserted chars= was PRESENT.
        foreach (var source in new[] { GsxSpeechSource.Receipt, GsxSpeechSource.BillingTimer,
                                       GsxSpeechSource.Message, GsxSpeechSource.Menu })
        {
            string line = GsxDiagnosticLog.FormatSpoke(source, "Invoice available from OneJet. Total 1761.42.",
                                                       SpeechRoute.Background);
            Assert.DoesNotContain("chars=", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_redacted_fingerprint_is_salted_so_a_known_phrase_cannot_be_confirmed()
    {
        // The whole attack in one assertion: an attacker holding the log knows the template
        // and needs only to confirm a guess. With an unsalted SHA-256 prefix they could —
        // this pins that the digest is NOT the bare hash of the text.
        const string phrase = "Invoice available from OneJet. Total 1761.42.";
        byte[] unsalted = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(phrase));
        string bare = Convert.ToHexString(unsalted, 0, 4);

        Assert.NotEqual(bare, GsxDiagnosticLog.ShortHash(phrase));
    }

    [Fact]
    public void The_fingerprint_still_tells_a_repeat_from_a_change_within_the_session()
    {
        // Redaction must not cost the channel its purpose: an identical fingerprint on
        // consecutive lines IS repeat-spam, a changing one IS countdown churn — the two
        // shapes this log exists to tell apart, neither of which needs the words.
        const string text = "Boarding: 41 of 186 passengers";
        string a = GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Message, text, SpeechRoute.Background);
        string b = GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Message, text, SpeechRoute.Background);
        string c = GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Message, "Boarding: 42 of 186 passengers",
                                                SpeechRoute.Background);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ShortHash_is_stable_within_the_process_and_separates_different_text()
    {
        Assert.Equal(GsxDiagnosticLog.ShortHash("in position"), GsxDiagnosticLog.ShortHash("in position"));
        Assert.NotEqual(GsxDiagnosticLog.ShortHash("in position"), GsxDiagnosticLog.ShortHash("leaving"));
        Assert.Equal(GsxDiagnosticLog.None, GsxDiagnosticLog.ShortHash(""));
    }

    [Fact]
    public void A_runaway_verbatim_phrase_is_truncated()
    {
        // Guards the two GSX-prose branches that ride in the Service tier by design
        // (StatePhrase's stateText fall-through and BusPhrase's busPhase).
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.Service, new string('x', GsxDiagnosticLog.MaxVerbatimChars + 250),
            SpeechRoute.Background);

        Assert.Contains("…", line, StringComparison.Ordinal);
        Assert.True(line.Length < GsxDiagnosticLog.MaxVerbatimChars + 120,
                    $"a truncated line should stay bounded, was {line.Length} chars");
    }

    // ── The route: the log must never claim something was HEARD ──────────────────────────

    [Fact]
    public void A_phrase_with_no_listener_is_recorded_as_heard_by_nobody()
    {
        // Background monitoring is OFF by default and Access GSX is built lazily, so a
        // default-configured pilot has neither route and the whole GSX stream is discarded
        // by configuration. Logging that as "spoke" would send an investigator hunting a
        // speech-engine fault that does not exist.
        string line = GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Service, "Refuel complete.", SpeechRoute.None);

        Assert.Contains("route=none", line, StringComparison.Ordinal);
        Assert.Contains("nobody heard", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_windowed_route_does_not_claim_delivery()
    {
        // A subscriber exists, but a HIDDEN Access GSX window drops the phrase, and this
        // layer cannot see the form's visibility — so it must not assert one way or another.
        string line = GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Service, "Refuel complete.", SpeechRoute.Window);

        Assert.Contains("route=window", line, StringComparison.Ordinal);
        Assert.Contains("only while Access GSX is open", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_event_token_is_publish_never_spoke()
        => Assert.StartsWith("ev=publish ",
                             GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Service, "x", SpeechRoute.None),
                             StringComparison.Ordinal);

    // ── Line shape (the conventions gsx-gate-select.log established) ─────────────────────

    [Fact]
    public void Multi_line_text_is_flattened_so_one_event_stays_one_line()
    {
        // GSX's statusText is natively multi-line; a wrapped entry breaks the scan pattern.
        string messy = "bus in position" + "\n" + "pax 181/186" + "\r\n" + "bags 100%";
        string line = GsxDiagnosticLog.FormatSpoke(GsxSpeechSource.Service, messy, SpeechRoute.Background);

        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", line, StringComparison.Ordinal);
        Assert.Contains("bus in position pax 181/186 bags 100%", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_value_renders_as_the_none_placeholder_never_an_empty_field()
    {
        string line = GsxDiagnosticLog.FormatVerb("settings.set", "error", null, null, 41);

        Assert.Contains("code=(none)", line, StringComparison.Ordinal);
        Assert.Contains("message=(none)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_verb_line_carries_the_result_and_never_the_args()
    {
        string line = GsxDiagnosticLog.FormatVerb("gate.select", "error", "not_found", "no such gate", 63);

        Assert.StartsWith("ev=verb ", line, StringComparison.Ordinal);
        Assert.Contains("verb=gate.select", line, StringComparison.Ordinal);
        Assert.Contains("result=error", line, StringComparison.Ordinal);
        Assert.Contains("code=not_found", line, StringComparison.Ordinal);
        Assert.Contains("elapsedMs=63", line, StringComparison.Ordinal);
        Assert.DoesNotContain("args", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_quotes_cannot_break_the_key_value_shape()
    {
        string line = GsxDiagnosticLog.FormatVerb("menu.pick", "error", "bad_args", "unknown \"key\"", 5);

        Assert.Equal(2, line.Count(c => c == '"'));
    }

    [Fact]
    public void The_session_header_names_the_airport_and_capabilities()
    {
        string line = GsxDiagnosticLog.FormatSession("ENGM", new[] { "menu", "gate", "settings" }, 12);

        Assert.Contains("ev=session", line, StringComparison.Ordinal);
        Assert.Contains("icao=ENGM", line, StringComparison.Ordinal);
        Assert.Contains("capabilities=[menu,gate,settings]", line, StringComparison.Ordinal);
        Assert.Contains("services=12", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_header_survives_an_airport_gsx_has_not_published_yet()
    {
        string line = GsxDiagnosticLog.FormatSession(null, Array.Empty<string>(), 0);

        Assert.Contains("icao=(none)", line, StringComparison.Ordinal);
        Assert.Contains("capabilities=(none)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_line_starts_with_its_ev_token_so_one_search_partitions_the_file()
    {
        Assert.StartsWith("ev=hushed ", GsxDiagnosticLog.FormatHushed(GsxSpeechSource.Service, "x", "baseline", "why"),
                          StringComparison.Ordinal);
        Assert.StartsWith("ev=verb ", GsxDiagnosticLog.FormatVerb("menu.get", "ok", null, null, 1),
                          StringComparison.Ordinal);
        Assert.StartsWith("ev=session ", GsxDiagnosticLog.FormatSession("ENGM", null, 0),
                          StringComparison.Ordinal);
    }
}
