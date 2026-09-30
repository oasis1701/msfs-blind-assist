using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A/THR button on the FBW A320 and A380 autopilot windows (Ctrl+P). It used to read a
/// fixed "Autothrust engage", so a pilot could not tell whether autothrust was already on,
/// while every other button there, and the autothrottle buttons of the HS787, iFly 737 and
/// MD-11 windows, show their state. The state is <see cref="StatusVar"/>, which both jets
/// keep live in the continuous batch, and its words come from that variable's own
/// <c>ValueDescriptions</c>, so the button says exactly what the auto-announcement says.
/// </summary>
public static class FbwAutothrustButton
{
    public const string StatusVar = "A32NX_AUTOTHRUST_STATUS";

    public readonly record struct ButtonLabel(string Text, string AccessibleName);

    /// <param name="value">The cached status, or null before the first delivery.</param>
    /// <param name="variables">The aircraft definition's variables.</param>
    public static ButtonLabel Label(double? value, IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        string? state = null;
        if (value is double v
            && variables.TryGetValue(StatusVar, out var def)
            && def.ValueDescriptions.TryGetValue(def.DescriptionKeyFor(v), out var described))
            state = described;

        return state == null
            ? new ButtonLabel("A/THR", "Autothrust")
            : new ButtonLabel($"A/THR ({state})", $"Autothrust {state}");
    }
}
