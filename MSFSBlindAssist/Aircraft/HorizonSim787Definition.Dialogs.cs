using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class HorizonSim787Definition
{
    // Dispose the aux windows this def instance owns (the synoptic-display window holds a live
    // MFD_2 Coherent socket; the autopilot window holds a refresh timer). MUST be called on
    // aircraft swap so the socket/timer don't outlive the def instance (see SwitchAircraft).
    public void CloseAuxWindows()
    {
        if (_displayWindow != null && !_displayWindow.IsDisposed) _displayWindow.Dispose();
        _displayWindow = null;
        if (_autopilotWindow != null && !_autopilotWindow.IsDisposed) _autopilotWindow.Dispose();
        _autopilotWindow = null;
    }

    // Open (or replace) the single live-display read-out window for a 787 Coherent display view.
    // Only one is kept open at a time so we never run more than one extra display socket.
    private void ShowHs787Display(string title, string viewNeedle, ScreenReaderAnnouncer announcer,
        HotkeyManager hotkeyManager)
    {
        hotkeyManager.ExitInputHotkeyMode();
        var old = _displayWindow;
        _displayWindow = null;
        if (old != null && !old.IsDisposed) old.Close();

        var w = new Forms.HS787.HS787DisplayForm(title, viewNeedle, announcer);
        w.FormClosed += (_, _) => { if (ReferenceEquals(_displayWindow, w)) _displayWindow = null; };
        _displayWindow = w;
        w.Show();
    }

    // =========================================================================
    // MCP Dialogs
    // =========================================================================

    private void ShowHeadingDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&LNAV", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_LNAV") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_NAV1_HOLD")),

            new("&Heading Hold", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_HDGHold") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_HDG_HOLD")),

            new("HDG / &TRK", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_TRKMode") ?? 0;
                return v > 0 ? "TRK" : "HDG";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_TRKMode") ?? 0;
                simConnect.SetLVar("XMLVAR_TRK_MODE_ACTIVE", current > 0 ? 0 : 1);
            })
        };

        var dialog = new ValueInputForm(
            "MCP Heading", "heading", "0-359", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 359)
                    return (true, "");
                return (false, "Enter a heading between 0 and 359");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int hdg))
                    simConnect.SendEvent("HEADING_BUG_SET", (uint)hdg);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowSpeedDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&Mode", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_MCP_IsMach") ?? 0;
                return v > 0 ? "Mach" : "IAS";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_MCP_IsMach") ?? 0;
                simConnect.SetLVar("XMLVAR_AirSpeedIsInMach", current > 0 ? 0 : 1);
            }),

            new("&FLCH", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("FLIGHT_LEVEL_CHANGE")),

            new("Speed &INTV", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_MCP_SpdManual") ?? 0;
                return v > 0 ? "Manual" : "FMC";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_MCP_SpdManual") ?? 0;
                simConnect.SetLVar("XMLVAR_SpeedIsManuallySet", current > 0 ? 0 : 1);
            })
        };

        var dialog = new ValueInputForm(
            "MCP Speed", "speed", "IAS: 100-399 knots / Mach: 0.40-0.99", announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    if (val >= 100 && val <= 399) return (true, "");
                    if (val >= 0.4 && val < 1.0)  return (true, "");
                }
                return (false, "Enter knots (100-399) or Mach (0.40-0.99)");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double spd))
                    return;

                if (spd < 10.0)
                {
                    // AP_MACH_VAR_SET takes value × 100 (e.g. Mach 0.82 → 82)
                    simConnect.SendEvent("AP_MACH_VAR_SET", (uint)(int)Math.Round(spd * 100));
                }
                else
                {
                    simConnect.SendEvent("AP_SPD_VAR_SET", (uint)(int)spd);
                }
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowAltitudeDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&VNAV", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0;
                simConnect.SetLVar("XMLVAR_VNAVButtonValue", current > 0 ? 0 : 1);
            }),

            new("&Level Change", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("FLIGHT_LEVEL_CHANGE")),

            new("Altitude &Hold", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_ALTHold") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_ALT_HOLD")),

            new("Alt &INTV", () => "Momentary", () =>
                // Fire the WT Boeing altitude-intervention H event via MobiFlight WASM. (The
                // CDU/EFB no longer run an HTTP bridge, so there is no Coherent command path here.)
                simConnect.SendHVar("AS01B_FMC_1_ALTITUDE_INTERVENTION"))
        };

        var dialog = new ValueInputForm(
            "MCP Altitude", "altitude", "0-45000 feet", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 45000)
                    return (true, "");
                return (false, "Enter a value between 0 and 45000");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int alt))
                    simConnect.SendEvent("AP_ALT_VAR_SET_ENGLISH", (uint)alt);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowVSDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&Engage V/S", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_VS_Active") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_VS_HOLD")),

            new("V/S &FPA", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0;
                return v > 0 ? "FPA" : "V/S";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0;
                simConnect.SetLVar("XMLVAR_FPA_MODE_ACTIVE", current > 0 ? 0 : 1);
            }),

            new("&Approach", () =>
            {
                bool gsActive  = (simConnect.GetCachedVariableValue("HS787_GS_Active") ?? 0) > 0;
                bool locActive = (simConnect.GetCachedVariableValue("HS787_LOC")       ?? 0) > 0;
                bool appHold   = (simConnect.GetCachedVariableValue("HS787_APP")       ?? 0) > 0;
                if (gsActive)  return "GS Active";
                if (locActive) return "LOC Active";
                if (appHold)   return "Armed";
                return "Off";
            }, () => simConnect.SendEvent("AP_APR_HOLD"))
        };

        var dialog = new ValueInputForm(
            "MCP Vertical Speed", "V/S or FPA",
            "V/S: -6000 to 6000 fpm / FPA: -9.9 to 9.9 deg",
            announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    bool isFPA = (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0;
                    if (isFPA)
                    {
                        if (val >= -9.9 && val <= 9.9) return (true, "");
                        return (false, "Enter FPA between -9.9 and 9.9 degrees");
                    }
                    else
                    {
                        if (val >= -6000 && val <= 6000) return (true, "");
                        return (false, "Enter V/S between -6000 and 6000 fpm");
                    }
                }
                return (false, "Enter a numeric value");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                    return;

                bool isFPA = (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0;
                if (isFPA)
                {
                    simConnect.SetLVar("WT_AP_FPA_Target:1", val);
                }
                else
                {
                    // AP_VS_VAR_SET_ENGLISH handles negative values via two's complement
                    simConnect.SendEvent("AP_VS_VAR_SET_ENGLISH", (uint)(int)val);
                }
            },
            inputEnabledCheck: () => (simConnect.GetCachedVariableValue("HS787_VS_Active") ?? 0) > 0
                                  || (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0);

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowBaroDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&Standard (STD)", () =>
            {
                double? v = simConnect.GetCachedVariableValue("HS787_Altimeter");
                return v != null && Math.Abs(v.Value - 29.92) < 0.005 ? "Set" : "Not set";
            }, () =>
            {
                // Standard pressure = 1013 HPA = 29.92 inHg
                simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(29.92 * 16));
            })
        };

        var dialog = new ValueInputForm(
            "Altimeter", "QNH", "HPA (e.g. 1013)", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 900 && val <= 1100)
                    return (true, "");
                return (false, "Enter QNH in HPA between 900 and 1100");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int hpa))
                {
                    double inHg = hpa / 33.8639;
                    simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(inHg * 16));
                }
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // Returns "Xh Ym" or "Ym" ETE string, or "" if ground speed is too low to be meaningful.
    private static string FormatEte(double distanceNm, double gsKnots)
    {
        if (gsKnots < 30 || distanceNm <= 0) return "";
        double hours = distanceNm / gsKnots;
        int totalMinutes = (int)Math.Round(hours * 60);
        int hh = totalMinutes / 60;
        int mm = totalMinutes % 60;
        return hh > 0 ? $"{hh}h {mm}m" : $"{mm}m";
    }

    // Returns "Xh Ym" or "Ym" from a raw seconds value.
    private static string FormatEteSeconds(double seconds)
    {
        int totalMinutes = (int)Math.Round(seconds / 60.0);
        int hh = totalMinutes / 60;
        int mm = totalMinutes % 60;
        return hh > 0 ? $"{hh}h {mm}m" : $"{mm}m";
    }

    /// <summary>
    /// Tries each candidate InputEvent name in order; fires the first one present in the
    /// catalog and returns true. Returns false if none match — caller should fall back.
    /// </summary>
    private static bool TryFireInputEvent(
        SimConnect.SimConnectManager simConnect, double value, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (simConnect.HasInputEvent(name) && simConnect.TrySetInputEvent(name, value))
                return true;
        }
        return false;
    }
}
