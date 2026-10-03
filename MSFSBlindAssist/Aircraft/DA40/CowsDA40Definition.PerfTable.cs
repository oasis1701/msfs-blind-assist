using System;
using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The POH's performance tables, on the panel the pilot is already looking at.
///
/// MSFSBA reads the live manifold pressure, RPM, fuel flow and load - the numbers a cruise
/// table is written IN - and had nothing to compare them against, because the tables are
/// CHARTS in a manual a blind pilot cannot open. <see cref="DA40PerformanceTables"/> holds
/// them; this puts one row on each airframe's Power and Levers panel.
///
/// They are rows and not announcements, and not a hotkey either: a state the display can
/// report is scanned for, not spoken at the pilot. And they REPORT - the XLS row names the
/// setting nearest to what is actually set, the NG row names the book minimum beside a
/// load the pilot can already read. Neither says what to do about it.
///
/// ⚠️ THE ROWS BIND THE OnRequest TWIN OF A BATCHED VARIABLE, deliberately. Two keys on one
/// SimVar are fatal only when BOTH ride the continuous batch - it sorts by name, so a
/// duplicate shifts every later variable's slot - and an OnRequest twin is the DA40's
/// established shape for exactly this (four of them already). The row's own value is never
/// used: the text is composed from the batched values captured as they pass.
/// </summary>
public partial class CowsDA40Definition
{
    private double? _perfAltitudeFt;
    private double? _perfOatC;
    private double? _perfMapInHg;
    private double? _perfRpm;
    private double? _perfLoadPct;

    private static Dictionary<string, SimVarDefinition> BuildPerfTableVariables(bool isNg)
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ⚠️ THE ONLY BATCHED KEY ON AMBIENT TEMPERATURE. The two that existed before it -
        // the cabin and the ice/pitot readouts - are both OnRequest, so they only answer
        // while their own panel is open, which is no use to a row on another panel. This
        // one rides the batch and is silenced; a temperature is a number and the numeric
        // rule keeps it quiet.
        v["DA40_PERF_OAT"] = new SimVarDefinition
        {
            Name = "AMBIENT TEMPERATURE",
            DisplayName = "Outside Air Temperature",
            Type = SimVarType.SimVar,
            Units = "celsius",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        if (isNg)
        {
            v["DA40_NG_MIN_LOAD"] = new SimVarDefinition
            {
                Name = "DISP_LD",
                DisplayName = "Book Minimum Load",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.OnRequest,
                IsAnnounced = false,
                RenderAsReadOnlyStatus = true,
                ExcludeFromMonitorManager = true
            };
        }
        else
        {
            v["DA40_XLS_CRUISE_TABLE"] = new SimVarDefinition
            {
                Name = "DISP_MAP",
                DisplayName = "Cruise Table",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.OnRequest,
                IsAnnounced = false,
                RenderAsReadOnlyStatus = true,
                ExcludeFromMonitorManager = true
            };
        }

        return v;
    }

    /// <summary>
    /// Remembers what the tables are entered with. Every one of these is already batched
    /// and already passes through here, so nothing new is subscribed for them.
    /// </summary>
    private void NotePerfTableValue(string varKey, double value)
    {
        switch (varKey)
        {
            case "INDICATED_ALTITUDE": _perfAltitudeFt = value; break;
            case "DA40_PERF_OAT": _perfOatC = value; break;
            case "DA40_XLS_MAP": _perfMapInHg = value; break;
            case "DA40_XLS_RPM": _perfRpm = value; break;
            case "DA40_POWER_LOAD": _perfLoadPct = value; break;
        }
    }

    private bool TryGetPerfTableDisplayOverride(string varKey, out string displayText)
    {
        switch (varKey)
        {
            case "DA40_NG_MIN_LOAD":
            {
                if (_perfAltitudeFt is null || _perfOatC is null)
                {
                    displayText = "Not available yet";
                    return true;
                }

                displayText = DA40PerformanceTables.DescribeNgMinimumLoad(
                    _perfAltitudeFt.Value, _perfOatC.Value);
                if (_perfLoadPct is not null)
                {
                    displayText += $"; making {_perfLoadPct.Value:F0}";
                }

                string isa = DA40PerformanceTables.IsaCorrection(
                    _perfAltitudeFt.Value, _perfOatC.Value);
                if (isa.Length > 0) displayText += $". {isa}";
                return true;
            }

            case "DA40_XLS_CRUISE_TABLE":
            {
                if (_perfAltitudeFt is null || _perfRpm is null || _perfMapInHg is null)
                {
                    displayText = "Not available yet";
                    return true;
                }

                displayText = DA40PerformanceTables.DescribeXlsCruise(
                    _perfAltitudeFt.Value, _perfRpm.Value, _perfMapInHg.Value);

                if (_perfOatC is not null)
                {
                    string isa = DA40PerformanceTables.IsaCorrection(
                        _perfAltitudeFt.Value, _perfOatC.Value);
                    if (isa.Length > 0) displayText += $". {isa}";
                }
                return true;
            }
        }

        displayText = string.Empty;
        return false;
    }

    /// <summary>Forgets the captured conditions when the aircraft is switched away.</summary>
    private void ResetPerfTableState()
    {
        _perfAltitudeFt = null;
        _perfOatC = null;
        _perfMapInHg = null;
        _perfRpm = null;
        _perfLoadPct = null;
    }
}
