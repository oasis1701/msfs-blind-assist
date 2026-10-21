# Fenix v3 package names our code does not use

Static scan, 2026-10-21. 1173 L:vars and 345 B: input events that the installed Fenix v3.0.0.7426 package files reference and that none of our Fenix sources use (case-insensitive). Found by expanding the ModelBehavior templates in `fnx-aircraft-a32x-common/ModelBehaviorDefs` for every behaviour XML, plus the JS, HTML and cfg files. FenixSystem.exe can publish more L:vars at runtime than these files mention.

How to read the names: the first letter is the Fenix kind, `S_` a switch or control input, `I_` an indicator light (`_U` upper legend, `_L` lower legend), `A_` an analogue position (knob, pot or lever), `N_` a numeric value, `B_` a state flag, `E_` a rotary encoder (`E_CB_` a circuit breaker). Next comes the area: `OH` overhead, `FCU` glareshield, `MIP` main instrument panel, `PED` pedestal, `ECAM` the ECAM control panel, `FC` flight controls, `EXT` exterior. Each entry gives the number of references and the attachment(s) that reference it. An entry found in only one attachment of a pair (ACP_Modern or ACP_Original, ADIRS or GNADIRS, Calls_A321 or Calls_A32X, Fuel_ACT_Enabled or Fuel_ACT_Disabled, Pneumatic_A321 or Pneumatic_A32X) exists only on that cockpit variant.

## Spotlight

- **Neo / engine-type awareness** (8): `B_CONFIG_IS_NEO`, `N_CONFIG_AIRCRAFT_TYPE`, `N_CONFIG_ENGINETYPE`, `S_OH_ENG_N1_MODE_1_Cover`, `S_OH_ENG_N1_MODE_2_Cover`, `I_OH_ENG_N1_MODE_1_L`, `I_OH_ENG_N1_MODE_2_L`, `N_CONFIG_ELEC_HYDRAULIC_TYPE`

- **Datalink, printers, recorders** (22): `S_DCDU2_MSGDWN`, `S_DCDU2_MSGUP`, `S_DCDU2_PRINT`, `S_DCDU2_PGUP`, `S_DCDU2_PGDN`, `S_DCDU2_LSK1L`, `S_DCDU2_LSK2L`, `S_DCDU2_LSK1R`, `S_DCDU2_LSK2R`, `S_PED_AIDS_PRINT`, `S_PED_DFDR_EVENT`, `N_DCDU1_BRIGHTNESS`, `N_DCDU2_BRIGHTNESS`, `B_CONFIG_DCDU`, `S_PED_ATC_1`, `S_PED_ATC_2`, `S_PED_ATC_3`, `S_PED_ATC_4`, `S_PED_ATC_5`, `S_PED_ATC_6`, `S_PED_ATC_7`, `S_PED_ATC_0`

- **EFB hardware** (5): `S_EFB_CHARGING_CABLE_CAPT_TEMP`, `S_EFB_CHARGING_CABLE_FO_TEMP`, `B_EFB_MOUNT_TYPE`, `N_BRIGHTNESS_EFB_CAPT`, `N_BRIGHTNESS_EFB_FO`

- **Lighting and brightness** (38): `S_EXT_CARGO_BAY_FWD_LT`, `S_EXT_CARGO_BAY_AFT_LT`, `S_EXT_CARGO_DOOR_SELECTOR_LOADING_AREA_LT_AFT`, `S_EXT_CARGO_DOOR_SELECTOR_LOADING_AREA_LT_FWD`, `S_EXT_GPU_LIGHT_TEST`, `I_EXT_REFUELING_COCKPIT_LT`, `I_EXT_REFUELING_END_LT`, `N_EXT_REFUELING_LIGHTING`, `N_BRIGHTNESS_RADIO_CAPT`, `N_BRIGHTNESS_CLOCK`, `N_BRIGHTNESS_VOLTAGE_MISC`, `B_LT_EMERGENCY_EXIT`, `S_CDU1_BRIGHTNESS_UP`, `S_CDU1_BRIGHTNESS_DOWN`, `S_CDU2_BRIGHTNESS_UP`, `S_CDU2_BRIGHTNESS_DOWN`, `S_CHART_LIGHT_TEMP_CAPT`, `S_CHART_LIGHT_TEMP_FO`, `N_BRIGHTNESS_ANNUNCIATORS`, `N_DISPLAY_BRIGHTNESS_CO`, `N_DISPLAY_BRIGHTNESS_FO`, `N_DISPLAY_BRIGHTNESS_CI`, `N_DISPLAY_BRIGHTNESS_FI`, `N_DISPLAY_BRIGHTNESS_ECAM_U`, `N_DISPLAY_BRIGHTNESS_ECAM_L`, `N_DCDU1_BRIGHTNESS`, `N_DCDU2_BRIGHTNESS`, `N_CDU1_BRIGHTNESS`, `N_CDU2_BRIGHTNESS`, `N_DISPLAY_BRIGHTNESS_STANDBY`, `N_BRIGHTNESS_RADIO_OH`, `N_FCU_LIGHTING`, `N_OH_LIGHTING_OVD`, `N_BRIGHTNESS_EFB_CAPT`, `N_BRIGHTNESS_EFB_FO`, `I_OH_IN_LT_ICE`, `N_BRIGHTNESS_FAP`, `N_BRIGHTNESS_RADIO_FO`

- **Variant-only overhead panels (A321 calls/econ flow, ACT fuel, ADIRS align/fault)** (15): `S_OH_CARGO_DISC_1_OLD_LAYOUT_Cover`, `S_OH_CARGO_DISC_2_OLD_LAYOUT_Cover`, `S_OH_CALLS_MID`, `S_OH_CALLS_EXIT`, `S_OH_FUEL_ACT`, `I_OH_FUEL_ACT_U`, `I_OH_FUEL_ACT_L`, `I_OH_NAV_IR1_ALIGN`, `I_OH_NAV_IR1_FAULT`, `I_OH_NAV_IR2_ALIGN`, `I_OH_NAV_IR2_FAULT`, `I_OH_NAV_IR3_ALIGN`, `I_OH_NAV_IR3_FAULT`, `S_OH_PNEUMATIC_ECON_FLOW`, `I_OH_PNEUMATIC_ECON_FLOW_L`

- **Guards (cover) we never open** (36): `S_EXT_GPU_APU_SHUTOFF_Cover`, `S_EXT_REFUELING_VALVE_LEFT_Cover`, `S_EXT_REFUELINGL_VALVE_CENTER_Cover`, `S_EXT_REFUELING_VALVE_RIGHT_Cover`, `S_EXT_REFUELING_MODE_Cover`, `S_EXT_REFUELING_ACT_VALVE_1_Cover`, `S_EXT_REFUELING_ACT_VALVE_2_Cover`, `S_OH_FIRE_ENG1_BUTTON_Cover`, `S_OH_FIRE_ENG2_BUTTON_Cover`, `S_OH_FIRE_APU_BUTTON_Cover`, `S_OH_ELEC_IDG1_Cover`, `S_OH_ELEC_IDG2_Cover`, `S_OH_HYD_BLUE_ELEC_PUMP_Cover`, `S_OH_HYD_RAT_MAN_ON_Cover`, `S_OH_PNEUMATIC_RAM_AIR_Cover`, `S_OH_PNEUMATIC_DITCHING_Cover`, `S_OH_ENG_MANSTART_1_Cover`, `S_OH_ENG_MANSTART_2_Cover`, `S_OH_CARGO_DISC_1_OLD_LAYOUT_Cover`, `S_OH_CARGO_DISC_2_OLD_LAYOUT_Cover`, `S_OH_HYD_BLUE_PUMP_OVERRIDE_Cover`, `S_OH_HYD_LMV_GREEN_Cover`, `S_OH_HYD_LMV_BLUE_Cover`, `S_OH_HYD_LMV_YELLOW_Cover`, `S_OH_AFT_FADEC_GND_1_Cover`, `S_OH_AFT_FADEC_GND_2_Cover`, `S_OH_ELT_Cover`, `S_ECAM_EMER_CANCEL_Cover`, `S_OH_OXYGEN_MASK_MAN_ON_Cover`, `S_OH_OXYGEN_HIGH_ALT_Cover`, `S_PED_RMP1_NAV_Cover`, `S_PED_RMP2_NAV_Cover`, `S_PED_RMP3_NAV_Cover`, `S_OH_EVAC_COMMAND_Cover`, `S_OH_ENG_N1_MODE_1_Cover`, `S_OH_ENG_N1_MODE_2_Cover`


## Model animation and state vars (A320_*) (51)

- `A320:Window_Lock`: 4 refs, Cockpit
- `A320_AOA_Sensor_Anim`: 3 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `A320_APU_Inlet_Anim`: 3 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `A320_Avionics_Cooling_Inlet_Anim`: 11 refs, Ext_Fuselage, Ext_Fuselage_A319, Ext_Fuselage_A320 +1
- `A320_Avionics_Cooling_Outlet_Anim`: 17 refs, Cabin, Cockpit_CEO, Ext_Fuselage +3
- `A320_Brake_Fan_Left_Anim`: 1 ref, Ext_Gear_Main
- `A320_Brake_Fan_Right_Anim`: 1 ref, Ext_Gear_Main
- `A320_Cabin_Pressure_Outflow_Anim`: 3 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `A320_Engine_Reverser_Left`: 8 refs, Ext_CFM, Ext_IAE
- `A320_Engine_Reverser_Right`: 8 refs, Ext_CFM, Ext_IAE
- `A320_FC_AIL_L`: 1 ref, Ext_Wings
- `A320_FC_AIL_R`: 1 ref, Ext_Wings
- `A320_FC_FLAPS_SHAKE`: 1 ref, Ext_Wings
- `A320_FC_HORIZONTAL_STAB_SHAKE`: 1 ref, Ext_Stabilizers
- `A320_FC_L_ELEV`: 1 ref, Ext_Stabilizers
- `A320_FC_L_ELEVTRIM`: 4 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_FC_L_SLATS`: 3 refs, Ext_Wings
- `A320_FC_L_SPLR_1`: 1 ref, Ext_Wings
- `A320_FC_L_SPLR_2`: 1 ref, Ext_Wings
- `A320_FC_L_SPLR_3`: 1 ref, Ext_Wings
- `A320_FC_L_SPLR_4`: 1 ref, Ext_Wings
- `A320_FC_L_SPLR_5`: 1 ref, Ext_Wings
- `A320_FC_RUDDER`: 1 ref, Ext_Stabilizers
- `A320_FC_R_ELEV`: 1 ref, Ext_Stabilizers
- `A320_FC_R_ELEVTRIM`: 4 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_FC_R_SLATS`: 3 refs, Ext_Wings
- `A320_FC_R_SPLR_1`: 1 ref, Ext_Wings
- `A320_FC_R_SPLR_2`: 1 ref, Ext_Wings
- `A320_FC_R_SPLR_3`: 1 ref, Ext_Wings
- `A320_FC_R_SPLR_4`: 1 ref, Ext_Wings
- `A320_FC_R_SPLR_5`: 1 ref, Ext_Wings
- `A320_GEAR_LEFT`: 7 refs, Ext_Gear_Main
- `A320_GEAR_NOSE`: 11 refs, Cabin, Ext_Fuselage, Ext_Fuselage_A319 +3
- `A320_Gear_Door_Left_Anim`: 4 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_Gear_Door_Nose_Anim`: 4 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_Gear_Door_Right_Anim`: 4 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_Ground_Power_Anim`: 34 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_Nav_Left`: 12 refs, Ext_Wings_A319_SL, Ext_Wings_A319_WF, Ext_Wings_A320_SL +3
- `A320_Nav_Right`: 12 refs, Ext_Wings_A319_SL, Ext_Wings_A319_WF, Ext_Wings_A320_SL +3
- `A320_RAT_Open`: 4 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `A320_RAT_Spin`: 1 ref, Ext_Gear_Main
- `A320_Ram_Air_Inlet_Left_Anim`: 7 refs, Ext_Fuselage, Ext_Fuselage_A319, Ext_Fuselage_A320 +1
- `A320_Ram_Air_Inlet_Right_Anim`: 7 refs, Ext_Fuselage, Ext_Fuselage_A319, Ext_Fuselage_A320 +1
- `A320_RetractAnimLeft`: 3 refs, Cabin, Ext_Wings
- `A320_RetractAnimRight`: 1 ref, Ext_Wings
- `A320_Wing_Flex_L`: 1 ref, Ext_Wings
- `A320_Wing_Flex_R`: 5 refs, Ext_Wings
- `A320_Wiper_Left`: 28 refs, Cockpit, Ext_Fuselage, Ext_Fuselage_A319 +2
- `A320_Wiper_Left_Running`: 8 refs, Ext_Fuselage, Ext_Fuselage_A319, Ext_Fuselage_A320 +1
- `A320_Wiper_Right`: 28 refs, Cockpit, Ext_Fuselage, Ext_Fuselage_A319 +2
- `A320_Wiper_Right_Running`: 8 refs, Ext_Fuselage, Ext_Fuselage_A319, Ext_Fuselage_A320 +1


