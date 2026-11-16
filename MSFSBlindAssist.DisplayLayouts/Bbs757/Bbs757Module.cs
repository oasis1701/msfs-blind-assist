using MSFSBlindAssist.Services.BBS757;

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>
/// BlueBird Simulations' 757-200 module: its file (BBS752.wasm, in the one 757 package) and its display readers
/// in anchor-search order. Unlike the PMDG 737, this build has no name section; the readers reach their objects
/// through the gauge manager and C++ RTTI (see the layout records). The readers grow as their recipes land.
/// </summary>
public static class Bbs757Module
{
    public const string Fma = "fma";
    public const string Efb = "efb";
    public const string LowerEicas = "lowerEicas";
    public const string UpperEicas = "upperEicas";
    public const string Ehsi = "ehsi";
    public const string Adi = "adi";

    public static ModuleDescription Description { get; } = new(
        "bbs757",
        "BBS752.wasm",
        "^bluebirdsim-aircraft-757-200$",
        new[] { "SimObjects", "Airplanes", "bbs-757-200-base", "panel" },
        new LayoutReader[]
        {
            new LayoutReader<Bbs757FmaLayout>(Fma, "FMA",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.Describe),
            new LayoutReader<Bbs757EfbMemLayout>(Efb, "EFB",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeEfb),
            new LayoutReader<Bbs757LowerEicasLayout>(LowerEicas, "lower EICAS",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeLowerEicas, LowerEicasFailures),
            new LayoutReader<Bbs757UpperEicasLayout>(UpperEicas, "upper EICAS",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeUpperEicas),
            new LayoutReader<Bbs757EhsiLayout>(Ehsi, "EHSI",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeEhsi),
            new LayoutReader<Bbs757AdiLayout>(Adi, "ADI",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeAdi, AdiFailures),
        });

    /// <summary>The lower EICAS pages whose part could not be derived, with the generator's reason (each page is an
    /// optional part of the layout): the generator and the store log them.</summary>
    private static IReadOnlyList<(string Part, string Reason)> LowerEicasFailures(Bbs757LowerEicasLayout l)
    {
        var failures = new List<(string, string)>();
        if (l.EngineMissing is { } engine) failures.Add(("engine page", engine));
        if (l.StatusMissing is { } status) failures.Add(("STATUS page", status));
        return failures;
    }

    /// <summary>The ADI parts that could not be derived, with the generator's reason (each part is optional), in the
    /// layout's order: the generator and the store log them.</summary>
    private static IReadOnlyList<(string Part, string Reason)> AdiFailures(Bbs757AdiLayout l)
    {
        var failures = new List<(string, string)>();
        if (l.AttitudeMissing is { } attitude) failures.Add(("attitude", attitude));
        if (l.SpeedMissing is { } speed) failures.Add(("speed tape", speed));
        if (l.IlsMissing is { } ils) failures.Add(("localizer and glideslope", ils));
        if (l.RadioAltitudeMissing is { } radioAltitude) failures.Add(("radio altitude and decision height", radioAltitude));
        if (l.FlightDirectorMissing is { } flightDirector) failures.Add(("flight director", flightDirector));
        if (l.TcasMissing is { } tcas) failures.Add(("TCAS", tcas));
        if (l.ReferenceSpeedsMissing is { } referenceSpeeds) failures.Add(("reference speeds", referenceSpeeds));
        return failures;
    }
}
