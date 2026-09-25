using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which hotkey actions the DA40 answers itself, and that no two of them share one answer.
///
/// ⚠️ THE FAULT THIS EXISTS FOR: ReadFuelQuantity and ReadFuelInfo were written as two
/// labels on ONE case, so F and Shift+F said the same sentence and one of the two keys was
/// wasted. Nothing in the compiler minds that — a shared case is legal C# — and nothing in
/// the cockpit shows it either until a pilot presses both and notices.
///
/// The guide is checked against the code for the same reason: it advertised "Read Fuel On
/// Board (pounds)" and "(kilograms)" on an aeroplane that answers in gallons and litres,
/// which is the airliner wording nobody had revisited.
/// </summary>
public class CowsDA40HotkeyCoverageTests
{
    private static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, name);
        Assert.True(File.Exists(path), "Not found: " + path);
        return File.ReadAllText(path);
    }

    private static string Hotkeys() =>
        Source(Path.Combine("MSFSBlindAssist", "Aircraft", "DA40", "CowsDA40Definition.Hotkeys.cs"));

    private static string Guide() =>
        Source(Path.Combine("MSFSBlindAssist", "HotkeyGuides", "COWS_DA40_Hotkeys.txt"));

    [Fact]
    public void NoTwoHotkeyActionsShareOneAnswer()
    {
        // A case label followed immediately by another case label is two keys with one
        // answer. Legal, occasionally deliberate — and on this aeroplane it was the bug.
        var doubled = Regex.Matches(Hotkeys(),
                @"case HotkeyAction\.(\w+):\s*\r?\n\s*case HotkeyAction\.(\w+):")
            .Select(m => m.Groups[1].Value + " + " + m.Groups[2].Value)
            .ToList();

        Assert.True(doubled.Count == 0,
            "These hotkey actions share a single answer, so one of each pair is a wasted " +
            "key: " + string.Join(", ", doubled));
    }

    [Theory]
    [InlineData("ReadFuelQuantity")]
    [InlineData("ReadFuelInfo")]
    [InlineData("ReadDisplayLowerECAM")]
    [InlineData("ReadDisplayISIS")]
    [InlineData("ReadEngineRpm")]
    [InlineData("ReadEnginePower")]
    [InlineData("ReadEngineTemps")]
    public void TheDA40AnswersThisKeyItself(string action)
    {
        // The base definition implements NONE of these — it does a variable-map lookup and
        // nothing else — so an action the DA40 does not name here simply does nothing.
        Assert.Contains("case HotkeyAction." + action + ":", Hotkeys(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFuelKeysAnswerDifferentQuestions()
    {
        string src = Hotkeys();

        // Quantity answers the TANKS; the second key answers how long that lasts.
        Assert.Contains("endurance", src, StringComparison.Ordinal);
        Assert.Contains("tank difference", src, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuideDoesNotAdvertiseAirlinerFuelUnits()
    {
        // The DA40 is fuelled by volume. "Fuel On Board (pounds)" was inherited wording.
        string guide = Guide();
        Assert.DoesNotContain("Read Fuel On Board (pounds)", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("Read Fuel On Board (kilograms)", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuideDocumentsEveryDisplayKeyTheAircraftAnswers()
    {
        string guide = Guide();

        // Alt+S and Alt+I are the two keys this aeroplane repurposed; a key that exists and
        // is not in the guide is a key nobody will ever press.
        Assert.Contains("Alt+S", guide, StringComparison.Ordinal);
        Assert.Contains("Alt+I", guide, StringComparison.Ordinal);
        Assert.Contains("Alt+M", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuideDocumentsTheVSpeedKeysAndHowToSetTakeOffTrim()
    {
        // ⚠️ FIVE V-SPEED KEYS EXISTED AND THE GUIDE NAMED NONE OF THEM. They carry Airbus
        // names and DA40 numbers, so a pilot reading an airliner guide would never guess
        // Shift+5 is rotate speed - and a key nobody can find is a key that does not exist.
        string guide = Guide();

        foreach (string key in new[] { "Shift+2", "Shift+3", "Shift+4", "Shift+5", "Shift+6" })
        {
            Assert.Contains(key, guide, StringComparison.Ordinal);
        }

        // And the question that kept being asked: what trim do I set for take-off?
        Assert.Contains("Centre Trim", guide, StringComparison.Ordinal);
        Assert.Contains("no number to calculate", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuideDocumentsTheGeneralAviationEngineKeys()
    {
        // A key that exists and is not in the guide is a key nobody will ever press. These
        // three are registered globally, so an undocumented one is also a letter silently
        // taken away from every other aircraft.
        string guide = Guide();

        Assert.Contains("Shift+O", guide, StringComparison.Ordinal);
        Assert.Contains("Engine (single values", guide, StringComparison.Ordinal);
    }
}