## Sound engine: aural call-out triggers (A320_Audio:*) (132)

`A320_Audio:1`, `A320_Audio:10`, `A320_Audio:100`, `A320_Audio:1000`, `A320_Audio:100_Prefix`, `A320_Audio:10_retard`, `A320_Audio:11`, `A320_Audio:12`, `A320_Audio:13`, `A320_Audio:14`, `A320_Audio:15`, `A320_Audio:16`, `A320_Audio:17`, `A320_Audio:18`, `A320_Audio:19`, `A320_Audio:2`, `A320_Audio:20`, `A320_Audio:200`, `A320_Audio:2000`, `A320_Audio:200_Prefix`, `A320_Audio:20_retard`, `A320_Audio:2500`, `A320_Audio:2500b`, `A320_Audio:3`, `A320_Audio:30`, `A320_Audio:300`, `A320_Audio:300_Prefix`, `A320_Audio:4`, `A320_Audio:40`, `A320_Audio:400`, `A320_Audio:400_Prefix`, `A320_Audio:5`, `A320_Audio:50`, `A320_Audio:500`, `A320_Audio:500_Prefix`, `A320_Audio:6`, `A320_Audio:60`, `A320_Audio:600_Prefix`, `A320_Audio:7`, `A320_Audio:70`, `A320_Audio:700_Prefix`, `A320_Audio:8`, `A320_Audio:80`, `A320_Audio:800_Prefix`, `A320_Audio:9`, `A320_Audio:90`, `A320_Audio:900_Prefix`, `A320_Audio:ATC_ring`, `A320_Audio:Attenuated_Buzzer`, `A320_Audio:Autobrake_Off_Dual`, `A320_Audio:Autobrake_Off_Single`, `A320_Audio:Brake_Max_Braking`, `A320_Audio:CRC`, `A320_Audio:CVR_test`, `A320_Audio:C_Chord`, `A320_Audio:Cabin_call_aft`, `A320_Audio:Cabin_call_exit`, `A320_Audio:Cabin_call_fwd`, `A320_Audio:Cabin_call_mid`, `A320_Audio:Cavalry_Charge`, `A320_Audio:Continuous_Buzzer`, `A320_Audio:Cricket`, `A320_Audio:Cricket_Stall`, `A320_Audio:Dont_sink`, `A320_Audio:Dual_Input`, `A320_Audio:EVAC_cockpit`, `A320_Audio:FLARE`, `A320_Audio:Glideslope`, `A320_Audio:Go_around_windshear`, `A320_Audio:Intermittent_Buzzer`, `A320_Audio:Keep_Max_Reverse`, `A320_Audio:Mech_call`, `A320_Audio:Monitor_radar_display`, `A320_Audio:Obstacle`, `A320_Audio:Obstacle_ahead`, `A320_Audio:Obstacle_ahead_pullup`, `A320_Audio:Obstacle_obstacle`, `A320_Audio:Obstacle_pullup`, `A320_Audio:Passenger_signs`, `A320_Audio:Pullup`, `A320_Audio:RUNWAY_TOO_SHORT`, `A320_Audio:SC`, `A320_Audio:Set_Max_Reverse`, `A320_Audio:Sinkrate`, `A320_Audio:Stop_Rudder_Input`, `A320_Audio:TCAS_adjust_vertical_speed`, `A320_Audio:TCAS_clear_of_conflict`, `A320_Audio:TCAS_climb`, `A320_Audio:TCAS_climb_climb`, `A320_Audio:TCAS_climb_now`, `A320_Audio:TCAS_crossing_climb`, `A320_Audio:TCAS_crossing_descend`, `A320_Audio:TCAS_descend`, `A320_Audio:TCAS_descend_descend`, `A320_Audio:TCAS_descend_now`, `A320_Audio:TCAS_increase_Descend`, `A320_Audio:TCAS_increase_climb`, `A320_Audio:TCAS_level_off`, `A320_Audio:TCAS_maintain`, `A320_Audio:TCAS_maintain_vertical_speed`, `A320_Audio:TCAS_maintain_vertical_speed_crossing_maintain`, `A320_Audio:TCAS_maintain_vertical_speed_maintain`, `A320_Audio:TCAS_monitor_vertical_speed`, `A320_Audio:TCAS_tcas_test_fail`, `A320_Audio:TCAS_tcas_test_pass`, `A320_Audio:TCAS_traffic`, `A320_Audio:TCAS_traffic_traffic`, `A320_Audio:TCAS_traffic_traffic_traffic`, `A320_Audio:Terrain`, `A320_Audio:Terrain_ahead`, `A320_Audio:Terrain_ahead_pullup`, `A320_Audio:Too_low_flap`, `A320_Audio:Too_low_gear`, `A320_Audio:Too_low_terrain`, `A320_Audio:Triple_click`, `A320_Audio:and`, `A320_Audio:bank_bank`, `A320_Audio:call_emerg`, `A320_Audio:hundred`, `A320_Audio:hundred_above`, `A320_Audio:hundred_and`, `A320_Audio:minimums`, `A320_Audio:pitch`, `A320_Audio:priority_left`, `A320_Audio:priority_right`, `A320_Audio:retard`, `A320_Audio:retard_retard`, `A320_Audio:speed`, `A320_Audio:stall`, `A320_Audio:standby`, `A320_Audio:v1`, `A320_Audio:windshear`


## Sound engine: system sound state (A320_Sound:*) (99)

`A320_Sound:AEVC`, `A320_Sound:AEVC_Blower`, `A320_Sound:AEVC_Extractor`, `A320_Sound:ANC`, `A320_Sound:APU_Contactor`, `A320_Sound:APU_N1`, `A320_Sound:APU_Starter`, `A320_Sound:APU_has_fuel`, `A320_Sound:Avatar_Movement`, `A320_Sound:Avatar_Surface`, `A320_Sound:Avionics_bus`, `A320_Sound:Battery_bus_1`, `A320_Sound:Battery_bus_2`, `A320_Sound:Battery_connected`, `A320_Sound:Brake_Fans`, `A320_Sound:Bus_A`, `A320_Sound:Bus_APU`, `A320_Sound:Bus_Gen`, `A320_Sound:Bus_Gen_2`, `A320_Sound:CDLS_Beep`, `A320_Sound:Cab_Fans`, `A320_Sound:Centerline_bump:1`, `A320_Sound:Centerline_bump:2`, `A320_Sound:Cockpit_pack`, `A320_Sound:Commercial_bus`, `A320_Sound:ENG_1_Compressor_Stall`, `A320_Sound:ENG_1_EngDamage`, `A320_Sound:ENG_1_FF`, `A320_Sound:ENG_1_N1`, `A320_Sound:ENG_1_N1_Combined`, `A320_Sound:ENG_1_N1_DeltaTime`, `A320_Sound:ENG_1_N1_Demand_Delta`, `A320_Sound:ENG_1_N1_IS_SLOWING`, `A320_Sound:ENG_1_N1_MOMENTUM`, `A320_Sound:ENG_1_N2`, `A320_Sound:ENG_1_Starter_Active`, `A320_Sound:ENG_2_Compressor_Stall`, `A320_Sound:ENG_2_EngDamage`, `A320_Sound:ENG_2_FF`, `A320_Sound:ENG_2_N1`, `A320_Sound:ENG_2_N1_Combined`, `A320_Sound:ENG_2_N1_DeltaTime`, `A320_Sound:ENG_2_N1_Demand_Delta`, `A320_Sound:ENG_2_N1_IS_SLOWING`, `A320_Sound:ENG_2_N1_MOMENTUM`, `A320_Sound:ENG_2_N2`, `A320_Sound:ENG_2_Starter_Active`, `A320_Sound:Elec_pump_blue`, `A320_Sound:Elec_pump_blue_demand`, `A320_Sound:Elec_pump_yellow`, `A320_Sound:Elec_pump_yellow_demand`, `A320_Sound:Engine_Bias`, `A320_Sound:FAP_Button_Click`, `A320_Sound:FAP_error`, `A320_Sound:FWC_ANC`, `A320_Sound:FWC_Speakers`, `A320_Sound:Flaps_Moving`, `A320_Sound:Fuel_pump_Center_1`, `A320_Sound:Fuel_pump_Center_2`, `A320_Sound:Fuel_pump_Left_1`, `A320_Sound:Fuel_pump_Left_2`, `A320_Sound:Fuel_pump_Right_1`, `A320_Sound:Fuel_pump_Right_2`, `A320_Sound:GPU_State`, `A320_Sound:Galley_fan`, `A320_Sound:Galycab`, `A320_Sound:Headset_On`, `A320_Sound:Highest_brake_temp`, `A320_Sound:In_Flight_Deck_Factor`, `A320_Sound:Inside_Factor`, `A320_Sound:Inside_factor`, `A320_Sound:Left_Wheel_Ground`, `A320_Sound:Maintenance_bus`, `A320_Sound:Nose_Wheel_Ground`, `A320_Sound:PTU`, `A320_Sound:PTU_Bark_Long`, `A320_Sound:PTU_Bark_Loud`, `A320_Sound:PTU_Bark_Soft`, `A320_Sound:PTU_Start`, `A320_Sound:Pack_Bypass_Left`, `A320_Sound:Pack_Bypass_Right`, `A320_Sound:Pack_Left`, `A320_Sound:Pack_Right`, `A320_Sound:RAT_N1`, `A320_Sound:Rattle_Factor`, `A320_Sound:Right_Wheel_Ground`, `A320_Sound:Sidestick_Lock`, `A320_Sound:Sidestick_Unlock`, `A320_Sound:Slats_Moving`, `A320_Sound:Speaker_Energised_Capt`, `A320_Sound:Speaker_Energised_FO`, `A320_Sound:Spoilers_Ext_L`, `A320_Sound:Spoilers_Ext_R`, `A320_Sound:THS_Moving`, `A320_Sound:TRU`, `A320_Sound:Touch_C_Rate`, `A320_Sound:Touch_L_Rate`, `A320_Sound:Touch_R_Rate`, `A320_Sound:nw_skidding`


## Sim plumbing (LOD, camera, enter/exit aircraft) (8)

