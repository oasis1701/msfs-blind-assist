using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the MCDU area registration's resume rule (review round 2, B7). Register() used to be one
/// all-or-nothing block behind a latch: a call that failed part-way had already made some of its
/// SimConnect calls, so the next retry re-ran the whole block, re-issuing those against a
/// connection where they had already succeeded — including mapping the area name again, which
/// SimConnect answers with DUPLICATE_ID, but only later and asynchronously; nothing in the retry
/// throws, so the old latch still completed. The tracker records each step the moment its call
/// returns, so a retry resumes at the one that failed instead of redoing what already succeeded.
/// </summary>
public class Md11McduRegistrationStepsTests
{
    [Fact]
    public void The_name_is_mapped_first_then_the_three_windows_then_their_structs()
    {
        Assert.Equal(new[]
        {
            Md11McduRegistrationStep.NameMapped,
            Md11McduRegistrationStep.LeftDefined,
            Md11McduRegistrationStep.CenterDefined,
            Md11McduRegistrationStep.RightDefined,
            Md11McduRegistrationStep.LeftStructRegistered,
            Md11McduRegistrationStep.CenterStructRegistered,
            Md11McduRegistrationStep.RightStructRegistered,
        }, Md11McduRegistrationSteps.Order);
    }

    [Fact]
    public void A_clean_run_makes_every_step_once_in_order_and_completes()
    {
        var steps = new Md11McduRegistrationSteps();
        var made = new List<Md11McduRegistrationStep>();

        Assert.False(steps.IsComplete);
        Assert.Equal(Md11McduRegistrationStep.NameMapped, steps.Next);

        steps.RunRemaining(made.Add);

        Assert.Equal(Md11McduRegistrationSteps.Order, made);
        Assert.True(steps.IsComplete);
        Assert.Null(steps.Next);
    }

    [Fact]
    public void A_retry_after_a_partial_failure_resumes_at_the_failed_step_and_never_maps_the_name_twice()
    {
        var steps = new Md11McduRegistrationSteps();
        var attempts = new List<Md11McduRegistrationStep>();
        var failOnce = true;

        var ex = Assert.Throws<InvalidOperationException>(() => steps.RunRemaining(step =>
        {
            attempts.Add(step);
            if (step == Md11McduRegistrationStep.CenterDefined && failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("send failed");
            }
        }));
        Assert.Equal("send failed", ex.Message);
        Assert.False(steps.IsComplete);
        Assert.Equal(Md11McduRegistrationStep.CenterDefined, steps.Next);   // the failed step is still owed

        steps.RunRemaining(attempts.Add);                                    // the next MD-11 load's Register()

        Assert.True(steps.IsComplete);
        Assert.Equal(new[]
        {
            Md11McduRegistrationStep.NameMapped,
            Md11McduRegistrationStep.LeftDefined,
            Md11McduRegistrationStep.CenterDefined,           // failed
            Md11McduRegistrationStep.CenterDefined,           // the retry starts here...
            Md11McduRegistrationStep.RightDefined,
            Md11McduRegistrationStep.LeftStructRegistered,
            Md11McduRegistrationStep.CenterStructRegistered,
            Md11McduRegistrationStep.RightStructRegistered,
        }, attempts);
        Assert.Single(attempts, s => s == Md11McduRegistrationStep.NameMapped);   // ...and never re-maps the name
    }

    [Fact]
    public void A_failure_at_the_very_first_step_leaves_every_step_owed()
    {
        var steps = new Md11McduRegistrationSteps();

        Assert.Throws<InvalidOperationException>(() => steps.RunRemaining(_ => throw new InvalidOperationException("send failed")));

        Assert.False(steps.IsComplete);
        Assert.Equal(Md11McduRegistrationStep.NameMapped, steps.Next);
    }

    [Fact]
    public void A_completed_registration_makes_no_call_at_all()
    {
        var steps = new Md11McduRegistrationSteps();
        steps.RunRemaining(_ => { });

        steps.RunRemaining(_ => throw new InvalidOperationException("a completed registration must not call SimConnect again"));

        Assert.True(steps.IsComplete);
    }
}
