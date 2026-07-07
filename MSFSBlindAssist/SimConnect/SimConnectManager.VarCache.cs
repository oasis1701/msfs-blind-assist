using System.Collections.Concurrent;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.SimConnect;

public partial class SimConnectManager
{

    /// <summary>
    /// Process individual variable response from our new registration system
    /// </summary>
    private void ProcessIndividualVariableResponse(int requestId, SingleValue data)
    {
        try
        {
            // Find the variable key for this request ID
            var variableEntry = variableDataDefinitions.FirstOrDefault(x => x.Value == requestId);
            if (variableEntry.Key == null)
            {
                return;
            }

            string varKey = variableEntry.Key;

            var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
            var varDef = variables.ContainsKey(varKey) ? variables[varKey] : null;

            if (varDef == null)
            {
                return;
            }

            double currentValue = data.value;

            // FlyByWire A32NX ECAM message processing for the ECAM Display window
            // This processes A32NX-specific variable names (A32NX_Ewd_LOWER_*).
            // Other aircraft variants (Fenix, PMDG) use different variable names, so this
            // block won't execute for them. Safe to keep aircraft-specific.
            if (varKey.StartsWith("A32NX_Ewd_LOWER_"))
            {
                // Convert numeric code to text message via EWDMessageLookup
                long numericCode = (long)currentValue;

                // Get raw message with ANSI codes
                string rawMessage = EWDMessageLookup.GetRawMessage(numericCode);

                // Store RAW message for ECAM Display window (it will clean and extract color itself)
                ecamStringData[varKey] = rawMessage;

                // Clean message for screen reader announcements
                string priority = EWDMessageLookup.GetMessagePriority(rawMessage);
                string cleanText = EWDMessageLookup.CleanANSICodes(rawMessage);

                // Create announcement text WITH color appended for screen readers (with comma)
                string announcementText = cleanText;
                if (!string.IsNullOrEmpty(priority) && !string.IsNullOrWhiteSpace(cleanText))
                {
                    announcementText = $"{cleanText}, {priority}";
                }
                ecamAnnouncementData[varKey] = announcementText;

                ecamStringsReceived++;

                System.Diagnostics.Debug.WriteLine($"[SimConnectManager] ECAM Line received: {varKey} = Code:{numericCode} → Display:'{cleanText}' | Announce:'{announcementText}' ({ecamStringsReceived}/{ecamTotalStringsExpected})");

                // Check if all 14 ECAM lines have been received (modulo ensures it fires every 14 lines)
                if (ecamStringsReceived % ecamTotalStringsExpected == 0)
                {
                    // Fire the ECAM data received event with all collected data
                    ECAMDataReceived?.Invoke(this, new ECAMDataEventArgs
                    {
                        LeftLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_1"] : "",
                        LeftLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_2"] : "",
                        LeftLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_3"] : "",
                        LeftLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_4"] : "",
                        LeftLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_5"] : "",
                        LeftLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_6"] : "",
                        LeftLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_7"] : "",
                        RightLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_1"] : "",
                        RightLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_2"] : "",
                        RightLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_3"] : "",
                        RightLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_4"] : "",
                        RightLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_5"] : "",
                        RightLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_6"] : "",
                        RightLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_7"] : "",
                        MasterWarning = ecamMasterWarning > 0.5,
                        MasterCaution = ecamMasterCaution > 0.5,
                        StallWarning = ecamStallWarning > 0.5
                    });

                    System.Diagnostics.Debug.WriteLine("[SimConnectManager] All ECAM data collected and event fired");

                    // Announce new ECAM messages (batch processing after all 14 lines collected)
                    AnnounceECAMChanges();
                }

                // Don't continue with normal processing for ECAM codes - batch collection is handled above
                return;
            }

            // Check if this is a forced update request
            bool isForceUpdate = false;
            lock (forceUpdateVariables)
            {
                isForceUpdate = forceUpdateVariables.Remove(varKey);
            }

            // Check for value changes
            bool hasChanged = true;
            if (lastVariableValues.ContainsKey(varKey))
            {
                hasChanged = Math.Abs(lastVariableValues[varKey] - currentValue) > 0.001; // Small tolerance for floating point
            }
            lastVariableValues.AddOrUpdate(varKey, currentValue, (key, oldValue) => currentValue);

            // Suppress SimVarUpdated for unchanged ANNOUNCED CONTINUOUS variables. Previously we
            // fired unconditionally so that displays would refresh; the unintended consequence was
            // double-firing aircraft-specific announce handlers (e.g. HS787's tri-state transition
            // handlers) whenever a panel opened and RequestPanelVariables produced a ONCE response
            // shortly after the continuous stream had already delivered the same value.
            //
            // The UpdateFrequency.Continuous qualifier is a deliberate safety narrowing (vs. a bare
            // IsAnnounced check). The double-fire only happens for continuously-monitored vars,
            // because those are the ones whose value also arrives via the continuous stream — so a
            // matching ONCE response is genuinely redundant. An OnRequest announced variable, by
            // contrast, has the ONCE response as its ONLY data source; suppressing it would strand
            // any display/control that depends on it. No existing aircraft ships such a variable
            // today (audited FBW/Fenix/PMDG: announced controls are all Continuous, and PMDG vars
            // never reach this path — they're CDA-broadcast), but the qualifier makes the safety
            // explicit and future-proofs the rule.
            //
            // This does NOT regress control/display population for continuous vars: panel controls
            // initialize from MainForm's currentSimVarValues cache at build time (kept current by
            // the continuous stream, which fires on first delivery and every change), display fields
            // fall back to SimConnectManager's lastVariableValues cache (populated just above, before
            // this return), and a forceUpdate caller (panel Refresh, state announcements) always
            // fires regardless. Non-announced variables also fire on every response.
            if (!hasChanged && varDef.IsAnnounced &&
                varDef.UpdateFrequency == UpdateFrequency.Continuous && !isForceUpdate)
            {
                return;
            }

            string description = FormatVariableValue(varKey, varDef, currentValue);

            System.Diagnostics.Debug.WriteLine($"[ProcessIndividualVariableResponse] Firing SimVarUpdated for {varKey}: Value={currentValue}, IsAnnounced={varDef.IsAnnounced}, HasChanged={hasChanged}, ForceUpdate={isForceUpdate}");

            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = varKey,
                Value = currentValue,
                Description = description
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing individual variable response: {ex.Message}");
        }
    }