- `A32X_AmbientLight`: 1 ref, LOD.js
- `A32X_Camera_X`: 1 ref, LOD.js
- `A32X_Camera_Y`: 1 ref, LOD.js
- `A32X_Camera_Z`: 1 ref, LOD.js
- `A32X_ExitState`: 7 refs, Cockpit
- `A32X_LOD`: 1 ref, LOD.js
- `ENTER_AIRCRAFT`: 12 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `EXIT_AIRCRAFT`: 2 refs, Cockpit


## Audio control panels (ASP = ACP) (115)

### ASP2 (first officer) (56)

- `A_ASP2_ADF_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_ADF_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_CAB_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_HF_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_HF_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_ILS_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_INT_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_MARKER_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_MLS_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_PA_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_VHF_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_VHF_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_VHF_3_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_VOR_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP2_VOR_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_ADF_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_ADF_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_CAB_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_HF_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_HF_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_ILS_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_INT_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_MARKER_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_MLS_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_NAV_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_NAV_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_PA_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_VHF_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_VHF_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP2_VHF_3_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_ADF_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_ADF_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_CAB_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_CAB_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_HF_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_HF_1_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_HF_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_HF_2_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_ILS_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_INTRAD` (switch): 49 refs, Panel_ACP_Modern, Panel_ACP_Original, Panel_RMP_Original
- `S_ASP2_INT_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_INT_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_MARKER_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_MLS_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_PA_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_PA_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_RESET` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VHF_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VHF_1_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VHF_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VHF_2_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VHF_3_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VHF_3_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VOICE` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VOR_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP2_VOR_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original

### ASP3 (third occupant) (56)

- `A_ASP3_ADF_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_ADF_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_CAB_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_HF_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_HF_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_ILS_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_INT_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_MARKER_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_MLS_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_PA_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_VHF_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_VHF_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_VHF_3_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_VOR_1_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `A_ASP3_VOR_2_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_ADF_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_ADF_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_CAB_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_HF_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_HF_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_ILS_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_INT_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_MARKER_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_MLS_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_NAV_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_NAV_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_PA_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_VHF_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_VHF_2_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP3_VHF_3_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_ADF_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_ADF_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_CAB_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_CAB_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_HF_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_HF_1_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_HF_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_HF_2_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_ILS_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_INTRAD` (switch): 49 refs, Panel_ACP_Modern, Panel_ACP_Original, Panel_RMP_Original
- `S_ASP3_INT_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_INT_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_MARKER_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_MLS_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_PA_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_PA_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_RESET` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VHF_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VHF_1_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VHF_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VHF_2_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VHF_3_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VHF_3_SEND` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VOICE` (switch): 10 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VOR_1_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP3_VOR_2_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original

### ASP (captain) (3)

- `A_ASP_MLS_VOLUME` (position): 14 refs, Panel_ACP_Modern, Panel_ACP_Original
- `I_ASP_MLS_1_REC` (light): 4 refs, Panel_ACP_Modern, Panel_ACP_Original
- `S_ASP_MLS_REC_LATCH` (switch): 12 refs, Panel_ACP_Modern, Panel_ACP_Original


## Exterior: doors, cargo, GPU panel, refuel panel (100)

### CARGO (19)

- `A_EXT_CARGO_DOOR_BULK` (position): 1 ref, Ext_Fuselage_A319
- `A_EXT_CARGO_DOOR_BULK_HANDLE` (position): 36 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `I_EXT_CARGO_DOOR_OPEN_AFT` (light): 1 ref, Ext_Panel_Cargo_Door_Selector_Aft
- `I_EXT_CARGO_DOOR_OPEN_FWD` (light): 1 ref, Ext_Panel_Cargo_Door_Selector_Forward
- `S_EXT_CARGO_BAY_AFT_LT` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_BAY_FWD_LT` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_AFT_HANDLE` (switch): 9 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_AFT_HANDLE_FLAP` (switch): 15 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_BULK_HANDLE` (switch): 15 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_FWD_HANDLE` (switch): 9 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_FWD_HANDLE_FLAP` (switch): 15 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_OPEN_AFT` (switch): 5 refs, Ext_Panel_Cargo_Door_Selector_Aft
- `S_EXT_CARGO_DOOR_OPEN_FWD` (switch): 5 refs, Ext_Panel_Cargo_Door_Selector_Forward
- `S_EXT_CARGO_DOOR_SELECTOR_AFT` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_SELECTOR_FWD` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_CARGO_DOOR_SELECTOR_LOADING_AREA_LT_AFT` (switch): 11 refs, Ext_Panel_Cargo_Door_Selector_Aft
- `S_EXT_CARGO_DOOR_SELECTOR_LOADING_AREA_LT_FWD` (switch): 11 refs, Ext_Panel_Cargo_Door_Selector_Forward
- `S_EXT_CARGO_DOOR_SELECTOR_SWITCH_AFT` (switch): 20 refs, Ext_Panel_Cargo_Door_Selector_Aft
- `S_EXT_CARGO_DOOR_SELECTOR_SWITCH_FWD` (switch): 20 refs, Ext_Panel_Cargo_Door_Selector_Forward

### HYD (6)

- `B_EXT_HYD_GEAR_DOOR_PRESSURE` (state): 3 refs, Ext_Gear_Main, Ext_Gear_Nose
- `S_EXT_HYD_ACCESS_DOOR_LEFT` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_HYD_ACCESS_DOOR_RIGHT` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_HYD_GEAR_DOOR_LEFT` (switch): 15 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_HYD_GEAR_DOOR_NOSE` (switch): 12 refs, Ext_Gear_Nose
- `S_EXT_HYD_GEAR_DOOR_RIGHT` (switch): 15 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1

### DOOR (36)

- `I_EXT_DOOR_L1_CABPR` (light): 3 refs, Galley_Forward_Doors
- `I_EXT_DOOR_L2_CABPR` (light): 3 refs, Cabin_Doors_Forward
- `I_EXT_DOOR_L3_CABPR` (light): 3 refs, Cabin_Doors_Aft
- `I_EXT_DOOR_L4_CABPR` (light): 3 refs, Galley_Aft_Doors
- `I_EXT_DOOR_R1_CABPR` (light): 3 refs, Galley_Forward_Doors
- `I_EXT_DOOR_R2_CABPR` (light): 3 refs, Cabin_Doors_Forward
- `I_EXT_DOOR_R3_CABPR` (light): 3 refs, Cabin_Doors_Aft
- `I_EXT_DOOR_R4_CABPR` (light): 3 refs, Galley_Aft_Doors
- `S_EXT_DOOR_L1_HANDLE` (switch): 84 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_L1_HANDLE_FLAP` (switch): 18 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_L1_INSIDE_GUST_BUTTON` (switch): 10 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_L1_INSIDE_GUST_STATE` (switch): 36 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_L1_INSIDE_HANDLE` (switch): 94 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_L1_INSIDE_LOCK` (switch): 31 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_L2E_INSIDE_LOCK` (switch): 12 refs, Cabin_Doors_Forward, Ext_Fuselage_A321
- `S_EXT_DOOR_L2_HANDLE` (switch): 84 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_L2_HANDLE_FLAP` (switch): 18 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_L2_INSIDE_GUST_BUTTON` (switch): 10 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_L2_INSIDE_GUST_STATE` (switch): 36 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_L2_INSIDE_HANDLE` (switch): 94 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_L2_INSIDE_LOCK` (switch): 31 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_L3E_INSIDE_LOCK` (switch): 12 refs, Cabin_Doors_Aft, Ext_Fuselage_A321
- `S_EXT_DOOR_R1_HANDLE` (switch): 84 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_R1_HANDLE_FLAP` (switch): 18 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_R1_INSIDE_GUST_BUTTON` (switch): 10 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_R1_INSIDE_GUST_STATE` (switch): 36 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_R1_INSIDE_HANDLE` (switch): 94 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_R1_INSIDE_LOCK` (switch): 31 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_R2E_INSIDE_LOCK` (switch): 12 refs, Cabin_Doors_Forward, Ext_Fuselage_A321
- `S_EXT_DOOR_R2_HANDLE` (switch): 84 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_R2_HANDLE_FLAP` (switch): 18 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_R2_INSIDE_GUST_BUTTON` (switch): 10 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_R2_INSIDE_GUST_STATE` (switch): 36 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_DOOR_R2_INSIDE_HANDLE` (switch): 94 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_R2_INSIDE_LOCK` (switch): 31 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_DOOR_R3E_INSIDE_LOCK` (switch): 12 refs, Cabin_Doors_Aft, Ext_Fuselage_A321

### GPU (12)

- `I_EXT_GPU_ADIRU_AVNCS_VENT` (light): 1 ref, Ext_Panel_GPU
- `I_EXT_GPU_APU_FIRE` (light): 1 ref, Ext_Panel_GPU
- `I_EXT_GPU_AVAIL` (light): 1 ref, Ext_Panel_GPU
- `I_EXT_GPU_CKPT_CALL` (light): 1 ref, Ext_Panel_GPU
- `I_EXT_GPU_ELT` (light): 1 ref, Ext_Panel_GPU
- `I_EXT_GPU_NOT_IN_USE` (light): 1 ref, Ext_Panel_GPU
- `S_EXT_GPU_APU_SHUTOFF` (switch): 5 refs, Ext_Panel_GPU
- `S_EXT_GPU_APU_SHUTOFF_Cover` (switch): 5 refs, Ext_Panel_GPU
- `S_EXT_GPU_COCKPIT_CALL` (switch): 5 refs, Ext_Panel_GPU
- `S_EXT_GPU_LIGHT_TEST` (switch): 5 refs, Ext_Panel_GPU
- `S_EXT_GPU_PLUG` (switch): 6 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `S_EXT_GPU_RESET` (switch): 5 refs, Ext_Panel_GPU

### REFUELINGL (3)

- `I_EXT_REFUELINGL_VALVE_CENTER` (light): 1 ref, Ext_Panel_Refueling
- `S_EXT_REFUELINGL_VALVE_CENTER` (switch): 23 refs, Ext_Panel_Refueling
- `S_EXT_REFUELINGL_VALVE_CENTER_Cover` (switch): 5 refs, Ext_Panel_Refueling

### REFUELING (23)

