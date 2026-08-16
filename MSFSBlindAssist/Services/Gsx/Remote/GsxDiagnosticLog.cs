using System.Text;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Which composer produced a spoken GSX phrase — a DIAGNOSTIC tag, recorded as <c>src=</c>.
/// It answers "which branch answered?", the same convention the SayIntentions import log
/// follows: when a phrase turns out to be wrong or missing, knowing whether it came from the
/// typed service announcer, GSX's message slot or the invoice path is most of the diagnosis.
///
/// <para>
/// It is NOT a privacy classification, and an earlier version of this file was wrong to make
/// it one. GSX is a flight-simulator ground-services add-on: its invoices, currency amounts,
/// handling companies and service prose are all SIMULATED — "Invoice available from OneJet.
/// Total 1761.42." is play money owed to a fictional ground crew. Redacting it cost real
/// diagnostic value (an invoice bug became unreadable in the one log built to explain it) and
/// protected nothing. Every phrase is now logged in full.
/// </para>
///
/// <para>
/// What still never reaches a log is unchanged and short, and none of it passes through here:
/// raw frames (the <c>handlerData</c> blob is ~1.7 MB — a size problem before anything else),
/// and command <c>args</c>, whose only genuinely real-world member is <c>simbrief_username</c>,
/// the pilot's third-party account alias. Neither was ever needed to diagnose an announcement.
/// </para>
/// </summary>
public enum GsxSpeechSource
{
    /// <summary>
    /// A phrase the typed service announcer composed — "pax 113 of 143.", "bags 40 percent.",
    /// "fuel 2221 kg loaded, aircraft 5252 kg.", "Deboard in progress by OneJet.",
    /// "Refuel complete." Also its two GSX-prose arms: <c>StatePhrase</c>'s default
    /// (<c>stateText</c>) and <c>BusPhrase</c> (<c>detail.busPhase</c>).
    /// </summary>
    Service,

    /// <summary>
    /// <c>GsxGateSelectAnnouncer</c> output — stand names and fixed sentences. The
    /// per-attempt detail lives in gsx-gate-select.log; this records what was SAID.
    /// </summary>
    GateSelect,

    /// <summary>GSX's "message" slot — the follow-me, marshaller and positioning banners.</summary>
    Message,

    /// <summary>Menu text.</summary>
    Menu,

    /// <summary>The invoice announcement, including GSX's simulated total.</summary>
    Receipt,

    /// <summary>A metered ground-connection timer phrase, including its simulated amount.</summary>
    BillingTimer,
}

/// <summary>
/// Where a published GSX phrase actually went. The log must never assert that something was
/// SPOKEN, because publication and audibility are different events and the gap between them
/// is one of the likeliest causes of the very report this channel exists to answer.
///
/// <para>
/// <c>GsxService.Announce</c> publishes on two routes: a direct queued announcement, taken
/// only when the pilot has opted into background monitoring, and an <c>AnnouncementReady</c>
/// event that <c>AccessGSXForm</c> speaks — but only while that window is actually VISIBLE
/// (its handler early-returns otherwise). Background monitoring is OFF by default and the
/// window is constructed lazily, so a default-configured pilot who has never opened Access
/// GSX has neither route: the entire GSX announcement stream is discarded, correctly and by
/// configuration. Logging that as "spoke" would send an investigator hunting a speech-engine
/// fault that does not exist, when the answer is a setting.
/// </para>
/// </summary>
public enum SpeechRoute
{
    /// <summary>
    /// Neither route exists — background monitoring is off AND nothing is subscribed to
    /// <c>AnnouncementReady</c>. The phrase is definitively discarded and NOBODY heard it.
    /// This line alone answers "GSX never says anything".
    /// </summary>
    None,

    /// <summary>
    /// Queued to the screen reader directly (the pilot opted into background monitoring).
    /// Heard, subject only to the speech queue.
    /// </summary>
    Background,

    /// <summary>
    /// Handed to the Access GSX window. Heard ONLY IF that window is open at this moment —
    /// a subscriber exists, but a hidden window drops it. Deliberately not claimed as
    /// delivery: this layer cannot see the form's visibility, and inventing certainty is
    /// what the route enum exists to prevent.
    /// </summary>
    Window,
}

