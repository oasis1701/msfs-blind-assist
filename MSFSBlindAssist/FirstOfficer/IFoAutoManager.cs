namespace MSFSBlindAssist.FirstOfficer;

public interface IFoAutoManager
{
    bool AutoGearUpEnabled { get; set; }
    bool AutoGearDownEnabled { get; set; }
    bool AutoFlapsEnabled { get; set; }
    bool AutoApEnabled { get; set; }
    void Reset();
    void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts);
}