- `I_EXT_REFUELING_ACT_VALVE_1` (light): 1 ref, Ext_Panel_Refueling_ACT
- `I_EXT_REFUELING_ACT_VALVE_2` (light): 1 ref, Ext_Panel_Refueling_ACT
- `I_EXT_REFUELING_COCKPIT_LT` (light): 1 ref, Ext_Panel_Refueling
- `I_EXT_REFUELING_END_LT` (light): 1 ref, Ext_Panel_Refueling
- `I_EXT_REFUELING_MODE` (light): 1 ref, Ext_Panel_Refueling
- `I_EXT_REFUELING_VALVE_LEFT` (light): 1 ref, Ext_Panel_Refueling
- `I_EXT_REFUELING_VALVE_RIGHT` (light): 1 ref, Ext_Panel_Refueling
- `N_EXT_REFUELING_LIGHTING` (value): 7 refs, Ext_Panel_Refueling, Ext_Panel_Refueling_ACT
- `S_EXT_REFUELING_ACT_FQI` (switch): 42 refs, Ext_Panel_Refueling_ACT
- `S_EXT_REFUELING_ACT_VALVE_1` (switch): 23 refs, Ext_Panel_Refueling_ACT
- `S_EXT_REFUELING_ACT_VALVE_1_Cover` (switch): 5 refs, Ext_Panel_Refueling_ACT
- `S_EXT_REFUELING_ACT_VALVE_2` (switch): 23 refs, Ext_Panel_Refueling_ACT
- `S_EXT_REFUELING_ACT_VALVE_2_Cover` (switch): 5 refs, Ext_Panel_Refueling_ACT
- `S_EXT_REFUELING_AMOUNT` (switch): 20 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_BATTERY` (switch): 11 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_DOOR` (switch): 33 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `S_EXT_REFUELING_MODE` (switch): 23 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_MODE_Cover` (switch): 5 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_TEST` (switch): 20 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_VALVE_LEFT` (switch): 23 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_VALVE_LEFT_Cover` (switch): 5 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_VALVE_RIGHT` (switch): 23 refs, Ext_Panel_Refueling
- `S_EXT_REFUELING_VALVE_RIGHT_Cover` (switch): 5 refs, Ext_Panel_Refueling

### MAINT (1)

- `S_EXT_MAINT_BUS` (switch): 44 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1


## Flight controls (levers, sidesticks, tiller, brakes, trim) (16)

### THROTTLE (2)

- `A_FC_THROTTLE_LEFT_GATE` (position): 12 refs, Cockpit, Cockpit_CEO
- `A_FC_THROTTLE_RIGHT_GATE` (position): 12 refs, Cockpit, Cockpit_CEO

### ELEVATOR (1)

- `B_FC_ELEVATOR_TRIM_MOTOR_POWER` (state): 2 refs, Cockpit

### RUDDER (2)

- `B_FC_RUDDER_TRIM` (state): 3 refs, Cockpit
- `N_FC_RUDDER` (value): 1 ref, Cockpit

### SPEEDBRAKE (1)

- `B_FC_SPEEDBRAKE` (state): 3 refs, Cockpit

### BRAKE (2)

- `N_FC_BRAKE_LEFT` (value): 2 refs, Cockpit
- `N_FC_BRAKE_RIGHT` (value): 2 refs, Cockpit

### CAPT (1)

- `N_FC_CAPT_TILLER` (value): 1 ref, Cockpit

### FO (2)

- `N_FC_FO_TILLER` (value): 1 ref, Cockpit
- `S_FC_FO_TILLER_PEDAL_DISCONNECT` (switch): 7 refs, Cockpit

### SIDESTICK (4)

- `N_FC_SIDESTICK_CAPT_BANK` (value): 1 ref, Cockpit
- `N_FC_SIDESTICK_CAPT_PITCH` (value): 1 ref, Cockpit
- `N_FC_SIDESTICK_FO_BANK` (value): 1 ref, Cockpit
- `N_FC_SIDESTICK_FO_PITCH` (value): 1 ref, Cockpit

### FLAPS (1)

- `S_FC_FLAPS_LATCH` (switch): 7 refs, Cockpit, Cockpit_CEO


## Main instrument panel (17)

### AIR (6)

- `A_MIP_AIR_VENT_CAPT_OPEN` (position): 2 refs, Cockpit
- `A_MIP_AIR_VENT_CAPT_PITCH` (position): 5 refs, Cockpit
- `A_MIP_AIR_VENT_CAPT_YAW` (position): 5 refs, Cockpit
- `A_MIP_AIR_VENT_FO_OPEN` (position): 2 refs, Cockpit
- `A_MIP_AIR_VENT_FO_PITCH` (position): 5 refs, Cockpit
- `A_MIP_AIR_VENT_FO_YAW` (position): 5 refs, Cockpit

### CLOCK (6)

- `E_MIP_CLOCK_SET` (encoder): 22 refs, Cockpit
- `S_MIP_CLOCK_CHR` (switch): 5 refs, Cockpit
- `S_MIP_CLOCK_ET` (switch): 20 refs, Cockpit
- `S_MIP_CLOCK_RST` (switch): 5 refs, Cockpit
- `S_MIP_CLOCK_SET` (switch): 5 refs, Cockpit
- `S_MIP_CLOCK_UTC` (switch): 19 refs, Cockpit

### AUTOLAND (4)

- `I_MIP_AUTOLAND_CAPT_L` (light): 7 refs, Cockpit
- `I_MIP_AUTOLAND_CAPT_U` (light): 8 refs, Cockpit
- `I_MIP_AUTOLAND_FO_L` (light): 7 refs, Cockpit
- `I_MIP_AUTOLAND_FO_U` (light): 8 refs, Cockpit

### ISFD (1)

- `S_MIP_ISFD_BARO_BUTTON` (switch): 5 refs, Cockpit


## Overhead panel (46)

### PNEUMATIC (5)

- `A_OH_PNEUMATIC_CARGO_AFT_TEMP` (position): 21 refs, Cockpit
- `I_OH_PNEUMATIC_ECON_FLOW_L` (light): 1 ref, Panel_Pneumatic_A321
- `S_OH_PNEUMATIC_DITCHING_Cover` (switch): 5 refs, Cockpit
- `S_OH_PNEUMATIC_ECON_FLOW` (switch): 7 refs, Panel_Pneumatic_A321
- `S_OH_PNEUMATIC_RAM_AIR_Cover` (switch): 5 refs, Cockpit

### COCKPIT (1)

- `I_OH_COCKPIT_DOOR_VIDEO` (light): 1 ref, Cockpit

### ENG (6)

- `I_OH_ENG_N1_MODE_1_L` (light): 1 ref, Cockpit_CEO
- `I_OH_ENG_N1_MODE_2_L` (light): 1 ref, Cockpit_CEO
- `S_OH_ENG_MANSTART_1_Cover` (switch): 5 refs, Cockpit
- `S_OH_ENG_MANSTART_2_Cover` (switch): 5 refs, Cockpit
- `S_OH_ENG_N1_MODE_1_Cover` (switch): 5 refs, Cockpit_CEO
- `S_OH_ENG_N1_MODE_2_Cover` (switch): 5 refs, Cockpit_CEO

### FIRE (4)

- `I_OH_FIRE_APU_BUTTON_ON_BATT` (light): 1 ref, Cockpit
- `S_OH_FIRE_APU_BUTTON_Cover` (switch): 5 refs, Cockpit
- `S_OH_FIRE_ENG1_BUTTON_Cover` (switch): 5 refs, Cockpit
- `S_OH_FIRE_ENG2_BUTTON_Cover` (switch): 5 refs, Cockpit

### FUEL (3)

- `I_OH_FUEL_ACT_L` (light): 1 ref, Panel_Fuel_ACT_Enabled
- `I_OH_FUEL_ACT_U` (light): 2 refs, Panel_Fuel_ACT_Enabled
- `S_OH_FUEL_ACT` (switch): 7 refs, Panel_Fuel_ACT_Enabled

### IN (1)

- `I_OH_IN_LT_ICE` (light): 2 refs, Cockpit

### NAV (6)

- `I_OH_NAV_IR1_ALIGN` (light): 1 ref, Panel_GNADIRS
- `I_OH_NAV_IR1_FAULT` (light): 2 refs, Panel_GNADIRS
- `I_OH_NAV_IR2_ALIGN` (light): 1 ref, Panel_GNADIRS
- `I_OH_NAV_IR2_FAULT` (light): 2 refs, Panel_GNADIRS
- `I_OH_NAV_IR3_ALIGN` (light): 1 ref, Panel_GNADIRS
- `I_OH_NAV_IR3_FAULT` (light): 2 refs, Panel_GNADIRS

### LIGHTING (1)

- `N_OH_LIGHTING_OVD` (value): 222 refs, Cockpit, Cockpit_CEO, Panel_ACP_Modern +10

### AFT (2)

- `S_OH_AFT_FADEC_GND_1_Cover` (switch): 5 refs, Cockpit
- `S_OH_AFT_FADEC_GND_2_Cover` (switch): 5 refs, Cockpit

### CALLS (2)

- `S_OH_CALLS_EXIT` (switch): 5 refs, Panel_Calls_A321
- `S_OH_CALLS_MID` (switch): 5 refs, Panel_Calls_A321

### CARGO (2)

- `S_OH_CARGO_DISC_1_OLD_LAYOUT_Cover` (switch): 5 refs, Cockpit
- `S_OH_CARGO_DISC_2_OLD_LAYOUT_Cover` (switch): 5 refs, Cockpit

### ELEC (3)

- `S_OH_ELEC_GEN1` (switch): 7 refs, Cockpit
- `S_OH_ELEC_IDG1_Cover` (switch): 5 refs, Cockpit
- `S_OH_ELEC_IDG2_Cover` (switch): 5 refs, Cockpit

### ELT (1)

- `S_OH_ELT_Cover` (switch): 5 refs, Cockpit

### EVAC (1)

- `S_OH_EVAC_COMMAND_Cover` (switch): 5 refs, Cockpit_CEO

### HYD (6)

- `S_OH_HYD_BLUE_ELEC_PUMP_Cover` (switch): 5 refs, Cockpit
- `S_OH_HYD_BLUE_PUMP_OVERRIDE_Cover` (switch): 5 refs, Cockpit
- `S_OH_HYD_LMV_BLUE_Cover` (switch): 5 refs, Cockpit
- `S_OH_HYD_LMV_GREEN_Cover` (switch): 5 refs, Cockpit
- `S_OH_HYD_LMV_YELLOW_Cover` (switch): 5 refs, Cockpit
- `S_OH_HYD_RAT_MAN_ON_Cover` (switch): 5 refs, Cockpit

### OXYGEN (2)

- `S_OH_OXYGEN_HIGH_ALT_Cover` (switch): 10 refs, Panel_Calls_A321, Panel_Calls_A32X
- `S_OH_OXYGEN_MASK_MAN_ON_Cover` (switch): 10 refs, Panel_Calls_A321, Panel_Calls_A32X


## Cabin, seats, windows, EFB and miscellaneous (47)

### CABIN (1)

- `B_CABIN_CURTAIN_OPEN` (state): 2 refs, Cabin_A321_TwoClass

### DISABLE (1)

- `B_DISABLE_REFUEL_PANEL_DOOR_INTERACTION` (state): 3 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321

### EFB (3)

- `B_EFB_MOUNT_TYPE` (state): 10 refs, Cockpit
- `S_EFB_CHARGING_CABLE_CAPT_TEMP` (switch): 4 refs, Cockpit
- `S_EFB_CHARGING_CABLE_FO_TEMP` (switch): 4 refs, Cockpit

### GEAR (1)

- `B_GEAR_RETRACT_INHIBIT` (state): 1 ref, Cockpit

### INT (1)

- `B_INT_CNOSMOK` (state): 8 refs, Cabin_A319, Cabin_A319_HD, Cabin_A319_SD +5

### LT (1)

- `B_LT_EMERGENCY_EXIT` (state): 15 refs, Cabin_A319, Cabin_A319_HD, Cabin_A319_SD +7

### REFUEL (1)

- `B_REFUEL_PANEL_POWER` (state): 1 ref, RefuelPanelGauge.js

### RUDDER (1)

- `B_RUDDER_TOGGLE` (state): 6 refs, Cockpit

### CAB (6)

- `I_CAB_FWD_LAV_OCCUPIED` (light): 3 refs, Cabin_A319, Cabin_A320, Cabin_A321
- `I_CAB_FWD_LAV_UNOCCUPIED` (light): 3 refs, Cabin_A319, Cabin_A320, Cabin_A321
- `I_CAB_REAR_LEFT_LAV_OCCUPIED` (light): 3 refs, Cabin_A319, Cabin_A320, Cabin_A321
- `I_CAB_REAR_LEFT_LAV_UNOCCUPIED` (light): 3 refs, Cabin_A319, Cabin_A320, Cabin_A321
- `I_CAB_REAR_RIGHT_LAV_OCCUPIED` (light): 3 refs, Cabin_A319_SD, Cabin_A320, Cabin_A321
- `I_CAB_REAR_RIGHT_LAV_UNOCCUPIED` (light): 3 refs, Cabin_A319_SD, Cabin_A320, Cabin_A321

### FAP (1)

- `N_FAP_SPEAKER_VOLUME` (value): 2 refs, Galley_Forward

### FUEL (5)

- `N_FUEL_CTR_TANK` (value): 1 ref, RefuelPanelGauge.js
- `N_FUEL_PRESEL` (value): 1 ref, RefuelPanelGauge.js
- `N_FUEL_TOTAL_QTY` (value): 1 ref, RefuelPanelGauge.js
- `N_FUEL_WING_TANK_L` (value): 1 ref, RefuelPanelGauge.js
- `N_FUEL_WING_TANK_R` (value): 1 ref, RefuelPanelGauge.js

### OXYGEN (4)

- `N_OXYGEN_MASK_1_TEST_CAPT` (value): 2 refs, Cockpit, Cockpit_CEO
- `N_OXYGEN_MASK_1_TEST_FO` (value): 2 refs, Cockpit, Cockpit_CEO
- `N_OXYGEN_MASK_2_TEST_CAPT` (value): 2 refs, Cockpit, Cockpit_CEO
- `N_OXYGEN_MASK_2_TEST_FO` (value): 2 refs, Cockpit, Cockpit_CEO

### SEAT (4)

- `N_SEAT_DISTANCE_CAPT` (value): 15 refs, Cockpit, Cockpit_CEO
- `N_SEAT_DISTANCE_FO` (value): 11 refs, Cockpit, Cockpit_CEO
- `N_SEAT_HEIGHT_CAPT` (value): 11 refs, Cockpit, Cockpit_CEO
- `N_SEAT_HEIGHT_FO` (value): 11 refs, Cockpit, Cockpit_CEO

### WINDOW (8)

- `N_WINDOW_CAPT` (value): 32 refs, Cockpit, Cockpit_CEO, Ext_Fuselage_A319 +2
- `N_WINDOW_FO` (value): 32 refs, Cockpit, Cockpit_CEO, Ext_Fuselage_A319 +2
- `S_WINDOW_CAPT_LATCH` (switch): 12 refs, Cockpit
- `S_WINDOW_CAPT_LEVER` (switch): 8 refs, Cockpit, Cockpit_CEO
- `S_WINDOW_CAPT_LEVER_BUTTON` (switch): 5 refs, Cockpit
- `S_WINDOW_FO_LATCH` (switch): 12 refs, Cockpit
- `S_WINDOW_FO_LEVER` (switch): 8 refs, Cockpit, Cockpit_CEO
- `S_WINDOW_FO_LEVER_BUTTON` (switch): 5 refs, Cockpit

### AUDIO (1)

- `S_AUDIO_SWITCHING` (switch): 22 refs, Cockpit_CEO

### CCTV (2)

- `S_CCTV` (switch): 5 refs, Cockpit_CEO
- `S_CCTV_SWITCH` (switch): 3 refs, Cockpit_CEO

### CHART (2)

- `S_CHART_LIGHT_TEMP_CAPT` (switch): 11 refs, Cockpit
- `S_CHART_LIGHT_TEMP_FO` (switch): 11 refs, Cockpit

### GALLEY (2)

- `S_GALLEY_AFT_BREAKERS_COVER` (switch): 22 refs, Galley_Aft, Galley_Aft_A319_HD
- `S_GALLEY_FWD_BREAKERS_COVER` (switch): 11 refs, Galley_Forward

### HYD (1)

- `S_HYD_GRAVITY_GEAR_HANDLE` (switch): 5 refs, Cockpit

### STAIRS (1)

- `S_STAIRS_BLOCKER` (switch): 37 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1


## Aircraft configuration and options (51)

### CONFIG (51)

- `B_CONFIG_ADIRS` (state): 2 refs, Cockpit, Ext_Readouts
- `B_CONFIG_BARO_UNIT` (state): 6 refs, Cockpit, Cockpit_CEO
- `B_CONFIG_BRAKEFANS` (state): 12 refs, Cockpit, Ext_Gear_Main
- `B_CONFIG_BULKCARGO` (state): 18 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `B_CONFIG_CABIN_VISIBILITY` (state): 4 refs, Cockpit, Ext_Fuselage_A319, Ext_Fuselage_A320 +1
- `B_CONFIG_CARGOMEAT` (state): 2 refs, Cockpit
- `B_CONFIG_CHOCKS` (state): 20 refs, gsx.cfg
- `B_CONFIG_CHOCK_LEFT_LF` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_LEFT_LR` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_LEFT_RF` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_LEFT_RR` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_NOSE_LF` (state): 4 refs, Ext_Gear_Nose
- `B_CONFIG_CHOCK_NOSE_LR` (state): 4 refs, Ext_Gear_Nose
- `B_CONFIG_CHOCK_NOSE_RF` (state): 4 refs, Ext_Gear_Nose
- `B_CONFIG_CHOCK_NOSE_RR` (state): 4 refs, Ext_Gear_Nose
- `B_CONFIG_CHOCK_RIGHT_LF` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_RIGHT_LR` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_RIGHT_RF` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CHOCK_RIGHT_RR` (state): 4 refs, Ext_Gear_Main
- `B_CONFIG_CONTRAILS` (state): 4 refs, Ext_CFM, Ext_IAE
- `B_CONFIG_DCDU` (state): 4 refs, Cockpit
- `B_CONFIG_DDRMI` (state): 6 refs, Cockpit, Ext_Readouts
- `B_CONFIG_DISPSAFE` (state): 2 refs, Cockpit_CEO
- `B_CONFIG_EXTRAREARWINDOW` (state): 2 refs, Ext_Fuselage_A319
- `B_CONFIG_GPU` (state): 2 refs, Ext_GPU
- `B_CONFIG_GPU_PLUG` (state): 1 ref, Ext_GPU
- `B_CONFIG_HUBCAPS` (state): 2 refs, Ext_Gear_Main
- `B_CONFIG_ICEDET` (state): 3 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `B_CONFIG_IS_NEO` (state): 303 refs, Cabin, Cockpit_CEO, Ext_Fuselage +3
- `B_CONFIG_JETPUMPS` (state): 1 ref, Panel_Fuel_ACT_Disabled
- `B_CONFIG_METRIC_STDBY` (state): 2 refs, Cockpit
- `B_CONFIG_NOISEPACKAGE` (state): 4 refs, Ext_CFM_A321
- `B_CONFIG_OWE` (state): 21 refs, A319, Cabin_A319, Cabin_A319_HD +4
- `B_CONFIG_READOUTS` (state): 39 refs, Cockpit, Cockpit_CEO, Ext_Readouts +1
- `B_CONFIG_REALISTIC_WINDOW` (state): 10 refs, Cockpit
- `B_CONFIG_REGPANEL` (state): 3 refs, Ext_Registration, Registration.js
- `B_CONFIG_STDBY` (state): 11 refs, Cockpit, Cockpit_CEO
- `B_CONFIG_WEIGHT_LBS` (state): 2 refs, Ext_Panel_Refueling
- `B_CONFIG_WINGBOX` (state): 14 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321 +1
- `N_CONFIG_ACP` (value): 6 refs, A319, A320, A321
- `N_CONFIG_ACT_COUNT` (value): 7 refs, Ext_Fuselage_A321, Ext_Panel_Refueling_ACT
- `N_CONFIG_AIRCRAFT_TYPE` (value): 5 refs, Ext_Gear_Main, Ext_Wings, LOD.js
- `N_CONFIG_ELEC_HYDRAULIC_TYPE` (value): 8 refs, Cockpit_CEO
- `N_CONFIG_ENGINETYPE` (value): 2 refs, Cockpit
- `N_CONFIG_FRONTWINDOW` (value): 6 refs, Ext_Fuselage_A319, Ext_Fuselage_A320
- `N_CONFIG_WIFI` (value): 9 refs, Ext_Fuselage_A319, Ext_Fuselage_A320, Ext_Fuselage_A321
- `N_CONFIG_WINDOW_PLUGS_LF` (value): 18 refs, Ext_Fuselage_A321
- `N_CONFIG_WINDOW_PLUGS_LR` (value): 6 refs, Ext_Fuselage_A321
- `N_CONFIG_WINDOW_PLUGS_RF` (value): 18 refs, Ext_Fuselage_A321
- `N_CONFIG_WINDOW_PLUGS_RR` (value): 6 refs, Ext_Fuselage_A321
- `N_CONFIG_WINDOW_PLUG_TYPE` (value): 48 refs, Ext_Fuselage_A321


