namespace MSFSBlindAssist.Services;

/// <summary>
/// The view a failed camera restore still owes the pilot, remembered across display reads.
///
/// <para>
/// A display read that cannot put the camera back leaves it on the instrument view it used. The
/// next read then takes THAT as "where the pilot was" — so it faithfully returns them to it and
/// reports success. The one warning the pilot got is never repeated and the app can never get
/// them home again. Remembering the view the restore could not reach is what turns that into a
/// debt the next read can settle.
/// </para>
///
/// <para>
/// It has to outlive the read, and a read builds a fresh <see cref="InstrumentViewSwitcher"/>
/// each time, so the memory is app-wide — one camera, one simulator, exactly like
/// <see cref="DisplayReadGate"/> beside it. <see cref="DisplayReadGate.Shared"/> serialises the
/// captures that touch this, so there is never more than one reader or writer in flight.
/// </para>
///
/// <para>
/// What settles the debt lives in <see cref="CameraHomePlan"/>, not here: this type only holds
/// the value. An instance rather than a bare static so tests get their own; production shares
/// <see cref="Shared"/>.
/// </para>
/// </summary>
public sealed class CameraHome
{
    /// <summary>The one memory every production display read uses.</summary>
    public static readonly CameraHome Shared = new();

    // A lock, not Volatile: the value is a nullable STRUCT, so it is three fields plus a flag and
    // a torn read is possible in a way it would not be for a reference. The lock is uncontended in
    // practice — DisplayReadGate.Shared serialises every path that touches this.
    private readonly object _lock = new();
    private CameraViewReading? _owed;

    /// <summary>The view still owed to the pilot, or null when nothing is owed.</summary>
    public CameraViewReading? Owed
    {
        get { lock (_lock) return _owed; }
    }

    /// <summary>
    /// Records a view a restore could not reach. A null reading owes nothing — there is no point
    /// remembering a home we never knew, and it must not clobber a real debt from an earlier read.
    /// </summary>
    public void Owe(CameraViewReading? home)
    {
        if (home is null) return;
        lock (_lock) _owed = home;
    }

    /// <summary>Forgets the debt: a restore reached it, or the pilot moved the camera themselves.</summary>
    public void Clear()
    {
        lock (_lock) _owed = null;
    }
}
