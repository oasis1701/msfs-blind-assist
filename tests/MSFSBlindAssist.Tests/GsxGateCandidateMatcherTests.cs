// Picking OUR stand out of a gate.select `ambiguous` reply.
//
// Live KATL 2026-08-27: sending gate:22 returned exactly the two stands the airport data
// contains (Concourse B and Concourse C), each carrying uiName + bglName. bglName is the
// only identifier gate.select answers to and GSX does not publish it in the gate list, so
// this list is the only place a client can obtain one.
//
// The rule is EXACT or nothing. GsxGateSelectAnnouncer's Ambiguous arm exists to surface
// that GSX would not guess; auto-resolving on a near-match would replace GSX's refusal with
// our own guess, which is worse.

using System.Collections.Generic;
using MSFSBlindAssist.Services.Gsx.Remote;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class GsxGateCandidateMatcherTests
{
    private static GsxGateSelectCandidate C(string uiName, string gate, int? number, string bglName)
        => new(uiName, gate, number, bglName);

    private static readonly List<GsxGateSelectCandidate> Gate22 = new()
    {
        C("Concourse B (B1-B36) | Gate 22", " Gate 22", 22, "Gate B 22"),
        C("Concourse C (C1-C55) | Gate 22", " Gate 22", 22, "Gate C 22"),
    };

    [Fact]
    public void Resolves_the_live_KATL_gate_22_pair_by_uiName()
    {
        var m = GsxGateCandidateMatcher.Match(
            Gate22, "Concourse C (C1-C55) | Gate 22", " Gate 22", 22);
        Assert.NotNull(m);
        Assert.Equal("Gate C 22", m!.BglName);
    }

    [Fact]
    public void Resolves_the_identically_named_Gate_5_pair_by_uiName()
    {
        var candidates = new List<GsxGateSelectCandidate>
        {
            C("Concourse T (T1-T21) | Gate 5", " Gate 5", 5, "Gate T 5"),
            C("Delta Tech Ops (E1-21) | Gate 5", " Gate 5", 5, "Gate E 5"),
        };
        var m = GsxGateCandidateMatcher.Match(
            candidates, "Concourse T (T1-T21) | Gate 5", " Gate 5", 5);
        Assert.Equal("Gate T 5", m!.BglName);
    }

    [Fact]
    public void Falls_back_to_gate_and_number_when_the_stand_has_no_uiName()
    {
        var candidates = new List<GsxGateSelectCandidate>
        {
            C("", "Ramp 1", 1, "Parking 1"),
            C("Concourse F (F1-F14) | Gate 1", " Gate 1", 1, "Gate F 1"),
        };
        var m = GsxGateCandidateMatcher.Match(candidates, null, "Ramp 1", 1);
        Assert.Equal("Parking 1", m!.BglName);
    }

    [Fact]
    public void A_leading_space_is_significant_in_the_gate_fallback()
    {
        var candidates = new List<GsxGateSelectCandidate>
        {
            C("", " Gate 1", 1, "Gate A 1"),
        };
        Assert.Null(GsxGateCandidateMatcher.Match(candidates, null, "Gate 1", 1));
        Assert.NotNull(GsxGateCandidateMatcher.Match(candidates, null, " Gate 1", 1));
    }

    [Fact]
    public void Two_survivors_resolve_to_nothing()
    {
        var candidates = new List<GsxGateSelectCandidate>
        {
            C("", " Gate 5", 5, "Gate T 5"),
            C("", " Gate 5", 5, "Gate E 5"),
        };
        Assert.Null(GsxGateCandidateMatcher.Match(candidates, null, " Gate 5", 5));
    }

    [Fact]
    public void No_survivor_resolves_to_nothing()
    {
        Assert.Null(GsxGateCandidateMatcher.Match(Gate22, "Concourse D (D1-D46) | Gate 22", " Gate 22", 22));
    }

    [Fact]
    public void An_empty_candidate_list_resolves_to_nothing()
    {
        Assert.Null(GsxGateCandidateMatcher.Match(
            new List<GsxGateSelectCandidate>(), "Concourse T (T1-T21) | Gate 5", " Gate 5", 5));
    }

    [Fact]
    public void A_candidate_with_a_blank_bglName_is_never_returned()
    {
        var candidates = new List<GsxGateSelectCandidate>
        {
            C("Concourse T (T1-T21) | Gate 5", " Gate 5", 5, ""),
        };
        Assert.Null(GsxGateCandidateMatcher.Match(
            candidates, "Concourse T (T1-T21) | Gate 5", " Gate 5", 5));
    }
}