## Pedestal (21)

### XPDR (1)

- `B_PED_XPDR` (state): 3 refs, Cockpit_CEO

### RMP1 (3)

- `E_PED_RMP1_INNER` (encoder): 44 refs, Cockpit_CEO, Panel_RMP_Original
- `E_PED_RMP1_OUTER` (encoder): 44 refs, Cockpit_CEO, Panel_RMP_Original
- `S_PED_RMP1_NAV_Cover` (switch): 10 refs, Cockpit_CEO, Panel_RMP_Original

### RMP2 (3)

- `E_PED_RMP2_INNER` (encoder): 44 refs, Cockpit_CEO, Panel_RMP_Original
- `E_PED_RMP2_OUTER` (encoder): 44 refs, Cockpit_CEO, Panel_RMP_Original
- `S_PED_RMP2_NAV_Cover` (switch): 10 refs, Cockpit_CEO, Panel_RMP_Original

### RMP3 (3)

- `E_PED_RMP3_INNER` (encoder): 44 refs, Cockpit_CEO, Panel_RMP_Original
- `E_PED_RMP3_OUTER` (encoder): 44 refs, Cockpit_CEO, Panel_RMP_Original
- `S_PED_RMP3_NAV_Cover` (switch): 10 refs, Cockpit_CEO, Panel_RMP_Original

### AIDS (1)

- `S_PED_AIDS_PRINT` (switch): 5 refs, Cockpit

### ATC (8)

- `S_PED_ATC_0` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_1` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_2` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_3` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_4` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_5` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_6` (switch): 5 refs, Cockpit_CEO
- `S_PED_ATC_7` (switch): 5 refs, Cockpit_CEO

### COCKPIT (1)

- `S_PED_COCKPIT_DOOR` (switch): 20 refs, Cockpit

### DFDR (1)

- `S_PED_DFDR_EVENT` (switch): 5 refs, Cockpit


## Circuit breakers (E_CB_<panel>VU_<slot>) (324)

### 106VU (2)

`E_CB_106VU_B03`, `E_CB_106VU_B04`

(referenced in: Cockpit_CEO)

### 121VU (147)

