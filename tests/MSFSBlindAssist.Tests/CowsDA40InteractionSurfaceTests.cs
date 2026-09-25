using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// EVERY THING A SIGHTED PILOT CAN TOUCH, checked against what MSFSBA exposes.
///
/// ⚠️ THIS EXISTS BECAUSE GAPS WERE BEING FOUND BY ACCIDENT. The pattern of this whole
/// project has been a pilot noticing something missing — the TO/GA button, the Hobbs meter,
/// the barometric knob, the aeroplane's own Tips page — and each was found by somebody
/// stumbling over it rather than by looking. That does not scale and it is not fair on the
/// person flying.
///
/// The aeroplane declares its own interaction surface. Every clickable thing in the cockpit
/// is a &lt;Component ID="..."&gt; in the model's interaction XML, and there are sixty-one of
/// them across the two variants. That list is FINITE, it is IN THE PACKAGE, and it changes
/// only when COWS ship an update — so it can be enumerated and compared, and a control added
/// by a future update shows up here as a failing test instead of as a surprise in the air.
///
/// The table below names, for every component, either the MSFSBA control that covers it or
/// the reason it is deliberately not exposed. Writing it out was the sweep; keeping it right
/// is what stops the next one being needed.
///
/// ⚠️ IT SKIPS ITSELF WHEN THE PACKAGE IS NOT INSTALLED. This reads the live aircraft out of
/// the Community folder, so it cannot run on a machine without it — and a test that fails
/// for being on the wrong computer teaches people to ignore failures.
/// </summary>
public class CowsDA40InteractionSurfaceTests
{
    /// <summary>
    /// Component ID → how it is reached in MSFSBA, or why it is not.
    ///
    /// A value starting with "-" is a deliberate omission and carries its reason.
    /// </summary>
    private static readonly Dictionary<string, string> Surface = new(StringComparer.Ordinal)
    {
        // ---------------- engine, fuel and the FADEC ----------------
        ["ENGINE_pedestal"] = "Power and Levers: power lever, engine master (NG); throttle (XLS)",
        ["ECU_TEST1"] = "ECU: test, and the 10-second reset-and-charge hold",
        ["ECU_VOTER1"] = "ECU: voter switch",
        ["MASTER_COVER1"] = "Engine Start: engine master guard",
        // One component ID, two aeroplanes. On the NG it is the start key; on the XLS the
        // same component is the five-position ignition key whose START detent cranks.
        ["STARTER"] = "Engine Start: start key (NG). Magnetos: ignition key and starter (XLS)",
        ["FUEL"] = "Fuel System: fuel pumps (NG); electric fuel pump (XLS)",
        ["FUEL_SELECTOR"] = "Fuel System: fuel valve (NG); left/right/off tank selector (XLS)",
        ["FUEL_WIRE"] = "Fuel System: break the safety wire",
        ["ALTAIR"] = "Ice and Pitot: alternate air",

        // ---------------- electrical ----------------
        ["ELECTRICAL"] = "Electrical: avionics and essential bus",
        ["EMERGENCY_BATT"] = "Electrical: emergency battery",
        ["EMERGENCY_switch"] = "Electrical: emergency battery switch",
        ["EMERGENCY_Cover_switch"] = "Electrical: emergency battery guard",
        ["ESSBUS_LIGHT"] = "Annunciators: essential bus lamp",

        // ---------------- lighting ----------------
        ["LIGHT_LDG"] = "Lighting Switches: landing light",
        ["LIGHT_TAXI"] = "Lighting Switches: taxi light",
        ["LIGHT_POS"] = "Lighting Switches: position lights",
        ["LIGHT_STROBE"] = "Lighting Switches: strobes",
        ["LIGHTING"] = "Lighting Switches: the group",
        ["LIGHTING_Switches_Overhead"] = "Lighting Switches: cabin lights",
        ["Checklist_Lights"] = "Lighting Switches: instrument and flood brightness",
        ["light_flaps_1"] = "Annunciators: flap position lamps",
        ["light_flaps_2"] = "Annunciators: flap position lamps",
        ["light_flaps_3"] = "Annunciators: flap position lamps",
        ["LIGHTING_Glareshield_Emissive"] =
            "- emissive material only. It is how the glareshield LOOKS lit, not a control.",
        ["SHCT_FLOOD"] =
            "- a shortcut clickspot on the airspeed needle that toggles the cabin lights. " +
            "The three cabin lights have their own switches; a second way in would be the " +
            "duplicate this aeroplane's panels are meant not to have.",

        // ---------------- flight controls and handling ----------------
        ["HANDLING_Flaps"] = "Flaps: selector",
        ["HANDLING_Wheel_ElevatorTrim_Pitch"] = "Elevator Trim: trim wheel",
        ["HANDLING_Yokes"] = "Flight Controls: surface positions",
        ["HANDLING_RudderPedals"] = "Flight Controls and Brakes: rudder and toe brakes",
        ["LANDING_GEAR_Switch_ParkingBrake"] = "Brakes: parking brake",
        ["AP_DISC"] = "Elevator Trim: autopilot disconnect",

        // ---------------- instruments ----------------
        ["Altimeter"] = "Standby Instruments and Ctrl+B: both subscales",
        ["Airspeed"] = "readout: indicated airspeed",
        ["Gyro"] = "Standby Instruments: the standby horizon",
        ["INSTRUMENT_AttitudeIndicator_Knob_1"] = "Standby Instruments: cage the gyro",
        ["INSTRUMENTS"] = "Standby Instruments: the magnetic compass",
        ["Backup_fix"] = "Standby Instruments: G1000 reversion",
        ["ALT_STATIC"] = "Ice and Pitot: alternate static valve",
        ["DEICE"] = "Ice and Pitot: pitot heat",
        ["SAFETY"] = "ELT",

        // ---------------- the G1000 ----------------
        ["AS1000_PFD"] = "the PFD window (Alt+P), with the bezel on the keyboard",
        ["AS1000_MFD"] = "the MFD window (Alt+M), with the bezel on the keyboard",
        ["CRS_fix_1"] = "GFC 700: course",
        ["CRS_fix_2"] = "GFC 700: course",
        ["AS1000_MID"] =
            "- the two display BRIGHTNESS knobs. Screen brightness has no operational " +
            "effect for a pilot who cannot see the screen, and the reversion switch beside " +
            "them (G1000_REV_FORCE) IS exposed, on Standby Instruments.",

        // ---------------- cabin ----------------
        ["DOORS"] = "Doors and Windows: the group",
        ["CanopyC"] = "Doors and Windows: Canopy",
        ["CanopyRC"] = "Doors and Windows: Rear Canopy",
        ["stormLC"] = "Doors and Windows: Left Window",
        ["stormRC"] = "Doors and Windows: Right Window",
        ["PASSENGER"] = "Cabin Heat and Vent",
        ["PILOT"] = "Seating and Payload: pilot figure",
        ["CO_PILOT"] = "Seating and Payload: copilot figure",
        ["JACK"] = "Audio: headset jacks",
        ["PANEL_SHAKE"] = "Aircraft Options via the MFD Engine page menu: panel shake",
        ["DA40_Breakers"] = "Circuit Breakers: all 34",

        // ---------------- XLS only, and deliberately not built yet ----------------
        ["ENGINE_Lever_Propeller_1"] = "Power and Levers: propeller lever (XLS)",
        ["ENGINE_Lever_Mixture_1"] = "Power and Levers: mixture (XLS)",
        ["ALT_Master"] = "Electrical: Alternator Master (XLS)",
        ["Bat_Master"] = "Electrical: Battery Master (XLS)"
    };

