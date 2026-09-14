using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Radios. Both variants.
///
/// THIS PANEL IS A DELIBERATE EXCEPTION to "anything doable inside a G1000 display does not
/// get a panel". The radios ARE tuned on the G1000 — but only by turning the dual
/// concentric knob, one character at a time, and there is no keyboard on the aeroplane to
/// do it any other way. Making a blind pilot click a knob event per digit to reach 124.80
/// is the kind of thing that rule exists to prevent, not to require. The same judgement as
/// Ctrl+B for the altimeters: the aeroplane's own mechanism stays, and MSFSBA offers a
/// faster road to the identical result.
///
/// TWO RADIOS, TWO DIFFERENT ENCODINGS, on the same aeroplane. This is the trap:
///
///   NAV   NAV{n}_STBY_SET_HZ        RAW HERTZ      110300000 -> 110.30
///   COM   COM{n}_STBY_RADIO_SET     BCD16          9344      -> 124.80
///
/// and the obvious-looking COM1_STBY_RADIO_SET_HZ does NOTHING — measured, the standby
/// stayed at 125.855 while the BCD form moved it immediately. Nor does the NAV pair accept
/// BCD. Both were established by writing and reading back, not by reasoning from the names.
///
/// Everything here sets the STANDBY and then swaps on demand, which is how the radio is
/// actually flown: you tune the next frequency while still talking on the current one.
///
/// A REAL LIMIT, stated rather than hidden: BCD16 carries four digits, so the COM radios
/// take 25 kHz spacing and no finer. 136.975 encodes as 0x3697 and comes back 136.97.
/// That is the only event this radio accepts, so an 8.33 kHz channel cannot be typed here
/// — it has to be dialled on the G1000's own knob.
/// </summary>
public partial class CowsDA40Definition
{
    private const string RadiosPanel = "Radios";

    private static Dictionary<string, SimVarDefinition> BuildRadioVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddStandbySet(v, "DA40_RADIO_COM1_SET", "COM STANDBY FREQUENCY:1", "COM 1 Standby");
        AddSwap(v, "DA40_RADIO_COM1_SWAP", "Swap COM 1");

        AddStandbySet(v, "DA40_RADIO_COM2_SET", "COM STANDBY FREQUENCY:2", "COM 2 Standby");
        AddSwap(v, "DA40_RADIO_COM2_SWAP", "Swap COM 2");

        AddStandbySet(v, "DA40_RADIO_NAV1_SET", "NAV STANDBY FREQUENCY:1", "NAV 1 Standby");
        AddSwap(v, "DA40_RADIO_NAV1_SWAP", "Swap NAV 1");

        AddStandbySet(v, "DA40_RADIO_NAV2_SET", "NAV STANDBY FREQUENCY:2", "NAV 2 Standby");
        AddSwap(v, "DA40_RADIO_NAV2_SWAP", "Swap NAV 2");



        // ---------- Status ----------

        AddFreqReadout(v, "DA40_RADIO_COM1_ACTIVE", "COM ACTIVE FREQUENCY:1", "COM 1 Active");
        AddFreqReadout(v, "DA40_RADIO_COM2_ACTIVE", "COM ACTIVE FREQUENCY:2", "COM 2 Active");
        AddFreqReadout(v, "DA40_RADIO_NAV1_ACTIVE", "NAV ACTIVE FREQUENCY:1", "NAV 1 Active");
        AddFreqReadout(v, "DA40_RADIO_NAV2_ACTIVE", "NAV ACTIVE FREQUENCY:2", "NAV 2 Active");

