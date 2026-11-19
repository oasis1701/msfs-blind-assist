namespace MSFSBlindAssist.Utils;

/// <summary>
/// The wall clock the taxi / landing-rollout guidance reads. In the app it IS
/// <see cref="DateTime.UtcNow"/>; the virtual-pilot harness (tools/VirtualPilot) installs
/// <see cref="Override"/> so a simulated landing or taxi runs faster than real time while
/// every cooldown, persistence window and grace period still sees simulated seconds.
/// Never set <see cref="Override"/> in the app.
/// </summary>
public static class SimClock
{
    internal static Func<DateTime>? Override;

    public static DateTime UtcNow => Override?.Invoke() ?? DateTime.UtcNow;
}
