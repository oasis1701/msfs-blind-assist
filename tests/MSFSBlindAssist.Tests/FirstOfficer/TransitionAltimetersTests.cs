using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.Settings;
using Xunit;
using X = MSFSBlindAssist.FirstOfficer.TransitionCrossingDetector.Crossing;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The "Automatically set altimeters at transition altitude and level" setting (FO-23):
/// on, the First Officer sets the altimeters at each crossing; off, it never touches them
/// and speaks a reminder instead. The phase monitors need a ScreenReaderAnnouncer, which has
/// no test constructor, so the decision is pinned through its pure helper and each monitor's
/// wiring by a source scan.
/// </summary>
public class TransitionAltimetersTests
{
    [Fact]
    public void Setting_DefaultsOn_SoNothingChangesForPilotsWhoNeverTouchIt()
        => Assert.True(new UserSettings().FOAutoAltimetersEnabled);

    [Theory]
    [InlineData(X.ClimbToStd)]
    [InlineData(X.DescendToQnh)]
    [InlineData(X.None)]
    public void AutomationOn_NoReminder_TheFirstOfficerSetsTheAltimeters(X crossing)
        => Assert.Null(TransitionAltimeters.ReminderInstead(crossing, autoAltimetersEnabled: true));

    [Fact]
    public void AutomationOff_ClimbingThroughTheTransitionAltitude_AsksForStandard()
        => Assert.Equal("Transition altitude. Set standard altimeters.",
            TransitionAltimeters.ReminderInstead(X.ClimbToStd, autoAltimetersEnabled: false));

    [Fact]
    public void AutomationOff_DescendingThroughTheTransitionLevel_AsksForLocalPressure()
        => Assert.Equal("Transition level. Set local altimeter pressure now.",
            TransitionAltimeters.ReminderInstead(X.DescendToQnh, autoAltimetersEnabled: false));

    [Fact]
    public void AutomationOff_NoCrossing_SaysNothing()
        => Assert.Null(TransitionAltimeters.ReminderInstead(X.None, autoAltimetersEnabled: false));

    [Fact]
    public void NoPlanLoaded_AutomationOn_OffersSimBriefForAutomaticChanges()
        => Assert.Equal(
            "Passing one eight thousand. No transition altitude loaded — set standard altimeters as required. "
            + "Load SimBrief in the First Officer window for automatic altimeter changes.",
            TransitionAltimeters.NoTransitionLoadedReminder(autoAltimetersEnabled: true));

    [Fact]
    public void NoPlanLoaded_AutomationOff_NeverPromisesAutomaticChanges()
        => Assert.Equal(
            "Passing one eight thousand. No transition altitude loaded — set standard altimeters as required.",
            TransitionAltimeters.NoTransitionLoadedReminder(autoAltimetersEnabled: false));

    /// <summary>The MD-11 is announce-only at the transition level either way, so the setting
    /// must not change what it says there.</summary>
    [Fact]
    public void Md11TransitionLevel_SaysTheSameAsTheReminder()
        => Assert.Equal(Md11FoFlightPhaseMonitor.TransitionLevelSentence,
            TransitionAltimeters.ReminderInstead(X.DescendToQnh, autoAltimetersEnabled: false));

    /// <summary>Every First Officer phase monitor reads the setting (beyond declaring it) and
    /// speaks the no-plan reminder through the shared helper, so the setting reaches all nine
    /// aircraft and any new one. Fails when a monitor declares the property but never reads it.</summary>
    [Fact]
    public void EveryPhaseMonitor_GatesItsAltimeterWritesOnTheSetting()
    {
        var monitorTypes = typeof(IFoPhaseMonitor).Assembly.GetTypes()
            .Where(t => typeof(IFoPhaseMonitor).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToList();
        string[] files = Directory.GetFiles(FirstOfficerSourceDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @":\s*IFoPhaseMonitor\b"))
            .ToArray();

        Assert.Equal(9, monitorTypes.Count);
        Assert.Equal(monitorTypes.Count, files.Length);
        foreach (string file in files)
        {
            string src = StripComments(File.ReadAllText(file));
            string name = Path.GetFileName(file);
            Assert.True(Regex.Matches(src, @"\bAutoAltimetersEnabled\b").Count >= 3,
                $"{name} must read AutoAltimetersEnabled at its transition crossings and its no-plan reminder.");
            Assert.Contains("TransitionAltimeters.NoTransitionLoadedReminder(AutoAltimetersEnabled)", src);
            Assert.DoesNotContain("Load SimBrief in the First Officer window", src);
        }
    }

    private static string StripComments(string src)
        => Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", "");

    private static string FirstOfficerSourceDir([CallerFilePath] string p = "")
    {
        string dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(p)!, "..", "..", "..",
            "MSFSBlindAssist", "FirstOfficer"));
        Assert.True(Directory.Exists(dir), dir + " was not found. If the folder moved, re-point this path.");
        return dir;
    }
}
