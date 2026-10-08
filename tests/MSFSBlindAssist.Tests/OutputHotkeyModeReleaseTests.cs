using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ EVERY KEY OUTPUT MODE CLAIMS MUST BE GIVEN BACK.
///
/// A global hotkey registered with <c>RegisterHotKey</c> is held by the process until it is
/// explicitly released — it does not lapse when the mode that took it ends. So a key registered
/// in <c>ActivateOutputHotkeyMode</c> and missing from the deactivate path is held FOR THE REST
/// OF THE SESSION, and because output mode's keys are BARE LETTERS, the pilot simply loses those
/// letters off their keyboard. Everywhere. Including typing.
///
/// THAT IS NOT THE WORST OF IT, AND THIS IS WHY THE TEST EXISTS RATHER THAN A CODE COMMENT.
/// Hand fly's quick-access set claims bare letters too, and the two sets OVERLAP on P. A leaked
/// P from output mode therefore made hand fly's own RegisterHotKey fail on activation, which is
/// announced as "Hand fly mode active, quick keys failed" — a message about the autopilot
/// handoff, with no visible connection to a readout key claimed minutes earlier. Meanwhile the
/// eight keys that DID register stayed held, so the pilot was told the quick keys had failed
/// while those same keys ate their keyboard.
///
/// Live report, 2026-09-02: "Q and E don't work at all... I can't even type with them", plus
/// "hand fly mode active, quick keys failed" — one missing release, two unrelated-looking
/// symptoms, and it only reproduced after output mode had been used once.
/// </summary>
public class OutputHotkeyModeReleaseTests
{
    private static string ReadHotkeyManager()
    {
        // Walk up from the test binary to the repo root, the way the other source-scanning
        // tests here do. A missing file must FAIL, never silently pass — a guard that skips
        // when it cannot find its subject is not a guard.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root from the test binary.");
        string path = Path.Combine(dir!.FullName, "MSFSBlindAssist", "Hotkeys", "HotkeyManager.cs");
        Assert.True(File.Exists(path), $"HotkeyManager.cs not found at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The body of one method, by brace matching from its signature.</summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find '{signature}' in HotkeyManager.cs — if it was renamed, update this test rather than deleting it.");

        int open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"No opening brace after '{signature}'.");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(open, i - open + 1);
            }
        }

        throw new Xunit.Sdk.XunitException($"Unbalanced braces while reading '{signature}'.");
    }

    private static HashSet<string> IdsIn(string body, string call) =>
        Regex.Matches(body, call + @"\(\s*windowHandle\s*,\s*(HOTKEY_[A-Z0-9_]+)")
             .Select(m => m.Groups[1].Value)
             .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryHotkeyOutputModeRegistersIsAlsoReleasedWhenItEnds()
    {
        string source = ReadHotkeyManager();
        var claimed = IdsIn(MethodBody(source, "private void ActivateOutputHotkeyMode()"), "RegisterHotKey");

        // The deactivate path is scanned across the whole file rather than one method, because
        // the release is allowed to live in a helper — what must never happen is a key with no
        // release anywhere.
        var released = IdsIn(source, "UnregisterHotKey");

        Assert.True(claimed.Count > 10,
            $"Only {claimed.Count} registrations found in ActivateOutputHotkeyMode — the parse is probably wrong, not the code.");

        var leaked = claimed.Except(released).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(leaked.Count == 0,
            "These output-mode hotkeys are registered but never unregistered, so they stay held " +
            "for the rest of the session and the pilot loses those keys everywhere — including " +
            "for typing: " + string.Join(", ", leaked));
    }

    [Fact]
    public void TheThreeEngineReadoutKeysAreReleased()
    {
        // The three that actually leaked, pinned by name. The sweep above would catch them
        // again, but naming them keeps the regression legible to whoever reads a failure.
        string source = ReadHotkeyManager();
        var released = IdsIn(source, "UnregisterHotKey");

        Assert.Contains("HOTKEY_READ_ENGINE_RPM", released);
        Assert.Contains("HOTKEY_READ_ENGINE_POWER", released);
        Assert.Contains("HOTKEY_READ_ENGINE_TEMPS", released);
    }
}
