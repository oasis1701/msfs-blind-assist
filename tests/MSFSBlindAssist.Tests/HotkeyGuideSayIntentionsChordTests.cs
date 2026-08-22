// The SayIntentions taxi-route chord is documented in EIGHT shipped guide files
// (MSFSBlindAssist\HotkeyGuides\*.txt) as well as in the code that registers it. Those
// guides are the ONLY place a blind pilot can look the key up — the app reads them
// verbatim into the Hotkey List window — so a chord change that lands in HotkeyManager
// and in six of the eight guides leaves two aircraft documenting a key that no longer
// does anything, with nothing to reveal which.
//
// That is exactly the failure this file pins: the guides are prose, nothing else
// compiles against them, and "did I update all eight?" is not answerable by the build.
//
// Stated POSITIVELY — "the entry exists exactly once and carries the registered chord" —
// rather than as a ban on the previous chord. A test naming the retired chord would be
// the one place in the repo it still appeared, which defeats the grep the next person
// doing this will run, and the positive form catches strictly more anyway: a guide that
// gained the new line WITHOUT losing the old one has two description lines, and fails.
//
// It reads the guides from the TEST output directory, which is where the main project's
// CopyToOutputDirectory="Always" items land through the ProjectReference — the same
// relative path (AppContext.BaseDirectory\HotkeyGuides) the app itself uses in
// Forms/HotkeyListForm.cs, so a guide the app cannot find is a guide this cannot find.

using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests;

public class HotkeyGuideSayIntentionsChordTests
{
    /// <summary>Sourced from the registration itself, so the chord the guides
    /// document and the chord the app registers cannot drift — this used to be an
    /// independent third spelling of it.</summary>
    private static readonly string BuildTaxiRouteChord = HotkeyManager.SayIntentionsBuildTaxiRouteChordText;

    /// <summary>The description the chord's entry carries in every guide. Used to FIND the
    /// entry independently of the chord, so the test can assert which chord introduces
    /// it — and can see a stale duplicate entry that still names an older one.</summary>
    private const string BuildTaxiRouteDescription =
        "Build a taxi route from the last SayIntentions taxi clearance.";

    private static readonly string[] GuideFiles =
    {
        "FBW_A320_Hotkeys.txt",
        "FBW_A330_Hotkeys.txt",
        "FBW_A380_Hotkeys.txt",
        "Fenix_A320_Hotkeys.txt",
        "HS787_Hotkeys.txt",
        "PMDG_737_Hotkeys.txt",
        "PMDG_777_Hotkeys.txt",
        "iFly_737MAX8_Hotkeys.txt",
    };

    private static string ReadGuide(string filename)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "HotkeyGuides", filename);
        Assert.True(File.Exists(path), $"Hotkey guide not found where the app looks for it: {path}");
        return File.ReadAllText(path);
    }

    public static TheoryData<string> AllGuides()
    {
        var data = new TheoryData<string>();
        foreach (var f in GuideFiles) data.Add(f);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllGuides))]
    public void Every_guide_documents_the_build_taxi_route_chord(string filename)
    {
        Assert.Contains(BuildTaxiRouteChord, ReadGuide(filename), StringComparison.Ordinal);
    }

    /// <summary>Exactly one entry, and the chord that introduces it is the registered one.
    /// A guide that gained the new line without losing the old one has two, and a guide
    /// updated everywhere except its chord still fails on the second assert.</summary>
    [Theory]
    [MemberData(nameof(AllGuides))]
    public void The_build_taxi_route_entry_appears_once_and_names_the_registered_chord(string filename)
    {
        var entries = ReadGuide(filename)
            .Split('\n')
            .Where(l => l.Contains(BuildTaxiRouteDescription, StringComparison.Ordinal))
            .ToList();

        Assert.True(entries.Count == 1,
            $"{filename}: expected exactly one taxi-route entry, found {entries.Count}.");
        Assert.StartsWith($"  {BuildTaxiRouteChord} ", entries[0], StringComparison.Ordinal);
    }

    /// <summary>The chord is an INPUT-mode hotkey, and each guide carries a SayIntentions
    /// block in BOTH mode sections — the output one lists the two readouts (Ctrl+S,
    /// Ctrl+Shift+S) and must not gain this. One mention per guide is what says the chord
    /// went into the input block only.</summary>
    [Theory]
    [MemberData(nameof(AllGuides))]
    public void The_chord_is_named_exactly_once_per_guide(string filename)
    {
        var lines = ReadGuide(filename)
            .Split('\n')
            .Where(l => l.Contains(BuildTaxiRouteChord, StringComparison.Ordinal))
            .ToList();

        Assert.True(lines.Count == 1,
            $"{filename}: expected exactly one line naming {BuildTaxiRouteChord}, found {lines.Count}.");
    }
}
