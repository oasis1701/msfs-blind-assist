using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The First Officer's side of the 757 (plan 10): the one variable it registers, and a SILENT,
/// AWAITABLE write path. The panel path (<see cref="HandleUIVariableSet"/>) is fire-and-forget and
/// speaks its own refusals; a flow step must instead know whether its switch landed, and the flow
/// speaks for itself. So <see cref="FoSetAsync"/> and <see cref="FoPressAsync"/> reuse the panel's
/// own decisions (<see cref="Bbs757Write.ToPosition"/>, <see cref="Bbs757Write.PressFor"/>,
/// <see cref="Bbs757Guarded.Plan"/>) and its sim access (<see cref="WriteIoFor"/>), speak nothing,
/// and return true only when the control reads its target afterwards.
///
/// A write runs as a pick (<see cref="BeginPick"/>): the control's deliveries are consumed while it
/// is in flight, so an open panel's combo is not moved under the pilot, and a pilot's pick of the
/// same control supersedes it (the write then reports false). A linked status row (the parking
/// brake, EXT PWR) has its change taken as this write's echo, as for a panel pick.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The engine variant, BlueBird's <c>L:B757_ENG_VARIANT</c> (0 RB211, 1 PW2000), OnRequest.</summary>
    public const string FoEngineVariantKey = "BBS757_FO_ENG_VARIANT";

    static partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars)
    {
        if (vars.ContainsKey(FoEngineVariantKey)) return;
        vars[FoEngineVariantKey] = new SimVarDefinition
        {
            Name = "B757_ENG_VARIANT", DisplayName = "Engine Variant",
            Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.OnRequest,
        };
    }

    /// <summary>How long a First Officer write waits before its first read-back.</summary>
    internal const int FoSettleMs = 150;

    /// <summary>The gap between the read-backs that follow while the switch has not landed.</summary>
    internal const int FoPollMs = 250;

    /// <summary>The most a First Officer write waits for a switch to land before it gives up.</summary>
    internal const int FoLandMaxMs = 3000;

    /// <summary>True when the map knows this node (the First Officer's tables are checked against it).</summary>
    internal bool HasControl(string node) => _byNode.ContainsKey(node);

    /// <summary>The map control for a node, or null.</summary>
    internal Bbs757Control? ControlFor(string node) => _byNode.TryGetValue(node, out var c) ? c : null;

    /// <summary>
    /// Sets a positional control (toggle, switch, select, or a guarded one) to <paramref name="target"/>,
    /// silently. True when the control already reads it, or reads it after the write; false when the
    /// control is unknown, a write cannot land, the control or its guard was not read, the target is
    /// none of its positions, the write did not take, or a newer pick superseded this one.
    /// </summary>
    internal async Task<bool> FoSetAsync(SimConnectManager sim, string node, double target, CancellationToken ct)
    {
        if (!_byNode.TryGetValue(node, out var control)) return false;
        if (Bbs757Write.IsMomentary(control)) return false;   // a button has no position: FoPressAsync
        Sim = sim;
        return await FoSetCoreAsync(control, target, WriteIoFor(sim), ct);
    }

    /// <summary>The testable core of <see cref="FoSetAsync"/>.</summary>
    internal async Task<bool> FoSetCoreAsync(Bbs757Control control, double target, Bbs757SimIo io, CancellationToken ct, bool pollForLanding = true)
    {
        if (!io.CanWrite()) return false;
        var pick = BeginPick(control.Node);
        if (pick == null) return false;   // switched away
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(pick.Token, ct);
        var token = linked.Token;
        bool landed = false;
        Bbs757Control? guard = null;
        try
        {
            IReadOnlyList<string> calls;
            if (!string.IsNullOrWhiteSpace(control.Guard))
            {
                if (!_byNode.TryGetValue(control.Guard, out guard)) return false;
                var reads = await Task.WhenAll(io.ReadFresh(control.Node, token), io.ReadFresh(guard.Node, token));
                token.ThrowIfCancellationRequested();
                var gplan = Bbs757Guarded.Plan(control, guard, reads[0], reads[1], target);
                if (gplan.Refused) return false;
                if (gplan.Calls.Count == 0) return landed = true;
                calls = gplan.Calls;
            }
            else
            {
                double? current = await io.ReadFresh(control.Node, token);
                token.ThrowIfCancellationRequested();
                var plan = Bbs757Write.ToPosition(control, current, target);
                if (plan.Outcome == Bbs757WriteOutcome.AtPosition) return landed = true;
                if (plan.Outcome == Bbs757WriteOutcome.Refused)
                {
                    Log.Debug("BBS757", $"FO {control.Node}: read {current?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}, " +
                        $"target {target.ToString(System.Globalization.CultureInfo.InvariantCulture)}: no safe write.");
                    return false;
                }
                calls = plan.Calls;
            }

            string? status = LinkedStatusKey(control.Node);
            if (status != null) ArmOwnWriteEcho(status);
            OnOwnWrite(control.Node);   // plan 13
            if (guard != null) OnOwnWrite(guard.Node);
            for (int i = 0; i < calls.Count; i++)
            {
                if (i > 0 && guard != null) await io.Delay(Bbs757Guarded.StepGapMs, token);
                io.Write(calls[i]);
            }

            double? after;
            if (!pollForLanding)
            {
                await io.Delay(ReadBackDelayMs, token);
                after = await io.ReadFresh(control.Node, token);
                landed = LandedOnPick(control, after, target);
            }
            else
            {
                // Landed only when TWO reads in a row show the target: BlueBird's REALISTIC parking brake
                // writes itself back, so one read of the target can lie (docs/b757.md, "The write path").
                // The budget counts the waits asked or the real time since the poll began, whichever is larger
                // (a real wait runs long and a read takes time; HoldSpringAsync counts the same way).
                long start = NowMs();
                int waited = FoSettleMs;
                await io.Delay(FoSettleMs, token);
                after = await io.ReadFresh(control.Node, token);
                int hits = LandedOnPick(control, after, target) ? 1 : 0;
                while (hits < 2 && Math.Max(waited, NowMs() - start) + FoPollMs <= FoLandMaxMs)
                {
                    await io.Delay(FoPollMs, token);
                    waited += FoPollMs;
                    after = await io.ReadFresh(control.Node, token);
                    hits = LandedOnPick(control, after, target) ? hits + 1 : 0;
                }
                landed = hits >= 2;
            }
            if (status != null)
            {
                double? state = await io.ReadFresh(status, token);
                if (state != null) SettleOwnWriteEcho(status, LinkedPhrase(status, state));
            }
            if (!landed)
                Log.Info("BBS757", $"FO {control.Node}: target {target.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"not reached, reads {after?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}.");
            return landed;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"FO {control.Node}: write failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (EndPick(control.Node, pick))
            {
                io.RequestRead(control.Node);
                if (guard != null) io.RequestRead(guard.Node);
            }
        }
    }

    /// <summary>
    /// Presses a momentary button (or holds a held one for its hold time), silently: true when the press
    /// was sent. A button has no position to read back; the caller confirms what the press did by the
    /// state it drives (EXT PWR ON, the speedbrake lever). False when the control is unknown, is not a
    /// button, has no press code, or a write cannot land. <paramref name="holdMs"/>, when given, replaces
    /// the map's hold of a held button (a preflight oxygen test is held 10 s, plan 19 task 8).
    /// </summary>
    internal async Task<bool> FoPressAsync(SimConnectManager sim, string node, CancellationToken ct, int? holdMs = null)
    {
        if (!_byNode.TryGetValue(node, out var control) || !Bbs757Write.IsMomentary(control)) return false;
        Sim = sim;
        return await FoPressCoreAsync(control, WriteIoFor(sim), ct, holdMs);
    }

    internal async Task<bool> FoPressCoreAsync(Bbs757Control control, Bbs757SimIo io, CancellationToken ct, int? holdMs = null)
    {
        var press = Bbs757Write.PressFor(control);
        if (press == null || !io.CanWrite() || _lifetime.IsCancellationRequested) return false;
        string? status = LinkedStatusKey(control.Node);
        if (status != null) ArmOwnWriteEcho(status);
        OnOwnWrite(control.Node);   // plan 13
        io.Write(press.Down);
        if (press.Up != null)
        {
            await io.Delay(holdMs ?? press.HoldMs, CancellationToken.None);   // a press is always let go
            io.Write(press.Up);
        }
        RelockIfUnlock(control, io);
        if (status != null)
        {
            try
            {
                await io.Delay(ReadBackDelayMs, ct);
                double? state = await io.ReadFresh(status, ct);
                if (state != null) SettleOwnWriteEcho(status, LinkedPhrase(status, state));
            }
            catch (OperationCanceledException) { /* the press went; only the echo read-back stopped */ }
        }
        return true;
    }

    /// <summary>
    /// A P61 test run by the First Officer (plan 19, task 8), silently: its spring switch held to the test's side for
    /// 3 s and let go, as the panel's press does (<see cref="PressSpringAsync"/>) but with nothing said; the flow speaks
    /// the step, and the lights and warnings the test raises are the cockpit watcher's. True when the switch rests
    /// afterwards; false for a key that is not a P61 test button, or see <see cref="FoSpringTestCoreAsync"/>.
    /// </summary>
    internal async Task<bool> FoSpringTestAsync(SimConnectManager sim, string buttonKey, CancellationToken ct)
    {
        if (Bbs757SpringControls.ForButtonKey(buttonKey) is not { } button) return false;
        Sim = sim;
        return await FoSpringTestCoreAsync(button, WriteIoFor(sim), ct);
    }

    /// <summary>
    /// The testable core of <see cref="FoSpringTestAsync"/>. False, with nothing sent, for a button that is not a P61 test
    /// (a trim or the cabin switch: <see cref="Bbs757SpringControl.ReadoutKey"/> not null), a write that cannot land, a
    /// profile switched away, a Stop already given, or a switch already holding (one hold per switch, shared with the
    /// panel's presses). Otherwise the hold (<see cref="HoldSpringAsync"/>: its waits are on
    /// <see cref="CancellationToken.None"/>, so a Stop never cuts it short) and the verified release; true when the switch
    /// rests. A switch still off rest is released again in the background, silently (<see cref="KeepReleasingAsync"/> with
    /// no announcer), and keeps its hold until that ends; the test is then false. Nothing is ever said.
    /// </summary>
    internal async Task<bool> FoSpringTestCoreAsync(Bbs757SpringButton button, Bbs757SimIo io, CancellationToken ct)
    {
        var control = button.Control;
        if (control.ReadoutKey != null || ct.IsCancellationRequested || _lifetime.IsCancellationRequested || !io.CanWrite())
            return false;
        if (!_springHolds.TryAdd(control.Key, 0))
        {
            Log.Debug("BBS757", $"FO {button.Label}: {control.LVar} is already holding; not pressed.");
            return false;
        }

        bool releasing = false;   // the background release owns the switch's hold from then on
        try
        {
            var (hold, release) = Bbs757SpringControls.Codes(control, button.Positive);
            OnOwnWrite(control.LVar);   // plan 13
            bool rested = await HoldSpringAsync(control, hold, release, io);
            if (!rested)
            {
                Log.Warn("BBS757", $"FO {control.LVar} did not read its rest value after {Bbs757SpringControls.MaxReleaseWrites} releases; " +
                    $"releasing it again every {Bbs757SpringControls.ReleaseRetryIntervalMs} ms for up to {Bbs757SpringControls.ReleaseRetryForMs} ms.");
                releasing = true;
                _ = ReleaseInBackgroundAsync(control, release, io);   // never throws
            }
            return rested;
        }
        finally
        {
            if (!releasing) _springHolds.TryRemove(control.Key, out _);
        }
    }

    /// <summary>The First Officer's background release: silent, and the switch's hold kept until it ends. Never throws.</summary>
    private async Task ReleaseInBackgroundAsync(Bbs757SpringControl control, string release, Bbs757SimIo io)
    {
        try { await KeepReleasingAsync(control, release, io, announcer: null); }
        finally { _springHolds.TryRemove(control.Key, out _); }
    }

    /// <summary>
    /// All three altimeters to standard, the Ctrl+B window's own way (one <c>KOHLSMAN_SET</c> 16212, its
    /// echo armed so the altimeter watch does not announce it), silently. True when all three already
    /// read standard or do after the set; false when a write cannot land or they did not all reach it.
    /// </summary>
    internal async Task<bool> FoSetAltimetersStandardAsync(SimConnectManager sim, CancellationToken ct)
    {
        try
        {
            var io = McpIoFor(sim);
            double std = Bbs757Altimeters.StandardParameter / 16.0;
            async Task<bool> AllStandard()
            {
                foreach (var key in Bbs757Altimeters.KohlsmanKeys)
                    if (await io.Read(key, ct) is not double v || Math.Abs(v - std) > 0.5) return false;
                return true;
            }
            if (await AllStandard()) return true;
            if (!CanWrite(sim) || _lifetime.IsCancellationRequested) return false;
            OnOwnAltimeterSet();   // plan 06, then 14: the watch takes the change as this set's echo
            await Bbs757Altimeters.SetAltimetersAsync(io, Bbs757Altimeters.StandardParameter, ct);
            return await AllStandard();
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"FO altimeters to standard failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The parking brake SET, silently: the lever's own write first (all a brake in EASY mode needs), then, only
    /// if it did not set, full braking in the same frame as the lever (REALISTIC mode, <see cref="Bbs757ParkingBrake"/>).
    /// True when the brake reads set; false when it cannot be written or did not set.
    /// </summary>
    internal async Task<bool> FoSetParkingBrakeAsync(SimConnectManager sim, CancellationToken ct)
    {
        Sim = sim;
        return await FoSetParkingBrakeCoreAsync(WriteIoFor(sim), ct);
    }

    internal async Task<bool> FoSetParkingBrakeCoreAsync(Bbs757SimIo io, CancellationToken ct)
    {
        if (!_byNode.TryGetValue(Bbs757ParkingBrake.Node, out var control)) return false;
        if (await FoSetCoreAsync(control, 1, io, ct, pollForLanding: false)) return true;
        if (!io.CanWrite() || _lifetime.IsCancellationRequested || ct.IsCancellationRequested) return false;
        if (!Bbs757ParkingBrake.ShouldRetry(await io.ReadFresh(control.Node, ct), set: true, ParkingBrakeRealistic()))
            return false;   // unread, already set, or EASY mode: never retry blind
        try
        {
            OnOwnWrite(control.Node);   // plan 13
            return await Bbs757ParkingBrake.RetryWithFullBrakingAsync(io, set: true, ReadBackDelayMs, ct);
        }
        catch (OperationCanceledException) { return false; }
        finally { io.RequestRead(control.Node); }
    }

    /// <summary>A registered variable read fresh, for the First Officer's verifications; null unread.</summary>
    internal Task<double?> FoReadAsync(SimConnectManager sim, string key, CancellationToken ct)
        => WriteIoFor(sim).ReadFresh(key, ct);

    /// <summary>A registered variable's cached value (the window polls the First Officer's controls each second); null unread.</summary>
    internal double? FoCached(SimConnectManager sim, string key) => WriteIoFor(sim).Cached(key);

    /// <summary>
    /// A positional control written NOW, planned from the cache, with no read-back: the First Officer
    /// window is closing and an engine start a stopped flow left short of its fuel must reach the sim
    /// before the window lets go of the aircraft (the MD-11 pushes its START switch in the same way).
    /// True when sent or already there; false for an unknown, guarded or momentary control, an unread
    /// value, or a write that cannot land.
    /// </summary>
    internal bool FoWriteNow(SimConnectManager sim, string node, double target)
        => _byNode.TryGetValue(node, out var control) && FoWriteNowCore(control, target, WriteIoFor(sim));

    /// <summary>The testable core of <see cref="FoWriteNow"/>.</summary>
    internal bool FoWriteNowCore(Bbs757Control control, double target, Bbs757SimIo io)
    {
        if (Bbs757Write.IsMomentary(control) || !string.IsNullOrWhiteSpace(control.Guard) || !io.CanWrite()) return false;
        var plan = Bbs757Write.ToPosition(control, io.Cached(control.Node), target);
        if (plan.Outcome == Bbs757WriteOutcome.AtPosition) return true;
        if (plan.Outcome == Bbs757WriteOutcome.Refused) return false;
        OnOwnWrite(control.Node);   // plan 13
        foreach (var call in plan.Calls) io.Write(call);
        return true;
    }
}
