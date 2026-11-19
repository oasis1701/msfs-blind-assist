namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>What the executor's safety rules need from the state evaluator.</summary>
public interface IMd11FoFlightState
{
    /// <summary>Engine 1, 2 or 3 N2 in percent; NaN until fed.</summary>
    double EngineN2(int engine);
    /// <summary>SIM ON GROUND; null until read.</summary>
    bool? OnGround { get; }
}
