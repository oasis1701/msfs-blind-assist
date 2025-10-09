using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace FBWBA.SimConnect
{
    public class MobiFlightWasmModule
    {
        private Microsoft.FlightSimulator.SimConnect.SimConnect simConnect;
        private const string CLIENT_NAME = "FBWBA";

        // MobiFlight client data area IDs
        private enum CLIENT_DATA_AREA_ID
        {
            MF_COMMAND = 1000,
            MF_RESPONSE = 1001,
            MF_LVARS = 1002,
            FBWBA_COMMAND = 1003,
            FBWBA_RESPONSE = 1004,
            FBWBA_LVARS = 1005
        }

        // Data definition IDs
        private enum DATA_DEFINITION_ID
        {
            MF_COMMAND_STRING = 2000,
            MF_RESPONSE_STRING = 2001,
            MF_LVAR_DATA = 2002,
            FBWBA_COMMAND_STRING = 2003,
            FBWBA_RESPONSE_STRING = 2004,
            FBWBA_LVAR_DATA = 2005
        }

        // Request IDs
        private enum DATA_REQUEST_ID
        {
            MF_RESPONSE_REQUEST = 3000,
            MF_LVAR_REQUEST = 3001,
            FBWBA_RESPONSE_REQUEST = 3002,
            FBWBA_LVAR_REQUEST = 3003
        }

        // Constants
        private const int MAX_COMMAND_SIZE = 1024;
        private const int MAX_RESPONSE_SIZE = 1024;
        private const int MAX_LVARS_SIZE = 4096;
        private const int MAX_LVARS_COUNT = 64;
        private const int LVAR_VALUE_SIZE = 4; // 4 bytes per float

        // State management
        public bool IsConnected { get; private set; }
        public bool IsRegistered { get; private set; }
        public bool CanSendHVars => IsConnected; // Allow immediate sending through default channel
        public string ConnectionStatus
        {
            get
            {
                if (!IsConnected) return "Disconnected";
                if (IsRegistered) return "Fully Registered";
                if (registrationTimeoutOccurred) return "Timeout - Using Fallback";
                return "Connecting...";
            }
        }

        // Events
        public event EventHandler<string> ConnectionStatusChanged;
        public event EventHandler<MobiFlightLVarUpdateEventArgs> LVarUpdated;
        public event EventHandler<string> ResponseReceived;
        public event EventHandler<MobiFlightLedValueEventArgs> LedValueReceived;

        // Variable tracking
        private Dictionary<string, int> registeredLVars = new Dictionary<string, int>();
        private List<string> lvarList = new List<string>();
        private System.Timers.Timer heartbeatTimer;
        private System.Timers.Timer registrationTimer;
        private bool registrationTimeoutOccurred = false;

        // Default channel L-variable tracking
        private Dictionary<string, int> defaultChannelLVars = new Dictionary<string, int>();
        private List<string> defaultChannelLVarList = new List<string>();
        private int nextDefaultLVarOffset = 0;

        // LED variable reading tracking
        private Dictionary<string, DateTime> pendingLedReads = new Dictionary<string, DateTime>();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct CommandData
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_COMMAND_SIZE)]
            public string command;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ResponseData
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_RESPONSE_SIZE)]
            public string response;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct LVarData
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LVARS_COUNT)]
            public float[] values;
        }

        public class MobiFlightLVarUpdateEventArgs : EventArgs
        {
            public string VariableName { get; set; }
            public float Value { get; set; }
            public int Index { get; set; }
        }

        public class MobiFlightLedValueEventArgs : EventArgs
        {
            public string LedVariable { get; set; }
            public float Value { get; set; }
        }

        public MobiFlightWasmModule(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
        {
            this.simConnect = simConnect;

            // Initialize heartbeat timer
            heartbeatTimer = new System.Timers.Timer(30000); // 30 seconds
            heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
            heartbeatTimer.AutoReset = true;

            // Initialize registration timeout timer
            registrationTimer = new System.Timers.Timer(2000); // 2 seconds timeout
            registrationTimer.Elapsed += RegistrationTimer_Elapsed;
            registrationTimer.AutoReset = false;
        }

        public void Initialize()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[MobiFlight] ===== INITIALIZING MOBIFLIGHT WASM INTEGRATION =====");
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Client Name: {CLIENT_NAME}");

                // Map default MobiFlight client data areas
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Step 1: Mapping client data areas...");
                MapClientDataAreas();

                // Set up data definitions
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Step 2: Setting up data definitions...");
                SetupDataDefinitions();

                // Subscribe to response channels
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Step 3: Subscribing to response channels...");
                SubscribeToResponses();

                // Send a dummy command first (MobiFlight documentation recommends this)
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Step 4: Sending dummy command...");
                SendDummyCommand();

                // Register our client with MobiFlight
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Step 5: Registering client...");
                RegisterClient();

                IsConnected = true;
                ConnectionStatusChanged?.Invoke(this, "MobiFlight WASM connected");
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] ===== INITIALIZATION COMPLETED - Status: {ConnectionStatus} =====");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] ===== INITIALIZATION FAILED: {ex.Message} =====");
                ConnectionStatusChanged?.Invoke(this, $"MobiFlight WASM connection failed: {ex.Message}");
            }
        }

        private void MapClientDataAreas()
        {
            // Map default MobiFlight channels
            simConnect.MapClientDataNameToID("MobiFlight.Command", CLIENT_DATA_AREA_ID.MF_COMMAND);
            simConnect.MapClientDataNameToID("MobiFlight.Response", CLIENT_DATA_AREA_ID.MF_RESPONSE);
            simConnect.MapClientDataNameToID("MobiFlight.LVars", CLIENT_DATA_AREA_ID.MF_LVARS);

            System.Diagnostics.Debug.WriteLine("[MobiFlight] Default client data areas mapped");
        }

        private void SetupDataDefinitions()
        {
            // Command string definition
            simConnect.AddToClientDataDefinition(DATA_DEFINITION_ID.MF_COMMAND_STRING, 0, MAX_COMMAND_SIZE, 0, 0);
            simConnect.AddToClientDataDefinition(DATA_DEFINITION_ID.FBWBA_COMMAND_STRING, 0, MAX_COMMAND_SIZE, 0, 0);

            // Response string definition
            simConnect.AddToClientDataDefinition(DATA_DEFINITION_ID.MF_RESPONSE_STRING, 0, MAX_RESPONSE_SIZE, 0, 0);
            simConnect.AddToClientDataDefinition(DATA_DEFINITION_ID.FBWBA_RESPONSE_STRING, 0, MAX_RESPONSE_SIZE, 0, 0);

            // LVar data definition for both default MF and custom FBWBA channels
            simConnect.AddToClientDataDefinition(DATA_DEFINITION_ID.MF_LVAR_DATA, 0, MAX_LVARS_SIZE, 0, 0);
            simConnect.AddToClientDataDefinition(DATA_DEFINITION_ID.FBWBA_LVAR_DATA, 0, MAX_LVARS_SIZE, 0, 0);

            System.Diagnostics.Debug.WriteLine("[MobiFlight] Data definitions configured");
        }

        private void SubscribeToResponses()
        {
            // Subscribe to MobiFlight response channel
            simConnect.RequestClientData(CLIENT_DATA_AREA_ID.MF_RESPONSE, DATA_REQUEST_ID.MF_RESPONSE_REQUEST,
                DATA_DEFINITION_ID.MF_RESPONSE_STRING, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);

            // Subscribe to MobiFlight LVars channel for default L-variable monitoring
            simConnect.RequestClientData(CLIENT_DATA_AREA_ID.MF_LVARS, DATA_REQUEST_ID.MF_LVAR_REQUEST,
                DATA_DEFINITION_ID.MF_LVAR_DATA, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[MobiFlight] Subscribed to response and LVar channels");
        }

        private void SendDummyCommand()
        {
            // Send a dummy command as recommended by MobiFlight documentation
            // Sometimes the first command is ignored, so we send this first
            SendMFCommand("MF.Ping");
            System.Diagnostics.Debug.WriteLine("[MobiFlight] Sent dummy ping command");
        }

        private void RegisterClient()
        {
            // Send client registration command
            string registerCommand = $"MF.Clients.Add.{CLIENT_NAME}";
            SendMFCommand(registerCommand);

            // Start registration timeout timer
            registrationTimer.Start();

            System.Diagnostics.Debug.WriteLine($"[MobiFlight] Sent client registration: {registerCommand}");
        }

        public void SendMFCommand(string command)
        {
            try
            {
                CommandData cmdData = new CommandData { command = command };
                simConnect.SetClientData(CLIENT_DATA_AREA_ID.MF_COMMAND, DATA_DEFINITION_ID.MF_COMMAND_STRING,
                    SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, cmdData);

                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Sent command: {command}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Failed to send command '{command}': {ex.Message}");
            }
        }

        public void AddDefaultChannelLVar(string lvarName)
        {
            if (defaultChannelLVars.ContainsKey(lvarName))
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] LVar already registered in default channel: {lvarName}");
                return;
            }

            // Register the L-variable at the next available offset
            defaultChannelLVars[lvarName] = nextDefaultLVarOffset;
            defaultChannelLVarList.Add(lvarName);

            // Send the command to MobiFlight
            string command = $"MF.SimVars.Add.(L:{lvarName})";
            SendMFCommand(command);

            System.Diagnostics.Debug.WriteLine($"[MobiFlight] Added default channel LVar: {lvarName} at offset {nextDefaultLVarOffset}");

            // Increment offset for next variable (each float takes 4 bytes)
            nextDefaultLVarOffset += 4;
        }

        public void ReadLedVariable(string lvarName)
        {
            // Use MF.SimVars.Set to directly read and return the L-variable value
            // This sends the value back through the response channel
            string command = $"MF.SimVars.Set.(L:{lvarName})";
            SendMFCommand(command);

            // Track this read request
            pendingLedReads[lvarName] = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[MobiFlight] Reading LED variable directly: {lvarName}");
        }

        private void SendFBWBACommand(string command)
        {
            try
            {
                if (!IsRegistered)
                {
                    System.Diagnostics.Debug.WriteLine($"[MobiFlight] Cannot send command - not registered yet: {command}");
                    return;
                }

                CommandData cmdData = new CommandData { command = command };
                simConnect.SetClientData(CLIENT_DATA_AREA_ID.FBWBA_COMMAND, DATA_DEFINITION_ID.FBWBA_COMMAND_STRING,
                    SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, cmdData);

                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Sent FBWBA command: {command}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Failed to send FBWBA command '{command}': {ex.Message}");
            }
        }

        public void ProcessClientDataResponse(SIMCONNECT_RECV_CLIENT_DATA response)
        {
            try
            {
                if (response.dwRequestID == (uint)DATA_REQUEST_ID.MF_RESPONSE_REQUEST)
                {
                    ResponseData responseData = (ResponseData)response.dwData[0];
                    string responseText = responseData.response?.Trim();

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        System.Diagnostics.Debug.WriteLine($"[MobiFlight] Response: {responseText}");
                        ProcessResponse(responseText);
                        ResponseReceived?.Invoke(this, responseText);
                    }
                }
                else if (response.dwRequestID == (uint)DATA_REQUEST_ID.MF_LVAR_REQUEST)
                {
                    LVarData lvarData = (LVarData)response.dwData[0];
                    ProcessDefaultChannelLVarUpdate(lvarData);
                }
                else if (response.dwRequestID == (uint)DATA_REQUEST_ID.FBWBA_RESPONSE_REQUEST)
                {
                    ResponseData responseData = (ResponseData)response.dwData[0];
                    string responseText = responseData.response?.Trim();

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        System.Diagnostics.Debug.WriteLine($"[MobiFlight] FBWBA Response: {responseText}");
                        ResponseReceived?.Invoke(this, responseText);
                    }
                }
                else if (response.dwRequestID == (uint)DATA_REQUEST_ID.FBWBA_LVAR_REQUEST)
                {
                    LVarData lvarData = (LVarData)response.dwData[0];
                    ProcessLVarUpdate(lvarData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Error processing client data response: {ex.Message}");
            }
        }

        private void ProcessResponse(string response)
        {
            System.Diagnostics.Debug.WriteLine($"[MobiFlight] Received response: {response}");

            if (response.StartsWith($"MF.Clients.Add.{CLIENT_NAME}.Finished"))
            {
                // Stop registration timeout timer
                registrationTimer.Stop();

                System.Diagnostics.Debug.WriteLine("[MobiFlight] ===== CLIENT REGISTRATION SUCCESSFUL =====");

                // Client registration completed - map our custom channels
                MapCustomClientDataAreas();
                SubscribeToCustomChannels();
                IsRegistered = true;

                // Start heartbeat
                heartbeatTimer.Start();

                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Status: {ConnectionStatus} - H-variables ready!");
                ConnectionStatusChanged?.Invoke(this, "MobiFlight WASM client registered");
            }
            else if (response.StartsWith("MF.Pong"))
            {
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Heartbeat pong received - connection healthy");
            }
            else
            {
                // Check if this is a numeric response to a pending LED read
                if (float.TryParse(response, out float value) && pendingLedReads.Count > 0)
                {
                    // Find the most recent LED read request (within last 2 seconds)
                    var recentRead = pendingLedReads
                        .Where(kvp => DateTime.Now - kvp.Value < TimeSpan.FromSeconds(2))
                        .OrderByDescending(kvp => kvp.Value)
                        .FirstOrDefault();

                    if (!recentRead.Equals(default(KeyValuePair<string, DateTime>)))
                    {
                        string ledVariable = recentRead.Key;
                        pendingLedReads.Remove(ledVariable);

                        // Trigger LED value event
                        LedValueReceived?.Invoke(this, new MobiFlightLedValueEventArgs
                        {
                            LedVariable = ledVariable,
                            Value = value
                        });

                        System.Diagnostics.Debug.WriteLine($"[MobiFlight] LED value received: {ledVariable} = {value}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MobiFlight] Unknown response: {response}");
                }
            }
        }

        private void MapCustomClientDataAreas()
        {
            // Map our custom client data areas
            simConnect.MapClientDataNameToID($"{CLIENT_NAME}.Command", CLIENT_DATA_AREA_ID.FBWBA_COMMAND);
            simConnect.MapClientDataNameToID($"{CLIENT_NAME}.Response", CLIENT_DATA_AREA_ID.FBWBA_RESPONSE);
            simConnect.MapClientDataNameToID($"{CLIENT_NAME}.LVars", CLIENT_DATA_AREA_ID.FBWBA_LVARS);

            System.Diagnostics.Debug.WriteLine("[MobiFlight] Custom client data areas mapped");
        }

        private void SubscribeToCustomChannels()
        {
            // Subscribe to our custom response and LVar channels
            simConnect.RequestClientData(CLIENT_DATA_AREA_ID.FBWBA_RESPONSE, DATA_REQUEST_ID.FBWBA_RESPONSE_REQUEST,
                DATA_DEFINITION_ID.FBWBA_RESPONSE_STRING, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);

            simConnect.RequestClientData(CLIENT_DATA_AREA_ID.FBWBA_LVARS, DATA_REQUEST_ID.FBWBA_LVAR_REQUEST,
                DATA_DEFINITION_ID.FBWBA_LVAR_DATA, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);

            System.Diagnostics.Debug.WriteLine("[MobiFlight] Subscribed to custom channels");
        }

        private void ProcessDefaultChannelLVarUpdate(LVarData lvarData)
        {
            try
            {
                // Process default MobiFlight channel LVar updates
                for (int i = 0; i < defaultChannelLVarList.Count && i < MAX_LVARS_COUNT; i++)
                {
                    if (lvarData.values != null && i < lvarData.values.Length)
                    {
                        string varName = defaultChannelLVarList[i];
                        float value = lvarData.values[i];

                        LVarUpdated?.Invoke(this, new MobiFlightLVarUpdateEventArgs
                        {
                            VariableName = varName,
                            Value = value,
                            Index = i
                        });

                        System.Diagnostics.Debug.WriteLine($"[MobiFlight] Default channel LVar updated: {varName} = {value}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Error processing default channel LVar update: {ex.Message}");
            }
        }

        private void ProcessLVarUpdate(LVarData lvarData)
        {
            try
            {
                for (int i = 0; i < lvarList.Count && i < MAX_LVARS_COUNT; i++)
                {
                    if (lvarData.values != null && i < lvarData.values.Length)
                    {
                        string varName = lvarList[i];
                        float value = lvarData.values[i];

                        LVarUpdated?.Invoke(this, new MobiFlightLVarUpdateEventArgs
                        {
                            VariableName = varName,
                            Value = value,
                            Index = i
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Error processing LVar update: {ex.Message}");
            }
        }

        public void SendHVar(string hvar)
        {
            if (!IsRegistered && !registrationTimeoutOccurred)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Cannot send H-variable - not registered and no timeout: {hvar}");
                return;
            }

            if (IsRegistered)
            {
                // Use registered client channel
                string command = $"MF.SimVars.Set.(>H:{hvar})";
                SendFBWBACommand(command);
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Sent H-variable via client channel: {command}");
            }
            else
            {
                // Fallback - use default MobiFlight channel
                string command = $"MF.SimVars.Set.(>H:{hvar})";
                SendMFCommand(command);
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Sent H-variable via default channel (fallback): {command}");
            }
        }

        public void ExecuteCalculatorCode(string calculatorCode)
        {
            if (!IsRegistered)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Cannot execute calculator code - not registered: {calculatorCode}");
                return;
            }

            string command = $"MF.SimVars.Set.{calculatorCode}";
            SendFBWBACommand(command);

            System.Diagnostics.Debug.WriteLine($"[MobiFlight] Sent calculator code: {command}");
        }

        public void AddLVar(string lvarName)
        {
            if (!IsRegistered)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Cannot add LVar - not registered: {lvarName}");
                return;
            }

            if (registeredLVars.ContainsKey(lvarName))
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] LVar already registered: {lvarName}");
                return;
            }

            int index = lvarList.Count;
            if (index >= MAX_LVARS_COUNT)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Maximum LVar count reached, cannot add: {lvarName}");
                return;
            }

            lvarList.Add(lvarName);
            registeredLVars[lvarName] = index;

            string command = $"MF.SimVars.Add.(L:{lvarName})";
            SendFBWBACommand(command);

            System.Diagnostics.Debug.WriteLine($"[MobiFlight] Added LVar: {lvarName} at index {index}");
        }

        public void ClearLVars()
        {
            if (!IsRegistered)
            {
                System.Diagnostics.Debug.WriteLine("[MobiFlight] Cannot clear LVars - not registered");
                return;
            }

            SendFBWBACommand("MF.SimVars.Clear");
            registeredLVars.Clear();
            lvarList.Clear();

            System.Diagnostics.Debug.WriteLine("[MobiFlight] Cleared all LVars");
        }

        private void HeartbeatTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (IsConnected && IsRegistered)
            {
                SendMFCommand("MF.Ping");
            }
        }

        private void RegistrationTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            registrationTimeoutOccurred = true;
            System.Diagnostics.Debug.WriteLine("[MobiFlight] Registration timeout - will allow H-variables through default channel");
            ConnectionStatusChanged?.Invoke(this, "MobiFlight WASM registration timeout - using fallback");
        }

        public void Disconnect()
        {
            try
            {
                heartbeatTimer?.Stop();
                registrationTimer?.Stop();
                IsConnected = false;
                IsRegistered = false;
                registrationTimeoutOccurred = false;
                registeredLVars.Clear();
                lvarList.Clear();

                System.Diagnostics.Debug.WriteLine("[MobiFlight] Disconnected");
                ConnectionStatusChanged?.Invoke(this, "MobiFlight WASM disconnected");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobiFlight] Error during disconnect: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Disconnect();
            heartbeatTimer?.Dispose();
            registrationTimer?.Dispose();
        }
    }
}