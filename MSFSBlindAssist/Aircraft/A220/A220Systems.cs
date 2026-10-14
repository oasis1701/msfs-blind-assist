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

    // ---- Colours: the `he` map in afdx/index.ts ([A220-38] evidence table, docs/a220.md) ----

    internal static string? ColourWord(int? colour) => colour switch
    {
        0 => "red", 1 => "amber", 2 => "white", 3 => "green", 4 => "cyan", 5 => "gray", _ => null
    };

    /// <summary>Only an alerting colour is spoken; green and white are the normal states.</summary>
    private static string AlertSuffix(int? colour) => colour is 0 or 1 ? $" ({ColourWord(colour)})" : "";

    // ---- Gear (EICAS/Components/Gear.tsx: icon 0 transit, 1 UP, 2 DN, null "--") ----------

    internal enum GearPhase { Unknown, Up, Down, Transit, Disagree }

    internal static string IconWord(int? icon) => icon switch
    {
        2 => "down", 1 => "up", 0 => "in transit", _ => "unknown"
    };

    internal static GearPhase Phase(GearBlock? g)
    {
        if (g == null) return GearPhase.Unknown;
        int?[] icons = { g.nose_icon, g.left_icon, g.right_icon };
        if (icons.Any(i => i == null)) return GearPhase.Unknown;
        if (icons.All(i => i == 2)) return GearPhase.Down;
        if (icons.All(i => i == 1)) return GearPhase.Up;
        if (icons.Any(i => i == 0)) return GearPhase.Transit;
        return GearPhase.Disagree;
    }

    private static (string Name, int? Icon, int? Colour)[] Legs(GearBlock g) => new[]
    {
        ("nose", g.nose_icon, g.nose_color), ("left", g.left_icon, g.left_color), ("right", g.right_icon, g.right_color)
    };

    /// <summary>"down and locked" / "up" / per-gear detail with any red or amber box named.</summary>
    internal static string FormatGear(GearBlock? g)
    {
        if (g == null) return NotAvailable;
        var legs = Legs(g);
        var phase = Phase(g);
        bool alerting = legs.Any(l => l.Colour is 0 or 1);
        if (phase == GearPhase.Down && legs.All(l => l.Colour == 3)) return "down and locked";
        if (phase == GearPhase.Up && !alerting) return "up";
        return string.Join(", ", legs.Select(l => $"{l.Name} {IconWord(l.Icon)}{AlertSuffix(l.Colour)}"));
    }

    /// <summary>The STATUS synoptic's BRAKE TEMP numbers, left outboard to right outboard
    /// (unitless, toFixed(0) as the synoptic prints them), amber/red named.</summary>
    internal static string FormatBrakeTemps(GearBlock? g)
    {
        if (g == null) return NotAvailable;
        var brakes = new (double? Temp, int? Colour)[]
        {
            (g.left_outb_brake_temp, g.left_outb_brake_color), (g.left_inbd_brake_temp, g.left_inbd_brake_color),
            (g.right_inbd_brake_temp, g.right_inbd_brake_color), (g.right_outb_brake_temp, g.right_outb_brake_color)
        };
        return string.Join(", ", brakes.Select(b => b.Temp is { } t ? Inv($"{t:0}") + AlertSuffix(b.Colour) : "--"));
    }

    /// <summary>The Shift+G readout: lever, gear, brakes.</summary>
    internal static string FormatGearReadout(double leverPct, GearBlock? g)
    {
        string lever = leverPct >= 0.5 ? "Gear lever down" : "Gear lever up";
        if (g == null) return $"{lever}. Gear position not available — display link not connected.";
        return $"{lever}. Gear {FormatGear(g)}. Brake temperatures {FormatBrakeTemps(g)}.";
    }

    /// <summary>Gear call-outs on a change of phase. Baseline-first, and an Unknown FIRST
    /// observation (unpowered, "--") is a baseline too: powering up is not a gear movement.
    /// Once a known phase is the baseline an Unknown poll (a "--" hiccup) never replaces it, so
    /// Down, Unknown, Transit still speaks the change.</summary>
    internal sealed class GearMonitor
    {
        private GearPhase? _prev;

        public string? Observe(GearBlock? g)
        {
            if (g == null) return null;
            var phase = Phase(g);
            if (phase == GearPhase.Unknown)
            {
                _prev ??= phase;
                return null;
            }
            var prev = _prev;
            _prev = phase;
            if (prev == null || prev == GearPhase.Unknown || prev == phase) return null;
            string detail = FormatGear(g);
            return phase switch
            {
                GearPhase.Down => detail == "down and locked" ? "Gear down and locked" : $"Gear down: {detail}",
                GearPhase.Up => "Gear up",
                GearPhase.Transit => "Gear in transit",
                GearPhase.Disagree => $"Gear disagree: {detail}",
                _ => null
            };
        }

        public void Reset() => _prev = null;
    }

    // ---- Doors (Synoptic/Pages/Door.tsx: the value IS a colour index) ---------------------

    internal readonly record struct DoorInfo(string Id, string Name, bool HasSlide);

    /// <summary>Every door the DOOR synoptic draws, in its order; pax and service doors carry
    /// a slide (ARMED/DISARMED), the overwing exits, cargo and equipment bays do not.</summary>
    internal static readonly DoorInfo[] Doors =
    {
        new("fwd_pax", "Forward passenger door", true),
        new("fwd_srv", "Forward service door", true),
        new("l_owee", "Left overwing exit", false),
        new("r_owee", "Right overwing exit", false),
        new("aft_pax", "Aft passenger door", true),
        new("aft_srv", "Aft service door", true),
        new("fwd_cargo", "Forward cargo door", false),
        new("aft_cargo", "Aft cargo door", false),
        new("fwd_equip", "Forward equipment bay door", false),
        new("mid_equip", "Mid equipment bay door", false),
        new("aft_equip", "Aft equipment bay door", false),
    };

    internal static int? DoorValue(DoorsBlock d, string id) => id switch
    {
        "fwd_pax" => d.fwd_pax, "fwd_srv" => d.fwd_srv, "aft_pax" => d.aft_pax, "aft_srv" => d.aft_srv,
        "l_owee" => d.l_owee, "r_owee" => d.r_owee, "fwd_cargo" => d.fwd_cargo, "aft_cargo" => d.aft_cargo,
        "fwd_equip" => d.fwd_equip, "mid_equip" => d.mid_equip, "aft_equip" => d.aft_equip,
        _ => null
    };

    internal static bool? DoorArmed(DoorsBlock d, string id) => id switch
    {
        "fwd_pax" => d.fwd_pax_armed, "fwd_srv" => d.fwd_srv_armed,
        "aft_pax" => d.aft_pax_armed, "aft_srv" => d.aft_srv_armed,
        _ => null
    };

    /// <summary>3 = closed (the synoptic hides the label: the normal state); 1 = open (the only
    /// value that FILLS the symbol); 0 and 2 the bundle draws as red/white outlines without
    /// naming them, so they are spoken by colour (Task 14 records what they mean live).</summary>
    internal static string DoorStateWord(int? v) => v switch
    {
        3 => "closed", 1 => "open", 0 => "not closed (red)", 2 => "not closed (white)", _ => "unknown"
    };

    internal static string FormatDoor(DoorsBlock? d, string id)
    {
        if (d == null) return NotAvailable;
        var info = Doors.First(x => x.Id == id);
        int? v = DoorValue(d, id);
        string text = DoorStateWord(v);
        // The synoptic hides ARMED/DISARMED at value 2, so it is not claimed there either.
        if (info.HasSlide && v != 2 && DoorArmed(d, id) is { } armed) text += armed ? ", armed" : ", disarmed";
        return text;
    }

    internal static string FormatDoorsSummary(DoorsBlock? d)
    {
        if (d == null) return NotAvailable;
        var notClosed = Doors.Where(x => DoorValue(d, x.Id) != 3).Select(x => x.Name.ToLowerInvariant()).ToList();
        return notClosed.Count == 0 ? "all closed" : Inv($"{notClosed.Count} not closed: {string.Join(", ", notClosed)}");
    }

    /// <summary>Door and slide changes, baseline-first; a door whose value becomes unknown is
    /// not spoken (the next known value is diffed against the last known one).</summary>
    internal sealed class DoorMonitor
    {
        private Dictionary<string, (int? Value, bool? Armed)>? _prev;

        public List<string> Observe(DoorsBlock? d)
        {
            var said = new List<string>();
            if (d == null) return said;
            var now = Doors.ToDictionary(x => x.Id, x => (DoorValue(d, x.Id), DoorArmed(d, x.Id)));
            var prev = _prev;
            if (prev != null)
                foreach (var door in Doors)
                {
                    var (pv, pa) = prev[door.Id];
                    var (nv, na) = now[door.Id];
                    if (nv == null) { now[door.Id] = (pv, na ?? pa); continue; }
                    if (pv != null && nv != pv) said.Add($"{door.Name} {DoorStateWord(nv)}");
                    if (door.HasSlide && pa != null && na != null && na != pa)
                        said.Add($"{door.Name} {(na.Value ? "armed" : "disarmed")}");
                }
            _prev = now;
            return said;
        }

        public void Reset() => _prev = null;
    }

    // ---- Engines (EICAS Engines: START box; IGN hidden 0, white 1, green 2) ---------------

    private static bool? Start(EngineBlock e, int n) => n == 1 ? e.eng1_start : e.eng2_start;
    private static int? Ign(EngineBlock e, int n) => n == 1 ? e.eng1_ign : e.eng2_ign;
    private static double? N1(EngineBlock e, int n) => n == 1 ? e.eng1_n1 : e.eng2_n1;
    private static double? N2(EngineBlock e, int n) => n == 1 ? e.eng1_n2 : e.eng2_n2;

    /// <summary>"Engine n" panel row: starting (with ignition and N2) / running (with N1) /
    /// spooling (N2) / off. A start is complete at N2 ~64 (live 2026-10-12), so 50 % N2
    /// without the START flag reads as running.</summary>
    internal static string EngineState(EngineBlock? e, int n)
    {
        if (e == null) return NotAvailable;
        double? n2 = N2(e, n);
        if (Start(e, n) == true)
        {
            string s = "starting";
            if (Ign(e, n) == 2) s += ", ignition on";
            if (n2 is { } v) s += Inv($", N2 {v:0} percent");
            return s;
        }
        if (n2 is { } r && r >= 50) return N1(e, n) is { } a ? Inv($"running, N1 {a:0.0} percent") : "running";
        if (n2 is { } sp && sp >= 5) return Inv($"N2 {sp:0} percent");
        return "off";
    }

    /// <summary>Three calls per start: "Engine n starting" (START box on), "Engine n
    /// ignition" (IGN turns green), "Engine n start complete" (START box off at N2 50+), or
    /// "... start ended, N2 x percent" for a start that stopped short. Baseline-first.</summary>
    internal sealed class EngineStartTracker
    {
        private readonly bool?[] _start = new bool?[3];
        private readonly int?[] _ign = new int?[3];
        private bool _baselined;

        public List<string> Observe(EngineBlock? e)
        {
            var said = new List<string>();
            if (e == null) return said;
            for (int n = 1; n <= 2; n++)
            {
                bool start = Start(e, n) == true;
                int? ign = Ign(e, n);
                bool? ps = _start[n];
                int? pi = _ign[n];
                _start[n] = start;
                _ign[n] = ign;
                if (!_baselined) continue;
                if (ps == false && start) said.Add($"Engine {n} starting");
                if (start && pi != 2 && ign == 2) said.Add($"Engine {n} ignition");
                if (ps == true && !start)
                    said.Add(N2(e, n) is { } v && v < 50
                        ? Inv($"Engine {n} start ended, N2 {v:0} percent")
                        : $"Engine {n} start complete");
            }
            _baselined = true;
            return said;
        }

        public void Reset()
        {
            Array.Clear(_start);
            Array.Clear(_ign);
            _baselined = false;
        }
    }

    // ---- IRS (FMS POS IRS page `Sx`; PFD Flags ATT/HDG ALIGNING, DO NOT TAXI) -------------

    internal enum IrsPhase { Unknown, Aligning, Aligned }

    internal readonly record struct IrsStatus(IrsPhase Phase, double? MinutesToNav, bool DoNotTaxi);

    /// <summary>Aligning while any IRS reports mode 0-2 (POWER UP / ATTITUDE) or the PFD shows
    /// ATT/HDG ALIGNING; aligned only when every reporting IRS is in NAVIGATION (3). With no
    /// FMS position data and no PFD flag the state is Unknown — "align false" alone also means
    /// unpowered, so it never reads as aligned. (Task 14 confirms the source from a cold start.)</summary>
    internal static IrsStatus Irs(PfdBlock? pfd, FmsPositionBlock? pos)
    {
        bool doNotTaxi = pfd?.stationary_align == true;
        var units = new[] { pos?.irs_1?.irs, pos?.irs_2?.irs, pos?.irs_3?.irs }
            .Where(u => u?.mode != null).Select(u => u!).ToList();
        var aligning = units.Where(u => u.mode is >= 0 and <= 2).ToList();
        if (aligning.Count > 0)
            return new IrsStatus(IrsPhase.Aligning, aligning.Select(u => u.time_to_align).Max(), doNotTaxi);
        if (pfd?.align == true) return new IrsStatus(IrsPhase.Aligning, null, doNotTaxi);
        if (units.Count > 0 && units.All(u => u.mode == 3)) return new IrsStatus(IrsPhase.Aligned, null, false);
        return new IrsStatus(IrsPhase.Unknown, null, doNotTaxi);
    }

    internal static string FormatIrs(IrsStatus s) => s.Phase switch
    {
        IrsPhase.Aligned => "aligned",
        IrsPhase.Aligning => "aligning"
            + (s.MinutesToNav is { } m ? Inv($", {m:0.0} minutes to navigation") : "")
            + (s.DoNotTaxi ? ", do not taxi" : ""),
        _ => NotAvailable
    };

    /// <summary>"IRS aligning" on entering alignment, "IRS aligned" when it completes.
    /// Baseline-first; a poll with neither block (haveData false) changes nothing, and neither
    /// does an Unknown status once a baseline exists (one block missing reads Unknown), so
    /// Aligning, Unknown, Aligned still speaks "IRS aligned".</summary>
    internal sealed class IrsMonitor
    {
        private IrsPhase? _prev;

        public string? Observe(IrsStatus s, bool haveData)
        {
            if (!haveData) return null;
            if (s.Phase == IrsPhase.Unknown && _prev != null) return null;
            var prev = _prev;
            _prev = s.Phase;
            if (prev == null || prev == s.Phase) return null;
            if (s.Phase == IrsPhase.Aligning) return "IRS aligning";
            if (s.Phase == IrsPhase.Aligned && prev == IrsPhase.Aligning) return "IRS aligned";
            return null;
        }

        public void Reset() => _prev = null;
    }
}
