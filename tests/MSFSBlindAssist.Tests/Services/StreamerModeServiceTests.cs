using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class StreamerModeServiceTests
{
    private class FakeCameraIo : ICameraViewIo
    {
        public List<(int Type, int Index)> SetCalls { get; } = new();
        public List<int> SetTypeCalls { get; } = new();
        public List<int> SetIndexCalls { get; } = new();

        public Task<CameraViewReading?> ReadAsync(int timeoutMs) => Task.FromResult<CameraViewReading?>(null);

        public bool Set(int viewType, int viewIndex)
        {
            SetCalls.Add((viewType, viewIndex));
            return true;
        }

        public bool SetViewType(int viewType)
        {
            SetTypeCalls.Add(viewType);
            return true;
        }

        public bool SetViewIndex(int viewIndex)
        {
            SetIndexCalls.Add(viewIndex);
            return true;
        }
    }

    private class FakeAircraft : BaseAircraftDefinition
    {
        public override string AircraftName => "Fake Aircraft";
        public override string AircraftCode => "FAKE";

        public override Dictionary<string, List<string>> GetPanelStructure() => new();
        protected override Dictionary<string, List<string>> BuildPanelControls() => new();
        public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();
        public override Dictionary<string, string> GetButtonStateMapping() => new();
        public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
        public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
        public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
        public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;
        protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables() => new();

        public override int? GetCameraViewForTarget(CockpitCameraTarget target) => target switch
        {
            CockpitCameraTarget.Overhead => 4,
            CockpitCameraTarget.Pedestal => 5,
            CockpitCameraTarget.Glareshield => 6,
            CockpitCameraTarget.MainPanel => 7,
            CockpitCameraTarget.MCDU => 0,
            CockpitCameraTarget.EFB => 2,
            _ => null
        };
    }

    [Fact]
    public async Task WhenDisabled_ManualActionDoesNothing()
    {
        var io = new FakeCameraIo();
        var settings = new UserSettings { StreamerModeEnabled = false };
        var service = new StreamerModeService(io, () => settings, delay: _ => Task.CompletedTask);
        var aircraft = new FakeAircraft();

        bool result = await service.PrepareManualActionAsync("Overhead", aircraft);

        Assert.False(result);
        Assert.Empty(io.SetCalls);
        Assert.Equal(CockpitCameraTarget.Forward, service.CurrentTarget);
    }

    [Fact]
    public async Task WhenEnabled_ManualActionMovesCameraToSection()
    {
        var io = new FakeCameraIo();
        var settings = new UserSettings { StreamerModeEnabled = true };
        var service = new StreamerModeService(io, () => settings, delay: _ => Task.CompletedTask);
        var aircraft = new FakeAircraft();

        bool result = await service.PrepareManualActionAsync("Overhead", aircraft);

        Assert.True(result);
        Assert.Single(io.SetCalls);
        Assert.Equal((2, 4), io.SetCalls[0]);
        Assert.Equal(CockpitCameraTarget.Overhead, service.CurrentTarget);
    }

    [Fact]
    public async Task WhenEnabled_GroupedFlowStepsInSameSectionDoNotReMoveCamera()
    {
        var io = new FakeCameraIo();
        var settings = new UserSettings { StreamerModeEnabled = true };
        var service = new StreamerModeService(io, () => settings, delay: _ => Task.CompletedTask);
        var aircraft = new FakeAircraft();

        service.BeginFlow();
        Assert.True(service.IsInFlow);

        // Step 1: in Overhead
        await service.PrepareFlowStepAsync("Overhead", aircraft);
        Assert.Single(io.SetCalls);
        Assert.Equal((2, 4), io.SetCalls[0]);

        // Step 2: also in Overhead (grouped action)
        await service.PrepareFlowStepAsync("Overhead", aircraft);
        Assert.Single(io.SetCalls); // No second move call!

        // Step 3: in Glareshield
        await service.PrepareFlowStepAsync("Glareshield", aircraft);
        Assert.Equal(2, io.SetCalls.Count);
        Assert.Equal((2, 6), io.SetCalls[1]);
        Assert.Equal(CockpitCameraTarget.Glareshield, service.CurrentTarget);

        // End flow restores forward
        await service.EndFlowAsync();
        Assert.False(service.IsInFlow);
        Assert.Equal(CockpitCameraTarget.Forward, service.CurrentTarget);
    }

    [Fact]
    public async Task FocusCamera_TogglesCloseUpAndRestoresForward()
    {
        var io = new FakeCameraIo();
        var settings = new UserSettings { StreamerModeEnabled = true };
        var service = new StreamerModeService(io, () => settings, delay: _ => Task.CompletedTask);
        var aircraft = new FakeAircraft();

        // Focus MCDU
        bool focused = await service.SetCameraFocusAsync(CockpitCameraTarget.MCDU, true, aircraft);
        Assert.True(focused);
        Assert.True(service.IsFocused);
        Assert.Equal(CockpitCameraTarget.MCDU, service.FocusedTarget);
        Assert.Equal((2, 0), io.SetCalls[^1]);

        // Manual action does not override focus
        bool manualResult = await service.PrepareManualActionAsync("Overhead", aircraft);
        Assert.False(manualResult);
        Assert.Equal(CockpitCameraTarget.MCDU, service.CurrentTarget);

        // Unfocus restores forward
        bool unfocused = await service.SetCameraFocusAsync(CockpitCameraTarget.MCDU, false, aircraft);
        Assert.True(unfocused);
        Assert.False(service.IsFocused);
        Assert.Equal(CockpitCameraTarget.Forward, service.CurrentTarget);
    }
}

