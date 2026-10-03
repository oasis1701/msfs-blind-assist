using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A BREAKER IS NAMED BY ITS PLACARD, VERBATIM, AND THE PLACARD IS READ FROM THE PACKAGE.
///
/// Expanding the abbreviations was wrong on four XLS breakers, not just wordy: the row MSFSBA
/// called "Autopilot Breaker" pops alternator protection. These tests read each model's own
/// CircuitBreakers.xml (the TOOLTIPID is the placard) and Failures.xml (which breaker each
/// trip pops), so a COWS update that renames or rewires a breaker fails here instead of
/// reaching a pilot. Skips on a machine without the aircraft.
/// </summary>
public class CowsDA40BreakerPlacardTests
{
    private static string ModelDir(string root, DA40Variant v)
        => Path.Combine(root, "SimObjects", "Airplanes", v == DA40Variant.NG ? "COWS_DA40NG" : "COWS_DA40XLS");

    private static string Strip(string xml) => Regex.Replace(xml, "<!--.*?-->", "", RegexOptions.Singleline);

    /// <summary>L:CB_* → TOOLTIPID, from the variant's own breaker file.</summary>
    private static Dictionary<string, string> PackagePlacards(string root, DA40Variant v)
    {
        string file = Directory.EnumerateFiles(ModelDir(root, v), "*CircuitBreakers.xml", SearchOption.AllDirectories).First();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match c in Regex.Matches(Strip(File.ReadAllText(file)),
                     @"<Component ID=""CB_[A-Z0-9_]+""(.*?)</Component>", RegexOptions.Singleline))
        {
            var tip = Regex.Match(c.Groups[1].Value, @"<TOOLTIPID>(.*?)</TOOLTIPID>");
            var lvar = Regex.Match(c.Groups[1].Value, @"L:(CB_[A-Z0-9_]+)");
            if (tip.Success && lvar.Success) map[lvar.Groups[1].Value] = tip.Groups[1].Value.Trim();
        }
        return map;
    }

    /// <summary>FAILURES_CB_* → the L:CB_* it pops, from the variant's own failure logic.</summary>
    private static Dictionary<string, string> PackagePops(string root, DA40Variant v)
    {
        string file = Directory.EnumerateFiles(ModelDir(root, v), "*Failures.xml", SearchOption.AllDirectories).First();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(Strip(File.ReadAllText(file)),
                     @"\(L:(FAILURES_CB_[A-Z0-9_]+)\)(.{0,300}?)1 \(&gt;L:(CB_[A-Z0-9_]+)\)", RegexOptions.Singleline))
            map[m.Groups[1].Value] = m.Groups[3].Value;
        return map;
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPlacardMatchesTheCockpit(DA40Variant variant)
    {
        string? root = CowsDA40PackagePresenceTests.PackageRoot();
        if (root is null) return;

        var package = PackagePlacards(root, variant);
        Assert.Equal(34, package.Count);
        foreach (var (lvar, placard) in package)
            Assert.True(DA40BreakerPlacards.AllPlacards.TryGetValue(lvar, out var ours) && ours == placard,
                $"{variant} {lvar}: cockpit says \"{placard}\", MSFSBA says \"{ours}\"");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryTripPopsTheBreakerTheModelSaysItPops(DA40Variant variant)
    {
        string? root = CowsDA40PackagePresenceTests.PackageRoot();
        if (root is null) return;

        var package = PackagePops(root, variant);
        var ours = DA40BreakerPlacards.PopsFor(variant == DA40Variant.NG);
        Assert.Equal(package.OrderBy(k => k.Key), ours.OrderBy(k => k.Key));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryBreakerRowIsItsPlacardAndEveryTripRowItsBreakers(DA40Variant variant)
    {
        bool isNg = variant == DA40Variant.NG;
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var (key, def) in vars)
        {
            if (key.StartsWith("DA40_CB_", StringComparison.Ordinal) && def.Name.StartsWith("CB_", StringComparison.Ordinal)
                && def.DisplayName.EndsWith(" Breaker", StringComparison.Ordinal))
                Assert.Equal(DA40BreakerPlacards.For(def.Name) + " Breaker", def.DisplayName);

            if (def.Name.StartsWith("FAILURES_CB_", StringComparison.Ordinal))
                Assert.Equal(DA40BreakerPlacards.TripLabel(def.Name, isNg) + " Breaker Trip", def.DisplayName);
        }
    }

    [Fact]
    public void TheXlsNamesAreTheCockpitsNotTheRealAfms()
    {
        // The four that were WRONG, not merely expanded. Pinned by value so a well-meant
        // "tidy" back to the AFM wording has to get past this first.
        var xls = new CowsDA40Definition(DA40Variant.XLS).GetVariables();
        string Name(string lvar) => xls.Values.First(d => d.Name == lvar).DisplayName;

        Assert.Equal("ALTPROT Breaker", Name("CB_APT"));   // was "Autopilot Breaker"
        Assert.Equal("ALTCONT Breaker", Name("CB_ACN"));   // was "Annunciator Panel Breaker"
        Assert.Equal("CDUFAN Breaker", Name("CB_FAN"));    // was "Fan and Outside Air Temperature Breaker"
        Assert.Equal("TAS Breaker", Name("CB_TAS"));       // was "Turn and Bank Indicator Breaker"
        Assert.Equal("AFCS Breaker", Name("CB_AFC"));      // the actual autopilot
    }

    /// <summary>
    /// ⚠️ A FAILURE ROW MUST BE ABLE TO DO SOMETHING. Every FAILURES_CB_* a variant binds must
    /// either pop a breaker or be read outside the failure picker (an overload, which adds
    /// draw to a bus). The NG's ALT, ESS TIE and MAIN TIE and the XLS's ADF and AV BUS are set
    /// by the random picker and read by nothing — a row would announce a failure the aeroplane
    /// does not have, which the package-presence test cannot see because the variable EXISTS.
    /// </summary>
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoBreakerTripRowIsInert(DA40Variant variant)
    {
        string? root = CowsDA40PackagePresenceTests.PackageRoot();
        if (root is null) return;

        var pops = PackagePops(root, variant);
        var elsewhere = Directory.EnumerateFiles(ModelDir(root, variant), "*.xml", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("Failures.xml", StringComparison.Ordinal))
            .Select(f => Strip(File.ReadAllText(f)))
            .ToList();

        var inert = new CowsDA40Definition(variant).GetVariables().Values
            .Select(d => d.Name)
            .Where(n => n.StartsWith("FAILURES_CB_", StringComparison.Ordinal))
            .Where(n => !pops.ContainsKey(n) && !elsewhere.Any(t => Regex.IsMatch(t, @"L:" + n + @"\b")))
            .ToList();

        Assert.True(inert.Count == 0, variant + " offers failures nothing in the aeroplane acts on: " + string.Join(", ", inert));
    }
}
