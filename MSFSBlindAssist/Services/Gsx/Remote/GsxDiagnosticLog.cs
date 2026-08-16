using System.Security.Cryptography;
using System.Text;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Where a spoken GSX phrase was composed. This is a PRIVACY classification as much as a
/// diagnostic one, and it is what makes the tiering STRUCTURAL rather than a filter that
/// someone has to remember to apply: <see cref="GsxDiagnosticLog.Spoke"/> decides from the
/// source alone whether the text may be written down, so a new call site cannot leak by
/// forgetting a flag — it can only be mis-CLASSIFIED, which is a visible, reviewable choice.
///
/// <para>
/// "It was spoken aloud, so it is safe to log" is NOT the rule here, and the distinction is
/// deliberate: speech is transient and reaches one person in their own headphones, while
/// <c>%APPDATA%\MSFSBlindAssist\logs</c> is durable and exists precisely to be zipped and
/// sent to a developer. This project already holds that line elsewhere — SayIntentions'
/// flight.json values are SHOWN to the pilot in the Ctrl+Shift+S window and are still
/// banned from every log.
/// </para>
/// </summary>
public enum GsxSpeechSource
{
    /// <summary>
    /// A phrase MSFSBA composed itself from typed service fields — "pax 113 of 143.",
    /// "bags 40 percent.", "fuel 2221 kg loaded, aircraft 5252 kg.", "Deboard in progress
    /// by OneJet.", "Refuel complete." Every input is an int, a double, a unit string, a
    /// displayName or an operator name, so the text is safe verbatim.
    ///
    /// <para>
    /// KNOWN, DELIBERATE RESIDUAL: two branches inside this tier echo GSX prose rather than
    /// compose it — <c>StatePhrase</c>'s default arm (<c>stateText</c>, e.g. "Refueling
    /// service can be requested") and <c>BusPhrase</c> (<c>detail.busPhase</c>, e.g. "on the
    /// way, ETA 15 secs"). They ride here on purpose: they are narrow per-service STATUS
    /// fields, not GSX's open render surface the way <see cref="Message"/> and
    /// <see cref="Menu"/> are, and their wording is exactly what a "why did it say that?"
    /// report needs. <see cref="MaxVerbatimChars"/> bounds them defensively.
    /// </para>
    /// </summary>
    Service,

    /// <summary>
    /// <c>GsxGateSelectAnnouncer</c> output — stand names and fixed sentences. Safe
    /// verbatim, and the precedent is already settled: gsx-gate-select.log records the
    /// target, the identifier sent, the resolved gate and GSX's own error message.
    /// </summary>
    GateSelect,

    /// <summary>
    /// GSX's "message" slot, spoken through verbatim and unvalidated. Metadata only: this
    /// is vendor free text whose content is unbounded by construction, and a pass-through
    /// channel inherits the vendor's choices permanently — a GSX update can widen what
    /// lands here with no code change on our side and no test failure.
    /// </summary>
    Message,

    /// <summary>
    /// Menu text. Metadata only, and the widest surface in the integration: GSX previews
    /// INVOICES through the menu, its Administration block browses stored receipts, and
    /// "Customize this Airplane" lists profile names (which carry filesystem paths under
    /// the user profile, i.e. the Windows account name).
    /// </summary>
    Menu,

    /// <summary>
    /// The invoice announcement. Metadata only — it carries the money figure
    /// ("… Total 1761.42."), which the "never log a raw frame … receipt, billing … carry
    /// operator names, cost" invariant names explicitly. The receipt's own digest is the
    /// loggable identity.
    /// </summary>
    Receipt,

