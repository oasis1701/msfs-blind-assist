namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// WHEN the A380's derived FCU altitude mode is spoken. <see cref="AltitudeManagedState"/>
/// owns the RULE (is the aircraft flying the FMS profile or the FCU altitude?); this owns the
/// sequencing around it — baseline-first, readiness, the autoland transient, and the
/// re-baseline a SimConnect reconnect needs.
///
/// It is a plain class with no SimConnect dependency precisely so all of that is unit-testable:
/// every one of the behaviours below was a defect found by reading rather than by a test.
/// </summary>
public sealed class AltitudeModeTracker
{
    private const int NoVerticalMode = 0;

    private int _vertical = -1;      // -1 = never delivered (0 is a real value, "None")
    private int _lateral;
    private int _armed = -1;         // -1 = never delivered
    private bool? _spoken;           // last value ACTUALLY announced, not last computed

    /// <summary>
    /// True once BOTH primary inputs have reported. They arrive on independent streams, so a
    /// half-delivered view would otherwise be published as a confident answer — consumers must
    /// render "--" (or omit the word) while this is false rather than guess.
    /// </summary>
    public bool IsKnown => _vertical >= 0 && _armed >= 0;

    /// <summary>Managed vs selected. Meaningful only when <see cref="IsKnown"/>.</summary>
    public bool IsManaged { get; private set; }

    /// <summary>
    /// Re-baseline. Called on a SimConnect reconnect as well as an aircraft switch: the
    /// aircraft definition object survives a reconnect while SimConnect clears its value cache,
    /// so without this flight 2 compares its first reading against a value spoken on flight 1
    /// and announces a phantom change at rotation.
    /// </summary>
    public void Reset()
    {
        _vertical = -1;
        _lateral = 0;
        _armed = -1;
        _spoken = null;
        IsManaged = false;
    }

    public string? OnVerticalMode(int mode)
    {
        _vertical = mode;
        return Recompute();
    }

    public string? OnVerticalArmed(int armedBitmask)
    {
        _armed = armedBitmask;
        return Recompute();
    }

    public string? OnLateralMode(int mode)
    {
        _lateral = mode;
        return Recompute();
    }

    private string? Recompute()
    {
        if (!IsKnown) return null;

        // Say nothing while the FMA shows NO vertical mode, and do not let that state move the
        // published value either. Two situations reach it: nothing is flying the aircraft
        // vertically (cold and dark, FD and AP off), or the FMA is mid-autoland, where the
        // #10855 shim files LAND/FLARE/ROLL OUT under the LATERAL mode and leaves the vertical
        // mode at None. Publishing there would read "Selected" through the flare.
        // ⚠️ THIS return, plus preserving IsManaged below it, is what keeps the flare quiet —
        // NOT AltitudeManagedState's lateral autoland rescue, which sits below it and therefore
        // cannot run on this path at all. Delete this and the flare announces "Selected".
        if (_vertical == NoVerticalMode)
        {
            _spoken ??= IsManaged;
            return null;
        }

        bool managed = AltitudeManagedState.IsManaged(_vertical, _armed, _lateral);
        IsManaged = managed;

        // Compared against the last value SPOKEN, not the last computed — that is what lets the
        // suppressed transient above pass through without leaving a phantom edge behind it.
        if (_spoken == managed) return null;

        bool firstReading = _spoken == null;
        _spoken = managed;
        return firstReading ? null : $"Altitude Mode: {AltitudeManagedState.Text(managed)}";
    }
}
