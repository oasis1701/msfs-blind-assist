using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's ILS control panel display, read from BlueBird's own variable (plan 19, task 5):
/// <c>L:BBS_757_ILS_FREQ</c> holds the panel's frequency and -1 while parked, <c>L:BBS_ILS_COURSE</c> the
/// course (measured 2026-11-05, [B757-10]).
/// </summary>
public class Bbs757IlsVariableDisplayTests
{
    [Fact]
    public async Task The_reader_reads_the_frequency_and_the_course_fresh()
    {
        var asked = new List<string>();
        using var display = new Bbs757IlsVariableDisplay((key, _) =>
        {
            asked.Add(key);
            return Task.FromResult<double?>(key == Bbs757NavRad.IlsFrequencyKey ? 108.15 : 95);
        });

        var reading = await display.ReadAsync(CancellationToken.None);

        Assert.Equal(new Bbs757IlsReading(false, 108.15, 95), reading);
        Assert.Contains(Bbs757NavRad.IlsFrequencyKey, asked);
        Assert.Contains(Bbs757NavRad.IlsCourseKey, asked);
    }

    [Fact]
    public async Task The_reader_says_parked_for_minus_one()
    {
        using var display = new Bbs757IlsVariableDisplay((key, _) =>
            Task.FromResult<double?>(key == Bbs757NavRad.IlsFrequencyKey ? -1 : 95));

        Assert.Equal(new Bbs757IlsReading(true, null, 95), await display.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_canceled_read_answers_null()
    {
        using var display = new Bbs757IlsVariableDisplay((_, _) => throw new OperationCanceledException());

        Assert.Null(await display.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_variable_that_is_absent_answers_null()
    {
        using var display = new Bbs757IlsVariableDisplay((_, _) => Task.FromResult<double?>(null));

        Assert.Null(await display.ReadAsync(CancellationToken.None));
    }
}

/// <summary>
/// The ILS display as the definition's tests see it: what a read answers (null: the display could not be
/// read), every read counted and, when a log is given, written into it as "read display".
/// </summary>
internal sealed class FakeIlsDisplay : IBbs757IlsDisplay
{
    private readonly List<string>? _log;

    public FakeIlsDisplay(List<string>? log = null) => _log = log;

    /// <summary>What the next read answers; null: the display could not be read.</summary>
    public Func<Bbs757IlsReading?> Answer { get; set; } = () => null;

    public int Reads { get; private set; }

    public Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct)
    {
        Reads++;
        _log?.Add("read display");
        return Task.FromResult(ct.IsCancellationRequested ? null : Answer());
    }

    public void Dispose() { }
}
