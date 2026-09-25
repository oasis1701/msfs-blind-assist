using System;
using System.Collections.Generic;
using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// MSFSBA answers in the units the PILOT set on the G1000, not in its own.
///
/// A pilot who switches the MFD's Aux System Setup page to kilograms has said what they
/// want to hear, and every panel carried on telling them pounds. Same for temperature,
/// distance and speed, altitude and vertical speed, and fuel. That is the aeroplane
/// disagreeing with itself, and on a display a blind pilot cannot cross-check.
///
/// WHERE THE SETTINGS LIVE, and why they arrive the way they do. They are not SimVars and
/// not L:vars: they are Working Title user settings held by the instrument's own
/// <c>settingSaveManager</c> and persisted into the aeroplane's profile
/// (<c>DA40.profile_1</c>). Nothing outside the instrument can read them, so they come down
/// the Coherent socket as a row on the ordinary scrape — no extra round trip, and no new
/// connection. ⚠️ BOTH DISPLAYS CARRY THE SAME SIX (verified live: the settings are shared
/// over the instrument bus), which is what makes this work with no display window open at
/// all — the always-on CAS monitor holds a socket to the PFD, and the PFD's copy is as good
/// as the MFD's.
///
/// UNTIL A SCRAPE ARRIVES the defaults below stand, and they are the G1000's own factory
/// defaults — nautical, feet, celsius, pounds, gallons — so a pilot who has changed nothing
/// hears exactly what they heard before.
///
/// ⚠️ NAV ANGLE IS DELIBERATELY NOT APPLIED, and this is a decision rather than an
/// omission. Magnetic-to-true is not a unit conversion; it is a change of DATUM that needs
/// the local variation, and — far more importantly — the heading bug and the course are
/// WRITTEN by MSFSBA's panels through <c>AUTOPILOT HEADING LOCK DIR</c> and
/// <c>NAV OBS</c>, both of which are magnetic. Displaying a true bearing in a field the
/// pilot then types a number into would put the aeroplane on a heading several degrees off
/// the one they asked for, silently. The setting is READ and reported in the scan so the
/// pilot knows what the G1000 is showing; nothing else changes.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>The row the agent emits. Change one and change the other.</summary>
    private const string UnitsRowPrefix = "Display units: ";

    private string _unitDistance = "nautical";
    private string _unitAltitude = "feet";
    private string _unitTemperature = "celsius";
    private string _unitWeight = "pounds";
    private string _unitFuel = "gallons";
    private string _unitBearings = "magnetic";
    private string _unitPressure = "inches";

    /// <summary>What the G1000 is set to, for the tests and for anything that wants to ask.</summary>
    public string DisplayUnitWeight => _unitWeight;
    public string DisplayUnitTemperature => _unitTemperature;
    public string DisplayUnitDistance => _unitDistance;
    public string DisplayUnitAltitude => _unitAltitude;
    public string DisplayUnitFuel => _unitFuel;
    public string DisplayUnitBearings => _unitBearings;
    public string DisplayUnitPressure => _unitPressure;

    /// <summary>True when the pilot has the G1000 set to hectopascals.</summary>
    public bool PressureInHectopascals => _unitPressure == "hectopascals";

    /// <summary>
    /// Picks the units off a display scrape. Called from the CAS monitor and from both
    /// display windows, because whichever of them holds a socket is the one that can see
    /// them — and only one of them can hold each socket at a time.
    /// </summary>
    public void NoteDisplayUnits(IEnumerable<string> rows)
    {
        foreach (string row in rows)
        {
            if (!row.StartsWith(UnitsRowPrefix, StringComparison.Ordinal)) continue;

            string before = _unitWeight + _unitTemperature + _unitDistance +
                            _unitAltitude + _unitFuel + _unitBearings + _unitPressure;

            foreach (string pair in row.Substring(UnitsRowPrefix.Length).Split(','))
            {
                var bits = pair.Trim().Split(' ');
                if (bits.Length < 2) continue;

                // The value can be two words ("imperial gallons"), so it is everything
                // after the dimension rather than the next token.
                string value = string.Join(" ", bits, 1, bits.Length - 1).Trim().ToLowerInvariant();
                if (value.Length == 0) continue;

                switch (bits[0].Trim().ToLowerInvariant())
                {
                    case "bearings": _unitBearings = value; break;
                    case "distance": _unitDistance = value; break;
                    case "altitude": _unitAltitude = value; break;
                    case "temperature": _unitTemperature = value; break;
                    case "weight": _unitWeight = value; break;
                    case "fuel": _unitFuel = value; break;
                    case "pressure": _unitPressure = value; break;
                }
            }

            string after = _unitWeight + _unitTemperature + _unitDistance +
                           _unitAltitude + _unitFuel + _unitBearings + _unitPressure;

            // NOT ANNOUNCED. The pilot has just changed it on the MFD and the display
            // window read the field back as they did — saying it again from a background
            // monitor is the second announcement of one action. It is logged, and the scan
            // carries the current setting for anyone who wants to check.
            if (before != after)
            {
                Log.Debug("DA40", $"Display units now: {row.Substring(UnitsRowPrefix.Length)}");
            }
            return;
        }
    }

    /// <summary>
    /// Renders a measured value in the units the pilot chose, or returns false when this
    /// variable is not one of the measured kinds and the caller should format it its own
    /// way.
    ///
    /// The DIMENSION comes from the variable's own declared unit, never from its name: a
    /// variable measured in <c>celsius</c> is a temperature whatever it is called, and a
    /// name-matching rule would have to be extended for every new readout and would be
    /// wrong the first time somebody wrote OIL_TEMP_SWITCH.
    /// </summary>
    private bool TryUnitText(string? nativeUnits, double value, string? format, out string text)
    {
        text = "";
        if (string.IsNullOrWhiteSpace(nativeUnits)) return false;

        double converted = value;
        string spoken;

        switch (nativeUnits)
        {
            case "celsius":
                if (_unitTemperature == "fahrenheit") { converted = value * 9.0 / 5.0 + 32.0; spoken = "degrees fahrenheit"; }
                else spoken = "degrees celsius";
                break;

            // The XLS's cylinder gauges are drawn in Fahrenheit; the same setting decides.
            case "fahrenheit":
                if (_unitTemperature == "fahrenheit") spoken = "degrees fahrenheit";
                else { converted = (value - 32.0) * 5.0 / 9.0; spoken = "degrees celsius"; }
                break;

            case "pounds":
                if (_unitWeight == "kilograms") { converted = value * KgPerLb; spoken = "kilograms"; }
                else spoken = "pounds";
                break;

            case "gallons":
            case "gallons per hour":
            {
                // "per hour" rides along so one table serves quantity and flow.
                string per = nativeUnits.EndsWith("per hour", StringComparison.Ordinal) ? " per hour" : "";
                switch (_unitFuel)
                {
                    case "liters":
                    case "litres":
                        converted = value * LitresPerGallon; spoken = "litres" + per; break;
                    case "imperial gallons":
                        converted = value * ImperialGallonsPerGallon; spoken = "imperial gallons" + per; break;
                    default:
                        // Anything else - the Garmin also offers weight-based fuel - is
                        // left alone rather than guessed at: converting gallons to pounds
                        // needs the fuel's density, and inventing one would put a wrong
                        // number in front of a pilot planning a flight on it.
                        spoken = "gallons" + per; break;
                }
                break;
            }

            case "knots":
                if (_unitDistance == "metric") { converted = value * KmhPerKnot; spoken = "kilometres per hour"; }
                else spoken = "knots";
                break;

            case "feet":
                if (_unitAltitude == "meters" || _unitAltitude == "metres")
                { converted = value * MetresPerFoot; spoken = "metres"; }
                else spoken = "feet";
                break;

            case "feet per minute":
                if (_unitAltitude == "meters" || _unitAltitude == "metres")
                { converted = value * MetresPerFoot; spoken = "metres per minute"; }
                else spoken = "feet per minute";
                break;

            case "feet per second":
                if (_unitAltitude == "meters" || _unitAltitude == "metres")
                { converted = value * MetresPerFoot; spoken = "metres per second"; }
                else spoken = "feet per second";
                break;

            default:
                return false;
        }

        text = converted.ToString(format, CultureInfo.CurrentCulture) + " " + spoken;
        return true;
    }

    /// <summary>
    /// The measured conversions, owned here for the whole partial class. Fuel.cs and
    /// Hotkeys.cs each carried their own copy of the gallons-to-litres factor before this,
    /// which is one number that has to be right in two places.
    /// </summary>
    private const double LitresPerGallon = 3.785411784;
    private const double ImperialGallonsPerGallon = 0.83267418;
    private const double KmhPerKnot = 1.852;
    private const double MetresPerFoot = 0.3048;
}
