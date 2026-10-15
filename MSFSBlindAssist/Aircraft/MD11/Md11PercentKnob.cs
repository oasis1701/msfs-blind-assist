namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// A knob whose positions are percentages: the audio volumes (10 % steps) and the lighting and
/// brightness knobs (5 %), positions the generator samples from a CONTINUOUS scale
/// (<c>split_volume_knob</c>, <c>percent_knob_positions</c>). Its reading need not rest on a
/// position — the weather radar brightness steps 3.6 and was read live at 39.60001, and a mouse
/// drag in the cockpit can leave any of them between steps — so its value space is not its combo's
/// key space, and like the gear lever and the flap controls it gets a classifier: the nearest
/// position, the walker's own reading (<see cref="Md11SelectorWalker.PositionIndex"/>). Looked up
/// exactly, such a reading selected nothing, and the first Down-arrow then committed row 0 (0 %).
/// Recognised by the generator's <see cref="Md11Control.Continuous"/> flag, never by its words: a
/// detented knob whose words happen to be percentages is not one, and a percent knob whose words
/// are later curated still is.
/// </summary>
public static class Md11PercentKnob
{
    /// <summary>True for a knob whose positions the generator sampled from a continuous scale.</summary>
    public static bool Is(Md11Control c)
        => c.Continuous
           && c.Composite == null
           && c.ValueMap.Count > 0;

    /// <summary>The classifier: a reading → the key of the nearest position.</summary>
    public static Func<double, double> NearestKey(Md11Control c)
    {
        var ordered = Md11SelectorWalker.OrderedValues(c);
        return v => ordered[Md11SelectorWalker.PositionIndex(c, ordered, v)];
    }
}