        return v;
    }

    private static void AddStandbySet(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = UpdateFrequency.Continuous,
            // IsAnnounced is REQUIRED for batch membership, which is the only way this
            // value reaches the cache at all. It was false, so the cache was empty and
            // the swap announcement had nothing to read - "COM 2 does not remember".
            // The generic announcer is kept out by NoteRadioChange, not by this flag.
            IsAnnounced = true,
            // ⚠️ NOT HighFrequency, AND "A STATIC RADIO COSTS NOTHING" WAS THE BUG ITSELF.
            // HighFrequency asks for SIM_FRAME with the CHANGED flag, which sends only when
            // the value MOVES - right for G_FORCE, whose touchdown spike the 1 Hz batch
            // missed and which never sits still, and fatal for a frequency that sits on
            // 127.850 for an hour. Nothing changed, so nothing was ever delivered, the cache
            // stayed empty, and the Radios panel read "COM 1 Active: --, COM 2 Active: --,
            // NAV 1 Active: --, NAV 2 Active: --" while the sim held all four values
            // (measured live: 127.85, 124.85, 110.50, 110.50).
            //
            // ⚠️ AND A FORCE-READ CANNOT RESCUE IT. RequestVariable refuses to issue a
            // PERIOD.ONCE against a var that owns a periodic subscription - re-issuing that
            // data-def id REPLACES the recurring request, which is this codebase's own
            // cancel idiom - so it defers to "the next periodic delivery", and under CHANGED
            // there is no next delivery. Opening the panel could never fill the row.
            //
            // ExcludeFromBatch stays: the dedicated subscription is still wanted, now at
            // PERIOD.SECOND with the DEFAULT flag, which delivers whether or not the value
            // moved. Nothing is lost on the knob, because a key the pilot just pressed is
            // read back over the Coherent socket; this path is for changes made ELSEWHERE.
            ExcludeFromBatch = true,
            Format = "F3"
        };
    }

    private static void AddSwap(Dictionary<string, SimVarDefinition> v, string key, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = key,
            DisplayName = display,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };
    }

    private static void AddFreqReadout(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "MHz",
            // Was OnRequest + silent, so a frequency set from OUTSIDE MSFSBA - by
            // SayIntentions, or by the pilot on the G1000 - changed the radio and said
            // nothing at all. Continuous and announced; the settle timer speaks it.
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // ⚠️ NOT HighFrequency, AND "A STATIC RADIO COSTS NOTHING" WAS THE BUG ITSELF.
            // HighFrequency asks for SIM_FRAME with the CHANGED flag, which sends only when
            // the value MOVES - right for G_FORCE, whose touchdown spike the 1 Hz batch
            // missed and which never sits still, and fatal for a frequency that sits on
            // 127.850 for an hour. Nothing changed, so nothing was ever delivered, the cache
            // stayed empty, and the Radios panel read "COM 1 Active: --, COM 2 Active: --,
            // NAV 1 Active: --, NAV 2 Active: --" while the sim held all four values
            // (measured live: 127.85, 124.85, 110.50, 110.50).
            //
            // ⚠️ AND A FORCE-READ CANNOT RESCUE IT. RequestVariable refuses to issue a
            // PERIOD.ONCE against a var that owns a periodic subscription - re-issuing that
            // data-def id REPLACES the recurring request, which is this codebase's own
            // cancel idiom - so it defers to "the next periodic delivery", and under CHANGED
            // there is no next delivery. Opening the panel could never fill the row.
            //
            // ExcludeFromBatch stays: the dedicated subscription is still wanted, now at
            // PERIOD.SECOND with the DEFAULT flag, which delivers whether or not the value
            // moved. Nothing is lost on the knob, because a key the pilot just pressed is
            // read back over the Coherent socket; this path is for changes made ELSEWHERE.
            ExcludeFromBatch = true,
            RenderAsReadOnlyStatus = true,
            Format = "F3"
        };
    }

    private static readonly List<string> RadioControls = new()
    {
        "DA40_RADIO_COM1_SET",
        "DA40_RADIO_COM1_SWAP",
        "DA40_RADIO_COM2_SET",
        "DA40_RADIO_COM2_SWAP",
        "DA40_RADIO_NAV1_SET",
        "DA40_RADIO_NAV1_SWAP",
        "DA40_RADIO_NAV2_SET",
        "DA40_RADIO_NAV2_SWAP"
        // The heading bug and the course knob MOVED to the GFC 700 panel. They are
        // physically PFD bezel knobs, which is why they started here beside the radio
        // knobs, but functionally they are what the autopilot flies - a pilot selecting
        // heading mode needs the bug in the same place, not two panels away. The
        // autopilot carries its own controls against the same SimVars, so they are
        // MOVED and not duplicated.
    };

    private static readonly List<string> RadioDisplay = new()
    {
        "DA40_RADIO_COM1_ACTIVE",
        "DA40_RADIO_COM2_ACTIVE",
        "DA40_RADIO_NAV1_ACTIVE",
        "DA40_RADIO_NAV2_ACTIVE"
    };

    /// <summary>
    /// 124.80 MHz becomes 0x2480, which is 9344.
    ///
    /// ⚠️ NO LONGER USED FOR TUNING, AND THE CLAIM IT CARRIED WAS WRONG. "The COM radios take
    /// this and nothing else" was recorded as measured and is not: COM_STBY_RADIO_SET_HZ lands
    /// exactly on an 8.33 channel (121705000 -> 121.705, 118185000 -> 118.185, both re-measured
    /// live). BCD16 holds four digits, so it could not express .705 at all and silently rounded
    /// every 8.33 channel to the nearest 25 kHz - the "COM tuning is unreliable" report.
    ///
    /// Kept because the encoding is still correct for what it is, and its test documents the
    /// shape; nothing in the tuning path calls it any more.
    /// </summary>
    public static int ComBcd16(double megahertz)
    {
        // Only the four digits after the leading "1" are encoded: 124.800 -> 2 4 8 0.
        int scaled = (int)Math.Round(megahertz * 1000.0) % 100000;   // 24800
        int d1 = scaled / 10000;                                      // 2
        int d2 = scaled / 1000 % 10;                                  // 4
        int d3 = scaled / 100 % 10;                                   // 8
        int d4 = scaled / 10 % 10;                                    // 0
        return (d1 << 12) | (d2 << 8) | (d3 << 4) | d4;
    }

    private void AnnounceSwap(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string swapEvent, string standbyKey, string radio, string format)
    {
        // UNIQUE, not plain. Two swaps of one radio in a row are two byte-identical
        // calculator strings and MobiFlight drops the second - which is exactly why a
        // second press announced a swap that never happened.
        simConnect.ExecuteCalculatorCodeUnique($"1 (>K:{swapEvent})");

        // Say only what we KNOW: that the swap was commanded. The frequency is NOT
        // predicted from the standby any more - that prediction was wrong whenever the
        // cache was stale and stayed confident when the event had been dropped. The
        // radio's own change announces itself a moment later, and that number is real.
        // ⚠️ A SWAP SUPPRESSES NOTHING. It moves the ACTIVE frequency, and the whole point of
        // this design is that the pilot hears what the radio actually did rather than what was
        // predicted - so the read-back that follows a moment later is the news, not an echo.
        // The empty key is deliberate: it marks the write as ours for nothing.
        MarkRadioSetByUs("");
        announcer.AnnounceImmediate($"{radio} swapped");
    }

    private bool HandleRadioSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_RADIO_COM1_SET":
            case "DA40_RADIO_COM2_SET":
            {
                int radio = varKey.Contains('2') ? 2 : 1;
                double mhz = Math.Clamp(value, 118.0, 136.999);

                // ⚠️ RAW HERTZ, NOT BCD16 - AND THE OLD NOTE SAYING OTHERWISE WAS WRONG.
                // This wrote COM_STBY_RADIO_SET with a BCD16 word on the recorded belief that
                // "COM ignores COM_STBY_RADIO_SET_HZ entirely, so COM is 25 kHz only and an
                // 8.33 channel cannot be typed". Re-measured live on the DA40: the Hz event
                // lands EXACTLY on an 8.33 channel both sides -
                //   121705000 (>K:COM_STBY_RADIO_SET_HZ)  -> 121.705
                //   118185000 (>K:COM2_STBY_RADIO_SET_HZ) -> 118.185
                // BCD16 carries four digits, so it cannot express .705 or .185 at all and
                // quietly rounded every 8.33 channel to the nearest 25 kHz - which is the
                // "COM tuning is unreliable" report. NAV was already on the Hz form; the two
                // now differ only in the event name.
                string evt = radio == 1 ? "COM_STBY_RADIO_SET_HZ" : "COM2_STBY_RADIO_SET_HZ";
                long hz = (long)Math.Round(mhz * 1_000_000.0);
                simConnect.ExecuteCalculatorCodeUnique($"{hz} (>K:{evt})");
                MarkRadioSetByUs(varKey);   // the case falls through COM1/COM2
                announcer.AnnounceImmediate($"COM {radio} standby {mhz:0.000}");
                return true;
            }

            case "DA40_RADIO_NAV1_SET":
            case "DA40_RADIO_NAV2_SET":
            {
                int radio = varKey.Contains('2') ? 2 : 1;
                double mhz = Math.Clamp(value, 108.0, 117.95);
                long hz = (long)Math.Round(mhz * 1_000_000.0);
                simConnect.ExecuteCalculatorCode($"{hz} (>K:NAV{radio}_STBY_SET_HZ)");
                MarkRadioSetByUs(varKey);   // the case falls through NAV1/NAV2
                announcer.AnnounceImmediate($"NAV {radio} standby {mhz:0.00}");
                return true;
            }

            // A swap is silent in the cockpit - the numbers just exchange places on a
            // screen - so it has to speak here, and the useful half is what is now ACTIVE.
            // That is the value the standby held a moment ago, read from the cache before
            // the write, which avoids waiting on a round trip to say something the pilot
            // needs immediately.
            case "DA40_RADIO_COM1_SWAP":
                AnnounceSwap(simConnect, announcer, "COM_STBY_RADIO_SWAP",
                    "DA40_RADIO_COM1_SET", "COM 1", "0.000");
                return true;

            case "DA40_RADIO_COM2_SWAP":
                AnnounceSwap(simConnect, announcer, "COM2_RADIO_SWAP",
                    "DA40_RADIO_COM2_SET", "COM 2", "0.000");
                return true;

            case "DA40_RADIO_NAV1_SWAP":
                AnnounceSwap(simConnect, announcer, "NAV1_RADIO_SWAP",
                    "DA40_RADIO_NAV1_SET", "NAV 1", "0.00");
                return true;

            case "DA40_RADIO_NAV2_SWAP":
                AnnounceSwap(simConnect, announcer, "NAV2_RADIO_SWAP",
                    "DA40_RADIO_NAV2_SET", "NAV 2", "0.00");
                return true;
        }

        return false;
    }
}
