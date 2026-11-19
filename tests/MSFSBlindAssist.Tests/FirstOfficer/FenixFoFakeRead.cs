using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.Services;
using Xunit;
using Answer = System.Collections.Generic.IReadOnlyDictionary<string, MSFSBlindAssist.Services.FenixDataValue>;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The fake data-service read the Fenix First Officer's once-a-second N2 poll is tested with, in place
/// of <see cref="FenixDataService.ReadManyAsync"/>. Each read the poller makes takes the next task of
/// the script (the last one repeats), so a test decides exactly when each answer exists: a completed
/// task answers at once, and a task the test completes later holds that read open.
///
/// Waiting: the poller's interval stays its one-second default, so a test waits for the read to be
/// called, or for the effect of an answer, never for a guessed time. The task sources are created
/// without RunContinuationsAsynchronously, so completing one runs the poller's hand-off to the
/// evaluator on the completing thread (usually before the call returns); the tests still wait on the
/// effect, as that is not guaranteed.
/// </summary>
internal sealed class FenixFoFakeRead
{
    /// <summary>The longest any test waits for something that must happen.</summary>
    public static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly Task<Answer>[] _script;
    private readonly List<string[]> _asked = new();
    private readonly List<CancellationToken> _tokens = new();
    private readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="script">What each read returns, in order; the last repeats. None: every read answers
    /// with nothing.</param>
    public FenixFoFakeRead(params Task<Answer>[] script)
        => _script = script.Length > 0 ? script : new[] { Task.FromResult(Of()) };

    /// <summary>Completes once the poller has made its first read.</summary>
    public Task FirstReadStarted => _firstRead.Task;

    /// <summary>The names each read was asked for, in order.</summary>
    public string[][] Asked { get { lock (_lock) return _asked.ToArray(); } }

    /// <summary>The token each read was given, in order.</summary>
    public CancellationToken[] Tokens { get { lock (_lock) return _tokens.ToArray(); } }

    /// <summary>The function to hand the evaluator's <c>AttachFenix</c> as its read.</summary>
    public Task<Answer> Read(IReadOnlyList<string> names, CancellationToken ct)
    {
        Task<Answer> answer;
        lock (_lock)
        {
            _asked.Add(names.ToArray());
            _tokens.Add(ct);
            answer = _script[Math.Min(_asked.Count, _script.Length) - 1];
        }
        _firstRead.TrySetResult();
        return answer;
    }

    // ---- Building answers -----------------------------------------------------------------------

    public static FenixDataValue Num(double n) => new(FenixValueKind.Number, n, false, null, "");

    /// <summary>What the service answers for an engine that is off (measured: null).</summary>
    public static FenixDataValue Null { get; } = new(FenixValueKind.Null, 0, false, null, "null");

    /// <summary>An answer holding the given values by name.</summary>
    public static Answer Of(params (string Name, FenixDataValue Value)[] pairs)
        => pairs.ToDictionary(p => p.Name, p => p.Value);

    /// <summary>The two N2 values the poll asks for.</summary>
    public static Answer N2(FenixDataValue engine1, FenixDataValue engine2)
        => Of((FenixFoLiveReadings.Engine1N2, engine1), (FenixFoLiveReadings.Engine2N2, engine2));

    /// <summary>A read that never answers: every read stays in flight, so a test can tell the poll was
    /// ended by its token being cancelled (a read that has already returned has nothing left to cancel).</summary>
    public static FenixFoFakeRead Held()
        => new(new TaskCompletionSource<Answer>().Task);

    /// <summary>Waits (up to <see cref="Generous"/>) until the condition holds, and fails the test if it never does.</summary>
    public static void WaitUntil(Func<bool> condition, string what)
        => Assert.True(SpinWait.SpinUntil(condition, Generous), "Never happened: " + what);
}
