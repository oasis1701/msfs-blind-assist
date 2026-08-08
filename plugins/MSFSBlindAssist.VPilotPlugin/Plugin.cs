using System;
using RossCarlson.Vatsim.Vpilot.Plugins;
using RossCarlson.Vatsim.Vpilot.Plugins.Events;

namespace MSFSBlindAssist.VPilotPlugin
{
    /// <summary>
    /// Forwards five VATSIM network events to MSFS Blind Assist over a named pipe.
    /// All gating, wording and speech happen app-side — this plugin makes no decisions.
    /// </summary>
    public class Plugin : IPlugin
    {
        private IBroker _vPilot;
        private PipeClient _pipe;

        public string Name { get { return "MSFS Blind Assist"; } }

        public void Initialize(IBroker broker)
        {
            _vPilot = broker;

            try
            {
                PluginLog.Info("Plugin initializing");
                _pipe = new PipeClient();

                // Unsubscribe first: vPilot can call Initialize twice on a reload, and a
                // double subscription would send every event twice.
                _vPilot.NetworkConnected -= OnNetworkConnected;
                _vPilot.NetworkDisconnected -= OnNetworkDisconnected;
                _vPilot.PrivateMessageReceived -= OnPrivateMessageReceived;
                _vPilot.RadioMessageReceived -= OnRadioMessageReceived;
                _vPilot.SelcalAlertReceived -= OnSelcalAlertReceived;

                _vPilot.NetworkConnected += OnNetworkConnected;
                _vPilot.NetworkDisconnected += OnNetworkDisconnected;
                _vPilot.PrivateMessageReceived += OnPrivateMessageReceived;
                _vPilot.RadioMessageReceived += OnRadioMessageReceived;
                _vPilot.SelcalAlertReceived += OnSelcalAlertReceived;

                PluginLog.Info("Plugin loaded — all events subscribed");
                _vPilot.PostDebugMessage("MSFS Blind Assist plugin loaded");
            }
            catch (Exception ex)
            {
                PluginLog.Error("Plugin failed to initialize: " + ex);
            }
        }

        private void OnNetworkConnected(object sender, NetworkConnectedEventArgs e)
        {
            _pipe.Send("connected", e.Callsign, "");
        }

        private void OnNetworkDisconnected(object sender, EventArgs e)
        {
            _pipe.Send("disconnected", "", "");
        }

        private void OnPrivateMessageReceived(object sender, PrivateMessageReceivedEventArgs e)
        {
            _pipe.Send("private_message", e.From, e.Message);
        }

        private void OnRadioMessageReceived(object sender, RadioMessageReceivedEventArgs e)
        {
            _pipe.Send("radio_message", e.From, e.Message);
        }

        private void OnSelcalAlertReceived(object sender, SelcalAlertReceivedEventArgs e)
        {
            _pipe.Send("selcal", e.From, "");
        }
    }
}
