using System.Globalization;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The shared FCU push/pull keys (input mode, Shift+1..4 push / Ctrl+1..4 pull: heading,
/// altitude, speed, V/S) on the A220's FCP. The A220 knobs have no pull, and their pushes are
/// knob internals (knots/Mach, heading sync, fine/coarse); managed vs selected lives on the
/// speed ring and the mode buttons (aircraft behaviour XML, tools/a220-gen/reference/
/// cockpit-controls.csv FCU_* rows). So each key is the A220 control that does that key's job:
/// speed push/pull = FMS / manual speed (the outer ring); heading push/pull = sync / HDG;
/// altitude push/pull = VNAV / FLC; V/S push/pull = level off (ALT) / V/S. Knots/Mach,
/// fine/coarse and feet/metres stay in the Ctrl+S / Ctrl+A dialogs. Pinned by A220FcpKeysTests.
/// </summary>
public static class A220FcpKeys
{
    public enum Act { SpeedFms, SpeedManual, HeadingSync, HeadingMode, Vnav, Flc, LevelOff, VsMode }

    internal static Act? For(HotkeyAction action) => action switch
    {
        HotkeyAction.FCUSpeedPush => Act.SpeedFms,
        HotkeyAction.FCUSpeedPull => Act.SpeedManual,
        HotkeyAction.FCUHeadingPush => Act.HeadingSync,
        HotkeyAction.FCUHeadingPull => Act.HeadingMode,
        HotkeyAction.FCUAltitudePush => Act.Vnav,
        HotkeyAction.FCUAltitudePull => Act.Flc,
        HotkeyAction.FCUVSPush => Act.LevelOff,
        HotkeyAction.FCUVSPull => Act.VsMode,
        _ => null
    };

    /// <summary>The FG annunciator var saying the act's mode is engaged; null for the speed
    /// ring and the heading sync, which are not mode buttons.</summary>
    internal static string? ModeKey(Act act) => act switch
    {
        Act.HeadingMode => "A22X_FG_HEADING",
        Act.Vnav => "A22X_FG_VNAV",
        Act.Flc => "A22X_FG_FLC",
        Act.LevelOff => "A22X_FG_ALT",
        Act.VsMode => "A22X_FG_VS",
        _ => null
    };

    /// <summary>The A220 mode buttons TOGGLE — pressing an engaged mode turns it off. A key
    /// names a target, so it presses only when the target is not engaged. An unknown state
    /// (null or a negative "no cache" value) presses, and the read-back speaks the truth.</summary>
    internal static bool ShouldPress(Act act, double? modeState)
        => ModeKey(act) == null || !(modeState is double s && s > 0.5);

    /// <summary>"L:A22X FG Speed Mode": 0 = FMS, 1 = manual (the ring's own L:var, the value
    /// the Ctrl+S dialog's measured toggle writes). A TARGET, never a blind toggle.</summary>
    internal static int SpeedRingValue(Act act) => act == Act.SpeedFms ? 0 : 1;

    internal static string Name(Act act) => act switch
    {
        Act.SpeedFms => "FMS speed",
        Act.SpeedManual => "Manual speed",
        Act.HeadingSync => "Heading sync",
        Act.HeadingMode => "HDG mode",
        Act.Vnav => "VNAV",
        Act.Flc => "FLC",
        Act.LevelOff => "Altitude hold",
        Act.VsMode => "Vertical speed mode",
        _ => ""
    };

    internal static string AlreadyText(Act act) => act switch
    {
        Act.SpeedFms => "Already FMS speed.",
        Act.SpeedManual => "Already manual speed.",
        _ => $"{Name(act)} already engaged."
    };

    /// <summary>What the speed ring shows after the write (CORE-7: the reader cannot say it).</summary>
    internal static string SpeedResult(bool wantFms, bool? isFmsNow, double? knots, double? mach, bool inMach)
    {
        if (isFmsNow is not bool fms) return "Speed mode not readable";
        if (fms != wantFms)
            return (wantFms ? "FMS speed" : "Manual speed") + " did not engage — still "
                + (fms ? "FMS speed" : "manual speed");
        if (fms) return "FMS speed";
        if (inMach && mach is double m) return "Manual speed, Mach " + m.ToString("0.00", CultureInfo.InvariantCulture);
        if (knots is double k) return "Manual speed, " + k.ToString("0", CultureInfo.InvariantCulture) + " knots";
        return "Manual speed";
    }

    internal static string HeadingSyncResult(double? hdgSel)
        => hdgSel is double h
            ? "Heading synced, " + ((int)Math.Round(h) % 360).ToString("000", CultureInfo.InvariantCulture)
            : "Heading synced to the current heading";
}