/// <summary>
/// The GSX integration's dedicated diagnostic channel — <c>%APPDATA%\MSFSBlindAssist\logs\gsx.log</c>.
///
/// <para>
/// It exists to answer the two questions a tester's GSX report always reduces to: "why did
/// it not say anything?" and "why did it keep saying it?" Before this channel, neither was
/// answerable — an audit of the integration found 141 of 169 speak-or-suppress decisions
/// wrote no log line at all, so "GSX never sent it", "we parsed it and dropped it" and "a
/// gate deliberately swallowed it" were indistinguishable after the fact, despite needing
/// completely different fixes. It is also why the refuel state lifecycle had to be
/// reconstructed from vendor documentation rather than simply read out of a log.
/// </para>
///
/// <para>
/// SHAPE: strict <c>key=value</c>, space-separated, ONE LINE PER EVENT — the sibling
/// gsx-gate-select.log's convention, because a GSX turnaround is a stream of events to grep
/// rather than an algorithm trace to read top-to-bottom. Free text is quoted and flattened
/// to one line; an absent value is <c>(none)</c>, never an empty field. The first field is
/// always <c>ev=</c> so one search partitions the file: <c>ev=session</c>, <c>ev=state</c>,
/// <c>ev=spoke</c>, <c>ev=hushed</c>, <c>ev=summary</c>, <c>ev=verb</c>, <c>ev=reset</c>.
/// Never hand-stamp a timestamp, level or category — <c>LogFormatter</c> adds all three.
/// </para>
///
/// <para>
/// WHAT IS DELIBERATELY NOT HERE. (1) NO PER-TICK LINES. GSX republishes <c>/services</c> at
/// ~1 Hz and the whole <c>/menu</c> ~3×/s, and the gates run at that rate: logging each
/// swallowed decision would reproduce the spam in the file — one 186-pax deboard alone is
/// ~580 suppression lines, and summed it reaches ~18 MB/h, which evicts the whole 5 MB × 3
/// rotation in about an hour, so a post-flight report would find its own evidence already
/// rotated away. Suppressions are COUNTED and flushed as one <c>ev=summary</c> per service
/// run instead (see <c>GsxServiceAnnouncer</c>). (2) NO RAW FRAMES — chiefly a SIZE rule:
/// <c>handlerData</c> alone is ~1.7 MB and <c>receipt</c> embeds rendered invoice HTML plus a
/// base64 logo, so one frame would bury a turnaround; the derived fields this channel logs say
/// more in a line than the payload says in a megabyte. (3) NO command <c>args</c>: a
/// <c>settings.set</c> carries the field's value, and one of those is <c>simbrief_username</c>,
/// the pilot's third-party account alias — the one genuinely real-world value in the
/// integration, and never needed to diagnose an announcement. (4) NO gate.select duplication —
/// gsx-gate-select.log is the documented first stop for "gate not found" and already carries
/// richer fields per attempt.
///
/// <para>
/// Everything else GSX publishes IS logged in full, including invoice totals, ground-connection
/// amounts, handling companies and menu text. This is a flight simulator: that money is
/// simulated and those companies are fictional. An earlier version of this file redacted them
/// behind a hash, which made an invoice bug unreadable in the one log built to explain it.
/// </para>
/// </para>
/// </summary>
public static class GsxDiagnosticLog
{
    /// <summary>
    /// Line-length sanity bound. Every real announcement is far shorter (they are spoken
    /// sentences); this only stops a vendor surprise from putting a wall of text through the
    /// GSX-prose arms (<c>stateText</c>, <c>busPhase</c>, the message slot). Generous on
    /// purpose — truncating diagnostic text is a cost, not a feature.
    /// </summary>
    internal const int MaxPhraseChars = 500;

    internal const string None = "(none)";

    // Declared ONCE as a static readonly: Log.Channel caches per name, and its
    // truncateOnLaunch is honoured only by the call that first creates the channel.
    // Not truncating is deliberate — a tester who notices a bad announcement mid-flight
    // and only then thinks to collect logs would otherwise have lost the session.
    private static readonly LogChannel Channel = Log.Channel("gsx");

    /// <summary>Writes an already-built <c>ev=…</c> body at Info. The sink handed to the announcers.</summary>
    public static void Write(string body)
    {
        if (!string.IsNullOrWhiteSpace(body)) Channel.Info(body);
    }

    /// <summary>A GSX session began — the hello/first-snapshot header that splits the file by session.</summary>
    public static void Session(string? icao, IReadOnlyCollection<string>? capabilities, int serviceCount) =>
        Channel.Info(FormatSession(icao, capabilities, serviceCount));

    /// <summary>
    /// A phrase was PUBLISHED — which is not the same as heard, and the difference is the
    /// whole reason this takes a route rather than a bool. See <see cref="SpeechRoute"/>.
    /// </summary>
    public static void Spoke(GsxSpeechSource source, string phrase, SpeechRoute route) =>
        Channel.Info(FormatSpoke(source, phrase, route));

    /// <summary>
    /// A phrase was withheld. For LOW-RATE suppressions only (a baseline swallow, a receipt
    /// already seen, the message slot standing down while a service performs) — a per-tick
    /// gate swallow must be counted into an <c>ev=summary</c>, never logged here.
    /// </summary>
    public static void Hushed(GsxSpeechSource source, string phrase, string gate, string why) =>
        Channel.Info(FormatHushed(source, phrase, gate, why));

