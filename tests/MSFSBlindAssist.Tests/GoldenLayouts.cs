using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The saved layouts the reading tests read through: layout files the generator wrote from synthetic test modules
/// (Layouts/pmdg737, written only by the generator's own golden tests, never by hand or from PMDG's real file),
/// loaded as the app loads a layout file. Each layout's source file is "fixture". A test that needs a part missing
/// takes one with <c>with { … }</c>; a reading test that needs another variant of a test module gets a golden of its own.
/// </summary>
internal static class GoldenLayouts
{
    private static readonly ModuleDescription Module = Pmdg737Module.Description;

    private static readonly Lazy<Pmdg737FmaLayout> FmaLayout = new(() => Layout<Pmdg737FmaLayout>("fma", Pmdg737Module.Fma));
    private static readonly Lazy<Pmdg737EngineDisplayLayout> EngineLayout =
        new(() => Layout<Pmdg737EngineDisplayLayout>("engine-displays", Pmdg737Module.EngineDisplays));
    private static readonly Lazy<Pmdg737NavDisplayLayout> NdLayout = new(() => Layout<Pmdg737NavDisplayLayout>("nav-display", Pmdg737Module.NavDisplay));
    private static readonly Lazy<Pmdg737NavDisplayLayout> NdVdevUpLayout =
        new(() => Layout<Pmdg737NavDisplayLayout>("nav-display-vdev-up", Pmdg737Module.NavDisplay));
    private static readonly Lazy<Pmdg737NavDisplayLayout> NdTrendLeftLayout =
        new(() => Layout<Pmdg737NavDisplayLayout>("nav-display-trend-left", Pmdg737Module.NavDisplay));
    private static readonly Lazy<Pmdg737PfdLayout> PfdLayout = new(() => Layout<Pmdg737PfdLayout>("pfd", Pmdg737Module.Pfd));

    /// <summary>The FMA fixture's layout.</summary>
    public static Pmdg737FmaLayout Fma => FmaLayout.Value;

    /// <summary>The engine display fixture's layout.</summary>
    public static Pmdg737EngineDisplayLayout EngineDisplays => EngineLayout.Value;

    /// <summary>The navigation display fixture's layout.</summary>
    public static Pmdg737NavDisplayLayout NavDisplay => NdLayout.Value;

    /// <summary>The navigation display fixture drawing the VDEV pointer up for a positive deviation.</summary>
    public static Pmdg737NavDisplayLayout NavDisplayVdevUp => NdVdevUpLayout.Value;

    /// <summary>The navigation display fixture curving the trend vector left for a positive turn rate.</summary>
    public static Pmdg737NavDisplayLayout NavDisplayTrendLeft => NdTrendLeftLayout.Value;

    /// <summary>The primary flight display fixture's layout.</summary>
    public static Pmdg737PfdLayout Pfd => PfdLayout.Value;

    /// <summary>The path of golden <paramref name="name"/> next to the test assembly.</summary>
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Layouts", "pmdg737", name + ".json");

    /// <summary>The text of golden <paramref name="name"/>, as saved.</summary>
    public static string Text(string name) => File.ReadAllText(PathOf(name));

    /// <summary>Golden <paramref name="name"/> loaded through <see cref="LayoutFileFormat.TryRead"/>, its layouts' source files
    /// empty as the file saves them. Throws with the reason when it does not load.</summary>
    public static LayoutFile Load(string name)
    {
        if (!LayoutFileFormat.TryRead(Module, Text(name), out var file, out string? failure))
            throw new InvalidOperationException($"golden layout file {PathOf(name)} does not load: {failure}");
        return file;
    }

    /// <summary>One reader's layout from golden <paramref name="name"/>, its source file "fixture".</summary>
    private static T Layout<T>(string name, string reader) where T : class
    {
        var result = Load(name).Readers.Single(r => r.Reader == reader);
        if (result.Layout is null)
            throw new InvalidOperationException($"golden layout file {PathOf(name)} has no {reader} layout: {result.Failure}");
        return (T)Module.Reader(reader).WithSourceFile(result.Layout, "fixture");
    }
}