    /// <summary>
    /// Format variable value for display/announcement
    /// </summary>
    private string FormatVariableValue(string varKey, SimVarDefinition varDef, double value)
    {
        // Check for custom value descriptions
        if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
        {
            // If AnnounceValueOnly is true, return just the value (e.g., "On ground")
            // Otherwise return "DisplayName: value" (e.g., "Parking brake: Set")
            if (varDef.AnnounceValueOnly)
            {
                return varDef.ValueDescriptions[value];
            }
            return $"{varDef.DisplayName}: {varDef.ValueDescriptions[value]}";
        }

        // Special formatting for FMA armed modes (bitmask decoding)
        if (varKey == "A32NX_FMA_LATERAL_ARMED")
        {
            return FormatFMALateralArmed((int)value);
        }
        else if (varKey == "A32NX_FMA_VERTICAL_ARMED")
        {
            return FormatFMAVerticalArmed((int)value);
        }
        else if (varKey == "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS")
        {
            return FormatNDFMMessage((int)value);
        }
        // Special formatting for different types of variables
        else if (varKey.StartsWith("A32NX_ECP_LIGHT_"))
        {
            string state = value > 0 ? "On" : "Off";
            return $"{varDef.DisplayName} {state}";
        }
        else if (varDef.Units == "volts")
        {
            return $"{varDef.DisplayName}: {value:F1}V";
        }
        else if (varDef.Units == "feet")
        {
            return $"{varDef.DisplayName}: {value:F0} feet";
        }
        else if (varDef.Units == "degrees")
        {
            return $"{varDef.DisplayName}: {value:F0} degrees";
        }
        else if (varDef.Units == "knots")
        {
            return $"{varDef.DisplayName}: {value:F0} knots";
        }
        else if (varDef.Units == "millibars" || varDef.Units == "millibar")
        {
            return $"{varDef.DisplayName}: {value:F2}";
        }
        else if (varDef.Units == "inHg" || varDef.Units == "inhg")
        {
            return $"{varDef.DisplayName}: {value:F2}";
        }

        // Default formatting
        return $"{varDef.DisplayName}: {value:F1}";
    }

