// PMDG's own SDK is invisible to this app (and to any other third-party CDU tool) until the
// pilot's *_Options.ini carries a working [SDK] block. Multiple PMDG forum threads report the
// exact same failure mode when a pilot hand-edits it: paste the lines in with no blank-line
// separation and PMDG's own writer silently drops the block on the next in-sim save. These tests
// pin both halves — detecting whether the block is really usable, and producing an edit PMDG's
// writer won't eat.

using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

public class PMDGOptionsIniFormatTests
{
    [Fact]
    public void A_file_with_no_SDK_section_is_not_configured()
    {
        var lines = new[] { "[IRS.0]", "LastPosValid=0" };

        Assert.False(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: false));
    }

    [Fact]
    public void An_empty_SDK_section_is_not_configured()
    {
        // The exact shape a live PMDG forum report showed: [SDK] present with nothing under it.
        var lines = new[] { "[IRS.0]", "LastPosValid=0", "", "[SDK]" };

        Assert.False(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: false));
    }

    [Fact]
    public void Every_required_key_present_and_set_to_1_is_configured()
    {
        var lines = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=1",
            "EnableCDUBroadcast.0=1",
            "EnableCDUBroadcast.1=1",
        };

        Assert.True(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: false));
    }

    [Fact]
    public void A_737_does_not_need_the_center_CDU_key()
    {
        var lines = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=1",
            "EnableCDUBroadcast.0=1",
            "EnableCDUBroadcast.1=1",
        };

        Assert.True(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: false));
        // The same lines are NOT enough for an aircraft with a center CDU (the 777).
        Assert.False(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: true));
    }

    [Fact]
    public void A_777_needs_the_center_CDU_key_too()
    {
        var lines = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=1",
            "EnableCDUBroadcast.0=1",
            "EnableCDUBroadcast.1=1",
            "EnableCDUBroadcast.2=1",
        };

        Assert.True(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: true));
    }

    [Fact]
    public void A_key_present_but_set_to_0_is_not_configured()
    {
        var lines = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=0",
            "EnableCDUBroadcast.0=1",
            "EnableCDUBroadcast.1=1",
        };

        Assert.False(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: false));
    }

    [Fact]
    public void The_space_in_the_key_typo_a_live_forum_report_made_does_not_count()
    {
        // "Enable Data Broadcast=1" (with a space) is not the same key as EnableDataBroadcast —
        // a real pilot pasted exactly this and PMDG never saw the setting.
        var lines = new[] { "[SDK]", "Enable Data Broadcast=1" };

        Assert.False(PMDGOptionsIniFormat.HasBroadcastEnabled(lines, requireCenterCdu: false));
    }

    [Fact]
    public void Applying_settings_to_a_file_with_no_SDK_section_appends_one_with_blank_line_padding()
    {
        var original = new[] { "[IRS.0]", "LastPosValid=0" };

        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(original, requireCenterCdu: false);

        // Original content untouched, in place.
        Assert.Equal("[IRS.0]", patched[0]);
        Assert.Equal("LastPosValid=0", patched[1]);
        // Blank line separates the new section — PMDG's writer needs this or it drops the block.
        Assert.Equal(string.Empty, patched[2]);
        Assert.Equal("[SDK]", patched[3]);
        Assert.Contains("EnableDataBroadcast=1", patched);
        Assert.Contains("EnableCDUBroadcast.0=1", patched);
        Assert.Contains("EnableCDUBroadcast.1=1", patched);
        // Trailing blank line at EOF — the other half of the same PMDG writer trap.
        Assert.Equal(string.Empty, patched[^1]);
        Assert.True(PMDGOptionsIniFormat.HasBroadcastEnabled(patched, requireCenterCdu: false));
    }

    [Fact]
    public void Applying_settings_to_an_already_blank_terminated_file_does_not_double_the_blank_line()
    {
        var original = new[] { "[IRS.0]", "LastPosValid=0", "" };

        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(original, requireCenterCdu: false);

        Assert.Equal("[IRS.0]", patched[0]);
        Assert.Equal("LastPosValid=0", patched[1]);
        Assert.Equal(string.Empty, patched[2]);
        Assert.Equal("[SDK]", patched[3]);
    }

    [Fact]
    public void Applying_settings_to_an_empty_file_appends_the_section_with_no_leading_blank_line()
    {
        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(Array.Empty<string>(), requireCenterCdu: false);

        Assert.Equal("[SDK]", patched[0]);
    }

    [Fact]
    public void Applying_settings_to_an_existing_empty_SDK_section_inserts_the_keys_into_it()
    {
        var original = new[] { "[IRS.0]", "LastPosValid=0", "", "[SDK]", "", "[IRS.1]", "LastPosValid=0" };

        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(original, requireCenterCdu: false);

        Assert.True(PMDGOptionsIniFormat.HasBroadcastEnabled(patched, requireCenterCdu: false));
        // Content outside [SDK] is completely untouched, including the section after it.
        Assert.Contains("[IRS.1]", patched);
        int sdkIndex = patched.IndexOf("[SDK]");
        int irs1Index = patched.IndexOf("[IRS.1]");
        Assert.True(sdkIndex < irs1Index);
    }

    [Fact]
    public void Applying_settings_updates_a_wrong_value_in_place_rather_than_duplicating_the_key()
    {
        var original = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=0",
            "EnableCDUBroadcast.0=1",
            "EnableCDUBroadcast.1=1",
        };

        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(original, requireCenterCdu: false);

        Assert.Equal(4, patched.Count);
        Assert.Equal("EnableDataBroadcast=1", patched[1]);
    }

    [Fact]
    public void Applying_settings_appends_only_the_missing_keys_into_an_existing_partially_configured_section()
    {
        var original = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=1",
        };

        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(original, requireCenterCdu: true);

        Assert.True(PMDGOptionsIniFormat.HasBroadcastEnabled(patched, requireCenterCdu: true));
        // The already-correct line was left alone, not rewritten.
        Assert.Equal("EnableDataBroadcast=1", patched[1]);
    }

    [Fact]
    public void Applying_settings_is_idempotent_on_an_already_configured_file()
    {
        var original = new[]
        {
            "[SDK]",
            "EnableDataBroadcast=1",
            "EnableCDUBroadcast.0=1",
            "EnableCDUBroadcast.1=1",
            "EnableCDUBroadcast.2=1",
        };

        var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(original, requireCenterCdu: true);

        Assert.Equal(original, patched);
    }
}
