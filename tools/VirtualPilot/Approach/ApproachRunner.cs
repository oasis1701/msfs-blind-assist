namespace VirtualPilot.Approach;

/// <summary>
/// The `approach` mode: flies simulated approaches through the real VisualGuidanceManager on two
/// synthetic runways, so it needs no navdata database. See docs/virtual-pilot.md, "Approaches".
/// </summary>
static class ApproachRunner
{
    public static int Run(Options opt)
    {
        if (opt.SelfTest) return ApproachSelfTest.Run();
        return 0;
    }
}
