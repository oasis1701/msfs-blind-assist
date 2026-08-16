using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The gsx.log line formatters.
///
/// <para>
/// GSX announcements are logged IN FULL, invoice totals and ground-connection amounts
/// included. That is a deliberate reversal: an earlier version of this file redacted them
/// behind a salted hash on privacy grounds, which was a category error — GSX is a
/// flight-simulator add-on whose invoices, currency figures and handling companies are all
/// simulated, so the redaction protected nothing while making an invoice bug unreadable in
/// the one log built to explain it. The tests below now PIN the figures as present, so a
/// future well-meaning "fix" cannot quietly re-hide them.
/// </para>
/// </summary>
public class GsxDiagnosticLogTests
{
    // -- Announcement text is logged in full ---------------------------------------------

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
    public void An_invoice_total_IS_logged()
    {
        // Simulated money owed to a fictional ground crew. Withholding it is what made an
        // invoice bug undiagnosable; "Total 1761.42" is the whole point of the line.
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.Receipt, "Invoice available from OneJet. Total 1761.42.", SpeechRoute.Background);

        Assert.Contains("1761.42", line, StringComparison.Ordinal);
        Assert.Contains("OneJet", line, StringComparison.Ordinal);
        Assert.Contains("src=receipt", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ground_connection_amount_IS_logged()
    {
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.BillingTimer,
            "Jetway operations still running, 1 hour 6 minutes, amount 116.97.", SpeechRoute.Background);

        Assert.Contains("116.97", line, StringComparison.Ordinal);
        Assert.Contains("1 hour 6 minutes", line, StringComparison.Ordinal);
    }

    [Fact]
    public void GSX_message_and_menu_prose_IS_logged()
    {
        foreach (var source in new[] { GsxSpeechSource.Message, GsxSpeechSource.Menu })
        {
            string line = GsxDiagnosticLog.FormatSpoke(
                source, "Follow the marshaller to stand B25", SpeechRoute.Background);

            Assert.Contains("Follow the marshaller to stand B25", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_source_is_redacted()
    {
        // The tiering is gone; every source renders its text. Pinning this as a THEORY over
        // the whole enum means adding a source cannot silently reintroduce a redacted tier.
        foreach (GsxSpeechSource source in Enum.GetValues<GsxSpeechSource>())
        {
            string line = GsxDiagnosticLog.FormatSpoke(source, "Refuel complete.", SpeechRoute.Background);

            Assert.Contains("phrase=\"Refuel complete.\"", line, StringComparison.Ordinal);
            Assert.DoesNotContain("hash=", line, StringComparison.Ordinal);
            Assert.DoesNotContain("chars=", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_runaway_phrase_is_truncated_for_line_sanity_only()
    {
        string line = GsxDiagnosticLog.FormatSpoke(
            GsxSpeechSource.Service, new string('x', GsxDiagnosticLog.MaxPhraseChars + 250),
            SpeechRoute.Background);

        Assert.Contains("…", line, StringComparison.Ordinal);
        Assert.True(line.Length < GsxDiagnosticLog.MaxPhraseChars + 120,
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
