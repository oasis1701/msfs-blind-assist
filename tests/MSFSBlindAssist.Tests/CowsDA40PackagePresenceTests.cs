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
/// ⚠️ EVERY L:VAR A VARIANT BINDS MUST EXIST IN THAT VARIANT'S OWN PACKAGE.
///
/// An L:var that nothing writes is not an error to SimConnect — it reads 0, for ever. So a
/// row bound to one reads as a plausible zero about a system the aeroplane has not got, and
/// a pilot scans it and is reassured. The XLS carried thirty-odd of these, all Austro
/// variables inherited from the NG: an ECU battery, a battery bus, a turbocharger, a water
/// jacket, an ESS BUS lamp. Measured live on the XLS they summed to exactly 0 while its own
/// buses read 23.4 V.
///
/// The scan is over the variant's own SimObjects folder plus the shared html_ui, and it is a
/// TEXT scan, which is complete for this package only because it ships no WASM (a WASM-only
/// variable is invisible to a text search — the trap CLAUDE.md records). It matches the WHOLE
/// name: a substring scan once passed FAILURES_FUEL_L on the XLS because FAILURES_FUEL_LEAK_L
/// contains it. Skips on a machine without the aircraft.
/// </summary>
public class CowsDA40PackagePresenceTests
{
    /// <summary>
    /// Variables the aeroplane uses but does not DEFINE, because a stock Asobo template in
    /// the simulator owns them. Each needs its reason.
    /// </summary>
    private static readonly Dictionary<string, string> SimTemplateOwned = new(StringComparer.Ordinal)
    {
        ["XMLVAR_CabinAir"] = "ASOBO_PASSENGER_Lever_Cabin_Air_Template, a stock sim template",
        ["XMLVAR_CabinHeat"] = "ASOBO_PASSENGER_Lever_Cabin_Heat_Template, a stock sim template",
    };

    internal static string? PackageRoot()
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

    private static readonly string[] TextExtensions = { ".xml", ".js", ".cfg", ".html", ".flt", ".json" };

    /// <summary>Every whole identifier in the variant's own model and the shared gauges.</summary>
    internal static HashSet<string> PackageTokens(string root, DA40Variant variant)
    {
        string folder = variant == DA40Variant.NG ? "COWS_DA40NG" : "COWS_DA40XLS";
        var dirs = new[]
        {
            Path.Combine(root, "SimObjects", "Airplanes", folder),
            Path.Combine(root, "html_ui")
        };

        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (string dir in dirs.Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                         .Where(f => TextExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"[A-Za-z_][A-Za-z0-9_]*"))
                    tokens.Add(m.Value);
            }
        }
        return tokens;
    }

    /// <summary>The L:vars a variant reads, keyed by MSFSBA key. Action controls excluded.</summary>
    private static IEnumerable<(string Key, string Base)> BoundLvars(DA40Variant variant)
    {
        foreach (var (key, def) in new CowsDA40Definition(variant).GetVariables())
        {
            if (def.Type != SimVarType.LVar) continue;
            // A button's Name is its own key: it writes through a setter, reads nothing.
            if (def.Name == key) continue;
            string baseName = def.Name.Split(':')[0];
            if (SimTemplateOwned.ContainsKey(baseName)) continue;
            yield return (key, baseName);
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryLvarTheVariantBindsExistsInItsOwnPackage(DA40Variant variant)
    {
        string? root = PackageRoot();
        if (root is null) return;   // aircraft not installed on this machine

        var tokens = PackageTokens(root, variant);
        Assert.True(tokens.Count > 1000, "Only " + tokens.Count + " tokens — the harvest has drifted.");

        var phantoms = BoundLvars(variant)
            .Where(b => !tokens.Contains(b.Base))
            .Select(b => b.Key + " (" + b.Base + ")")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(phantoms.Count == 0,
            variant + " binds " + phantoms.Count + " L:vars its own package never mentions. Each " +
            "reads 0 for ever about a system this aeroplane does not have:\n  " +
            string.Join("\n  ", phantoms));
    }

    [Fact]
    public void TheNgKeepsEveryNgOnlyVariable()
    {
        // The removal is XLS-only. And an entry the NG does not define is a stale line in a
        // list whose whole value is being true.
        var def = new CowsDA40Definition(DA40Variant.NG);
        var vars = def.GetVariables();
        var missing = CowsDA40Definition.NgOnlyVariableKeys.Where(k => !vars.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "not defined on the NG: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheXlsHasNoNgOnlyVariableOrRow()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        var vars = def.GetVariables();
        var rows = def.GetPanelControls().Values.SelectMany(r => r)
            .Concat(def.GetPanelDisplayVariables().Values.SelectMany(r => r))
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in CowsDA40Definition.NgOnlyVariableKeys)
        {
            Assert.False(vars.ContainsKey(key), key + " is still defined on the XLS");
            Assert.False(rows.Contains(key), key + " is still on an XLS panel");
        }
    }

    [Fact]
    public void TheXlsMasterReadBackNoLongerSpeaksAPhantomBatteryBus()
    {
        // With the battery bus absent its cached value stays null and the clause drops out,
        // where it used to say "battery 0.0" every time the XLS was switched on.
        Assert.Equal("main bus 24.2 volts, essential 24.1.",
            CowsDA40Definition.ComposeBusState(true, 24.2, 24.1, null));
    }

    [Fact]
    public void TheTemplateOwnedListOnlyNamesVariablesTheModelActuallyUses()
    {
        // The allow-list must not become a place to hide phantoms: every entry is a variable
        // the aeroplane's own XML never names, so the evidence is the TEMPLATE, not the name.
        string? root = PackageRoot();
        if (root is null) return;

        foreach (DA40Variant v in new[] { DA40Variant.NG, DA40Variant.XLS })
        {
            var tokens = PackageTokens(root, v);
            Assert.Contains("ASOBO_PASSENGER_Lever_Cabin_Air_Template", tokens);
            Assert.Contains("ASOBO_PASSENGER_Lever_Cabin_Heat_Template", tokens);
        }
    }
}