    /// <summary>An outgoing Remote API command and its result. Never its args.</summary>
    public static void Verb(string verb, string result, string? code, string? message, long elapsedMs)
    {
        string body = FormatVerb(verb, result, code, message, elapsedMs);
        if (string.Equals(result, "ok", StringComparison.Ordinal)) Channel.Info(body);
        else Channel.Warn(body);
    }

    /// <summary>Session models were torn down — the reason a baseline is about to be re-taken.</summary>
    public static void Reset(string reason, bool clearedReceiptDigests) =>
        Channel.Info($"ev=reset reason={Quote(reason)} clearedReceiptDigests={Lower(clearedReceiptDigests)}");

    /// <summary>The socket came up or went down.</summary>
    public static void Connection(bool up, int generation) =>
        Channel.Info($"ev=session state={(up ? "connected" : "disconnected")} generation={generation}");

    // ── Pure formatting (unit-tested; no I/O) ────────────────────────────────────────────

    internal static string FormatSession(string? icao, IReadOnlyCollection<string>? capabilities, int serviceCount)
    {
        string caps = capabilities is { Count: > 0 }
            ? "[" + string.Join(",", capabilities.Select(c => c.Trim()).Where(c => c.Length > 0)) + "]"
            : None;
        return $"ev=session icao={OrNone(icao)} capabilities={caps} services={serviceCount}";
    }

    internal static string FormatSpoke(GsxSpeechSource source, string phrase, SpeechRoute route) =>
        $"ev=publish src={Name(source)} {DescribePhrase(source, phrase)} route={Name(route)}";

    internal static string FormatHushed(GsxSpeechSource source, string phrase, string gate, string why) =>
        $"ev=hushed src={Name(source)} {DescribePhrase(source, phrase)} gate={OrNone(gate)} why={Quote(why)}";

    internal static string FormatVerb(string verb, string result, string? code, string? message, long elapsedMs) =>
        $"ev=verb verb={OrNone(verb)} result={OrNone(result)} code={OrNone(code)} " +
        $"message={Quote(message)} elapsedMs={elapsedMs}";

    /// <summary>
    /// The phrase, in full. Every GSX announcement is loggable: the money is simulated, the
    /// handling companies are fictional, and the service prose is GSX's own status text —
    /// see <see cref="GsxSpeechSource"/> for why an earlier redaction tier was removed.
    /// <see cref="MaxPhraseChars"/> is a line-length sanity bound, not a privacy device.
    /// </summary>
    internal static string DescribePhrase(GsxSpeechSource source, string phrase)
    {
        _ = source;
        string flat = Flatten(phrase);
        string shown = flat.Length > MaxPhraseChars ? flat[..MaxPhraseChars] + "…" : flat;
        return $"phrase={Quote(shown)}";
    }

    /// <summary>
    /// Newlines and tabs to single spaces, runs collapsed. GSX's statusText is natively
    /// multi-line ("bus in position\npax 181/186\nbags 100%"), and a wrapped entry breaks
    /// the one-line-per-event scan pattern the whole channel is read by.
    /// </summary>
    internal static string Flatten(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        bool lastWasSpace = false;
        foreach (char c in value)
        {
            char ch = c is '\r' or '\n' or '\t' ? ' ' : c;
            if (ch == ' ')
            {
                if (lastWasSpace) continue;
                lastWasSpace = true;
            }
            else lastWasSpace = false;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private static string Name(SpeechRoute route) => route switch
    {
        SpeechRoute.Background => "background",
        SpeechRoute.Window     => "window(heard only while Access GSX is open)",
        _                      => "none(nobody heard this)",
    };

    private static string Name(GsxSpeechSource source) => source switch
    {
        GsxSpeechSource.Service      => "service",
        GsxSpeechSource.GateSelect   => "gate-select",
        GsxSpeechSource.Message      => "message",
        GsxSpeechSource.Menu         => "menu",
        GsxSpeechSource.Receipt      => "receipt",
        GsxSpeechSource.BillingTimer => "billing-timer",
        _                            => "unknown",
    };

    /// <summary>Quoted, flattened, embedded quotes escaped — or a bare <c>(none)</c> when empty.</summary>
    internal static string Quote(string? value)
    {
        string flat = Flatten(value);
        return flat.Length == 0 ? None : "\"" + flat.Replace("\"", "'") + "\"";
    }

    private static string OrNone(string? value)
    {
        string flat = Flatten(value);
        return flat.Length == 0 ? None : flat;
    }

    private static string Lower(bool value) => value ? "true" : "false";
}