    /// <summary>
    /// A metered ground-connection timer phrase. Metadata only — it carries an accrued
    /// amount ("… still running, 1 hour 6 minutes, amount 116.97.").
    /// </summary>
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
/// run instead (see <c>GsxServiceAnnouncer</c>). (2) NO RAW FRAMES, ever, at any verbosity —
/// frames carry <c>handlerData</c> (SimBrief flight data), <c>receipt</c> (invoice HTML) and
/// <c>billing</c> (money). (3) NO command <c>args</c>: a <c>settings.set</c> can carry
/// <c>simbrief_username</c>, the pilot's real account alias. (4) NO gate.select duplication —
/// gsx-gate-select.log is the documented first stop for "gate not found" and already carries
/// richer fields per attempt.
/// </para>
/// </summary>
public static class GsxDiagnosticLog
{
    /// <summary>
    /// Defensive cap on a verbatim phrase. Every Tier-1 phrase is far shorter; this only
    /// bounds the two GSX-prose branches noted on <see cref="GsxSpeechSource.Service"/>, so
    /// a vendor surprise is truncated rather than dumped into the file.
    /// </summary>
    internal const int MaxVerbatimChars = 200;

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
    /// The privacy tiering, in one place: a phrase MSFSBA composed is written out; a phrase
    /// GSX authored is reduced to a length and a stable short hash. The hash is what keeps
    /// the metadata form diagnostically useful — a repeated identical hash IS repeat-spam,
    /// and a hash that changes every tick IS countdown-style churn, which are the two
    /// shapes this channel exists to tell apart, neither of which needs the words.
    /// </summary>
    internal static string DescribePhrase(GsxSpeechSource source, string phrase)
    {
        string flat = Flatten(phrase);
        if (!IsVerbatimSafe(source))
            return $"phrase={None} hash={ShortHash(flat)}";

        string shown = flat.Length > MaxVerbatimChars ? flat[..MaxVerbatimChars] + "…" : flat;
        return $"phrase={Quote(shown)}";
    }

    /// <summary>True only for the tiers MSFSBA composes itself from typed fields.</summary>
    internal static bool IsVerbatimSafe(GsxSpeechSource source) =>
        source is GsxSpeechSource.Service or GsxSpeechSource.GateSelect;

    /// <summary>
    /// Per-PROCESS random salt, generated in memory and NEVER written to the log, to any
    /// file, or to any announcement.
    ///
    /// <para>
    /// It exists because an unsalted digest of a redacted phrase is NOT redaction — it is
    /// encryption with a public key. The phrase templates here are fixed and public
    /// (<c>"Invoice available from {operator}. Total {n}."</c>), so the only unknown in a
    /// receipt line is the money; a 4-byte digest over a ~10^6 candidate space inverts to a
    /// SINGLE preimage in well under a second, and the handling company is printed in clear
    /// on the <c>ev=state</c> lines of the same file. That was measured against this exact
    /// code, not theorised: the invoice total and the ground-connection amount both came
    /// back uniquely. Truncating the hash further would not help (it widens collisions but
    /// still leaks), and dropping the length field would not help either (the digest alone
    /// is decisive) — the input has to stop being guessable, which is what the salt does.
    /// </para>
    ///
    /// <para>
    /// Salting per process costs nothing diagnostically: every use is a WITHIN-SESSION
    /// comparison — "is this the same phrase the slot just published, or a different one?" —
    /// and lines from one gsx.log are only ever compared with each other. Cross-session or
    /// cross-pilot comparison was never a feature, and is exactly the capability that makes
    /// the digest an inversion oracle.
    /// </para>
    /// </summary>
    private static readonly byte[] HashSalt = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// A short, stable-WITHIN-THIS-SESSION fingerprint of a phrase whose text may not be
    /// written down: identical fingerprints on consecutive lines mean the phrase repeated
    /// (repeat-spam), a fingerprint that changes every tick means it is churning (a
    /// countdown). Salted — see <see cref="HashSalt"/>; without that it is reversible.
    /// </summary>
    internal static string ShortHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return None;

        byte[] text = Encoding.UTF8.GetBytes(value);
        byte[] salted = new byte[HashSalt.Length + text.Length];
        Buffer.BlockCopy(HashSalt, 0, salted, 0, HashSalt.Length);
        Buffer.BlockCopy(text, 0, salted, HashSalt.Length, text.Length);
        return Convert.ToHexString(SHA256.HashData(salted), 0, 4);
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
