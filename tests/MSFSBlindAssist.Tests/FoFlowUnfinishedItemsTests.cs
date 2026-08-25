namespace MSFSBlindAssist.Tests;

/// <summary>
/// Intended scope: pin FlowManager's <c>_unfinishedChecklistItemIds</c> lifetime — that a
/// Skip-policy step failure records its <c>CompletesChecklistItemId</c>; that Stop, an
/// exhausted RetryThenStop, and the "Already set" skip-condition early-continue do NOT
/// (the first two never reach FlowCompleted at all; the third is a success); and that the
/// set is cleared at the start of the next run (RunFlowAsync's
/// <c>_unfinishedChecklistItemIds.Clear()</c>).
///
/// This is currently verified only by reading FlowManager.cs — it could not be exercised
/// here. FlowManager's constructor requires a real <c>ScreenReaderAnnouncer</c>
/// (MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer), and every method that would need
/// to run to reach this logic (RunFlowAsync, ExecuteStepAsync) calls into it directly —
/// AnnounceImmediate on flow start, before a single step is examined, then Announce/
/// AnnounceImmediate on every subsequent branch. ScreenReaderAnnouncer:
///   - has no parameterless constructor (only <c>ScreenReaderAnnouncer(IntPtr handle)</c>),
///   - is a concrete class with no interface and no virtual members, so it cannot be
///     substituted with a test double without adding a seam to production code — out of
///     scope for this change, which touches only the five items in the review it answers,
///   - and its real constructor has production side effects a unit test must not trigger:
///     it loads the Tolk native DLL, starts an NVDA Controller Client, and constructs a
///     System.Speech SpeechSynthesizer — i.e. constructing one in a test process can
///     probe/drive whatever screen reader happens to be running on the test machine.
///
/// This is not a novel obstacle: MSFSBlindAssist.Tests.FirstOfficer.IFly737AutoManagerTests
/// and IFly737ExecutorTests already document the identical blocker for classes that take a
/// ScreenReaderAnnouncer, and fall back to testing internal static/pure seams the classes
/// expose for exactly this reason. FlowManager exposes no such seam for the unfinished-items
/// bookkeeping — it is private instance state (<c>_unfinishedChecklistItemIds</c>) touched
/// only from inside RunFlowAsync/ExecuteStepAsync, both instance methods requiring the full
/// announcer-dependent construction above.
///
/// Passing <c>null!</c> for the announcer does not work around this: RunFlowAsync's very
/// first statement after clearing the set is
/// <c>_announcer.AnnounceImmediate($"{flow.Name} flow started")</c>, so the run would throw
/// a NullReferenceException before a single step — including the ones this file exists to
/// cover — ever executes.
///
/// Per the review instructions, this is reported rather than worked around with reflection
/// (e.g. constructing FlowManager via RuntimeHelpers.GetUninitializedObject, or reflecting
/// a fake announcer into an interface it doesn't implement) or by adding a new seam to
/// FlowManager, both of which would go beyond the five items this change is scoped to.
///
/// What IS covered: the same four properties are pinned at the ChecklistManager layer by
/// tests/MSFSBlindAssist.Tests/FoFlowCompletionExclusionTests.cs and FoChecklistLatchTests.cs
/// — i.e. that MarkGroupComplete's <c>excludeItemIds</c> parameter (which FirstOfficerForm
/// populates from FlowManager.UnfinishedChecklistItemIds) behaves correctly once FlowManager
/// hands it the right set. What is NOT covered here is FlowManager's own bookkeeping that
/// decides which ids go into that set and when it resets — that half remains verified by
/// reading, as stated above.
/// </summary>
public class FoFlowUnfinishedItemsTests
{
    // Intentionally no [Fact]s — see the class summary for why real FlowManager runs
    // cannot be constructed in this test project without either a production seam change
    // (out of scope) or a reflection-based workaround (explicitly disallowed).
}
