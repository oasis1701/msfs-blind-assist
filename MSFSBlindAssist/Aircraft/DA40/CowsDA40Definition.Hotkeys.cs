using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.DA40;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The DA40's output-mode readouts.
///
/// These have to be here because BaseAircraftDefinition implements NONE of them — it does
/// a variable-map lookup and nothing else, so every aircraft answers its own readout keys.
/// Until this file existed, B and L simply did nothing on the DA40.
///
/// Everything below answers in the aeroplane's own terms rather than an airliner's:
///   - Flaps read UP / T/O / LDG, the three positions this aeroplane has.
///   - Gear reports FIXED. The DA40 has fixed tricycle gear; saying "gear down" would be
///     technically true and completely useless, and saying nothing would look broken.
///   - Fuel is in US gallons AND litres, because the AFM quotes both and the aeroplane is
///     fuelled in either depending on where you are.
///   - The characteristic-speed keys carry the DA40's V-speeds instead of the Airbus green
///     dot / S / F set, which do not exist here.
///   - The altimeter reads BOTH subscales, because this aeroplane has two and the AFM
///     descent check says "Altimeters (2) SET". Reading only one would hide the other
///     being wrong.
/// </summary>
public partial class CowsDA40Definition
{
    private static readonly string[] FlapDetents = { "UP", "T/O", "LDG" };

    /// <summary>US gallons to litres, for the dual-unit fuel readouts.</summary>

    /// <summary>
    /// Input mode + B: SET both altimeters.
    ///
    /// This aeroplane has two and the AFM descent check is "Altimeters (2) ... SET", so
    /// one prompt sets both — asking twice for the same number would be a chore invented
    /// by the software, not by the aircraft.
    ///
    /// The G1000 subscale is NOT an L:var: it is the stock SimVar, and the aeroplane's own
    /// Logic.xml drives it with `(L:STATE_BARO1) 16 * (>K:KOHLSMAN_SET, Millibars)` — the
    /// UNINDEXED event, in millibars times sixteen. Verified live, including the two ways
    /// that do NOT work: writing `L:KOHLSMAN SETTING HG:1` (a name with a space and a
    /// colon is a stock SimVar, so that just creates a stray L:var nothing reads) and the
    /// indexed `K:2:KOHLSMAN_SET` both left the value untouched.
    ///
    /// The STANDBY one really is an L:var of that shape — `L:KOHLSMAN SETTING HG:2`,
    /// which the model reads back into STATE_BARO2 — so it is written as one. The two
    /// altimeters genuinely take different transports.
    /// </summary>
    private bool HandleDA40BaroSet(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form? parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }

        // ⚠️ TWO FIELDS, ONE PER ALTIMETER, SEEDED FROM THE LIVE VALUES.
        //
        // This used to be a SINGLE field that wrote both. That is the common case and it is
        // still one keystroke away - type the same number twice - but it could not express
        // the state the standby exists to reveal: the two disagreeing. A pilot could not set
        // them apart deliberately (a QFE standby against a QNH main), and could not correct
        // one without silently overwriting the other's setting with it.
        //
        // Both are pre-filled with what the instrument currently reads, in INCHES to two
        // decimals, so opening the dialog is also how you READ them side by side - and the
        // box is selected on focus, so overtyping replaces rather than appends.
        string SeedOf(string key) =>
            ReadNow(simConnect, key) is double v && v > 0
                ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : "";

        var dialog = new Forms.ValueInputForm(
            "Set Altimeters",
            "Main altimeter setting",
            "948 to 1066 hectopascals, or 28.00 to 31.50 inches",
            announcer,
            ValidateBaroEntry,
            new List<Forms.ValueInputForm.ExtraFieldDef>
            {
                new()
                {
                    Label = "Standby altimeter setting",
                    InitialValue = SeedOf("DA40_STBY_ALTIMETER_SET"),
                    Validator = ValidateBaroEntry
                }
            });

        dialog.InitialValue = SeedOf("DA40_G1000_BARO");

