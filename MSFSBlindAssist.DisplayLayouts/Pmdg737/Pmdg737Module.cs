using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>The PMDG 737 NG3's module: its file, and its four display readers in anchor-search order.</summary>
public static class Pmdg737Module
{
    public const string Fma = "fma", EngineDisplays = "engineDisplays", NavDisplay = "navDisplay", Pfd = "pfd";

    public static ModuleDescription Description { get; } = new(
        "pmdg737",
        "737NG3.wasm",
        "^pmdg-aircraft-73[6-9]$",
        new[] { "SimObjects", "Airplanes", "*", "attachments", "pmdg", "Function_Interior_*", "panel" },
        new LayoutReader[]
        {
            new LayoutReader<Pmdg737FmaLayout>(Fma, "FMA",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Pmdg737LayoutDescriptions.Describe),
            new LayoutReader<Pmdg737EngineDisplayLayout>(EngineDisplays, "engine displays",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Pmdg737LayoutDescriptions.Describe),
            new LayoutReader<Pmdg737NavDisplayLayout>(NavDisplay, "navigation display",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Pmdg737LayoutDescriptions.Describe, NavFailures),
            new LayoutReader<Pmdg737PfdLayout>(Pfd, "primary flight display",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Pmdg737LayoutDescriptions.Describe,
                l => l.Optional.Failures.Select(f => (f.Part, f.Reason)).ToList()),
        });

    private static IReadOnlyList<(string Part, string Reason)> NavFailures(Pmdg737NavDisplayLayout l)
    {
        var failures = new List<(string, string)>();
        if (l.Approach is null) failures.Add(("APP/VOR", l.ApproachFailure!));
        if (l.Pointers is null) failures.Add(("pointers", l.PointersFailure!));
        return failures;
    }
}