    /// <summary>
    /// Decode FMA lateral armed mode bitmask
    /// </summary>
    private string FormatFMALateralArmed(int bitmask)
    {
        if (bitmask == 0)
        {
            return "Armed Lateral: None";
        }

        var modes = new List<string>();

        // Check each bit for lateral modes
        if ((bitmask & (1 << 0)) != 0) modes.Add("NAV");
        if ((bitmask & (1 << 1)) != 0) modes.Add("LOC");

        return modes.Count > 0 ? $"Armed Lateral: {string.Join(", ", modes)}" : "Armed Lateral: None";
    }

    /// <summary>
    /// Decode FMA vertical armed mode bitmask
    /// </summary>
    private string FormatFMAVerticalArmed(int bitmask)
    {
        if (bitmask == 0)
        {
            return "Armed Vertical: None";
        }

        var modes = new List<string>();

        // Check each bit for vertical modes
        if ((bitmask & (1 << 0)) != 0) modes.Add("ALT");
        if ((bitmask & (1 << 1)) != 0) modes.Add("ALT CST");
        if ((bitmask & (1 << 2)) != 0) modes.Add("CLB");
        if ((bitmask & (1 << 3)) != 0) modes.Add("DES");
        if ((bitmask & (1 << 4)) != 0) modes.Add("GS");
        if ((bitmask & (1 << 5)) != 0) modes.Add("FINAL");
        if ((bitmask & (1 << 6)) != 0) modes.Add("TCAS");

        return modes.Count > 0 ? $"Armed Vertical: {string.Join(", ", modes)}" : "Armed Vertical: None";
    }

    /// <summary>
    /// Decode ND FM message flags bitmask
    /// </summary>
    private string FormatNDFMMessage(int bitmask)
    {
        if (bitmask == 0)
        {
            return "ND Message: None";
        }

        // Check each bit for ND FM messages
        // Note: Only one message is typically active at a time, but we check in priority order
        if ((bitmask & (1 << 0)) != 0) return "ND Message: Select True Ref";
        if ((bitmask & (1 << 1)) != 0) return "ND Message: Check North Ref";
        if ((bitmask & (1 << 2)) != 0) return "ND Message: Nav Accuracy Downgrade";
        if ((bitmask & (1 << 3)) != 0) return "ND Message: Nav Accuracy Upgrade No GPS";
        if ((bitmask & (1 << 4)) != 0) return "ND Message: Specified VOR DME Unavailable";
        if ((bitmask & (1 << 5)) != 0) return "ND Message: Nav Accuracy Upgrade GPS";
        if ((bitmask & (1 << 6)) != 0) return "ND Message: GPS Primary";
        if ((bitmask & (1 << 7)) != 0) return "ND Message: Map Partly Displayed";
        if ((bitmask & (1 << 8)) != 0) return "ND Message: Set Offside Range Mode";
        if ((bitmask & (1 << 9)) != 0) return "ND Message: Offside FM Control";
        if ((bitmask & (1 << 10)) != 0) return "ND Message: Offside FM Wxr Control";
        if ((bitmask & (1 << 11)) != 0) return "ND Message: Offside Wxr Control";
        if ((bitmask & (1 << 12)) != 0) return "ND Message: GPS Primary Lost";
        if ((bitmask & (1 << 13)) != 0) return "ND Message: RTA Missed";
        if ((bitmask & (1 << 14)) != 0) return "ND Message: Backup Nav";

        return "ND Message: None";
    }

    /// <summary>
    /// Process batched continuous variable updates for Batch 1.
    /// Extracts values from GenericBatch1 struct and routes to existing variable processing pipeline.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch1 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 2.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch2 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 3.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch3 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 4.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch4 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Process batched continuous variable updates for Batch 5.
    /// </summary>
    private void ProcessContinuousBatch(int batchNum, in GenericBatch5 batch) => ProcessContinuousBatchImpl(batchNum, in batch);

    /// <summary>
    /// Generic implementation for processing batch data.
    /// Uses unsafe pointer access for efficient memory access across all batch types.
    /// </summary>
    private void ProcessContinuousBatchImpl<T>(int batchNum, in T batch) where T : unmanaged
    {
        // Get variables dictionary once at the start
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();

        int processedCount = 0;
        int skippedCount = 0;
        int invalidIndexCount = 0;
        int exceptionCount = 0;

        // SAFETY: Check if map is empty (possible race condition)
        if (continuousVariableIndexMap.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] WARNING: Map is empty! Possible race condition with StartContinuousMonitoring");
            return;
        }

