namespace MSFSBlindAssist.FirstOfficer;

public interface IFoPhaseMonitor
{
    void SetThresholds(int transAltFt, int transLevelFt);
    void Reset();
    void Update(double altitudeFt, double verticalSpeedFpm);
}
