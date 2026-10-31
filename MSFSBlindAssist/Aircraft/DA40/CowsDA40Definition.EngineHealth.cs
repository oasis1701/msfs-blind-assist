using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// How healthy the engine is, which MSFSBA could not say at all.
///
/// ⚠️ THIS AEROPLANE ACCUMULATES ENGINE DAMAGE AND IT SURVIVES A RELOAD. The COWS damage
/// model tracks the block, the oil system, the turbocharger and the fuel system separately -
/// `DAMAGE_BLOCK`, `DAMAGE_OIL`, `DAMAGE_TURBO` with its own friction and heat terms,
/// `DAMAGE_FUEL` with cut and oscillation terms, plus dust ingestion and overstress - and
/// publishes the result as `HEALTH_*` fractions from 1.0 down. MSFSBA exposed the SWITCH that
/// turns damage modelling on and the BUTTON that resets it, and nothing whatsoever about the
/// state in between.
///
/// So a pilot could mishandle the engine, land, reload, and take off again in a damaged
/// aeroplane with no way to find out - which is precisely the trap the state-saving system
/// already sprang once on this project with flat batteries.
///
/// WHY IT IS A PERCENTAGE. The model works in fractions where 1.0 is a factory engine; a
/// pilot thinks in percent, and "block 100 percent" is a number anybody can act on where
/// "1.0" is a number you have to be told the scale of.
///
/// WHY A DROP IS ANNOUNCED. Damage happens DURING something - a cold-power application, an
/// overspeed, running the oil hot - and the moment it starts is the moment a pilot can still
/// do something about it. Waiting for them to open a panel is waiting for the flight to be
/// over. Only a material fall speaks, and only downwards: health climbing back means the
/// pilot pressed Reset, and the Reset button confirms itself.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>How far health must fall, in percent, before it is said again.</summary>
    private const double HealthStep = 5;

    /// <summary>
    /// ⚠️ THE BLOCK IS NOT ON THE SAME SCALE AS THE REST, AND TREATING IT AS THOUGH IT WERE WAS
    /// A BUG IN THE FIRST VERSION OF THIS FILE. The model's own formulas, read out of
    /// `COWS_DA40NG_Logic.xml`:
    ///
    ///     1 (L:DAMAGE_BLOCK:1) 800 / -   (&gt;L:HEALTH_BLOCK:1)      block:  1 - damage/800
    ///     100 (L:DAMAGE_OIL:1) - 100 /   (&gt;L:HEALTH_OIL:1)        oil:    1 - damage/100
    ///     100 (L:DAMAGE_FUEL:1) - 100 /  (&gt;L:HEALTH_FUEL:1)       fuel:   1 - damage/100
    ///
    /// Damage is capped at 100 on all of them, so oil and fuel health really do run 100 percent
    /// down to nothing — but the BLOCK divides by 800, so a completely destroyed block reads
    /// **87.5 percent**. Announced raw beside the others that is actively misleading: a pilot
    /// hearing "engine block 88 percent" would think it barely marked when in fact it is as bad
    /// as the model can make it, and the 5-point step meant nothing was said until 40 of its 100
    /// damage points were already on the clock.
    ///
    /// So the block is rescaled onto the same 0-100 the pilot hears everywhere else. This is not
    /// cosmetic: one number a pilot acts on must mean one thing.
    /// </summary>
    private const double BlockHealthSpan = 8.0;

    private static readonly Dictionary<string, string> HealthKeys = new(StringComparer.Ordinal)
    {
        ["DA40_HEALTH_BLOCK"] = "Engine block",
        ["DA40_HEALTH_OIL"] = "Oil system",
        ["DA40_HEALTH_FUEL"] = "Fuel system",

        // ⚠️ SETTLED: :11 and :12 are the two FUEL PUMPS, not a mystery. The old note here said
        // the package never says what they track and guessing was worse than omitting them; the
        // package does say, one line down in the fuel-pressure calculation —
        //
        //   (A:CIRCUIT ON:42) (L:HEALTH_FUEL:11) * (A:CIRCUIT ON:43) (L:HEALTH_FUEL:12) * + ...
        //     (&gt;L:FUEL_PRESS:1)
        //
        // circuits 42 and 43 being the two pumps whose switches are already on the Fuel panel.
        // A degraded pump makes less pressure, which is a fault a pilot diagnoses and MSFSBA had
        // no way to show. They damage independently of each other and of the fuel system.
        ["DA40_HEALTH_PUMP1"] = "Fuel pump 1",
        ["DA40_HEALTH_PUMP2"] = "Fuel pump 2"
    };

    /// <summary>Exposed for the tests, which check each one exists and is polled.</summary>
    public static IReadOnlyCollection<string> HealthKeyNames => HealthKeys.Keys;

    internal static void AddEngineHealth(Dictionary<string, SimVarDefinition> v)
    {
        Add("DA40_HEALTH_BLOCK", "HEALTH_BLOCK:1", "Engine Block Health");
        Add("DA40_HEALTH_OIL", "HEALTH_OIL:1", "Oil System Health");

        Add("DA40_HEALTH_FUEL", "HEALTH_FUEL:1", "Fuel System Health");
        Add("DA40_HEALTH_PUMP1", "HEALTH_FUEL:11", "Fuel Pump 1 Health");
        Add("DA40_HEALTH_PUMP2", "HEALTH_FUEL:12", "Fuel Pump 2 Health");

        void Add(string key, string lvar, string label)
        {
            v[key] = new SimVarDefinition
            {
                Name = lvar,
                DisplayName = label,
                Type = SimVarType.LVar,
                Units = "percent",
                // The model publishes 0 to 1; a pilot thinks in percent.
                Scale = 100,
                Format = "F0",
                UpdateFrequency = UpdateFrequency.Continuous,
                // Announced only to reach the batch - the falling-health announcer below
                // speaks instead, because health sitting at 100 percent is not news and a
                // percentage that drifts would otherwise chatter.
                IsAnnounced = true,
                ExcludeFromMonitorManager = false
            };
        }
    }

    /// <summary>
    /// One number, one meaning. Oil, fuel and the two pumps are already a plain fraction of a
    /// factory part; the block covers its whole damage range in the top eighth (see
    /// <see cref="BlockHealthSpan"/>), so its shortfall is multiplied back out.
    /// </summary>
    internal static double HealthPercent(string varKey, double raw)
    {
        double fraction = raw <= 1.5 ? raw : raw / 100.0;
        if (varKey == "DA40_HEALTH_BLOCK") fraction = 1.0 - (1.0 - fraction) * BlockHealthSpan;
        return Math.Clamp(fraction, 0, 1) * 100.0;
    }

    /// <summary>
    /// The panel row must read the SAME number the announcement said. Without this the block
    /// row would show the model's raw 87.5 percent for a destroyed engine while the call-out
    /// said 0 - two readings of one fact, which is the defect this whole codebase treats as
    /// worse than either reading alone.
    /// </summary>
    private static bool TryGetEngineHealthDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = string.Empty;
        if (!HealthKeys.ContainsKey(varKey)) return false;
        displayText = $"{HealthPercent(varKey, value):F0}%";
        return true;
    }

    private readonly Dictionary<string, double> _healthSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true for a health reading, keeping it off the generic path: the generic
    /// announcer would read a percentage that moves, several times a second.
    /// </summary>
    private bool NoteEngineHealth(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!HealthKeys.TryGetValue(varKey, out string? what)) return false;

        // The value arriving here is the RAW fraction; Scale is applied for display, not
        // for ProcessSimVarUpdate, so it is turned into a percentage here too.
        double percent = HealthPercent(varKey, value);

        if (!_healthSpoken.ContainsKey(varKey))
        {
            _healthSpoken[varKey] = percent;
            return true;
        }

        double last = _healthSpoken[varKey];

        // Upwards is a Reset, and the Reset button already says so.
        if (percent >= last - 0.01)
        {
            if (percent > last) _healthSpoken[varKey] = percent;
            return true;
        }

        if (percent > last - HealthStep) return true;
        _healthSpoken[varKey] = percent;

        if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(varKey))
        {
            return true;
        }

        announcer.AnnounceImmediate($"{what} health {percent:0} percent");
        return true;
    }
}
