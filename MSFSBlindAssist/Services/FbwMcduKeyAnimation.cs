namespace MSFSBlindAssist.Services;

/// <summary>
/// The cockpit push-animation variable of a Captain MCDU key. Setting it to 1 moves the key in
/// the 3D cockpit and plays its <c>mcdubuttons</c> click (FBW_MCDU_BUTTON_Template in
/// A32NX_Interior_MCDU.xml binds SWITCH_POSITION_VAR to it); FBW's own relay handler and keyboard
/// handler both set it on every press, so the Coherent key path does too — for a blind pilot
/// that click is the one immediate sign a press registered.
///
/// The variable is named after the key's MODEL name, which for five keys differs from the key's
/// H-event name (the relay handler writes the H-event name, so over SimBridge those five never
/// animated). The same names serve the Headwind A330, whose bundle writes the same variables.
/// </summary>
internal static class FbwMcduKeyAnimation
{
    private const string Prefix = "L:A32NX_MCDU_PUSH_ANIM_1_";

    private static readonly Dictionary<string, string> ModelNames = new(StringComparer.Ordinal)
    {
        ["DIV"] = "SLASH",
        ["UP"] = "UARROW",
        ["DOWN"] = "DARROW",
        ["PREVPAGE"] = "LARROW",
        ["NEXTPAGE"] = "RARROW",
    };

    public static string VarFor(string key) => Prefix + (ModelNames.TryGetValue(key, out var model) ? model : key);
}