        if (dialog.ShowDialog(parentForm) != DialogResult.OK || !dialog.IsValidInput) return true;
        if (!double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double entered))
        {
            return true;
        }

        // Same convention as the standby panel's own field: the ranges cannot overlap, so
        // magnitude says which unit was meant.
        // Same unit convention as everywhere else on this aeroplane: the ranges cannot
        // overlap, so magnitude says which unit was meant, per field independently.
        static double ToInHg(double v) =>
            Math.Clamp(v > 100 ? v / 33.8639 : v, 28.00, 31.50);

        double mainInHg = ToInHg(entered);
        double stbyInHg = mainInHg;
        if (dialog.ExtraValues.Count > 0 &&
            double.TryParse(dialog.ExtraValues[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double stbyEntered))
        {
            stbyInHg = ToInHg(stbyEntered);
        }

        // ⚠️ WRITE ONLY WHAT THE PILOT ASKED FOR. The dialog has a Set button per altimeter
        // and a "Set all"; SetFieldIndex says which was pressed (-1 all, 0 main, 1 standby).
        // Before this it had ONE button, named "Set Main altimeter setting" and writing
        // BOTH - reported from the cockpit as "there is only a set main button, there's no
        // set standby button, and you can't set them individually unless you go to the
        // panel itself".
        bool doMain = dialog.SetFieldIndex is -1 or 0;
        bool doStby = dialog.SetFieldIndex is -1 or 1;

        // ⚠️ THE TWO ALTIMETERS TAKE DIFFERENT TRANSPORTS - see this method's own summary.
        // The G1000 subscale is the stock unindexed K:KOHLSMAN_SET in millibars times
        // sixteen; the standby is a real L:var written through the calculator.
        if (doMain)
        {
            simConnect.ExecuteCalculatorCode(
                $"{mainInHg * 33.8639 * 16:0.###} (>K:KOHLSMAN_SET)".Replace(",", "."));
        }
        if (doStby) SetStandbyBaro(simConnect, stbyInHg);
        MarkBaroSetByUs();

        // Say them SEPARATELY when they differ, and as one phrase when they do not. A pilot
        // who deliberately set them apart needs to hear that it took; one who set them the
        // same does not need the same number read twice. A single-altimeter set names that
        // altimeter, because saying "both" over one write would be a lie.
        announcer.AnnounceImmediate(
            doMain && doStby ? BaroSetPhrase(mainInHg, stbyInHg)
            : doMain ? $"Main altimeter set, {BaroBothUnits(mainInHg)}"
                     : $"Standby altimeter set, {BaroBothUnits(stbyInHg)}");
        return true;
    }

    /// <summary>
    /// What Ctrl+B says once both altimeters are set. Pure so the suite can pin it.
    ///
    /// ⚠️ A DISAGREEMENT IS NAMED, NEVER AVERAGED OR SUMMARISED. Two altimeters set apart is
    /// either deliberate or a mistake, and both readings have to be spoken for the pilot to
    /// tell which - "Both altimeters set" over two different numbers would hide exactly the
    /// state the standby exists to reveal.
    /// </summary>
    /// <summary>
    /// One setting in both units, the app-wide spelling. Shared so a single-altimeter set
    /// and a both-altimeter set can never drift into two renderings of one number.
    /// </summary>
    internal static string BaroBothUnits(double inHg) =>
        $"{inHg * 33.8639:0} hectopascals, {inHg:0.00} inches";

    internal static string BaroSetPhrase(double mainInHg, double stbyInHg)
    {
        static string P(double inHg) => BaroBothUnits(inHg);

        // A hundredth of an inch is the knob's own detent, so anything smaller is rounding
        // rather than a real difference.
        if (Math.Abs(mainInHg - stbyInHg) < 0.005)
            return $"Both altimeters set, {P(mainInHg)}";

        return $"Main altimeter {P(mainInHg)}. Standby altimeter {P(stbyInHg)}.";
    }

    private static (bool isValid, string message) ValidateBaroEntry(string text)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return (false, "Enter a number.");
        }

        if (value > 100)
        {
            return value is >= 948 and <= 1066
                ? (true, "")
                : (false, "Hectopascals must be between 948 and 1066.");
        }

        return value is >= 28.00 and <= 31.50
            ? (true, "")
            : (false, "Inches must be between 28.00 and 31.50.");
    }

    /// <summary>
    /// Input mode + N: set the NAV standby frequencies.
    ///
    /// On the aeroplane these are tuned with the G1000's NAV knob, and that stays true —
    /// this is the same convenience Ctrl+B is for the altimeters, not a replacement for
    /// the bezel. Both radios are offered in one pass because a pilot tuning an approach
    /// sets the ILS and the missed-approach aid together; either prompt can be cancelled.
    ///
    /// The event is NAV1_STBY_SET_HZ in RAW HERTZ. Verified live, including the two
    /// spellings that do not work: NAV1_STBY_SET with a BCD-ish 11030 left the value at
    /// its previous 113.90, and NAV1_STBY_SET_HZ with a "MHz" unit hint set it to ZERO.
    /// </summary>
    private bool HandleDA40NavRadios(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form? parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }

        var set = new List<string>();

        foreach (int radio in new[] { 1, 2 })
        {
            var dialog = new Forms.ValueInputForm(
                $"Set NAV {radio} Standby",
                $"NAV {radio} standby frequency",
                "108.00 to 117.95 megahertz",
                announcer,
                ValidateNavFrequency);

            if (dialog.ShowDialog(parentForm) != DialogResult.OK || !dialog.IsValidInput) continue;
            if (!double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double mhz))
            {
                continue;
            }

            long hz = (long)Math.Round(mhz * 1_000_000.0);
            simConnect.ExecuteCalculatorCode($"{hz} (>K:NAV{radio}_STBY_SET_HZ)");
            set.Add($"NAV {radio} standby {mhz:0.00}");
        }

        announcer.AnnounceImmediate(set.Count == 0 ? "No frequency set" : string.Join(", ", set));
        return true;
    }

    private static (bool isValid, string message) ValidateNavFrequency(string text)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double mhz))
        {
            return (false, "Enter a frequency in megahertz.");
        }

        return mhz is >= 108.00 and <= 117.95
            ? (true, "")
            : (false, "NAV frequencies run from 108.00 to 117.95.");
    }

    /// <summary>
    /// One variable, rendered the way the PANELS render it - the pilot's chosen units, the
    /// declared format, and the published arc where the gauge has one - and appended only
    /// if it could be read at all.
    ///
    /// Going through TryGetDisplayOverride rather than formatting here is the whole point:
    /// a hotkey answering "85 celsius" while the panel beside it says "85 degrees celsius,
    /// green" would be a second, worse answer to the same question.
    /// </summary>
    private void Add(List<string> bits, SimConnectManager simConnect, string varKey, string label)
    {
        double? value = ReadNow(simConnect, varKey);
        if (value is null) return;

        string text = TryGetDisplayOverride(varKey, value.Value, out string rendered)
            ? rendered
            : value.Value.ToString("0.#");

        bits.Add(label.Length > 0 ? label + " " + text : text);
    }

    private bool HandleDA40Readout(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form? parentForm)
    {
        var speeds = DA40Speeds.For(_variant);

        switch (action)
        {
            // ---------- B: both altimeters ----------
            case HotkeyAction.ReadAltimeter:
            {
                // Both subscales, BY KEY. The standby one is the Standby panel's own
                // setting control, which is Continuous - not STATE_BARO2, whose mirror is
                // OnRequest and so reads nothing unless that panel happens to be open.
                double? g1000 = ReadNow(simConnect, "DA40_G1000_BARO");
                double? standby = ReadNow(simConnect, "DA40_STBY_ALTIMETER_SET");

                announcer.AnnounceImmediate(
                    $"Altimeter {BaroPhrase(g1000)}. Standby {BaroPhrase(standby)}.");
                return true;
            }

            // ---------- Output mode + Shift A, H, S, V: the SELECTED values ----------
            //
            // These four are the SELECTED values and nothing else. The registrations name
            // them for it - "Shift+A (FCU Altitude)", "Shift+H (FCU Heading)" - and the
            // CURRENT values already have their own keys in output mode: plain A is
            // altitude MSL, H magnetic heading, S indicated airspeed, V vertical speed.
            // Answering with both put the current value on a key that exists to say what
            // the autopilot is aiming at, and buried the number actually being asked for.
            case HotkeyAction.ReadAltitude:
                announcer.AnnounceImmediate(ComposeSelected(
                    "Selected altitude", ReadNow(simConnect, "DA40_AP_ALT_SET"), "feet"));
                return true;

            case HotkeyAction.ReadHeading:
                announcer.AnnounceImmediate(ComposeSelected(
                    "Heading bug", ReadNow(simConnect, "DA40_AP_HDG_SET"), "degrees"));
                return true;

            case HotkeyAction.ReadSpeed:
                announcer.AnnounceImmediate(ComposeSelected(
                    "Selected airspeed", ReadNow(simConnect, "DA40_AP_IAS_SET"), "knots"));
                return true;

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                announcer.AnnounceImmediate(ComposeSelected(
                    "Selected vertical speed", ReadNow(simConnect, "DA40_AP_VS_SET"),
                    "feet per minute"));
                return true;

            // ---------- L: flaps ----------
            case HotkeyAction.ReadFlaps:
            {
                double? f = ReadNow(simConnect, "DA40_FLAPS_POSITION");
                if (f is null)
                {
                    announcer.AnnounceImmediate("Flap position not available yet");
                    return true;
                }

                int i = (int)Math.Round(f.Value);
                announcer.AnnounceImmediate(i >= 0 && i < FlapDetents.Length
                    ? $"Flaps {FlapDetents[i]}"
                    : $"Flaps {i}");
                return true;
            }

            // ---------- Shift+G: gear ----------
            case HotkeyAction.ReadGear:
                // Answering honestly beats both silence and a meaningless "gear down".
                announcer.AnnounceImmediate("Fixed gear");
                return true;

            // ---------- F: fuel ----------
            // ---------- F: how much fuel is in the tanks ----------
            //
            // ⚠️ F AND SHIFT+F USED TO BE THE SAME CASE, so both keys said the same
            // sentence and one of the two was wasted. On an airliner they are pounds and
            // kilograms; this aeroplane is fuelled in volume, so the second key answers a
            // different QUESTION instead - see ReadFuelInfo below.
            case HotkeyAction.ReadFuelQuantity:
            {
                double? leftOpt = ReadNow(simConnect, "DA40_FUEL_MAIN_ACTUAL");
                double? rightOpt = ReadNow(simConnect, "DA40_FUEL_AUX_ACTUAL");
                if (leftOpt is null || rightOpt is null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available yet");
                    return true;
                }

                double left = leftOpt.Value;
                double right = rightOpt.Value;
                double total = left + right;

                // The NG's tanks are Main and Auxiliary, not left and right — the AFM is
                // explicit about it, and calling them left/right here would mislead.
                string a = IsNG ? "Main" : "Left";
                string b = IsNG ? "Auxiliary" : "Right";

                // THE PILOT'S OWN UNITS, from the G1000's Display Units. The AFM's second
                // unit is kept ONLY while the setting is the default gallons - the AFM
                // quotes both and the aeroplane is fuelled in either - because a pilot who
                // has explicitly chosen litres has already said which one they want.
                string totalText = TryUnitText("gallons", total, "0.0", out string tt)
                    ? tt : $"{total:0.0} gallons";
                string second = DisplayUnitFuel == "gallons"
                    ? $", {total * LitresPerGallon:0} litres" : "";
                string leftText = TryUnitText("gallons", left, "0.0", out string lt)
                    ? lt : $"{left:0.0} gallons";
                string rightText = TryUnitText("gallons", right, "0.0", out string rt)
                    ? rt : $"{right:0.0} gallons";

                announcer.AnnounceImmediate(
                    $"Fuel {totalText}{second}. {a} {leftText}, {b} {rightText}.");
                return true;
            }

            // ---------- Shift+F: how long that fuel lasts ----------
            //
            // The other half of the fuel question, and the half that matters in the air.
            // Endurance is computed from the CURRENT flow rather than a book figure,
            // because that is what the pilot can act on, and it is stated as the assumption
            // it is.
            //
            // ⚠️ IT IS NOT THE G1000's FUEL CALCULATOR AND MUST NOT BE CONFUSED WITH IT.
            // The MFD's Fuel Calculator is a TOTALIZER - a bookkeeping figure the pilot
            // sets with the INC/DEC/RST FUEL softkeys and which then counts down by fuel
            // USED. It reads differently from the tanks on purpose (measured live: tanks
            // 37.2 gallons, calculator 32.2 remaining), and that difference is the point of
            // having both. This key answers the TANKS.
            case HotkeyAction.ReadFuelInfo:
            {
                double? mainOpt = ReadNow(simConnect, "DA40_FUEL_MAIN_ACTUAL");
                double? auxOpt = ReadNow(simConnect, "DA40_FUEL_AUX_ACTUAL");
                double? flowOpt = ReadNow(simConnect, "DA40_POWER_FUEL_FLOW");

                if (mainOpt is null || auxOpt is null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available yet");
                    return true;
                }

                double onBoard = mainOpt.Value + auxOpt.Value;
                var fuelBits = new List<string>();

                double flow = flowOpt ?? 0;
                fuelBits.Add(TryUnitText("gallons per hour", flow, "0.0", out string ft)
                    ? "Flow " + ft : $"Flow {flow:0.0} gallons per hour");

                if (flow >= 0.1)
                {
                    double hours = onBoard / flow;
                    int h = (int)hours;
                    int m = (int)Math.Round((hours - h) * 60);
                    if (m == 60) { h++; m = 0; }
                    fuelBits.Add($"endurance {h} hours {m} minutes at this flow");
                }
                else
                {
                    fuelBits.Add("engine not burning, no endurance figure");
                }

                // The tank DIFFERENCE is an AFM limit on this aeroplane, and on the NG the
                // fuel moves by itself - burnt from the main, returned through the aux - so
                // it drifts without the pilot doing anything.
                double diff = Math.Abs(mainOpt.Value - auxOpt.Value);
                fuelBits.Add($"tank difference {diff:0.0} of {FuelMaxTankDifferenceGal:0} gallons allowed" +
                             (diff > FuelMaxTankDifferenceGal ? ", OVER the limit" : ""));

                announcer.AnnounceImmediate(string.Join(". ", fuelBits) + ".");
                return true;
            }

            // ---------- P: RPM, and the propeller if there is one ----------
            //
            // The single most-asked number in a piston aeroplane, and it means different
            // things on the two variants. The NG's Austro is a FADEC diesel: there is no
            // propeller lever, the ECU governs the blade angle, and it publishes the RPM it
            // is TARGETING as well as the one the sensor reads - so the pilot can hear the
            // governor working. The XLS has a blue pitch lever and no target.
            case HotkeyAction.ReadEngineRpm:
            {
                var bits = new List<string>();
                Add(bits, simConnect, "DA40_POWER_RPM", "");
                Add(bits, simConnect, "DA40_XLS_RPM", "");

                // ⚠️ THE LEVER BELONGS WITH THE RPM ON THIS AEROPLANE, because it is the ONLY
                // engine control there is. A FADEC diesel has no mixture, no propeller lever
                // and no boost - the single power lever commands LOAD, and the governor picks
                // the RPM from it. So "2100 RPM" alone answers half a question: a pilot needs
                // to know what they asked for as well as what they got, and on a non-monotonic
                // curve (2150 at idle, 1800 at 20 percent, 2300 at full) the two cannot be
                // inferred from each other in either direction.
                double? lever = ReadNow(simConnect, "DA40_POWER_LEVER_SET");
                if (lever is not null) bits.Add($"lever {lever.Value:0} percent");

                if (IsNG)
                {
                    // Only worth saying when it DIFFERS - a target equal to the reading is
                    // the governor doing its job and needs no words.
                    double? target = ReadNow(simConnect, "DA40_POWER_TARGET_RPM");
                    double? actual = ReadNow(simConnect, "DA40_POWER_RPM");
                    if (target is not null && actual is not null &&
                        Math.Abs(target.Value - actual.Value) >= 25)
                    {
                        bits.Add($"governor targeting {target.Value:0} RPM");
                    }
                }
                else
                {
                    // The XLS has a blue pitch lever, and its position IS the RPM request:
                    // the governor target runs linearly from the lever's coarse stop to its
                    // fine stop. The lever is the half of the answer the tachometer omits.
                    double? prop = ReadNow(simConnect, "DA40_XLS_PROP_SET");
                    if (prop is not null) bits.Add($"prop lever {prop.Value:0} percent");
                }

                announcer.AnnounceImmediate(bits.Count == 0
                    ? "RPM not available yet"
                    : string.Join(", ", bits) + ".");
                return true;
            }

            // ---------- E: how much power the engine is making ----------
            //
            // On the NG this is LOAD PERCENT and the power lever position, because that is
            // what an Austro has - no manifold pressure gauge, no mixture, one lever. On a
            // carburetted or injected piston it would be manifold pressure and mixture,
            // which is why the ACTION is called ReadEnginePower rather than ReadLoad: the
            // question is the same on every aeroplane and only the answer changes.
            case HotkeyAction.ReadEnginePower:
            {
                var bits = new List<string>();
                Add(bits, simConnect, "DA40_POWER_LOAD", "Load");
                // On the XLS, power is manifold pressure and RPM - the two numbers every
                // Lycoming cruise table is written in - and the fuel flow beside them.
                Add(bits, simConnect, "DA40_XLS_MAP", "Manifold pressure");
                Add(bits, simConnect, "DA40_XLS_RPM", "");
                Add(bits, simConnect, "DA40_POWER_FUEL_FLOW", "fuel flow");
                Add(bits, simConnect, "DA40_XLS_FUEL_FLOW", "fuel flow");

                if (IsNG) Add(bits, simConnect, "DA40_POWER_LEVER_SET", "power lever");
                else
                {
                    Add(bits, simConnect, "DA40_XLS_THROTTLE_SET", "throttle");
                    Add(bits, simConnect, "DA40_XLS_MIXTURE_SET", "mixture");
                }

                announcer.AnnounceImmediate(bits.Count == 0
                    ? "Engine power not available yet"
                    : string.Join(", ", bits) + ".");
                return true;
            }

            // ---------- Shift+O: the engine's temperatures ----------
            //
            // O reads the OUTSIDE air temperature; Shift+O reads the engine's own, which is
            // the pair of questions a pilot actually asks about heat. The Austro is
            // liquid-cooled and geared, so it has coolant and gearbox temperatures that no
            // air-cooled Lycoming has - and on a diesel the coolant temperature is the one
            // that limits climb power on a hot day.
            case HotkeyAction.ReadEngineTemps:
            {
                var bits = new List<string>();
                Add(bits, simConnect, "DA40_START_OIL_TEMP", "Oil");
                Add(bits, simConnect, "DA40_XLS_OIL_TEMP", "Oil");
                Add(bits, simConnect, "DA40_START_COOLANT_TEMP", "coolant");
                Add(bits, simConnect, "DA40_START_GEARBOX_TEMP", "gearbox");
                // The XLS's air-cooled Lycoming: the hottest cylinder head and exhaust,
                // the two temperatures leaning is judged by.
                Add(bits, simConnect, "DA40_XLS_CHT_HOT", "hottest cylinder head");
                Add(bits, simConnect, "DA40_XLS_EGT_HOT", "hottest exhaust");

                announcer.AnnounceImmediate(bits.Count == 0
                    ? "Engine temperatures not available yet"
                    : string.Join(", ", bits) + ".");
                return true;
            }

            // ---------- Alt+S: the engine, at a glance ----------
            //
            // The DA40 has no lower ECAM, and this is what that key is FOR on an aeroplane
            // that does: one press, the whole engine picture. On a single-engine aeroplane
            // it is the most valuable key on the keyboard - a sighted pilot takes the EIS
            // strip in with one look, and a blind pilot otherwise has to open a panel and
            // arrow down it.
            //
            // Every gauge with a published arc reports its arc, because the arc IS the
            // reading a sighted pilot takes: "85 degrees celsius, green" is the answer,
            // "85" is a number.
            case HotkeyAction.ReadDisplayLowerECAM:
            {
                var bits = new List<string>();

                Add(bits, simConnect, "DA40_POWER_LOAD", "Load");
                Add(bits, simConnect, "DA40_XLS_MAP", "Manifold pressure");
                Add(bits, simConnect, "DA40_POWER_RPM", "");
                Add(bits, simConnect, "DA40_XLS_RPM", "");
                Add(bits, simConnect, "DA40_START_OIL_PRESSURE", "Oil pressure");
                Add(bits, simConnect, "DA40_XLS_OIL_PRESSURE", "Oil pressure");
                Add(bits, simConnect, "DA40_START_OIL_TEMP", "Oil");
                Add(bits, simConnect, "DA40_XLS_OIL_TEMP", "Oil");
                Add(bits, simConnect, "DA40_START_COOLANT_TEMP", "Coolant");
                Add(bits, simConnect, "DA40_START_GEARBOX_TEMP", "Gearbox");
                Add(bits, simConnect, "DA40_POWER_FUEL_FLOW", "Fuel flow");
                Add(bits, simConnect, "DA40_XLS_FUEL_FLOW", "Fuel flow");
                Add(bits, simConnect, "DA40_ELEC_BUS_MAIN_VOLT", "Bus");
                Add(bits, simConnect, "DA40_ELEC_DISP_AMPS", "Amps");

                // ⚠️ A WINDOW, NOT ONE UTTERANCE. Fourteen readings spoken end to end is a
                // sighted pilot's single look turned into a paragraph no part of which can
                // be re-heard. The pilot's ruling: "output alt plus s should open up a
                // little refreshing window, all that in one go might be hard for some
                // users." The ROWS are unchanged - same keys, same display overrides, so a
                // value here can never disagree with the panel showing it.
                //
                // The reading is rebuilt on each tick from the live cache, so the window
                // keeps up with the engine rather than freezing what it said on opening.
                if (parentForm != null)
                {
                    ShowEngineGlance(parentForm, simConnect);
                    return true;
                }

                // No parent to own a window (a headless or early-startup path): fall back to
                // the sentence, which is always better than nothing happening.
                announcer.AnnounceImmediate(bits.Count == 0
                    ? "Engine readings not available yet"
                    : string.Join(". ", bits) + ".");
                return true;
            }

            // ---------- Alt+I: the standby instruments ----------
            //
            // The ISIS key, and on this aeroplane it is not a stand-in for anything: the
            // DA40 really does carry a standby airspeed indicator, altimeter, attitude gyro
            // and compass, and the AFM's own descent check is "Altimeters (2) SET". A
            // reversion to standbys is the one moment a pilot needs all four at once and
            // has no G1000 to read them from.
            case HotkeyAction.ReadDisplayISIS:
            {
                var bits = new List<string>();

                // ⚠️ NOT DA40_STBY_AIRSPEED / DA40_STBY_ALTITUDE. Those read the very same
                // SimVars as DA40_AIRSPEED and INDICATED_ALTITUDE - the standby ASI and
                // altimeter share the pitot-static system with the G1000 on this aeroplane
                // - and those two are ALREADY in the continuous batch. Promoting the
                // standby copies as well would put two keys with one SimVar name into a
                // batch that sorts by name, which shifts every later variable's slot.
                Add(bits, simConnect, "DA40_AIRSPEED", "Standby airspeed");
                Add(bits, simConnect, "INDICATED_ALTITUDE", "altitude");
                Add(bits, simConnect, "DA40_STBY_COMPASS", "compass");

                double? pitch = ReadNow(simConnect, "DA40_STBY_GYRO_PITCH");
                double? bank = ReadNow(simConnect, "DA40_STBY_GYRO_BANK");
                if (pitch is not null && bank is not null)
                {
                    // Sign, not a bare number: "minus three" is not an attitude.
                    bits.Add($"attitude {Math.Abs(pitch.Value):0} degrees nose " +
                             (pitch.Value >= 0 ? "up" : "down") +
                             $", {Math.Abs(bank.Value):0} degrees bank " +
                             (Math.Abs(bank.Value) < 1 ? "level" : bank.Value > 0 ? "right" : "left"));
                }

                // A CAGED or TOPPLED gyro is the whole point of reading it - the
                // instrument is showing something that is not the aeroplane's attitude.
                if (ReadNow(simConnect, "DA40_STBY_GYRO_CAGED") is > 0.5) bits.Add("gyro CAGED");
                if (ReadNow(simConnect, "DA40_STBY_GYRO_TOPPLE") is > 0.5) bits.Add("gyro TOPPLED");

                announcer.AnnounceImmediate(bits.Count == 0
                    ? "Standby instruments not available yet"
                    : string.Join(", ", bits) + ".");
                return true;
            }

            // ---------- X: squawk ----------
            case HotkeyAction.ReadSquawkCode:
            {
                double? code = ReadNow(simConnect, "DA40_XPDR_CODE");
                double? mode = ReadNow(simConnect, "DA40_XPDR_MODE");
                if (code is null)
                {
                    announcer.AnnounceImmediate("Squawk not available yet");
                    return true;
                }

                string modeText = mode switch
                {
                    0 => "off",
                    1 => "standby",
                    2 => "test",
                    3 => "on",
                    4 => "altitude reporting",
                    _ => "unknown mode"
                };
                announcer.AnnounceImmediate($"Squawk {code.Value:0000}, {modeText}");
                return true;
            }

            // ---------- W: weight ----------
            // Ctrl+W. Answered from the standing once-a-second GPS frame, so it is instant and
            // costs no round trip - and the SAME data drives the automatic passing call, so the
            // key and the call can never disagree about which waypoint is active.
            // D and Shift+D. Both were DEAD on this aeroplane - handled by the Airbuses, the
            // Boeings and the HS787 and by nothing on a Diamond - which is the same class of
            // gap Ctrl+W turned out to be.
            case HotkeyAction.ReadDistanceToDest:
                announcer.AnnounceImmediate(ComposeDestinationReadout());
                return true;

            case HotkeyAction.ReadDistanceToTOD:
                announcer.AnnounceImmediate(ComposeTopOfDescentReadout(simConnect));
                return true;

            case HotkeyAction.ReadNDWaypoint:
                announcer.AnnounceImmediate(ComposeWaypointReadout());
                return true;

            case HotkeyAction.ReadGrossWeightKg:
            {
                double? lbOpt = ReadNow(simConnect, "DA40_GROSS_WEIGHT");
                if (lbOpt is null)
                {
                    announcer.AnnounceImmediate("Gross weight not available yet");
                    return true;
                }

                double lb = lbOpt.Value;
                double maxLb = IsNG ? 2888 : 2646;

                announcer.AnnounceImmediate(
                    $"Gross weight {lb * 0.45359237:0} kilograms, {lb:0} pounds. " +
                    $"Maximum {maxLb * 0.45359237:0} kilograms.");
                return true;
            }

            // ---------- Characteristic speeds ----------
            // The Airbus set (green dot / S / F / VLS) does not exist on a DA40, so these
            // keys carry the speeds this aeroplane actually flies, from AFM section 2.
            case HotkeyAction.ReadSpeedVS:
                announcer.AnnounceImmediate(
                    $"Rotate {speeds.Vr:0} knots. Best rate of climb {speeds.VyFlapsTakeoff:0} " +
                    $"with take-off flap, {speeds.VyFlapsUp:0} clean.");
                return true;

            case HotkeyAction.ReadSpeedVLS:
                announcer.AnnounceImmediate(
                    $"Approach {speeds.Vref:0} knots. Short field {speeds.VrefShortField:0}. " +
                    $"Best glide {speeds.VbestGlide:0}.");
                return true;

            case HotkeyAction.ReadSpeedVFE:
            {
                int flap = (int)Math.Round(ReadNow(simConnect, "DA40_FLAPS_POSITION") ?? 0);
                string limit = flap switch
                {
                    1 => $"Take-off flap limit {speeds.VfeTakeoff:0} knots",
                    2 => $"Landing flap limit {speeds.VfeLanding:0} knots",
                    _ => $"Next flap limit {speeds.VfeTakeoff:0} knots"
                };
                announcer.AnnounceImmediate(limit);
                return true;
            }

            case HotkeyAction.ReadSpeedGD:
                announcer.AnnounceImmediate($"Best glide {speeds.VbestGlide:0} knots.");
                return true;

            case HotkeyAction.ReadSpeedS:
                announcer.AnnounceImmediate(
                    $"Manoeuvring {speeds.Va:0} knots. Never exceed {speeds.Vne:0}. " +
                    $"Maximum structural cruise {speeds.Vno:0}.");
                return true;

            case HotkeyAction.ReadSpeedF:
                announcer.AnnounceImmediate(
                    $"Flap limits: take-off {speeds.VfeTakeoff:0}, landing {speeds.VfeLanding:0} knots.");
                return true;
        }

        return false;
    }

    /// <summary>
    /// A barometric setting in BOTH units. ATIS gives one or the other depending where you
    /// are, and converting in your head while flying is not the pilot's job.
    /// </summary>
    /// <summary>
    /// An altimeter setting, LEADING with the unit the pilot set on the G1000.
    ///
    /// Both are still given, because this aeroplane has two altimeters and a pilot
    /// cross-checking a controller's QNH against a chart's field elevation wants whichever
    /// one is in front of them. But the order is not cosmetic: outside North America every
    /// clearance comes in hectopascals, and hearing inches first means doing the conversion
    /// in your head on every descent. The G1000's own setting (PFD Opt, ALT Units) says
    /// which the pilot is working in, so that is the one that comes first.
    /// </summary>
    private string BaroPhrase(double? inHg)
    {
        if (inHg is null) return "not available yet";

        string hpa = $"{inHg.Value * 33.8639:0} hectopascals";
        string inches = $"{inHg.Value:0.00} inches";
        return PressureInHectopascals ? $"{hpa}, {inches}" : $"{inches}, {hpa}";
    }

    /// <summary>
    /// Reads a variable straight from the cache. Readout hotkeys must answer immediately -
    /// a hotkey that silently queues a request and says nothing looks exactly like a
    /// broken key, which is what flaps and baro did before this file existed.
    ///
    /// The argument is an MSFSBA VARIABLE KEY, never a SimVar name. The cache is written
    /// as lastVariableValues[varKey], so looking up "FUEL TANK LEFT MAIN QUANTITY" or
    /// "KOHLSMAN SETTING HG:1" misses however correct the name is. Every readout here once
    /// did exactly that, and the old "?? 0" then turned the miss into a reading: a full
    /// aeroplane reported "0 hectopascals" and "0.0 gallons".
    ///
    /// It returns null rather than 0 for the same reason. A missing reading and a genuine
    /// zero are different facts, and only the caller knows how to say which it has.
    /// </summary>
    private static double? ReadNow(SimConnectManager simConnect, string key)
        => simConnect.GetCachedVariableValue(key);

    /// <summary>
    /// One selected value, or an honest statement that it could not be read - never a
    /// zero standing in for a number the cache did not have.
    /// </summary>
    private static string ComposeSelected(string label, double? value, string unit)
    {
        if (value is null) return label + " not available.";

        return label + " " + Math.Round(value.Value).ToString("N0",
            System.Globalization.CultureInfo.InvariantCulture) + " " + unit + ".";
    }

    /// <summary>
    /// The Alt+S window, opened once and re-shown thereafter, so a pilot pressing the key
    /// twice gets the window they already have rather than a stack of them.
    /// </summary>
    private CowsDA40EngineGlanceForm? _engineGlance;

    private void ShowEngineGlance(Form parentForm, SimConnectManager simConnect)
    {
        if (_engineGlance == null || _engineGlance.IsDisposed)
        {
            _engineGlance = new CowsDA40EngineGlanceForm(() => EngineGlanceRows(simConnect));
            _engineGlance.FormClosed += (_, _) => _engineGlance = null;
            _engineGlance.Show(parentForm);
            return;
        }

        if (_engineGlance.WindowState == FormWindowState.Minimized)
            _engineGlance.WindowState = FormWindowState.Normal;
        _engineGlance.Activate();
    }

    /// <summary>
    /// One row per reading, from the SAME keys and the SAME display overrides the spoken
    /// form used - so the window and the panels can never disagree, and every gauge with a
    /// published arc still reports its arc, because the arc IS the reading a sighted pilot
    /// takes.
    ///
    /// ⚠️ BOTH VARIANTS' KEYS ARE LISTED AND THE MISSING ONES SIMPLY DO NOT ADD A ROW. That
    /// is how the spoken form worked and it is why one list serves the Austro and the
    /// Lycoming without a variant test here.
    /// </summary>
    internal List<string> EngineGlanceRows(SimConnectManager simConnect)
    {
        var rows = new List<string>();

        Add(rows, simConnect, "DA40_POWER_LOAD", "Load");
        Add(rows, simConnect, "DA40_XLS_MAP", "Manifold pressure");
        Add(rows, simConnect, "DA40_POWER_RPM", "");
        Add(rows, simConnect, "DA40_XLS_RPM", "");
        Add(rows, simConnect, "DA40_START_OIL_PRESSURE", "Oil pressure");
        Add(rows, simConnect, "DA40_XLS_OIL_PRESSURE", "Oil pressure");
        Add(rows, simConnect, "DA40_START_OIL_TEMP", "Oil");
        Add(rows, simConnect, "DA40_XLS_OIL_TEMP", "Oil");
        Add(rows, simConnect, "DA40_START_COOLANT_TEMP", "Coolant");
        Add(rows, simConnect, "DA40_START_GEARBOX_TEMP", "Gearbox");
        Add(rows, simConnect, "DA40_POWER_FUEL_FLOW", "Fuel flow");
        Add(rows, simConnect, "DA40_XLS_FUEL_FLOW", "Fuel flow");
        Add(rows, simConnect, "DA40_ELEC_BUS_MAIN_VOLT", "Bus");
        Add(rows, simConnect, "DA40_ELEC_DISP_AMPS", "Amps");

        return rows;
    }
}