`E_CB_121VU_J18`, `E_CB_121VU_K02`, `E_CB_121VU_K06`, `E_CB_121VU_K07`, `E_CB_121VU_K08`, `E_CB_121VU_K10`, `E_CB_121VU_K11`, `E_CB_121VU_K12`, `E_CB_121VU_K19`, `E_CB_121VU_K23`, `E_CB_121VU_K25`, `E_CB_121VU_K39`, `E_CB_121VU_L02`, `E_CB_121VU_L03`, `E_CB_121VU_L04`, `E_CB_121VU_L05`, `E_CB_121VU_L13`, `E_CB_121VU_L15`, `E_CB_121VU_L16`, `E_CB_121VU_L17`, `E_CB_121VU_L18`, `E_CB_121VU_L23`, `E_CB_121VU_L24`, `E_CB_121VU_L25`, `E_CB_121VU_L32`, `E_CB_121VU_L35`, `E_CB_121VU_L38`, `E_CB_121VU_L39`, `E_CB_121VU_L40`, `E_CB_121VU_L41`, `E_CB_121VU_L42`, `E_CB_121VU_L43`, `E_CB_121VU_L44`, `E_CB_121VU_M02`, `E_CB_121VU_M05`, `E_CB_121VU_M06`, `E_CB_121VU_M08`, `E_CB_121VU_M09`, `E_CB_121VU_M10`, `E_CB_121VU_M11`, `E_CB_121VU_M12`, `E_CB_121VU_M17`, `E_CB_121VU_M18`, `E_CB_121VU_M19`, `E_CB_121VU_M20`, `E_CB_121VU_M21`, `E_CB_121VU_M22`, `E_CB_121VU_M23`, `E_CB_121VU_M24`, `E_CB_121VU_M25`, `E_CB_121VU_M26`, `E_CB_121VU_M27`, `E_CB_121VU_M31`, `E_CB_121VU_M32`, `E_CB_121VU_M33`, `E_CB_121VU_M34`, `E_CB_121VU_M35`, `E_CB_121VU_M36`, `E_CB_121VU_M37`, `E_CB_121VU_M38`, `E_CB_121VU_M39`, `E_CB_121VU_M40`, `E_CB_121VU_M41`, `E_CB_121VU_M42`, `E_CB_121VU_N03`, `E_CB_121VU_N04`, `E_CB_121VU_N05`, `E_CB_121VU_N06`, `E_CB_121VU_N07`, `E_CB_121VU_N10`, `E_CB_121VU_N16`, `E_CB_121VU_N20`, `E_CB_121VU_N23`, `E_CB_121VU_N26`, `E_CB_121VU_N28`, `E_CB_121VU_N30`, `E_CB_121VU_N34`, `E_CB_121VU_N35`, `E_CB_121VU_N36`, `E_CB_121VU_N37`, `E_CB_121VU_N39`, `E_CB_121VU_N40`, `E_CB_121VU_N41`, `E_CB_121VU_N42`, `E_CB_121VU_N44`, `E_CB_121VU_N45`, `E_CB_121VU_P02`, `E_CB_121VU_P05`, `E_CB_121VU_P06`, `E_CB_121VU_P07`, `E_CB_121VU_P09`, `E_CB_121VU_P10`, `E_CB_121VU_P11`, `E_CB_121VU_P15`, `E_CB_121VU_P16`, `E_CB_121VU_P19`, `E_CB_121VU_P20`, `E_CB_121VU_P30`, `E_CB_121VU_P33`, `E_CB_121VU_P39`, `E_CB_121VU_P40`, `E_CB_121VU_P41`, `E_CB_121VU_P42`, `E_CB_121VU_Q02`, `E_CB_121VU_Q03`, `E_CB_121VU_Q05`, `E_CB_121VU_Q06`, `E_CB_121VU_Q07`, `E_CB_121VU_Q08`, `E_CB_121VU_Q09`, `E_CB_121VU_Q14`, `E_CB_121VU_Q16`, `E_CB_121VU_Q17`, `E_CB_121VU_Q18`, `E_CB_121VU_Q19`, `E_CB_121VU_Q20`, `E_CB_121VU_Q21`, `E_CB_121VU_Q23`, `E_CB_121VU_Q26`, `E_CB_121VU_Q34`, `E_CB_121VU_Q35`, `E_CB_121VU_Q38`, `E_CB_121VU_Q39`, `E_CB_121VU_Q40`, `E_CB_121VU_Q41`, `E_CB_121VU_Q42`, `E_CB_121VU_Q43`, `E_CB_121VU_Q44`, `E_CB_121VU_R03`, `E_CB_121VU_R04`, `E_CB_121VU_R05`, `E_CB_121VU_R06`, `E_CB_121VU_R08`, `E_CB_121VU_R10`, `E_CB_121VU_R11`, `E_CB_121VU_R14`, `E_CB_121VU_R16`, `E_CB_121VU_R20`, `E_CB_121VU_R21`, `E_CB_121VU_R23`, `E_CB_121VU_R24`, `E_CB_121VU_R25`, `E_CB_121VU_R26`, `E_CB_121VU_R27`, `E_CB_121VU_R28`, `E_CB_121VU_R29`, `E_CB_121VU_R41`

(referenced in: Cockpit_CEO)

### 122VU (65)

`E_CB_122VU_S06`, `E_CB_122VU_S07`, `E_CB_122VU_S22`, `E_CB_122VU_T02`, `E_CB_122VU_T03`, `E_CB_122VU_T04`, `E_CB_122VU_T05`, `E_CB_122VU_T06`, `E_CB_122VU_T07`, `E_CB_122VU_T13`, `E_CB_122VU_U03`, `E_CB_122VU_U06`, `E_CB_122VU_U19`, `E_CB_122VU_U21`, `E_CB_122VU_V02`, `E_CB_122VU_V03`, `E_CB_122VU_V05`, `E_CB_122VU_V12`, `E_CB_122VU_V13`, `E_CB_122VU_W02`, `E_CB_122VU_W03`, `E_CB_122VU_W05`, `E_CB_122VU_W06`, `E_CB_122VU_W07`, `E_CB_122VU_W08`, `E_CB_122VU_W09`, `E_CB_122VU_W10`, `E_CB_122VU_W12`, `E_CB_122VU_W13`, `E_CB_122VU_W18`, `E_CB_122VU_W20`, `E_CB_122VU_W25`, `E_CB_122VU_W26`, `E_CB_122VU_X10`, `E_CB_122VU_X12`, `E_CB_122VU_X13`, `E_CB_122VU_X18`, `E_CB_122VU_X20`, `E_CB_122VU_Y03`, `E_CB_122VU_Y04`, `E_CB_122VU_Y05`, `E_CB_122VU_Y06`, `E_CB_122VU_Y11`, `E_CB_122VU_Y12`, `E_CB_122VU_Y13`, `E_CB_122VU_Y14`, `E_CB_122VU_Y15`, `E_CB_122VU_Y16`, `E_CB_122VU_Y17`, `E_CB_122VU_Y22`, `E_CB_122VU_Y23`, `E_CB_122VU_Z04`, `E_CB_122VU_Z05`, `E_CB_122VU_Z06`, `E_CB_122VU_Z07`, `E_CB_122VU_Z08`, `E_CB_122VU_Z09`, `E_CB_122VU_Z12`, `E_CB_122VU_Z13`, `E_CB_122VU_Z14`, `E_CB_122VU_Z15`, `E_CB_122VU_Z16`, `E_CB_122VU_Z20`, `E_CB_122VU_Z22`, `E_CB_122VU_Z23`

(referenced in: Cockpit_CEO)

### 123VU (3)

`E_CB_123VU_AB01`, `E_CB_123VU_AB09`, `E_CB_123VU_AB12`

(referenced in: Cockpit_CEO)

### 2000VU (4)

`E_CB_2000VU_F01`, `E_CB_2000VU_J06`, `E_CB_2000VU_J07`, `E_CB_2000VU_J08`

(referenced in: Galley_Forward)

### 2001VU (8)

`E_CB_2001VU_A01`, `E_CB_2001VU_A02`, `E_CB_2001VU_A03`, `E_CB_2001VU_A04`, `E_CB_2001VU_A06`, `E_CB_2001VU_A08`, `E_CB_2001VU_B04`, `E_CB_2001VU_B06`

(referenced in: Galley_Aft, Galley_Aft_A319_HD)

### 49VU (95)

`E_CB_49VU_A01`, `E_CB_49VU_A02`, `E_CB_49VU_A03`, `E_CB_49VU_A04`, `E_CB_49VU_A05`, `E_CB_49VU_A06`, `E_CB_49VU_A07`, `E_CB_49VU_A08`, `E_CB_49VU_A09`, `E_CB_49VU_A10`, `E_CB_49VU_A11`, `E_CB_49VU_A12`, `E_CB_49VU_A13`, `E_CB_49VU_A14`, `E_CB_49VU_B01`, `E_CB_49VU_B02`, `E_CB_49VU_B05`, `E_CB_49VU_B06`, `E_CB_49VU_B07`, `E_CB_49VU_B08`, `E_CB_49VU_B09`, `E_CB_49VU_B10`, `E_CB_49VU_B11`, `E_CB_49VU_B12`, `E_CB_49VU_B14`, `E_CB_49VU_C01`, `E_CB_49VU_C02`, `E_CB_49VU_C07`, `E_CB_49VU_C09`, `E_CB_49VU_C10`, `E_CB_49VU_C11`, `E_CB_49VU_C12`, `E_CB_49VU_C13`, `E_CB_49VU_C14`, `E_CB_49VU_D02`, `E_CB_49VU_D03`, `E_CB_49VU_D04`, `E_CB_49VU_D05`, `E_CB_49VU_D06`, `E_CB_49VU_D08`, `E_CB_49VU_D09`, `E_CB_49VU_D10`, `E_CB_49VU_D11`, `E_CB_49VU_D12`, `E_CB_49VU_D13`, `E_CB_49VU_E01`, `E_CB_49VU_E02`, `E_CB_49VU_E03`, `E_CB_49VU_E04`, `E_CB_49VU_E05`, `E_CB_49VU_E06`, `E_CB_49VU_E07`, `E_CB_49VU_E09`, `E_CB_49VU_E10`, `E_CB_49VU_E11`, `E_CB_49VU_E12`, `E_CB_49VU_E13`, `E_CB_49VU_E14`, `E_CB_49VU_F01`, `E_CB_49VU_F02`, `E_CB_49VU_F03`, `E_CB_49VU_F04`, `E_CB_49VU_F06`, `E_CB_49VU_F09`, `E_CB_49VU_F11`, `E_CB_49VU_G01`, `E_CB_49VU_G02`, `E_CB_49VU_G03`, `E_CB_49VU_G06`, `E_CB_49VU_G07`, `E_CB_49VU_G09`, `E_CB_49VU_G10`, `E_CB_49VU_G11`, `E_CB_49VU_G12`, `E_CB_49VU_G13`, `E_CB_49VU_G14`, `E_CB_49VU_H01`, `E_CB_49VU_H04`, `E_CB_49VU_H08`, `E_CB_49VU_H10`, `E_CB_49VU_H11`, `E_CB_49VU_H14`, `E_CB_49VU_HA01`, `E_CB_49VU_HA02`, `E_CB_49VU_HA03`, `E_CB_49VU_HA04`, `E_CB_49VU_HA05`, `E_CB_49VU_HA06`, `E_CB_49VU_HA07`, `E_CB_49VU_HA08`, `E_CB_49VU_HA09`, `E_CB_49VU_HA10`, `E_CB_49VU_HA11`, `E_CB_49VU_HA12`, `E_CB_49VU_HA14`

(referenced in: Cockpit_CEO)


## Standby instruments (altimeter, ASI, attitude, compass, DDRMI) (40)

### ALT (24)

- `E_STANDBY_ALT_IMPERIAL_BARO` (encoder): 26 refs, Cockpit
- `E_STANDBY_ALT_METRIC_BARO` (encoder): 22 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_100` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_IMPERIAL_1000` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_IMPERIAL_10000` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_IMPERIAL_BARO_1` (value): 5 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_BARO_2` (value): 5 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_BARO_3` (value): 5 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_BARO_4` (value): 5 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_MARKER_1` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_MARKER_2` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_MARKER_3` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_IMPERIAL_MARKER_4` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_METRIC_100` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_1000` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_10000` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_BARO_1` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_BARO_2` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_BARO_3` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_BARO_4` (value): 1 ref, Cockpit
- `N_STANDBY_ALT_METRIC_MARKER_1` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_METRIC_MARKER_2` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_METRIC_MARKER_3` (value): 44 refs, Cockpit
- `N_STANDBY_ALT_METRIC_MARKER_4` (value): 44 refs, Cockpit

### ASI (5)

- `N_STANDBY_ASI` (value): 1 ref, Cockpit
- `N_STANDBY_ASI_MARKER_1` (value): 44 refs, Cockpit
- `N_STANDBY_ASI_MARKER_2` (value): 44 refs, Cockpit
- `N_STANDBY_ASI_MARKER_3` (value): 44 refs, Cockpit
- `N_STANDBY_ASI_MARKER_4` (value): 44 refs, Cockpit