        // Use unsafe pointer access instead of reflection for performance and stability
        // Each batch struct is a sequential struct of 300 doubles, so we can access directly
        try
        {
            unsafe
            {
                // batch is an 'in' parameter (readonly reference)
                // Use 'fixed' to get a pointer to the readonly reference
                fixed (T* batchPtr = &batch)
                {
                    double* values = (double*)batchPtr;  // Treat struct as array of doubles

                    // Process each continuous variable using direct memory access (no reflection!)
                    // Filter to only process variables belonging to this batch
                    foreach (var kvp in continuousVariableIndexMap)
                    {
                        string varKey = kvp.Key;
                        (int varBatchNum, int index) = kvp.Value;

                        // Skip variables that don't belong to this batch
                        if (varBatchNum != batchNum) continue;

                        // SAFETY: Validate index is within bounds
                        // Each batch struct has 300 doubles (matches BATCH_SIZE = 300)
                        if (index < 0 || index >= 300)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] ERROR: Batch {batchNum} index {index} out of bounds [0-299] for variable '{varKey}'");
                            invalidIndexCount++;
                            continue;
                        }

                        // SAFETY: Wrap value access to catch any memory exceptions
                        double value;
                        try
                        {
                            // Direct memory access - blazing fast, no reflection overhead!
                            value = values[index];

                            // Get variable definition
                            if (!variables.TryGetValue(varKey, out var varDef))
                            {
                                skippedCount++;
                                System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] WARNING: Variable {varKey} not found in aircraft definition!");
                                continue;
                            }

                        // Special handling for ECAM variables (convert numeric codes to readable text)
                        if (varKey.StartsWith("A32NX_Ewd_LOWER_"))
                        {
                            // Convert numeric code to readable message via EWDMessageLookup
                            long numericCode = (long)value;
                            string rawMessage = EWDMessageLookup.GetRawMessage(numericCode);
                            string priority = EWDMessageLookup.GetMessagePriority(rawMessage);
                            string cleanText = EWDMessageLookup.CleanANSICodes(rawMessage);

                            // Store RAW message for ECAM Display window
                            ecamStringData[varKey] = rawMessage;

                            // Create announcement text WITH color appended for screen readers
                            string announcementText = cleanText;
                            if (!string.IsNullOrEmpty(priority) && !string.IsNullOrWhiteSpace(cleanText))
                            {
                                announcementText = $"{cleanText}, {priority}";
                            }
                            ecamAnnouncementData[varKey] = announcementText;

                            ecamStringsReceived++;

                            // Check if all 14 ECAM lines have been received
                            if (ecamStringsReceived % ecamTotalStringsExpected == 0)
                            {
                                // Fire the ECAM data received event with all collected data
                                ECAMDataReceived?.Invoke(this, new ECAMDataEventArgs
                                {
                                    LeftLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_1"] : "",
                                    LeftLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_2"] : "",
                                    LeftLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_3"] : "",
                                    LeftLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_4"] : "",
                                    LeftLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_5"] : "",
                                    LeftLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_6"] : "",
                                    LeftLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_LEFT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_LEFT_LINE_7"] : "",
                                    RightLine1 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_1") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_1"] : "",
                                    RightLine2 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_2") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_2"] : "",
                                    RightLine3 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_3") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_3"] : "",
                                    RightLine4 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_4") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_4"] : "",
                                    RightLine5 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_5") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_5"] : "",
                                    RightLine6 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_6") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_6"] : "",
                                    RightLine7 = ecamStringData.ContainsKey("A32NX_Ewd_LOWER_RIGHT_LINE_7") ? ecamStringData["A32NX_Ewd_LOWER_RIGHT_LINE_7"] : "",
                                    MasterWarning = ecamMasterWarning > 0.5,
                                    MasterCaution = ecamMasterCaution > 0.5,
                                    StallWarning = ecamStallWarning > 0.5
                                });

                                System.Diagnostics.Debug.WriteLine("[ProcessContinuousBatch] All ECAM data collected and event fired");

                                // Announce new ECAM messages (batch processing after all 14 lines collected)
                                AnnounceECAMChanges();
                            }

                            processedCount++;
                            continue; // Skip normal processing for ECAM variables
                        }

                        // Check for value changes (skip unchanged values to reduce announcement spam)
                        bool hasChanged = true;
                        if (lastVariableValues.TryGetValue(varKey, out double lastValue))
                        {
                            hasChanged = Math.Abs(lastValue - value) > 0.001; // Small tolerance for floating point
                        }

                        // Honor a pending forceUpdate (RequestVariable(key, forceUpdate:true)). Batch-covered
                        // vars (Continuous+IsAnnounced) no longer have an individual data def, so a force-read
                        // is delivered HERE via the batch stream, not via ProcessIndividualVariableResponse —
                        // without this a force-read of an UNCHANGED batch-covered value would never fire.
                        bool isForceUpdate;
                        lock (forceUpdateVariables)
                        {
                            isForceUpdate = forceUpdateVariables.Remove(varKey);
                        }

                        // Update cache
                        lastVariableValues[varKey] = value;

                        // Only fire event if value changed, was force-requested, or it's the first time we see it
                        if (hasChanged || isForceUpdate || !lastVariableValues.ContainsKey(varKey))
                        {
                            // Check if we should only announce matches to ValueDescriptions (e.g., thrust lever detents)
                            if (varDef.OnlyAnnounceValueDescriptionMatches &&
                                varDef.ValueDescriptions != null &&
                                varDef.ValueDescriptions.Count > 0)
                            {
                                // Check if value matches any defined detent (within tolerance)
                                const double DETENT_TOLERANCE = 0.1;
                                bool matchesDetent = varDef.ValueDescriptions.Keys.Any(key =>
                                    Math.Abs(value - key) < DETENT_TOLERANCE);

                                if (!matchesDetent)
                                {
                                    // Skip announcement for intermediate values (e.g., "4.3" while moving between detents)
                                    continue;
                                }
                            }

                            string description = FormatVariableValue(varKey, varDef, value);

                            // Fire SimVarUpdated event directly (no routing through ProcessIndividualVariableResponse)
                            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                            {
                                VarName = varKey,
                                Value = value,
                                Description = description
                            });

                            processedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] EXCEPTION: Error accessing value for variable '{varKey}' at index {index}: {ex.GetType().Name}: {ex.Message}");
                        exceptionCount++;
                    }
                }  // end foreach
                }  // end fixed
            }  // end unsafe
        }
        catch (Exception ex)
        {
            // NOTE: a fatal CLR error during the unsafe marshalling of a received batch
            // (heap-corruption AccessViolation → 0x80131506 ExecutionEngineException, e.g. the
            // struct over-read fixed in 8cbb502) is NOT catchable by managed try/catch and will
            // FailFast regardless — there is intentionally no ExecutionEngineException catch here
            // (it is obsolete/never-raised on modern .NET). This handler covers ordinary exceptions.
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch] UNEXPECTED EXCEPTION in unsafe block: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch]   Message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ProcessContinuousBatch]   Stack trace: {ex.StackTrace}");
            return;  // Abort processing to prevent crash
        }
    }

    /// <summary>
    /// Get cached value for a variable if available
    /// </summary>
    public double? GetCachedVariableValue(string varKey)
    {
        if (lastVariableValues.TryGetValue(varKey, out double value))
            return value;
        return null;
    }

    /// <summary>
    /// Decoded ECAM memo line (raw text, still with ANSI color markers) for an
    /// A32NX_Ewd_LOWER_* key, or "" if absent. The batch handler converts these memo
    /// CODE vars to strings (ecamStringData) and `continue`s past the numeric-cache write,
    /// so GetCachedVariableValue returns null for them — callers that want the memo text
    /// (e.g. the decoded Upper E/WD readout) must use this accessor. Run CleanANSICodes on it.
    /// </summary>
    public string GetEcamLineRaw(string varKey)
    {
        try { return ecamStringData.TryGetValue(varKey, out var raw) ? raw : ""; }
        catch { return ""; }
    }

    /// <summary>
    /// Get snapshot of multiple cached variables
    /// </summary>
    public Dictionary<string, double> GetCachedVariableSnapshot(List<string> varKeys)
    {
        var snapshot = new Dictionary<string, double>();
        foreach (var key in varKeys)
        {
            if (lastVariableValues.TryGetValue(key, out double value))
                snapshot[key] = value;
        }
        return snapshot;
    }
}
