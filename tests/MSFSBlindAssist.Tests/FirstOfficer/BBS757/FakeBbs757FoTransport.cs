using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.BBS757;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// A simulated 757 behind the First Officer's executor (<see cref="IBbs757FoTransport"/>): the
/// definition's silent write path without a sim. A set that the aircraft takes (<see cref="Accepts"/>,
/// true by default) leaves the control reading its target, as <c>FoSetAsync</c>'s read-back would;
/// one it refuses leaves the reading unchanged and returns false. Waits end at once and are recorded.
/// </summary>
internal sealed class FakeBbs757FoTransport : IBbs757FoTransport
{
    public bool Ready { get; set; } = true;

    /// <summary>The nodes the map knows; null knows every node.</summary>
    public HashSet<string>? KnownNodes { get; set; }

    /// <summary>The momentary buttons.</summary>
    public HashSet<string> Buttons { get; } = new(StringComparer.Ordinal);

    /// <summary>What a fresh read and the cache return; missing or null is unread.</summary>
    public Dictionary<string, double?> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether the aircraft takes a set; default every set lands.</summary>
    public Func<string, double, bool> Accepts { get; set; } = (_, _) => true;

    /// <summary>Called after each set the aircraft took, so a test can make the cockpit follow it.</summary>
    public Action<string, double>? OnSet { get; set; }

    /// <summary>Called after each press, so a test can make the cockpit follow it.</summary>
    public Action<string>? OnPress { get; set; }

    /// <summary>Called after each wait with how many waits there have been, so a test can make time pass in the cockpit.</summary>
    public Action<int>? OnDelay { get; set; }

    public List<(string Node, double Target)> Sets { get; } = new();
    public List<string> Presses { get; } = new();
    public List<(string Node, double Target)> WritesNow { get; } = new();
    public List<int> Delays { get; } = new();
    public List<string> Reads { get; } = new();

    /// <summary>
    /// Every set, press, write-now, held press and P61 test in order ("set X=1", "press X", "now X=1",
    /// "held X 10000" or "held X map" for the map's own hold, "p61 X").
    /// </summary>
    public List<string> Events { get; } = new();

    /// <summary>The held presses of the preflight tests, with the hold asked (null: the map's own).</summary>
    public List<(string Node, int? HoldMs)> HeldPresses { get; } = new();

    /// <summary>The P61 test buttons pressed.</summary>
    public List<string> P61Tests { get; } = new();

    /// <summary>Whether a held press or a P61 test goes out (and, for a P61 test, its switch rests); default it does.</summary>
    public bool TestPressLands { get; set; } = true;

    public int AltimeterSets { get; private set; }
    public bool AltimetersLand { get; set; } = true;

    public bool Knows(string node) => KnownNodes == null || KnownNodes.Contains(node);

    public bool IsButton(string node) => Buttons.Contains(node);

    public Task<bool> SetAsync(string node, double target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Sets.Add((node, target));
        Events.Add($"set {node}={target}");
        if (!Accepts(node, target)) return Task.FromResult(false);
        Values[node] = target;
        OnSet?.Invoke(node, target);
        return Task.FromResult(true);
    }

    public Task<bool> PressAsync(string node, CancellationToken ct)
    {
        Presses.Add(node);
        Events.Add($"press {node}");
        if (!Ready) return Task.FromResult(false);
        OnPress?.Invoke(node);
        return Task.FromResult(true);
    }

    public Task<bool> PressHeldAsync(string node, int? holdMs, CancellationToken ct)
    {
        HeldPresses.Add((node, holdMs));
        Events.Add($"held {node} {(holdMs is int ms ? ms.ToString(CultureInfo.InvariantCulture) : "map")}");
        return Task.FromResult(Ready && TestPressLands);
    }

    public Task<bool> P61TestAsync(string buttonKey, CancellationToken ct)
    {
        P61Tests.Add(buttonKey);
        Events.Add($"p61 {buttonKey}");
        return Task.FromResult(Ready && TestPressLands);
    }

    public Task<double?> ReadAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Reads.Add(key);
        return Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
    }

    public double? Cached(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public bool WriteNow(string node, double target)
    {
        WritesNow.Add((node, target));
        Events.Add($"now {node}={target}");
        if (!Ready) return false;
        Values[node] = target;
        return true;
    }

    public Task<bool> SetAltimetersStandardAsync(CancellationToken ct)
    {
        AltimeterSets++;
        return Task.FromResult(AltimetersLand);
    }

    /// <summary>What a parking-brake set answers; true sets the brake node to 1.</summary>
    public bool ParkingBrakeLands { get; set; } = true;

    public int ParkingBrakeSets { get; private set; }

    public Task<bool> SetParkingBrakeAsync(CancellationToken ct)
    {
        ParkingBrakeSets++;
        Events.Add("park brake");
        if (!Ready || !ParkingBrakeLands) return Task.FromResult(false);
        Values["BBS757_PEDESTAL_Park_Brake_Lever"] = 1;
        return Task.FromResult(true);
    }

    public Task Delay(int ms, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Delays.Add(ms);
        OnDelay?.Invoke(Delays.Count);
        return Task.CompletedTask;
    }
}

/// <summary>A flow step as the executor sees it (FlowManager hands it an <see cref="IFlowStepDispatch"/>).</summary>
internal sealed class Bbs757Step : IFlowStepDispatch
{
    public FlowStepActionType ActionType { get; init; } = FlowStepActionType.SetSwitch;
    public string? EventName { get; init; }
    public int? TargetValue { get; init; }
    public IReadOnlyList<(string EventName, int? TargetValue)> MultiActions { get; init; } = Array.Empty<(string, int?)>();
    public bool UsesMouseFlag => false;
    public bool IsMomentary => false;
}

/// <summary>
/// The aircraft title a test cockpit carries: a Rolls-Royce is known by its title alone (an unwritten
/// <c>L:B757_ENG_VARIANT</c> reads 0 too, so a 0 is no evidence). Reached like the cache, through an
/// <see cref="UnsafeAccessorAttribute"/> on the private field, never reflection.
/// </summary>
internal static class Bbs757TestTitles
{
    public const string Pw = "BlueBird 757-200 PW N/W";
    public const string Rr = "BlueBird 757-200 RR Scimitar/W";

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "currentAircraftTitle")]
    internal static extern ref string AircraftTitle(SimConnectManager manager);

    /// <summary>A never-connected manager holding <paramref name="values"/> under the aircraft title <paramref name="title"/>.</summary>
    public static SimConnectManager With(string title, params (string Key, double Value)[] values)
    {
        var sc = SeededSimConnectCache.With(values);
        AircraftTitle(sc) = title;
        return sc;
    }

    /// <summary>As <see cref="With"/>, reading as connected.</summary>
    public static SimConnectManager ConnectedWith(string title, params (string Key, double Value)[] values)
    {
        var sc = SeededSimConnectCache.ConnectedWith(values);
        AircraftTitle(sc) = title;
        return sc;
    }
}
