// Closed-loop auto-AP contract for the iFly MAX8 (same NG AFDS airframe rules as
// the PMDG 737: CMD inhibited below 400 ft RA after takeoff; engaged readback must
// be null — never false — before the first SDK snapshot, because a guessed false
// would make the universal service's retry press a click that could disengage an
// engaged AP), plus the public ApplyUIVariable write path used by the FO executor.

namespace MSFSBlindAssist.Tests.FirstOfficer;

using MSFSBlindAssist.Aircraft;

public class IFly737DefApTests
{
    [Fact]
    public void MinimumApEngageAltitude_Is400()
        => Assert.Equal(400, new IFly737MAXDefinition().MinimumAutopilotEngageAltitudeAgl);

    [Fact]
    public void IsAutopilotEngaged_NullBeforeFirstSnapshot()
        => Assert.Null(new IFly737MAXDefinition().IsAutopilotEngaged(null!));

    [Fact]
    public void ApplyUIVariable_UnknownKey_ReturnsFalse()
        => Assert.False(new IFly737MAXDefinition().ApplyUIVariable(
            "NO_SUCH_KEY", 1, null!, null!));
}