### ATTITUDE (3)

- `N_STANDBY_ATTITUDE_BANK` (value): 2 refs, Cockpit, Cockpit_CEO
- `N_STANDBY_ATTITUDE_PITCH` (value): 2 refs, Cockpit, Cockpit_CEO
- `S_STANDBY_ATTITUDE_FLAG` (switch): 1 ref, Cockpit

### COMPASS (1)

- `N_STANDBY_COMPASS` (value): 1 ref, Cockpit

### DDRMI (6)

- `N_STANDBY_DDRMI_COMPASS` (value): 1 ref, Cockpit
- `N_STANDBY_DDRMI_DME1` (value): 1 ref, Cockpit
- `N_STANDBY_DDRMI_DME2` (value): 1 ref, Cockpit
- `S_STANDBY_DDRMI_HDG` (switch): 1 ref, Cockpit
- `S_STANDBY_DDRMI_VOR1` (switch): 1 ref, Cockpit
- `S_STANDBY_DDRMI_VOR2` (switch): 1 ref, Cockpit

### HORIZON (1)

- `N_STANDBY_HORIZON_RPM` (value): 5 refs, Cockpit_CEO


## Livery and registration (FNX_*) (3)

- `FNX_SELCAL`: 1 ref, Registration.js
- `FNX_TAIL_HI`: 1 ref, Registration.js
- `FNX_TAIL_LO`: 1 ref, Registration.js


## Display and panel brightness (16)

### BRIGHTNESS (9)

- `N_BRIGHTNESS_ANNUNCIATORS` (value): 479 refs, Cockpit, Cockpit_CEO, Panel_ACP_Modern +10
- `N_BRIGHTNESS_CLOCK` (value): 4 refs, Cockpit, Ext_Readouts
- `N_BRIGHTNESS_EFB_CAPT` (value): 1 ref, Cockpit
- `N_BRIGHTNESS_EFB_FO` (value): 1 ref, Cockpit
- `N_BRIGHTNESS_FAP` (value): 1 ref, Galley_Forward
- `N_BRIGHTNESS_RADIO_CAPT` (value): 7 refs, Cockpit_CEO, Ext_Readouts, Panel_RMP_Original
- `N_BRIGHTNESS_RADIO_FO` (value): 6 refs, Cockpit_CEO, Panel_RMP_Original
- `N_BRIGHTNESS_RADIO_OH` (value): 6 refs, Cockpit, Panel_GNADIRS, Panel_RMP_Original
- `N_BRIGHTNESS_VOLTAGE_MISC` (value): 4 refs, Cockpit, Ext_Readouts

### DISPLAY (7)

- `N_DISPLAY_BRIGHTNESS_CI` (value): 1 ref, Cockpit
- `N_DISPLAY_BRIGHTNESS_CO` (value): 1 ref, Cockpit
- `N_DISPLAY_BRIGHTNESS_ECAM_L` (value): 1 ref, Cockpit
- `N_DISPLAY_BRIGHTNESS_ECAM_U` (value): 1 ref, Cockpit
- `N_DISPLAY_BRIGHTNESS_FI` (value): 1 ref, Cockpit
- `N_DISPLAY_BRIGHTNESS_FO` (value): 1 ref, Cockpit
- `N_DISPLAY_BRIGHTNESS_STANDBY` (value): 1 ref, Cockpit


## MCDU (6)

### CDU1 (3)

- `N_CDU1_BRIGHTNESS` (value): 1 ref, Cockpit
- `S_CDU1_BRIGHTNESS_DOWN` (switch): 5 refs, Cockpit
- `S_CDU1_BRIGHTNESS_UP` (switch): 5 refs, Cockpit

### CDU2 (3)

- `N_CDU2_BRIGHTNESS` (value): 1 ref, Cockpit
- `S_CDU2_BRIGHTNESS_DOWN` (switch): 5 refs, Cockpit
- `S_CDU2_BRIGHTNESS_UP` (switch): 5 refs, Cockpit


## Template and view helpers (animation momentum, view counters) (8)

`N_CLOCK_VIEWS`, `N_DUMMY_VAR_VIEWS`, `N_INTERACTIVE_POINT_OPEN_0_MOMENTUM`, `N_INTERACTIVE_POINT_OPEN_10`, `N_INTERACTIVE_POINT_OPEN_3_MOMENTUM`, `N_INTERACTIVE_POINT_OPEN_4_MOMENTUM`, `N_INTERACTIVE_POINT_OPEN_7_MOMENTUM`, `N_READOUTS_VIEWS`


## DCDU (datalink) (11)

### DCDU1 (1)

- `N_DCDU1_BRIGHTNESS` (value): 1 ref, Cockpit

### DCDU2 (10)

- `N_DCDU2_BRIGHTNESS` (value): 1 ref, Cockpit
- `S_DCDU2_LSK1L` (switch): 5 refs, Cockpit
- `S_DCDU2_LSK1R` (switch): 5 refs, Cockpit
- `S_DCDU2_LSK2L` (switch): 5 refs, Cockpit
- `S_DCDU2_LSK2R` (switch): 5 refs, Cockpit
- `S_DCDU2_MSGDWN` (switch): 5 refs, Cockpit
- `S_DCDU2_MSGUP` (switch): 5 refs, Cockpit
- `S_DCDU2_PGDN` (switch): 5 refs, Cockpit
- `S_DCDU2_PGUP` (switch): 5 refs, Cockpit
- `S_DCDU2_PRINT` (switch): 5 refs, Cockpit


## Glareshield: FCU and EFIS (1)

### FCU (1)

- `N_FCU_LIGHTING` (value): 34 refs, Cockpit


## ECAM control panel (1)

### ECAM (1)

- `S_ECAM_EMER_CANCEL_Cover` (switch): 5 refs, Cockpit


## Working Title avionics library bundled in the EFB/LOD/refuel JS (not Fenix cockpit controls) (60)

`WTAP_GP_Approach_Mode`, `WTAP_GP_Distance`, `WTAP_GP_FPA`, `WTAP_GP_Required_VS`, `WTAP_GP_Service_Level`, `WTAP_GP_Vertical_Deviation`, `WTAP_LNav_Along_Track_Speed`, `WTAP_LNav_Course_To_Steer`, `WTAP_LNav_DTK`, `WTAP_LNav_Is_Suspended`, `WTAP_LNav_Is_Tracking`, `WTAP_LNav_Leg_Distance_Along`, `WTAP_LNav_Leg_Distance_Remaining`, `WTAP_LNav_Obs_Active`, `WTAP_LNav_Obs_Course`, `WTAP_LNav_Tracked_Leg_Index`, `WTAP_LNav_Tracked_Vector_Index`, `WTAP_LNav_Transition_Mode`, `WTAP_LNav_Vector_Anticipation_Distance`, `WTAP_LNav_Vector_Distance_Along`, `WTAP_LNav_Vector_Distance_Remaining`, `WTAP_LNav_XTK`, `WTAP_Lateral_Active_Mode`, `WTAP_Lateral_Armed_Mode`, `WTAP_VNAV_Required_VS`, `WTAP_VNav_Alt_Capture_Type`, `WTAP_VNav_BOC_Leg_Index`, `WTAP_VNav_BOD_Leg_Index`, `WTAP_VNav_Constraint_Altitude`, `WTAP_VNav_Constraint_Leg_Index`, `WTAP_VNav_Distance_To_BOC`, `WTAP_VNav_Distance_To_BOD`, `WTAP_VNav_Distance_To_TOC`, `WTAP_VNav_Distance_To_TOD`, `WTAP_VNav_FPA`, `WTAP_VNav_Next_Constraint_Altitude`, `WTAP_VNav_Path_Available`, `WTAP_VNav_Path_Mode`, `WTAP_VNav_State`, `WTAP_VNav_TOC_Distance_In_Leg`, `WTAP_VNav_TOC_Leg_Index`, `WTAP_VNav_TOD_Distance_In_Leg`, `WTAP_VNav_TOD_Leg_Index`, `WTAP_VNav_Target_Altitude`, `WTAP_VNav_Vertical_Deviation`, `WTAP_Vertical_Active_Mode`, `WTAP_Vertical_Armed_Mode`, `WT_AP_FPA_Target:`, `WT_LNavData_CDI_Scale`, `WT_LNavData_DTK_Mag`, `WT_LNavData_DTK_True`, `WT_LNavData_Destination_Distance`, `WT_LNavData_Waypoint_Bearing_Mag`, `WT_LNavData_Waypoint_Bearing_True`, `WT_LNavData_Waypoint_Distance`, `WT_LNavData_XTK`, `WT_MINIMUMS_MODE`, `XMLVAR_Baro`, `XMLVAR_SpeedIsManuallySet`, `XMLVAR_VNAVButtonValue`


## Input events (B:) (345)

The v3 package exposes MSFS input events for its knobs, none of which our code uses. Preset names follow `FNX320_Input_Knob_PushPull_<var>` with actions such as `_Push`, `_Pull`, `_Knob_Inc`, `_Knob_Dec`, `_Toggle`, plus the standby-instrument bug markers. Grouped by the variable each one drives:

