using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every DA40 calculator write is UNIQUE and built in the INVARIANT culture.
///
/// ⚠️ UNIQUE: MobiFlight drops a calculator string identical to the one before it, so setting
/// a control to the value MSFSBA last sent — after the cockpit, a failure or the aeroplane
/// itself had moved it — was silently thrown away (engine master, avionics master, fuel pump,
/// flaps, lights, the altimeter).
///
/// ⚠️ INVARIANT: a negative number interpolated under sv-SE and similar cultures is written
/// with U+2212 MINUS SIGN, and a decimal under de-DE with a comma, both of which the RPN
/// parser rejects — a descent rate typed into the GFC 700 did nothing on those machines.
/// </summary>
public class CowsDA40CalcWriteTests
{
    private static string[] Sources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Directory.GetFiles(Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft", "DA40"), "*.cs");
    }

    [Fact]
    public void NoDA40WriteCanBeCoalescedWithTheOneBeforeIt()
    {
        foreach (string file in Sources())
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\.ExecuteCalculatorCode\("))
                Assert.Fail($"{Path.GetFileName(file)}: plain ExecuteCalculatorCode at offset {m.Index}");
    }

    [Fact]
    public void NoDA40WriteFormatsANumberInThePilotsCulture()
    {
        foreach (string file in Sources())
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"ExecuteCalculatorCodeUnique\(\s*\$"""))
                Assert.Fail($"{Path.GetFileName(file)}: interpolated calculator write without the invariant culture at offset {m.Index}");
        }
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void ANegativeDecimalIsWrittenTheWayRpnReadsIt(string culture)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            int fpm = -500;
            double inHg = 29.92;
            Assert.Equal("-500 29.92", FormattableString.Invariant($"{fpm} {inHg:0.00}"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }
}
