using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// Pure parsing for the A220 FMS and ECL (CHKL) window scrapes returned by the
/// displays agent's <c>fms()</c>/<c>ecl()</c> calls (see
/// coherent-a220-displays-agent.js). No I/O — the forms deserialize the agent JSON
/// through <see cref="ParseAgentTokens"/> and shape it here; geometry constants were
/// measured live 2026-07-29 (labels are gray with their value 10-60 px BELOW at the
/// same x — the Pro Line Fusion label-above-value layout; ECL item checkboxes are
/// 28 px rects hugging the window's left edge, black fill = unchecked).
/// Pinned by A220FmsScreenParsingTests.
/// </summary>
internal static class A220FmsScreenParsing
{
    /// <summary><paramref name="Dropdown"/>/<paramref name="Options"/> mark a value
    /// the agent identified as the FACE OF A CLOSED DROPDOWN (its component carries
    /// an options array + onSelect). A closed dropdown renders only its current
    /// value, so without this it read as an ordinary data row and a blind pilot
    /// could not know a chooser was there. Optional so existing token fixtures
    /// (and the tests built on them) keep their positional construction.</summary>
    /// <summary><paramref name="Clickable"/> means the agent's fiber walk found an
    /// onClick on the token's OWN component (depth ≤ 3) — a genuine pressable
    /// (tile, sub-tab, soft key), as opposed to a data cell that merely inherits
    /// the page-level click catcher. Optional for the same fixture-compat reason
    /// as Dropdown; when NO token in a scrape carries it (older agent, dialog
    /// scrapes, test fixtures), button classification falls back to the text
    /// heuristic.</summary>
    internal readonly record struct WinToken(string Text, double X, double Y, string Color,
        bool Dropdown = false, int Options = 0, bool Clickable = false);
    internal readonly record struct WinBox(string Kind, double X, double Y, string Fill, double W = 0, double H = 0);

    internal sealed class AgentWindow
    {
        public bool Ok;
        public string? Reason;
        public List<WinToken> Tokens = new();
        public List<WinBox> Boxes = new();
    }