- `A_ASP2_ADF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_ADF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_1_VOLUME_Toggle_Set`
- `A_ASP2_ADF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_ADF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_ADF_2_VOLUME_Toggle_Set`
- `A_ASP2_CAB_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_CAB_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_CAB_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_CAB_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_CAB_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_CAB_VOLUME_Toggle_Set`
- `A_ASP2_HF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_HF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_1_VOLUME_Toggle_Set`
- `A_ASP2_HF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_HF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_HF_2_VOLUME_Toggle_Set`
- `A_ASP2_ILS_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_ILS_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_ILS_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_ILS_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_ILS_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_ILS_VOLUME_Toggle_Set`
- `A_ASP2_INT_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_INT_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_INT_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_INT_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_INT_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_INT_VOLUME_Toggle_Set`
- `A_ASP2_MARKER_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_MARKER_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_MARKER_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_MARKER_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_MARKER_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_MARKER_VOLUME_Toggle_Set`
- `A_ASP2_MLS_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_MLS_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_MLS_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_MLS_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_MLS_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_MLS_VOLUME_Toggle_Set`
- `A_ASP2_PA_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_PA_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_PA_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_PA_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_PA_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_PA_VOLUME_Toggle_Set`
- `A_ASP2_VHF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_VHF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_1_VOLUME_Toggle_Set`
- `A_ASP2_VHF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_VHF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_2_VOLUME_Toggle_Set`
- `A_ASP2_VHF_3_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_VHF_3_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_3_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_3_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_3_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_VHF_3_VOLUME_Toggle_Set`
- `A_ASP2_VOR_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_VOR_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_1_VOLUME_Toggle_Set`
- `A_ASP2_VOR_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP2_VOR_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP2_VOR_2_VOLUME_Toggle_Set`
- `A_ASP3_ADF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_ADF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_1_VOLUME_Toggle_Set`
- `A_ASP3_ADF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_ADF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_ADF_2_VOLUME_Toggle_Set`
- `A_ASP3_CAB_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_CAB_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_CAB_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_CAB_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_CAB_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_CAB_VOLUME_Toggle_Set`
- `A_ASP3_HF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_HF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_1_VOLUME_Toggle_Set`
- `A_ASP3_HF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_HF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_HF_2_VOLUME_Toggle_Set`
- `A_ASP3_ILS_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_ILS_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_ILS_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_ILS_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_ILS_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_ILS_VOLUME_Toggle_Set`
- `A_ASP3_INT_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_INT_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_INT_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_INT_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_INT_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_INT_VOLUME_Toggle_Set`
- `A_ASP3_MARKER_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_MARKER_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_MARKER_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_MARKER_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_MARKER_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_MARKER_VOLUME_Toggle_Set`
- `A_ASP3_MLS_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_MLS_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_MLS_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_MLS_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_MLS_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_MLS_VOLUME_Toggle_Set`
- `A_ASP3_PA_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_PA_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_PA_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_PA_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_PA_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_PA_VOLUME_Toggle_Set`
- `A_ASP3_VHF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_VHF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_1_VOLUME_Toggle_Set`
- `A_ASP3_VHF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_VHF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_2_VOLUME_Toggle_Set`
- `A_ASP3_VHF_3_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_VHF_3_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_3_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_3_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_3_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_VHF_3_VOLUME_Toggle_Set`
- `A_ASP3_VOR_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_VOR_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_1_VOLUME_Toggle_Set`
- `A_ASP3_VOR_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP3_VOR_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP3_VOR_2_VOLUME_Toggle_Set`
- `A_ASP_ADF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_ADF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_1_VOLUME_Toggle_Set`
- `A_ASP_ADF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_ADF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_ADF_2_VOLUME_Toggle_Set`
- `A_ASP_CAB_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_CAB_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_CAB_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_CAB_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_CAB_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_CAB_VOLUME_Toggle_Set`
- `A_ASP_HF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_HF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_HF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_HF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_HF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_HF_1_VOLUME_Toggle_Set`
- `A_ASP_HF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_HF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_HF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_HF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_HF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_HF_2_VOLUME_Toggle_Set`
- `A_ASP_ILS_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_ILS_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_ILS_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_ILS_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_ILS_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_ILS_VOLUME_Toggle_Set`
- `A_ASP_INT_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_INT_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_INT_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_INT_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_INT_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_INT_VOLUME_Toggle_Set`
- `A_ASP_MARKER_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_MARKER_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_MARKER_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_MARKER_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_MARKER_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_MARKER_VOLUME_Toggle_Set`
- `A_ASP_MLS_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_MLS_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_MLS_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_MLS_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_MLS_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_MLS_VOLUME_Toggle_Set`
- `A_ASP_PA_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_PA_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_PA_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_PA_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_PA_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_PA_VOLUME_Toggle_Set`
- `A_ASP_VHF_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_VHF_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_1_VOLUME_Toggle_Set`
- `A_ASP_VHF_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_VHF_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_2_VOLUME_Toggle_Set`
- `A_ASP_VHF_3_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_VHF_3_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_3_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_3_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_3_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_VHF_3_VOLUME_Toggle_Set`
- `A_ASP_VOR_1_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_VOR_1_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_1_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_1_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_1_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_1_VOLUME_Toggle_Set`
- `A_ASP_VOR_2_VOLUME`: `FNX320_Input_Knob_PushPull_A_ASP_VOR_2_VOLUME_Knob_Finite`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_2_VOLUME_Knob_Finite_Dec`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_2_VOLUME_Knob_Finite_Inc`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_2_VOLUME_Toggle`, `FNX320_Input_Knob_PushPull_A_ASP_VOR_2_VOLUME_Toggle_Set`
- `E_FCU_ALTITUDE`: `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Knob`, `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Knob_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Pull`, `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Pull_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Push`, `FNX320_Input_Knob_PushPull_E_FCU_ALTITUDE_Push_Inc`
- `E_FCU_EFIS1_BARO`: `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Knob`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Knob_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Pull`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Pull_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Push`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS1_BARO_Push_Inc`
- `E_FCU_EFIS2_BARO`: `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Knob`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Knob_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Pull`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Pull_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Push`, `FNX320_Input_Knob_PushPull_E_FCU_EFIS2_BARO_Push_Inc`
- `E_FCU_HEADING`: `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Knob`, `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Knob_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Pull`, `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Pull_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Push`, `FNX320_Input_Knob_PushPull_E_FCU_HEADING_Push_Inc`
- `E_FCU_SPEED`: `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Knob`, `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Knob_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Pull`, `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Pull_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Push`, `FNX320_Input_Knob_PushPull_E_FCU_SPEED_Push_Inc`
- `E_FCU_VS`: `FNX320_Input_Knob_PushPull_E_FCU_VS_Knob`, `FNX320_Input_Knob_PushPull_E_FCU_VS_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_FCU_VS_Knob_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_VS_Pull`, `FNX320_Input_Knob_PushPull_E_FCU_VS_Pull_Inc`, `FNX320_Input_Knob_PushPull_E_FCU_VS_Push`, `FNX320_Input_Knob_PushPull_E_FCU_VS_Push_Inc`
- `E_MIP_CLOCK_SET`: `FNX320_Input_Knob_PushPull_E_MIP_CLOCK_SET_Knob`, `FNX320_Input_Knob_PushPull_E_MIP_CLOCK_SET_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_MIP_CLOCK_SET_Knob_Inc`
- `E_MIP_ISFD_BARO`: `FNX320_Input_Knob_PushPull_E_MIP_ISFD_BARO_Knob`, `FNX320_Input_Knob_PushPull_E_MIP_ISFD_BARO_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_MIP_ISFD_BARO_Knob_Inc`
- `E_PED_RMP1_INNER`: `FNX320_Input_Knob_PushPull_E_PED_RMP1_INNER_Knob`, `FNX320_Input_Knob_PushPull_E_PED_RMP1_INNER_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_PED_RMP1_INNER_Knob_Inc`
- `E_PED_RMP1_OUTER`: `FNX320_Input_Knob_PushPull_E_PED_RMP1_OUTER_Knob`, `FNX320_Input_Knob_PushPull_E_PED_RMP1_OUTER_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_PED_RMP1_OUTER_Knob_Inc`
- `E_PED_RMP2_INNER`: `FNX320_Input_Knob_PushPull_E_PED_RMP2_INNER_Knob`, `FNX320_Input_Knob_PushPull_E_PED_RMP2_INNER_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_PED_RMP2_INNER_Knob_Inc`
- `E_PED_RMP2_OUTER`: `FNX320_Input_Knob_PushPull_E_PED_RMP2_OUTER_Knob`, `FNX320_Input_Knob_PushPull_E_PED_RMP2_OUTER_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_PED_RMP2_OUTER_Knob_Inc`
- `E_PED_RMP3_INNER`: `FNX320_Input_Knob_PushPull_E_PED_RMP3_INNER_Knob`, `FNX320_Input_Knob_PushPull_E_PED_RMP3_INNER_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_PED_RMP3_INNER_Knob_Inc`
- `E_PED_RMP3_OUTER`: `FNX320_Input_Knob_PushPull_E_PED_RMP3_OUTER_Knob`, `FNX320_Input_Knob_PushPull_E_PED_RMP3_OUTER_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_PED_RMP3_OUTER_Knob_Inc`
- `E_STANDBY_ALT_IMPERIAL_BARO`: `FNX320_Input_Knob_PushPull_E_STANDBY_ALT_IMPERIAL_BARO_Knob`, `FNX320_Input_Knob_PushPull_E_STANDBY_ALT_IMPERIAL_BARO_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_STANDBY_ALT_IMPERIAL_BARO_Knob_Inc`
- `E_STANDBY_ALT_METRIC_BARO`: `FNX320_Input_Knob_PushPull_E_STANDBY_ALT_METRIC_BARO_Knob`, `FNX320_Input_Knob_PushPull_E_STANDBY_ALT_METRIC_BARO_Knob_Dec`, `FNX320_Input_Knob_PushPull_E_STANDBY_ALT_METRIC_BARO_Knob_Inc`
- `N_STANDBY_ALT_IMPERIAL_MARKER_1`: `N_STANDBY_ALT_IMPERIAL_MARKER_1`, `N_STANDBY_ALT_IMPERIAL_MARKER_1_Dec`, `N_STANDBY_ALT_IMPERIAL_MARKER_1_Inc`, `N_STANDBY_ALT_IMPERIAL_MARKER_1_Set`
- `N_STANDBY_ALT_IMPERIAL_MARKER_2`: `N_STANDBY_ALT_IMPERIAL_MARKER_2`, `N_STANDBY_ALT_IMPERIAL_MARKER_2_Dec`, `N_STANDBY_ALT_IMPERIAL_MARKER_2_Inc`, `N_STANDBY_ALT_IMPERIAL_MARKER_2_Set`
- `N_STANDBY_ALT_IMPERIAL_MARKER_3`: `N_STANDBY_ALT_IMPERIAL_MARKER_3`, `N_STANDBY_ALT_IMPERIAL_MARKER_3_Dec`, `N_STANDBY_ALT_IMPERIAL_MARKER_3_Inc`, `N_STANDBY_ALT_IMPERIAL_MARKER_3_Set`
- `N_STANDBY_ALT_IMPERIAL_MARKER_4`: `N_STANDBY_ALT_IMPERIAL_MARKER_4`, `N_STANDBY_ALT_IMPERIAL_MARKER_4_Dec`, `N_STANDBY_ALT_IMPERIAL_MARKER_4_Inc`, `N_STANDBY_ALT_IMPERIAL_MARKER_4_Set`
- `N_STANDBY_ALT_METRIC_MARKER_1`: `N_STANDBY_ALT_METRIC_MARKER_1`, `N_STANDBY_ALT_METRIC_MARKER_1_Dec`, `N_STANDBY_ALT_METRIC_MARKER_1_Inc`, `N_STANDBY_ALT_METRIC_MARKER_1_Set`
- `N_STANDBY_ALT_METRIC_MARKER_2`: `N_STANDBY_ALT_METRIC_MARKER_2`, `N_STANDBY_ALT_METRIC_MARKER_2_Dec`, `N_STANDBY_ALT_METRIC_MARKER_2_Inc`, `N_STANDBY_ALT_METRIC_MARKER_2_Set`
- `N_STANDBY_ALT_METRIC_MARKER_3`: `N_STANDBY_ALT_METRIC_MARKER_3`, `N_STANDBY_ALT_METRIC_MARKER_3_Dec`, `N_STANDBY_ALT_METRIC_MARKER_3_Inc`, `N_STANDBY_ALT_METRIC_MARKER_3_Set`
- `N_STANDBY_ALT_METRIC_MARKER_4`: `N_STANDBY_ALT_METRIC_MARKER_4`, `N_STANDBY_ALT_METRIC_MARKER_4_Dec`, `N_STANDBY_ALT_METRIC_MARKER_4_Inc`, `N_STANDBY_ALT_METRIC_MARKER_4_Set`
- `N_STANDBY_ASI_MARKER_1`: `N_STANDBY_ASI_MARKER_1`, `N_STANDBY_ASI_MARKER_1_Dec`, `N_STANDBY_ASI_MARKER_1_Inc`, `N_STANDBY_ASI_MARKER_1_Set`
- `N_STANDBY_ASI_MARKER_2`: `N_STANDBY_ASI_MARKER_2`, `N_STANDBY_ASI_MARKER_2_Dec`, `N_STANDBY_ASI_MARKER_2_Inc`, `N_STANDBY_ASI_MARKER_2_Set`
- `N_STANDBY_ASI_MARKER_3`: `N_STANDBY_ASI_MARKER_3`, `N_STANDBY_ASI_MARKER_3_Dec`, `N_STANDBY_ASI_MARKER_3_Inc`, `N_STANDBY_ASI_MARKER_3_Set`
- `N_STANDBY_ASI_MARKER_4`: `N_STANDBY_ASI_MARKER_4`, `N_STANDBY_ASI_MARKER_4_Dec`, `N_STANDBY_ASI_MARKER_4_Inc`, `N_STANDBY_ASI_MARKER_4_Set`
