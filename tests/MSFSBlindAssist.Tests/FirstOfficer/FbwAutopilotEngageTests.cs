using System;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-21: the FlyByWire family's universal auto-engage. FlyByWire captures the stock
/// AUTOPILOT_ON into a flag nothing reads, so before 2026-10-09 the app announced
/// "Autopilot engaged" over an autopilot that never engaged. The engage is now the cockpit's
/// AP 1 push — a TOGGLE — sent only while no autopilot reads engaged, verified on AP 1 / AP 2
/// active, and spaced 3 s apart. Every other aircraft keeps the every-tick retry.
/// </summary>
public class FbwAutopilotEngageTests
{
    [Theory]
    [InlineData(typeof(PMDG777Definition))]
    [InlineData(typeof(PMDG737Definition))]
    [InlineData(typeof(IFly737MAXDefinition))]
    [InlineData(typeof(FenixA320Definition))]
    [InlineData(typeof(TFDiMD11Definition))]
    [InlineData(typeof(HorizonSim787Definition))]
    public void Other_aircraft_keep_the_every_tick_retry(Type defType)
    {
        var def = (IAircraftDefinition)Activator.CreateInstance(defType)!;
        try { Assert.Equal(0, def.AutopilotEngageRetryIntervalMs); }
        finally { (def as IDisposable)?.Dispose(); }
    }
}