    /// <summary>Deserialize an agent fms()/ecl() JSON payload. Null on garbage.</summary>
    internal static AgentWindow? ParseAgentTokens(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var win = new AgentWindow { Ok = root.TryGetProperty("ok", out var ok) && ok.GetBoolean() };
            if (root.TryGetProperty("reason", out var reason)) win.Reason = reason.GetString();
            if (root.TryGetProperty("tokens", out var tokens))
                foreach (var t in tokens.EnumerateArray())
                    win.Tokens.Add(new WinToken(
                        t.GetProperty("t").GetString() ?? "",
                        t.GetProperty("x").GetDouble(), t.GetProperty("y").GetDouble(),
                        t.TryGetProperty("c", out var c) ? c.GetString() ?? "white" : "white",
                        t.TryGetProperty("dd", out var dd) && dd.GetInt32() != 0,
                        t.TryGetProperty("n", out var n) ? n.GetInt32() : 0,
                        t.TryGetProperty("cl", out var cl) && cl.GetInt32() != 0));
            if (root.TryGetProperty("boxes", out var boxes))
                foreach (var b in boxes.EnumerateArray())
                    win.Boxes.Add(new WinBox(
                        b.TryGetProperty("k", out var k) ? k.GetString() ?? "box" : "box",
                        b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(),
                        b.TryGetProperty("f", out var f) ? f.GetString() ?? "" : "",
                        b.TryGetProperty("w", out var w) ? w.GetDouble() : 0,
                        b.TryGetProperty("h", out var h) ? h.GetDouble() : 0));
            return win;
        }
        catch { return null; }
    }

    // ==== FMS window ========================================================

    internal static readonly string[] FmsTiles = { "DBASE", "POS", "FPLN", "PERF", "ROUTE" };

    internal sealed class FmsField
    {
        public string Label = "";
        public string Value = "";
        /// <summary>0-based occurrence of this label text in document order — the
        /// argument clickFmsField needs when labels repeat (TRANS appears 3 times).</summary>
        public int Occurrence;
        /// <summary>True when the value sits inside a drawn value-box rect (the Fusion
        /// enterable-field frame) — only these read as "edit box" in the form. When the
        /// scrape carries no vbox rects at all (older agent / unexpected DOM), every
        /// field defaults to editable so nothing is lost.</summary>
        public bool Editable = true;
        /// <summary>The value is the face of a CLOSED dropdown: Enter opens the
        /// option list (it is a chooser, not a typeable field).</summary>
        public bool IsDropdown;
        /// <summary>How many options the closed dropdown holds (0 when unknown).</summary>
        public int OptionCount;
        /// <summary>Screen y of the field's LABEL — used to interleave fields,
        /// buttons and plain lines back into READING order in the form.</summary>
        public double Y;
    }

    /// <summary>A pressable page text (soft key, sub-tab) with its screen y.</summary>
    internal readonly record struct FmsButton(string Text, double Y);

    /// <summary>A plain unclaimed line with the y of its first token.</summary>
    internal readonly record struct FmsLine(string Text, double Y);

    internal sealed class FmsModel
    {
        public string Side = "";                 // FMS1 / FMS2
        public string Mode = "";                 // ACT / MOD / SEC
        public List<string> Tiles = new();
        public List<FmsField> Fields = new();
        /// <summary>Clickable non-field texts (soft keys, sub-tabs) with their y.</summary>
        public List<FmsButton> ButtonRows = new();
        /// <summary>Button texts alone (projection of <see cref="ButtonRows"/>).</summary>
        public List<string> Buttons => ButtonRows.Select(b => b.Text).ToList();
        public List<string> Lines = new();       // full page, y-grouped reading order
        /// <summary>Lines claimed by NO field/button/tile row, with their y.</summary>
        public List<FmsLine> OrphanRows = new();
        /// <summary>Orphan line texts alone (projection of <see cref="OrphanRows"/>).</summary>
        public List<string> OrphanLines => OrphanRows.Select(l => l.Text).ToList();
        public bool ExecAvailable;
        /// <summary>ROUTE ▸ LEGS only: one entry per flight-plan leg, already grouped
        /// (waypoint + track + distance + constraint + RNP). Empty on every other page.
        /// See <see cref="A220FmsLegParsing"/>.</summary>
        public List<A220FmsLegParsing.FmsLeg> Legs = new();
    }

    private const double FieldMinDy = 10, FieldMaxDy = 60, FieldMaxDx = 45;
    private const double LineTolY = 8;

    /// <summary>
    /// Restrict a scrape to an open dialog's rectangle (window-relative pixels, as
    /// the agent reports both). A Fusion dialog (CROSSING…, HOLD…, FIX…, DEP/ARR)
    /// is drawn OVER the page, so an unfiltered scrape interleaves the dialog's own
    /// fields with the page's — the pilot then edits a constraint while reading
    /// rows belonging to the page behind it. Clipping to the dialog is what makes
    /// it modal, exactly as it is for a sighted pilot (the aircraft's backdrop
    /// swallows every click outside).
    /// </summary>
    internal static (List<WinToken> Tokens, List<WinBox> Boxes) ClipTo(
        IReadOnlyList<WinToken> tokens, IReadOnlyList<WinBox> boxes,
        double x, double y, double w, double h)
    {
        // Tokens carry their TOP-LEFT; a token whose origin is on the frame edge
        // still belongs to the dialog, hence the small pad.
        const double Pad = 6;
        bool Inside(double tx, double ty)
            => tx >= x - Pad && tx <= x + w + Pad && ty >= y - Pad && ty <= y + h + Pad;
        return (
            tokens.Where(t => Inside(t.X, t.Y)).ToList(),
            boxes.Where(b => Inside(b.X, b.Y)).ToList());
    }

    /// <summary>Unit labels this renderer draws beside a value. Kept as a CLOSED
    /// set: any gray token to the right of a value would otherwise be swallowed as
    /// a "unit", including a neighbouring field's own label.</summary>
    private static readonly HashSet<string> UnitLabels = new(StringComparer.Ordinal)
    {
        "KT", "KTS", "NM", "MIN", "FT", "KG", "LB", "°", "Z", "%", "HR", "SEC", "M", "C",
    };

    /// <summary>
    /// The unit label annotating the value at <paramref name="valueIdx"/>: a gray
    /// token from <see cref="UnitLabels"/> on the same row, to its right, close by.
    /// Null when there is none — nothing is invented.
    /// </summary>
    private static string? FindTrailingUnit(IReadOnlyList<WinToken> tokens, int valueIdx)
    {
        const double SameRowDy = 12, MaxDx = 130;
        var v = tokens[valueIdx];
        string? best = null;
        double bestDx = double.MaxValue;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i == valueIdx) continue;
            var t = tokens[i];
            if (t.Color != "gray") continue;
            string text = t.Text.Trim();
            if (!UnitLabels.Contains(text)) continue;
            if (Math.Abs(t.Y - v.Y) > SameRowDy) continue;
            double dx = t.X - v.X;
            if (dx <= 0 || dx > MaxDx) continue;
            if (dx < bestDx) { bestDx = dx; best = text; }
        }
        return best;
    }

    /// <summary>
    /// What the nth repeated choice button in a dialog would pick: the value drawn
    /// on the SAME row, to that button's right. The A220's SELECT CONSTRAINT dialog
    /// (raised when deleting a discontinuity whose neighbouring legs carry
    /// constraints — live 2026-07-30) draws TWO identical "SELECT" buttons, one
    /// beside each candidate constraint, so spoken alone they are indistinguishable
    /// and a blind pilot is choosing blind. Returns null when nothing sits on that
    /// row, so the caller can say so honestly instead of inventing a meaning.
    /// </summary>
    internal static string? DialogChoiceValueFor(
        IReadOnlyList<WinToken> tokens, string button, int occurrence)
    {
        const double RowTolY = 12;
        var rows = tokens.Where(t => t.Text.Trim() == button).OrderBy(t => t.Y).ToList();
        if (occurrence < 0 || occurrence >= rows.Count) return null;
        var self = rows[occurrence];

        var parts = tokens
            .Where(t => Math.Abs(t.Y - self.Y) <= RowTolY && t.X > self.X)
            .OrderBy(t => t.X)
            .Select(t => t.Text.Trim())
            .Where(s => s.Length > 0 && s != button)
            .ToList();
        if (parts.Count > 0)
        {
            // A constraint renders as adjacent tokens ("↓" then "/8000A"), so join
            // before speaking it through the same formatter the legs rows use.
            string raw = string.Concat(parts);
            string spoken = A220FmsLegParsing.FormatConstraint(raw);
            return spoken.Length > 0 ? spoken : raw;
        }

        // SELECT WPT / SELECT AIRWAY layout (bundle-verified 2026-07-30): the
        // describing content sits in a BAND BELOW each button, not on its row —
        // region, frequency, name and coordinates for a duplicate fix; the two
        // endpoint waypoints for an airway (child pitch 160/111 units). Read the
        // band down to the next same-text button so each choice is named; without
        // this every option said "no value shown on this line" and a blind pilot
        // was choosing between identical rows.
        double bandEnd = occurrence + 1 < rows.Count ? rows[occurrence + 1].Y : self.Y + 150;
        var band = tokens
            .Where(t => t.Y > self.Y + RowTolY && t.Y < bandEnd)
            .OrderBy(t => t.Y).ThenBy(t => t.X)
            .Select(t => t.Text.Trim())
            .Where(s => s.Length > 0 && s != button
                        && s.Trim('▯', '□', '-', '.', ' ').Length > 0)
            .ToList();
        if (band.Count == 0) return null;
        string joined = string.Join(" ", band);
        return joined.Length > 90 ? joined[..90] : joined;
    }

    /// <summary>
    /// Spoken form of a field value: runs of the enterable-slot glyph (▯ U+25AF,
    /// with □ U+25A1 tolerated) collapse to "blank" — a screen reader would
    /// otherwise spell out "white vertical rectangle" per slot.
    /// </summary>
    internal static string SpokenFieldValue(string value)
    {
        string collapsed = System.Text.RegularExpressions.Regex
            .Replace(value, "[▯□]+", "blank");
        collapsed = collapsed.Trim();
        return collapsed.Length == 0 ? "blank" : collapsed;
    }

    /// <summary>One plan-leg row of the Direct-To dialog (Dialog/DirectTo.tsx).</summary>
    internal sealed class DirectToLeg
    {
        public string Ident = "";
        /// <summary>0-based occurrence of <see cref="Ident"/> among the dialog's
        /// texts in reading order — the argument clickDialogText needs (KEA appears
        /// four times on a route that leaves and returns to it).</summary>
        public int IdentOcc;
        /// <summary>The row's VERT → (vertical direct) is pressable — the aircraft
        /// grays it out on legs with no crossing altitude.</summary>
        public bool VertEnabled;
        /// <summary>The crossing altitude drawn on the VERT side ("" when empty).</summary>
        public string VertAlt = "";
        /// <summary>0-based occurrence of the row's "VERT →" token in reading order
        /// (counted over ALL VERT tokens, enabled or not) for clickDialogText.</summary>
        public int VertOcc;
        /// <summary>The first (magenta) row is the active leg.</summary>
        public bool Active;
    }

    internal sealed class DirectToModel
    {
        public List<DirectToLeg> Legs = new();
        /// <summary>OFFSET / CRS / ALT SEL rows, as parsed by the generic field
        /// pass (inline layout) — re-listed here so the renderer keeps one order.</summary>
        public List<FmsField> Fields = new();
        /// <summary>The typed-ident entry row (gray arrow + empty box) is present.</summary>
        public bool HasEntry;
    }

    /// <summary>
    /// Structure of the aircraft's Direct-To dialog (header "→ FPLN"), from its
    /// dialog-scoped token dump. Layout (live capture 2026-07-30): each plan leg is
    /// a row [→][ident-in-box] [VERT →][altitude-in-box]; the arrow and ident sit
    /// on the same text row, the ident inside a value box starting 40-60 units
    /// right of the arrow. The typed-entry row is the one whose arrow is GRAY.
    /// The generic ParseFms pass renders this as unreadable soup (glyph buttons,
    /// orphan altitudes, "VERT →: -----" pseudo-fields) — the user report this
    /// parser exists to fix.
    /// </summary>
    internal static DirectToModel ParseDirectTo(
        IReadOnlyList<WinToken> tokens, IReadOnlyList<WinBox>? boxes)
    {
        var m = new DirectToModel();
        var vboxes = boxes?.Where(b => b.Kind == "vbox" && b.W > 0 && b.H > 0).ToList()
                     ?? new List<WinBox>();
        bool InVbox(WinToken t) => vboxes.Any(b =>
            t.X >= b.X - 6 && t.X <= b.X + b.W + 6 && t.Y >= b.Y - 8 && t.Y <= b.Y + b.H + 8);

        var ordered = tokens.OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
        var identOcc = new Dictionary<string, int>(StringComparer.Ordinal);
        int vertOcc = 0;

        foreach (var t in ordered)
        {
            string text = t.Text.Trim();
            if (text != "→") continue;
            // The row's ident: the next token on the same text row, inside a vbox
            // that starts right of the arrow. Nothing there → not a leg row (the
            // dialog header draws a bare arrow too).
            var ident = ordered.FirstOrDefault(o =>
                Math.Abs(o.Y - t.Y) <= 8 && o.X > t.X && o.X - t.X <= 130
                && o.Text.Trim().Length > 0 && o.Text.Trim() != "→" && InVbox(o));
            if (ident == default) continue;
            string id = ident.Text.Trim();
            bool placeholder = id.Trim('▯', '□', '-', '.', ' ').Length == 0;
            if (t.Color == "gray" || placeholder)
            {
                // the typed-ident entry row
                m.HasEntry = m.HasEntry || placeholder;
                continue;
            }
            var leg = new DirectToLeg { Ident = id, Active = ident.Color == "magenta" };
            int occ = identOcc.TryGetValue(id, out int o) ? o : 0;
            identOcc[id] = occ + 1;
            leg.IdentOcc = occ;
            // The VERT half: "VERT →" on the same row; its altitude sits in a vbox
            // to ITS right. Gray = disabled (no crossing altitude to fly to).
            var vert = ordered.FirstOrDefault(o =>
                Math.Abs(o.Y - t.Y) <= 8 && o.Text.Trim() == "VERT →");
            if (vert != default)
            {
                leg.VertEnabled = vert.Color != "gray";
                leg.VertOcc = vertOcc++;
                var alt = ordered.FirstOrDefault(o =>
                    Math.Abs(o.Y - vert.Y) <= 8 && o.X > vert.X && o.X - vert.X <= 130
                    && o.Text.Trim().Length > 0 && o.Text.Trim() != "VERT →" && InVbox(o));
                if (alt != default && alt.Text.Trim('▯', '□', '-', '.', ' ').Length > 0)
                    leg.VertAlt = alt.Text.Trim();
            }
            m.Legs.Add(leg);
        }

        // The scalar fields (OFFSET, ALT SEL, CRS) parse fine generically now that
        // the inline layout pairs — reuse that pass rather than re-deriving them.
        var generic = ParseFms(tokens, boxes);
        m.Fields = generic.Fields
            .Where(f => f.Label is "OFFSET" or "CRS" or "ALT SEL")
            .ToList();
        return m;
    }

    internal static FmsModel ParseFms(IReadOnlyList<WinToken> tokens, IReadOnlyList<WinBox>? boxes = null)
    {
        var m = new FmsModel();
        var consumedAsValue = new HashSet<int>();
        var labelIdx = new List<int>();
        var chrome = new HashSet<int>();          // header/tile tokens — never orphans

        // ROUTE ▸ LEGS is a repeating TABLE, not a label/value form: group it into
        // one row per leg FIRST and mark those tokens consumed, so the generic
        // passes below never re-emit "266° 29.9", "GENOS" and "2.00" as three
        // separate rows. Every other page leaves legConsumed empty and behaves
        // exactly as before.
        var legConsumed = new HashSet<int>();
        if (A220FmsLegParsing.IsLegsPage(tokens))
            m.Legs = A220FmsLegParsing.ParseLegs(tokens, out legConsumed);

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Text is "FMS1" or "FMS2") { if (m.Side.Length == 0) m.Side = t.Text; chrome.Add(i); continue; }
            if (t.Text is "ACT" or "MOD" or "SEC" && m.Mode.Length == 0 && t.Y < 60) { m.Mode = t.Text; chrome.Add(i); continue; }
            if (FmsTiles.Contains(t.Text) && t.Y < 60) { m.Tiles.Add(t.Text); chrome.Add(i); continue; }
            if (t.Color == "gray") labelIdx.Add(i);
        }

        // Value-box rects (the drawn frame around ENTERABLE fields). Whether "no
        // vbox" means "nothing editable" or "editability unknown" depends on the
        // scrape's vintage: a scrape carrying Clickable markers comes from the
        // current agent, whose vbox pass definitely ran — there, an empty box list
        // is the TRUTH (the DBASE status page has no editable field at all, and
        // reading its every value as "edit box" was part of the "FMS is a mess"
        // report). Without markers (dialog scrapes, fixtures, an older agent) the
        // old default-everything-editable fallback stands, so nothing is hidden.
        var valueBoxes = boxes?.Where(b => b.Kind == "vbox" && b.W > 0 && b.H > 0).ToList();
        bool haveBoxes = valueBoxes is { Count: > 0 };
        bool haveClickInfo = tokens.Any(t => t.Clickable);

        var occurrence = new Dictionary<string, int>();
        var pairedLabels = new HashSet<int>();
        foreach (int li in labelIdx)
        {
            var label = tokens[li];
            int occ = occurrence.TryGetValue(label.Text, out int o) ? o : 0;
            occurrence[label.Text] = occ + 1;

            if (legConsumed.Contains(li)) continue;
            int best = -1, bestTier = int.MaxValue;
            double bestDx = double.MaxValue;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i == li || tokens[i].Color == "gray" || legConsumed.Contains(i)) continue;
                // A "…" soft key is an ACTION, never a field's value. On the legs
                // page the pinned THRUST… sits ~49 px under the ALTN heading and
                // was being paired as "ALTN: THRUST…".
                if (tokens[i].Text.EndsWith("…", StringComparison.Ordinal)) continue;
                double dy = tokens[i].Y - label.Y;
                // Fusion draws fields in TWO layouts: value ~34 px BELOW the label
                // (pages, CROSSING), or INLINE — label with the value box directly
                // to its right on the same text row ("OFFSET [---.-] NM",
                // "CRS [255]°" — live Direct-To dialog 2026-07-30). The inline
                // form only counts when the value really sits in a vbox that
                // starts at/after the label: without the box gate, any white token
                // to the right of a gray label (a table cell, a chrome pair) would
                // false-pair.
                bool below = dy >= FieldMinDy && dy <= FieldMaxDy;
                bool inline_ = Math.Abs(dy) <= 8 && tokens[i].X > label.X;
                if (!below && !inline_) continue;
                double dx = Math.Abs(tokens[i].X - label.X);
                // The renderer CENTERS a short label over its value box instead of
                // left-aligning it (live 2026-07-30: the CROSSING dialog's "AT"
                // label sat at x=413 over the altitude box at x=342..445, value
                // "8000" at x=349 — dx 64, so the plain-dx test dropped the edit
                // box the pilot needed, while "AT/ABOVE" at dx 7 paired fine). A
                // label and value that fall inside the SAME value-box x-range are
                // the same field whatever their dx.
                bool sharedBox = haveBoxes && valueBoxes!.Any(b =>
                    label.X >= b.X - 6 && label.X <= b.X + b.W + 6 &&
                    tokens[i].X >= b.X - 6 && tokens[i].X <= b.X + b.W + 6 &&
                    tokens[i].Y >= b.Y - 8 && tokens[i].Y <= b.Y + b.H + 8);
                // 170, not 130: the FUEL page's "NUMBER OF PAX" entry box sits
                // 162 units right of its label (bundle Fuel.tsx: label x=8,
                // input x=170) — with 130 the field was unreachable. Keep in
                // sync with the agent's clickFmsField/clickDialogField gates.
                bool inlineBoxed = inline_ && haveBoxes && valueBoxes!.Any(b =>
                    tokens[i].X >= b.X - 6 && tokens[i].X <= b.X + b.W + 6 &&
                    tokens[i].Y >= b.Y - 8 && tokens[i].Y <= b.Y + b.H + 8 &&
                    b.X >= label.X - 6 && b.X - label.X <= 170);
                // INLINE beats BELOW, and only then is nearest-dx used. In the
                // FUEL page's entry tables the row BELOW a label is the NEXT
                // field's box, sitting at the SAME dx as this label's own inline
                // box — a flat nearest-dx pick resolved that tie by DOM order,
                // i.e. by luck. Tiering makes the label's own row always win.
                // Keep in sync with the agent's bestValueNode.
                int tier = inlineBoxed ? 0
                    : below && (dx < FieldMaxDx || sharedBox) ? 1
                    : int.MaxValue;
                if (tier == int.MaxValue) continue;
                if (tier < bestTier || (tier == bestTier && dx < bestDx))
                { bestTier = tier; bestDx = dx; best = i; }
            }
            if (best < 0) continue;
            consumedAsValue.Add(best);
            pairedLabels.Add(li);
            bool editable = haveBoxes
                ? valueBoxes!.Any(b =>
                    tokens[best].X >= b.X - 6 && tokens[best].X <= b.X + b.W + 6 &&
                    tokens[best].Y >= b.Y - 8 && tokens[best].Y <= b.Y + b.H + 8)
                : !haveClickInfo;
            // The UNIT is drawn as its own gray label just right of the value
            // ("HOLD SPD" / "200" / "KT" — live hold dialog 2026-07-30), so a
            // value spoken alone loses it: "hold speed 200" then, rows later, a
            // bare "KT". Attach it to the value it annotates.
            string value = tokens[best].Text;
            string? unit = FindTrailingUnit(tokens, best);
            if (unit != null) value = $"{value} {unit}";
            m.Fields.Add(new FmsField
            {
                Label = label.Text, Value = value, Occurrence = occ, Editable = editable,
                // A chooser is never a typeable box, whatever frame it is drawn in.
                IsDropdown = tokens[best].Dropdown,
                OptionCount = tokens[best].Options,
                Y = label.Y
            });
        }

        // A pending modification is the MODE flag reading "MOD", NOT an on-screen
        // "EXEC" soft key. The A220's EXEC is a PHYSICAL key on the MKP with its own
        // lamp (MKP.xml, driven by `L:A22X Flight Plan Modified`) — the FMS window
        // never renders the word "EXEC" at all, so the old `Any(t => t.Text ==
        // "EXEC")` test was ALWAYS false and the "EXEC available" cue could never
        // fire. The renderer's own mode strings are ACT / SEC / MOD.
        m.ExecAvailable = m.Mode == "MOD";
        var consumedAsButton = new HashSet<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (consumedAsValue.Contains(i) || chrome.Contains(i) || legConsumed.Contains(i)) continue;
            if (t.Color is "gray" or "dim") continue;
            if (t.Text.Length < 2 && !char.IsLetter(t.Text.FirstOrDefault())) continue;
            if (t.Text.All(char.IsDigit)) continue;
            // A run of dashes/placeholder glyphs is an EMPTY slot, not a button —
            // "-----, button" is a row the user can only be misled by.
            if (t.Text.Trim('-', '–', '—', '.', '▯', '□', ' ').Length == 0) continue;
            // With clickability info present, ONLY genuinely pressable tokens are
            // buttons — a DBASE table cell ("BD-500-1A11") or a date range is data,
            // and reading it as "…, button" was exactly the reported soup. Without
            // the info (dialog scrapes, fixtures) the text heuristic stands.
            if (haveClickInfo && !t.Clickable) continue;
            m.ButtonRows.Add(new FmsButton(t.Text, t.Y));
            consumedAsButton.Add(i);
        }

        m.Lines = ComposeLines(tokens);

        // Anything no field/button/tile row claimed (bare numbers, dim dashes,
        // unpaired gray headings…) still reaches the user — as plain rows, not a
        // duplicated full-page dump.
        var orphanTokens = new List<WinToken>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (chrome.Contains(i) || consumedAsValue.Contains(i) || consumedAsButton.Contains(i)) continue;
            if (pairedLabels.Contains(i) || legConsumed.Contains(i)) continue;
            orphanTokens.Add(tokens[i]);
        }
        m.OrphanRows = ComposeRows(orphanTokens);
        return m;
    }

    /// <summary>Group tokens into reading-order lines (y within tolerance, x-sorted).</summary>
    internal static List<string> ComposeLines(IReadOnlyList<WinToken> tokens)
        => ComposeRows(tokens).Select(l => l.Text).ToList();

    /// <summary>As <see cref="ComposeLines"/>, keeping each line's y (its first token's).</summary>
    internal static List<FmsLine> ComposeRows(IReadOnlyList<WinToken> tokens)
    {
        var lines = new List<FmsLine>();
        foreach (var group in tokens
            .OrderBy(t => t.Y).ThenBy(t => t.X)
            .Aggregate(new List<List<WinToken>>(), (acc, t) =>
            {
                if (acc.Count > 0 && Math.Abs(acc[^1][0].Y - t.Y) <= LineTolY) acc[^1].Add(t);
                else acc.Add(new List<WinToken> { t });
                return acc;
            }))
        {
            lines.Add(new FmsLine(
                string.Join("  ", group.OrderBy(t => t.X).Select(t => t.Text)),
                group[0].Y));
        }
        return lines;
    }

    // ==== ECL (CHKL) window =================================================

    internal static readonly string[] EclTiles = { "SUMMARY", "NORMAL", "NON-NORMAL", "PROC", "FCTN" };

    internal sealed class EclItem
    {
        public string Text = "";
        /// <summary>null = no checkbox (free text / checklist name / option row).</summary>
        public bool? Checked;
        public string Color = "white";
        public double Y;
    }

    internal sealed class EclModel
    {
        public List<string> Tiles = new();
        /// <summary>Open checklist title, or null when a tile listing (Summary) shows.</summary>
        public string? Title;
        public string? PackName;                 // ECL_BCS3_SYN_072826_KG footer
        public List<EclItem> Items = new();
    }

    private const double BoxPairTolAbove = 8, BoxPairTolBelow = 30;

    internal static EclModel ParseEcl(IReadOnlyList<WinToken> tokens, IReadOnlyList<WinBox> boxes)
    {
        var m = new EclModel();
        var body = new List<WinToken>();
        foreach (var t in tokens)
        {
            if (EclTiles.Contains(t.Text) && t.Y < 50) { m.Tiles.Add(t.Text); continue; }
            if (t.Text.StartsWith("ECL_", StringComparison.Ordinal)) { m.PackName = t.Text; continue; }
            // Title: centered heading under the tile row (items hug x≈50, the live
            // "Preflight" title sat at x=273).
            if (t.Y is > 50 and < 120 && t.X > 150 && m.Title == null) { m.Title = t.Text; continue; }
            body.Add(t);
        }

        var checkboxes = boxes.Where(b => b.Kind == "box").OrderBy(b => b.Y).ToList();
        var claimed = new HashSet<int>();

        foreach (var box in checkboxes)
        {
            var lineTokens = new List<int>();
            for (int i = 0; i < body.Count; i++)
            {
                if (claimed.Contains(i)) continue;
                double dy = body[i].Y - box.Y;
                if (dy >= -BoxPairTolAbove && dy <= BoxPairTolBelow && body[i].X > 40) lineTokens.Add(i);
            }
            if (lineTokens.Count == 0) continue;
            foreach (int i in lineTokens) claimed.Add(i);
            var first = body[lineTokens[0]];
            m.Items.Add(new EclItem
            {
                Text = string.Join(" ", lineTokens.OrderBy(i => body[i].X).Select(i => body[i].Text)),
                Checked = IsBoxChecked(box.Fill),
                Color = first.Color,
                Y = box.Y
            });
        }

        // Remaining body tokens: continuation lines get appended to the item above;
        // anything before the first checkbox (or in a no-checkbox listing) becomes
        // its own boxless row (checklist names on Summary, YES/NO options, notes).
        foreach (var (tok, idx) in body.Select((t, i) => (t, i)).Where(p => !claimed.Contains(p.i)).OrderBy(p => p.t.Y))
        {
            var owner = m.Items.LastOrDefault(it => it.Checked != null && it.Y < tok.Y
                && tok.Y - it.Y < 60 && checkboxes.All(b => !(b.Y > it.Y && b.Y <= tok.Y)));
            if (owner != null) owner.Text += " " + tok.Text;
            else m.Items.Add(new EclItem { Text = tok.Text, Checked = null, Color = tok.Color, Y = tok.Y });
        }
        m.Items.Sort((a, b) => a.Y.CompareTo(b.Y));
        return m;
    }

    /// <summary>Black/none fill = unchecked; anything painted = checked (sensed/ticked).</summary>
    internal static bool IsBoxChecked(string fill)
        => fill.Length != 0 && fill != "none" && fill != "rgb(0, 0, 0)" && fill != "rgba(0, 0, 0, 0)";

    /// <summary>
    /// Normalize a checklist line for matching against checklists.json: strip the
    /// dot-leader run (U+2024 "․" repeated) and everything after it (the response),
    /// collapse whitespace, uppercase. "* Ice detector test․․․․Complete" →
    /// "* ICE DETECTOR TEST".
    /// </summary>
    internal static string NormalizeChallenge(string scrapedLine)
    {
        int i = scrapedLine.IndexOf('․');
        string s = i >= 0 ? scrapedLine[..i] : scrapedLine;
        return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
