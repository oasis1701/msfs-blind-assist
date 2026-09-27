namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>
/// One call of the MCDU area's registration (<see cref="Md11McduDataManager.Register"/>), in the
/// order the calls are made: the area name first, then each window's definition, then each
/// window's struct (a struct is registered against its own definition's id, so it comes after it).
/// </summary>
internal enum Md11McduRegistrationStep
{
    NameMapped,
    LeftDefined,
    CenterDefined,
    RightDefined,
    LeftStructRegistered,
    CenterStructRegistered,
    RightStructRegistered,
}

/// <summary>
/// How far the MCDU registration got on this connection, so a retry after a partial failure
/// resumes at the step that failed instead of starting over.
///
/// Starting over is not harmless, even though nothing in it throws. <c>MapClientDataNameToID</c>
/// and <c>AddToClientDataDefinition</c> register state with SimConnect and return normally even
/// when they repeat a name or id already registered on the connection — SimConnect reports a
/// repeated name as DUPLICATE_ID, but only later, through the asynchronous
/// <c>OnRecvException</c> callback, never by throwing at the call. <c>RegisterStruct</c> makes no
/// SimConnect call at all; it only registers the struct marshaller in the managed wrapper. So the
/// old all-or-nothing latch did not get stuck: a retry after a part-way failure re-issued every
/// step that had already succeeded — re-adding a definition already defined on the connection —
/// and still closed. That needless re-issue is the harm the step tracker removes. Each window's
/// struct registration is a step of its own because it is its own call on its own id; resuming
/// after the last completed step needs that granularity.
///
/// Pure — no SimConnect type — so the resume rule is unit-tested (Md11McduRegistrationStepsTests)
/// and the manager's test-seam constructor can run the field initializer that creates it; the
/// manager supplies the calls. Used on the UI thread only (InitializePMDG), as the registration
/// always was.
/// </summary>
internal sealed class Md11McduRegistrationSteps
{
    /// <summary>Every step, in the order it is made.</summary>
    public static readonly IReadOnlyList<Md11McduRegistrationStep> Order = new[]
    {
        Md11McduRegistrationStep.NameMapped,
        Md11McduRegistrationStep.LeftDefined,
        Md11McduRegistrationStep.CenterDefined,
        Md11McduRegistrationStep.RightDefined,
        Md11McduRegistrationStep.LeftStructRegistered,
        Md11McduRegistrationStep.CenterStructRegistered,
        Md11McduRegistrationStep.RightStructRegistered,
    };

    private int _completed;

    /// <summary>True once every step has been made.</summary>
    public bool IsComplete => _completed == Order.Count;

    /// <summary>The step still owed — where the next <see cref="RunRemaining"/> starts — or null when complete.</summary>
    public Md11McduRegistrationStep? Next => IsComplete ? null : Order[_completed];

    /// <summary>
    /// Makes every step not yet made, in order, through <paramref name="perform"/>, recording each
    /// the moment its call returns. A step that throws is NOT recorded: it, and every step after
    /// it, is where the next call starts, and the exception propagates to the caller. Once
    /// complete, makes no call at all.
    /// </summary>
    public void RunRemaining(Action<Md11McduRegistrationStep> perform)
    {
        while (_completed < Order.Count)
        {
            perform(Order[_completed]);
            _completed++;
        }
    }
}
