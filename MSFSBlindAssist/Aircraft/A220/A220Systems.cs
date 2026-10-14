using System.Globalization;
using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The Synaptic A220's SYSTEMS data on the AFDX CommBus — CAS, gear, doors, engines, IRS,
/// speed bugs, transponder, the CTP altimeter and minimums — and the pure decoders, monitors
/// and formatters that turn it into spoken and displayed text. Fed once a second by the
/// display agent's <c>sys</c> block (coherent-a220-displays-agent.js, sysBlocks()); no I/O
/// here, so every rule is pinned by SynapticA220SystemsTests against the 2026-10-12 live
/// capture (Fixtures/a220-2026-10-12-commbus-stores-engines-running.json).
///
/// EVERY ENUM IS BUNDLE-DECODED (instrument.js, Synaptic A220 v1.0.10), never guessed — the
/// evidence table is in docs/a220.md "Systems over the CommBus". A value the bundle does not
/// name is spoken by its colour, not by a meaning someone inferred.
/// </summary>
internal static partial class A220Systems
{
    /// <summary>What every readout of a missing block says: never a plausible-looking state.</summary>
    internal const string NotAvailable = "not available — display link not connected";

    private static string Inv(FormattableString text) => FormattableString.Invariant(text);

    /// <summary>The agent's sys() JSON, or null when it is missing or malformed.</summary>
    internal static SystemsBlocks? ParseSystems(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<SystemsBlocks>(json); }
        catch (JsonException) { return null; }
    }

    // ---- Store DTOs. Property names == the aircraft's own field names, and every one
    // ---- of them is listed in the agent's CB_FIELDS (A220AgentContractTests). ----------

    /// <summary>The agent's <c>sys</c> block: one entry per CommBus store (null = stale).</summary>
    internal sealed class SystemsBlocks
    {
        public PfdBlock? lpfd { get; set; }
        public AtcBlock? atc { get; set; }
        public GearBlock? gear { get; set; }
        public DoorsBlock? doors { get; set; }
        public CasBlock? cas { get; set; }
        public EngineBlock? eng { get; set; }
        public CtpBlock? lctp { get; set; }
        public CtpBlock? rctp { get; set; }
        public AdsBlock? lads { get; set; }
        public AdsBlock? rads { get; set; }
        public FmsPositionBlock? fmspos { get; set; }
        public StatusBlock? status { get; set; }
        public NavBlock? lnav { get; set; }
    }

    /// <summary>"A22X.L PFD Data" — speed bugs, minimums, the IRS alignment flags.</summary>
    internal sealed class PfdBlock
    {
        public double? v1 { get; set; }
        public double? vr { get; set; }
        public double? v2 { get; set; }
        public double? vref { get; set; }
        public double? vac { get; set; }
        public double? vt { get; set; }
        public double? vf0 { get; set; }
        public double? vf1 { get; set; }
        public double? vf2 { get; set; }
        public double? vf3 { get; set; }
        public double? vf4 { get; set; }
        public double? vf5 { get; set; }
        public int? minimums_mode { get; set; }
        public double? minimums { get; set; }
        public bool? align { get; set; }
        public bool? stationary_align { get; set; }
    }

    /// <summary>"A22X.ATC Data" — transponder and TCAS (the CNS XPDR/TCAS CONTROL dialog).</summary>
    internal sealed class AtcBlock
    {
        public bool? xpdr1 { get; set; }
        public bool? xpdr2 { get; set; }
        public int? selected { get; set; }
        public int? mode { get; set; }
        public string? id { get; set; }
        public int? code { get; set; }
        public bool? ident { get; set; }
        public int? tss_mode { get; set; }
        public int? tss_limits_sel { get; set; }
        public int? tss_limits { get; set; }
        public bool? tss_other_tfc { get; set; }
        public bool? tss_alt_abs { get; set; }
    }

    /// <summary>"A22X.Gear Data" — gear icons and colours (EICAS), tyres and brakes (STATUS synoptic).</summary>
    internal sealed class GearBlock
    {
        public bool? wow { get; set; }
        public int? nose_icon { get; set; }
        public int? nose_color { get; set; }
        public int? left_icon { get; set; }
        public int? left_color { get; set; }
        public int? right_icon { get; set; }
        public int? right_color { get; set; }
        public int? nose_l_tire_color { get; set; }
        public int? nose_r_tire_color { get; set; }
        public double? nose_l_tire_press { get; set; }
        public double? nose_r_tire_press { get; set; }
        public int? left_outb_tire_color { get; set; }
        public int? left_inbd_tire_color { get; set; }
        public double? left_outb_tire_press { get; set; }
        public double? left_inbd_tire_press { get; set; }
        public int? right_outb_tire_color { get; set; }
        public int? right_inbd_tire_color { get; set; }
        public double? right_outb_tire_press { get; set; }
        public double? right_inbd_tire_press { get; set; }
        public int? left_outb_brake_color { get; set; }
        public int? left_inbd_brake_color { get; set; }
        public int? right_outb_brake_color { get; set; }
        public int? right_inbd_brake_color { get; set; }
        public double? left_outb_brake_temp { get; set; }
        public double? left_inbd_brake_temp { get; set; }
        public double? right_outb_brake_temp { get; set; }
        public double? right_inbd_brake_temp { get; set; }
    }

    /// <summary>"A22X.Doors Data" — the DOOR synoptic: a colour index per door, ARMED per slide.</summary>
    internal sealed class DoorsBlock
    {
        public int? fwd_pax { get; set; }
        public bool? fwd_pax_armed { get; set; }
        public int? aft_pax { get; set; }
        public bool? aft_pax_armed { get; set; }
        public int? fwd_srv { get; set; }
        public bool? fwd_srv_armed { get; set; }
        public int? aft_srv { get; set; }
        public bool? aft_srv_armed { get; set; }
        public int? l_owee { get; set; }
        public int? r_owee { get; set; }
        public int? fwd_cargo { get; set; }
        public int? aft_cargo { get; set; }
        public int? fwd_equip { get; set; }
        public int? mid_equip { get; set; }
        public int? aft_equip { get; set; }
    }

    /// <summary>"A22X.CAS Data" — three bitsets, sent WHOLE every message (cas.ts dV). Values
    /// exceed int.MaxValue (3486581764 live), hence long.</summary>
    internal sealed class CasBlock
    {
        public long[]? msg { get; set; }
        public long[]? info { get; set; }
        public long[]? ecl { get; set; }
    }

    /// <summary>"A22X.Engine Data" — the EICAS engine block (start, ignition, N1/N2/EGT/FF).</summary>
    internal sealed class EngineBlock
    {
        public double? eng1_n1 { get; set; }
        public double? eng1_n2 { get; set; }
        public double? eng1_egt { get; set; }
        public double? eng1_fuel_flow { get; set; }
        public bool? eng1_start { get; set; }
        public int? eng1_ign { get; set; }
        public double? eng2_n1 { get; set; }
        public double? eng2_n2 { get; set; }
        public double? eng2_egt { get; set; }
        public double? eng2_fuel_flow { get; set; }
        public bool? eng2_start { get; set; }
        public int? eng2_ign { get; set; }
        public int? thrust_profile { get; set; }
        public double? to_n1 { get; set; }
    }

    /// <summary>"A22X.L/R CTP Data" — the CTP's own settings: QNH, STD, minimums, nav source.</summary>
    internal sealed class CtpBlock
    {
        public QnhValue? qnh { get; set; }
        public bool? qnh_std { get; set; }
        public int? minimums_mode { get; set; }
        public double? minimums { get; set; }
        public double? ldg_elev { get; set; }
        public int? ldg_elev_mode { get; set; }
        public int? nav_source { get; set; }
        public double? course { get; set; }
        public bool? crosstune { get; set; }
    }

    /// <summary>The CTP's QNH: <c>type</c> "InHg" or "Hpa" (its display unit) and the value in it.</summary>
    internal sealed class QnhValue
    {
        public string? type { get; set; }
        public double? value { get; set; }
    }

    /// <summary>"A22X.L/R ADS Data" — what the PFD's baro block draws: STD, unit (0 IN, 1 HPA), value.</summary>
    internal sealed class AdsBlock
    {
        public int? qnh_unit { get; set; }
        public double? qnh_hpa { get; set; }
        public double? qnh_inhg { get; set; }
        public bool? qnh_std { get; set; }
        public bool? qnh_misconfig_set { get; set; }
        public bool? qnh_misconfig_std { get; set; }
    }

    /// <summary>"A220.FMS Position" — the POS pages' sensor data; only the IRS slots are read.</summary>
    internal sealed class FmsPositionBlock
    {
        public IrsSlot? irs_1 { get; set; }
        public IrsSlot? irs_2 { get; set; }
        public IrsSlot? irs_3 { get; set; }
    }

    internal sealed class IrsSlot
    {
        public IrsUnit? irs { get; set; }
    }

    /// <summary>One IRS: mode 0 POWER UP, 1-2 ATTITUDE, 3 NAVIGATION; time_to_align in minutes.</summary>
    internal sealed class IrsUnit
    {
        public int? mode { get; set; }
        public double? time_to_align { get; set; }
        public double? drift { get; set; }
    }

    /// <summary>"A22X.Status Data" — the STATUS synoptic's numbers (read by Plan C's SYN window).</summary>
    internal sealed class StatusBlock
    {
        public double? tat { get; set; }
        public double? sat { get; set; }
        public double? apu_rpm { get; set; }
        public double? apu_egt { get; set; }
        public int? apu_door { get; set; }
        public double? l_eng_oil_temp { get; set; }
        public double? l_eng_oil_press { get; set; }
        public double? l_eng_oil_qty { get; set; }
        public double? r_eng_oil_temp { get; set; }
        public double? r_eng_oil_press { get; set; }
        public double? r_eng_oil_qty { get; set; }
    }

    /// <summary>"A22X.L Nav Data" — the captain's HSI data block. <c>to</c> is the TO waypoint
    /// ident for an FMS source and a TO/FROM bool for a VOR/LOC source, hence JsonElement.</summary>
    internal sealed class NavBlock
    {
        public JsonElement? to { get; set; }
        public double? dtg { get; set; }
        public double? ttg { get; set; }
        public int? source { get; set; }
        public double? course { get; set; }
    }

    /// <summary>The agent's fplnProgress(): destination DTG (NM) / ETE (SECONDS) and TOD (NM).</summary>
    internal sealed class FplnProgress
    {
        public bool ok { get; set; }
        public double ageMs { get; set; }
        public double? dtg { get; set; }
        public double? ete { get; set; }
        public double? tod { get; set; }
        public double? todFromStart { get; set; }
        public double? total { get; set; }
        public int legs { get; set; }
    }

    // ---- CAS ([A220-36]) ------------------------------------------------------------
    // "A22X.CAS Data".msg is a bitset over SynapticA220EclData.CasMessages: bit index =
    // word << 5 | bit (cas.ts dV). The EICAS shows warnings first, then cautions, advisories,
    // status, newest first within a level (CasStack.tsx).

    internal static List<int> DecodeCasBits(long[]? words)
    {
        var active = new List<int>();
        if (words == null) return active;
        for (int w = 0; w < words.Length; w++)
        {
            uint bits = unchecked((uint)words[w]);
            for (int b = 0; bits != 0; b++, bits >>= 1)
                if ((bits & 1) != 0) active.Add((w << 5) | b);
        }
        return active;
    }

    internal readonly record struct CasLine(int Index, A220CasLevel Level, string Text);

    /// <summary>The table's line, or a quiet status line for an index the table does not know.</summary>
    internal static CasLine Line(int index)
        => SynapticA220EclData.CasMessage(index) is { } m
            ? new CasLine(index, m.Level, m.Text)
            : new CasLine(index, A220CasLevel.Status, Inv($"CAS message {index}"));

    internal static string SeverityWord(A220CasLevel level) => level switch
    {
        A220CasLevel.Warning => "Warning",
        A220CasLevel.Caution => "Caution",
        A220CasLevel.Advisory => "Advisory",
        _ => "Status"
    };

    /// <summary>EICAS order: level descending, then newest first (higher first-seen stamp).</summary>
    internal static List<CasLine> OrderForDisplay(IEnumerable<int> active, IReadOnlyDictionary<int, long> firstSeen)
        => active.Distinct().Select(Line)
            .OrderByDescending(l => (int)l.Level)
            .ThenByDescending(l => firstSeen.TryGetValue(l.Index, out long s) ? s : long.MinValue)
            .ThenBy(l => l.Index)
            .ToList();

    /// <summary>ONE line naming every active warning and caution ("2 cautions: EMER LTS OFF,
    /// FMS PERF DEP CONFIG"), or null when there are none. Status lines are never listed.</summary>
    internal static string? CasSummary(IEnumerable<CasLine> ordered)
    {
        var w = ordered.Where(l => l.Level == A220CasLevel.Warning).ToList();
        var c = ordered.Where(l => l.Level == A220CasLevel.Caution).ToList();
        if (w.Count + c.Count == 0) return null;
        var counts = new List<string>();
        if (w.Count > 0) counts.Add(Count(w.Count, "warning", "warnings"));
        if (c.Count > 0) counts.Add(Count(c.Count, "caution", "cautions"));
        return $"{string.Join(", ", counts)}: {string.Join(", ", w.Concat(c).Select(l => l.Text))}";
    }

    private static string Count(int n, string one, string many)
        => n == 1 ? $"1 {one}" : Inv($"{n} {many}");

    /// <summary>The Alt+E window: one row per message, "Caution: EMER LTS OFF".</summary>
    internal static string FormatCasWindow(IReadOnlyList<CasLine> ordered)
        => ordered.Count == 0
            ? "No CAS messages"
            : string.Join("\n", ordered.Select(l => $"{SeverityWord(l.Level)}: {l.Text}"));

    /// <summary>The CAS panel row: "1 caution, 1 advisory, 4 status".</summary>
    internal static string FormatCasCounts(IReadOnlyList<CasLine>? lines)
    {
        if (lines == null) return NotAvailable;
        if (lines.Count == 0) return "no messages";
        var parts = new List<string>();
        void Add(A220CasLevel level, string one, string many)
        {
            int n = lines.Count(l => l.Level == level);
            if (n > 0) parts.Add(Count(n, one, many));
        }
        Add(A220CasLevel.Warning, "warning", "warnings");
        Add(A220CasLevel.Caution, "caution", "cautions");
        Add(A220CasLevel.Advisory, "advisory", "advisories");
        Add(A220CasLevel.Status, "status", "status");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The CAS call-out state machine. Baseline-first: the first observation speaks only the
    /// warnings-and-cautions summary (once per baseline, the warnings row's). After it: a new
    /// warning/caution interrupts ("Caution: EMER LTS OFF"), a new advisory queues, status is
    /// never spoken, a cleared warning/caution queues "... cleared"; at most
    /// <see cref="A220DisplayParsing.MaxNewCasPerPoll"/> new lines per poll plus "... and N
    /// more". A null bitset (the store stale for a poll) changes NOTHING — the next real one
    /// diffs against the old baseline, so a gap never speaks false clears.
    /// </summary>
    internal sealed class CasMonitor
    {
        private HashSet<int>? _prev;
        private readonly Dictionary<int, long> _firstSeen = new();
        private long _seq;
        private bool _summaryDone;

        /// <summary>The messages at the last observation, in EICAS order (reference-swapped).</summary>
        public List<CasLine> Current { get; private set; } = new();

        /// <summary>True once a bitset has been observed since construction or Reset.</summary>
        public bool HasData => _prev != null;

        public A220DisplayParsing.Announcements Observe(long[]? msgWords, bool warningsMuted, bool advisoriesMuted)
        {
            var result = new A220DisplayParsing.Announcements();
            if (msgWords == null) return result;
            var active = DecodeCasBits(msgWords);
            var next = new HashSet<int>(active);

            if (_prev == null)
            {
                foreach (int i in active.OrderBy(i => i)) _firstSeen[i] = ++_seq;
                _prev = next;
                Current = OrderForDisplay(next, _firstSeen);
                if (!_summaryDone)
                {
                    _summaryDone = true;
                    if (!warningsMuted && CasSummary(Current) is { } summary) result.Queued.Add(summary);
                }
                return result;
            }

            foreach (int i in next.Where(i => !_prev.Contains(i)).OrderBy(i => i)) _firstSeen[i] = ++_seq;
            Current = OrderForDisplay(next, _firstSeen);

            int announced = 0, overflow = 0;
            foreach (var line in Current)
            {
                if (_prev.Contains(line.Index)) continue;
                bool spoken = line.Level switch
                {
                    A220CasLevel.Warning or A220CasLevel.Caution => !warningsMuted,
                    A220CasLevel.Advisory => !advisoriesMuted,
                    _ => false
                };
                if (!spoken) continue;
                if (announced >= A220DisplayParsing.MaxNewCasPerPoll) { overflow++; continue; }
                announced++;
                string phrase = $"{SeverityWord(line.Level)}: {line.Text}";
                if (line.Level is A220CasLevel.Warning or A220CasLevel.Caution) result.Immediate.Add(phrase);
                else result.Queued.Add(phrase);
            }
            if (overflow > 0) result.Queued.Add(Inv($"... and {overflow} more"));

            if (!warningsMuted)
                foreach (int i in _prev.Where(i => !next.Contains(i)).OrderBy(i => i))
                {
                    var line = Line(i);
                    if (line.Level is A220CasLevel.Warning or A220CasLevel.Caution)
                        result.Queued.Add($"{line.Text} cleared");
                }

            foreach (int i in _prev.Where(i => !next.Contains(i)).ToList()) _firstSeen.Remove(i);
            _prev = next;
            return result;
        }

        public void Reset()
        {
            _prev = null;
            _firstSeen.Clear();
            _seq = 0;
            _summaryDone = false;
            Current = new List<CasLine>();
        }
    }
}