    private static string? PackageRoot()
    {
        string[] roots =
        {
            @"C:\Users\franc\AppData\Local\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community\cows-da40",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft Flight Simulator 2024", "Packages", "Community", "cows-da40"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft Flight Simulator", "Packages", "Community", "cows-da40")
        };

        return roots.FirstOrDefault(Directory.Exists);
    }

    /// <summary>Every &lt;Component ID&gt; the aeroplane declares, across both variants.</summary>
    private static List<string> DeclaredComponents(string root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (!name.EndsWith("_IN.xml", StringComparison.Ordinal) &&
                !name.EndsWith("_Inputs.xml", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"<Component\s+ID=""([^""]+)"""))
            {
                string id = m.Groups[1].Value;
                // The template placeholder, not a real component.
                if (id.StartsWith("#", StringComparison.Ordinal)) continue;
                found.Add(id);
            }
        }

        return found.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void EveryThingThePilotCanTouchIsAccountedFor()
    {
        string? root = PackageRoot();
        if (root is null) return;   // aircraft not installed on this machine

        var declared = DeclaredComponents(root);
        Assert.True(declared.Count > 40,
            "Only " + declared.Count + " components found — the harvest has drifted, not the aircraft.");

        var unaccounted = declared.Where(c => !Surface.ContainsKey(c)).ToList();

        Assert.True(unaccounted.Count == 0,
            "The aeroplane has controls MSFSBA has never been told about. Each one is " +
            "something a sighted pilot can touch and a blind pilot cannot. Add it to the " +
            "aircraft, or add it to this table with the reason it is not exposed: " +
            string.Join(", ", unaccounted));
    }

    [Fact]
    public void TheTableDoesNotNameAControlTheAeroplaneNoLongerHas()
    {
        // The other direction: an entry for a component COWS removed is a comment that has
        // quietly become fiction, and this table is only worth having while it is true.
        string? root = PackageRoot();
        if (root is null) return;

        var declared = DeclaredComponents(root).ToHashSet(StringComparer.Ordinal);
        var stale = Surface.Keys.Where(k => !declared.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            "these are in the table but no longer in the aircraft — " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryDeliberateOmissionCarriesItsReason()
    {
        // A "-" entry with no explanation is the same as no entry: the next person cannot
        // tell a decision from an oversight.
        var unexplained = Surface
            .Where(kv => kv.Value.StartsWith("-", StringComparison.Ordinal) && kv.Value.Length < 40)
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(unexplained.Count == 0,
            "omitted with no reason given — " + string.Join(", ", unexplained));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryDoorAnnouncedIsARealControl(DA40Variant variant)
    {
        // ⚠️ A door must never announce a PERCENTAGE. EXIT OPEN sweeps as the canopy
        // swings, so the settle speaks the state it comes to rest in - and that only works
        // if every name in the table is a control the announcer will actually see.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (string key in CowsDA40Definition.DoorAnnounceKeys)
        {
            Assert.True(vars.ContainsKey(key), key + " is announced but not defined");

            var v = vars[key];
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced, key + " would never reach the settle");
        }
    }

    [Fact]
    public void TheDoorToggleIsUniqueOrClosingSilentlyFails()
    {
        // Opening a door and then closing it is the SAME calculator string twice running,
        // and MobiFlight coalesces byte-identical consecutive commands - so the close was
        // dropped and the door stayed open. Doing another door in between made it work,
        // which is exactly the shape of that bug.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "MSFSBlindAssist",
            "Aircraft", "DA40", "CowsDA40Definition.Doors.cs"));

        Assert.Contains("ExecuteCalculatorCodeUnique($\"{exit} (>K:TOGGLE_AIRCRAFT_EXIT)\")",
            src, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryLampWatchedHasBothHalvesDefined(DA40Variant variant)
    {
        // The switch/circuit comparison is only meaningful if both halves exist and are
        // both in the cache — a null on either side silently disables the whole check.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var (sw, circuit, _) in CowsDA40Definition.LampPairKeys)
        {
            Assert.True(vars.ContainsKey(sw), sw + " is watched but not defined");
            Assert.True(vars.ContainsKey(circuit), circuit + " is watched but not defined");

            foreach (string key in new[] { sw, circuit })
            {
                var v = vars[key];
                Assert.True(v.UpdateFrequency == MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous
                            && v.IsAnnounced && !v.ExcludeFromBatch,
                    key + " is compared from the cache but never reaches it");
            }
        }
    }
}
