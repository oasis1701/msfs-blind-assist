// Read-only audit helper: inventories the Synaptic A220 cockpit behaviour XMLs,
// locates every control node in the cockpit glTF, and cross-references MSFSBA's
// registered panel controls. Data paths come in as arguments; nothing is executed
// from the aircraft package (XML/JSON are parsed as text only).
// usage: node tools/a220-gen/cockpit_inventory.js <cockpitDir> <gltfPath> <outDir>
// Outputs vendored as reference/cockpit-controls.csv and reference/cockpit-layout.txt
// (Synaptic A220 v1.0.10, 2026-10-12); re-run after an aircraft update.
'use strict';
const fs = require('fs');
const path = require('path');
const [cockpitDir, gltfPath, outDir] = process.argv.slice(2);

// ---------- 1. XML inventory ------------------------------------------------
const files = [
  'Overhead/Air.xml', 'Overhead/AntiIce.xml', 'Overhead/APU.xml', 'Overhead/Cooling.xml',
  'Overhead/Electrical.xml', 'Overhead/Fuel.xml', 'Overhead/Fire.xml', 'Overhead/FlightControls.xml',
  'Overhead/Heat.xml', 'Overhead/Hydraulics.xml', 'Overhead/Lights.xml', 'Overhead/Misc.xml',
  'Overhead/Pressurization.xml', 'Overhead/TAWS.xml', 'Overhead/Wipers.xml',
  'Glareshield/Autopilot.xml', 'Glareshield/CTP.xml', 'Glareshield/Gear.xml', 'Glareshield/ISI.xml',
  'Glareshield/Panel.xml',
  'Pedestal/ACP.xml', 'Pedestal/CCP.xml', 'Pedestal/CockpitDoor.xml', 'Pedestal/FlightControls.xml',
  'Pedestal/Lights.xml', 'Pedestal/MKP.xml', 'Pedestal/RSP.xml', 'Pedestal/Throttles.xml',
  'Misc/Breakers.xml', 'Misc/Interactive.xml', 'Misc/Lights.xml', 'Misc/Maintenance.xml', 'Misc/Sidestick.xml',
];
// Instances from A220_Cockpit.xml
const instances = {
  'Glareshield/CTP.xml': [{ INDEX: '1', SIDE: 'L' }, { INDEX: '2', SIDE: 'R' }],
  'Glareshield/Panel.xml': [{ INDEX: '1', SIDE: 'L' }, { INDEX: '2', SIDE: 'R' }],
  'Pedestal/ACP.xml': [{ INDEX: '1' }, { INDEX: '2' }, { INDEX: '3' }],
  'Pedestal/CCP.xml': [{ INDEX: '1' }, { INDEX: '2' }],
  'Pedestal/MKP.xml': [{ INDEX: '1' }, { INDEX: '2' }],
  'Misc/Lights.xml': [{ INDEX: '1', SIDE: 'L' }, { INDEX: '2', SIDE: 'R' }],
  'Misc/Sidestick.xml': [{ SIDE: 'CPT' }, { SIDE: 'FO' }],
};
const sub = {
  A220_CTP_Button: { node: 'CTP_#ID#_#INDEX#', VAR: 'H:A220_CTP_#ID#_#INDEX#', base: 'A220_ButtonMomentary' },
  A220_ACP_Button: { node: 'ACP_PUSH_#ID#_#INDEX#', VAR: '', base: 'A220_ButtonMomentary' },
  A220_ACP_Knob: { node: 'ACP_KNOB_#ID#_#INDEX#', VAR: '', base: 'A220_KnobPotentiometer+push' },
  A220_CCP_Button: { node: 'CCP_#ID#_#INDEX#', VAR: 'H:A220_CCP_#ID#_#INDEX#', base: 'A220_ButtonMomentary' },
  A220_MKP_Button: { node: 'KBD_#ID#_#INDEX#', VAR: 'H:A220_KBD_#ID#_#INDEX#', base: 'A220_ButtonMomentary' },
  A220_Breaker: { node: null, VAR: 'L:A22X Circuit Breaker #ID#', base: 'A220_Button(latched breaker)' },
};
const kindOf = (tpl, p) => {
  const t = tpl;
  if (/Breaker/.test(t)) return ['circuit breaker', '2 (in/pulled)'];
  if (t === 'A220_ButtonPBA' || t === 'A220_Button') {
    if (p.IS_HELD) return ['pushbutton (held while pressed)', 'momentary'];
    if (p.IS_LATCHED) return ['latched korry pushbutton', '2'];
    return ['momentary korry pushbutton (writes 1, aircraft clears)', 'momentary'];
  }
  if (/ButtonMomentary/.test(t)) return ['momentary pushbutton', 'momentary'];
  if (t === 'A220_KnobRing') return ['latched push ring', '2'];
  if (t === 'A220_KnobBattery') return ['rotary 2-pos', '2'];
  if (/KnobPotentiometer/.test(t)) return ['potentiometer knob', 'continuous 0-1 (29 steps)'];
  if (t === 'A220_KnobSmall2State') return ['rotary selector', '2'];
  if (/KnobSmall3|KnobLarge3/.test(t)) return ['rotary selector', '3'];
  if (/KnobLarge4StateLooping/.test(t)) return ['rotary selector (looping)', '4'];
  if (/Knob(Small|Large)4State/.test(t)) return ['rotary selector', '4'];
  if (t === 'A220_KnobContinuous') return ['endless knob (inc/dec)' + (p.BTN_VAR ? ' + push' : ''), 'endless'];
  if (t === 'A220_Knob') return ['rotary selector', p.NUM_STATES || '?'];
  if (t === 'A220_Knob3State') return ['rotary (no var: animation only)', '3'];
  if (t === 'A220_Switch') return ['toggle switch' + (p.IS_GUARDED ? ' (guarded)' : ''), p.NUM_STATES || '2'];
  if (t === 'A220_Lever') return ['lever', p.NUM_STATES ? p.NUM_STATES + ' detents' : 'continuous'];
  if (t === 'A220_Throttle') return ['thrust lever', 'axis'];
  if (t === 'A220_AnimateToggle') return ['cosmetic toggle (animation)', '2'];
  if (t === 'A220_AnimateDragX') return ['cosmetic drag (animation)', 'drag'];
  if (t === 'A220_Clickspot') return ['clickspot', 'momentary'];
  return [t, '?'];
};
const tag = (body, name) => {
  const m = body.match(new RegExp('<' + name + '>([\\s\\S]*?)</' + name + '>'));
  return m ? m[1].replace(/\s+/g, ' ').trim() : '';
};
const subst = (s, ctx) => s.replace(/#(\w+)#/g, (m, k) => (ctx[k] !== undefined ? ctx[k] : m));
const controls = [];
for (const rel of files) {
  const xml = fs.readFileSync(path.join(cockpitDir, rel), 'utf8');
  const area = rel.split('/')[0];
  const insts = instances[rel] || [{}];
  const re = /<UseTemplate Name="([^"]+)">([\s\S]*?)<\/UseTemplate>/g;
  let m;
  const blocks = [];
  while ((m = re.exec(xml))) blocks.push([m[1], m[2], m.index]);
  for (const ctx of insts) {
    for (const [tpl, body] of blocks) {
      if (/^ASOBO_/.test(tpl) || tpl === 'A220_Emissive' || tpl === 'A220_Wiper_Anim_Template') continue;
      const p = {};
      for (const k of ['NODE_ID', 'VAR', 'VAR_READ', 'VAR_WRITE', 'VAR_INC', 'VAR_DEC', 'BTN_VAR', 'BTN_TOGGLE', 'ID', 'IS_GUARDED', 'IS_LATCHED', 'IS_HELD', 'NUM_STATES', 'INC_CODE', 'DEC_CODE', 'CODE', 'POSITION_VAR', 'SEQ1_CODE', 'SEQ2_CODE']) p[k] = tag(body, k);
      let tplName = tpl, node = p.NODE_ID, v = p.VAR;
      const local = Object.assign({}, ctx, p.ID ? { ID: p.ID } : {});
      if (sub[tpl]) {
        node = sub[tpl].node ? sub[tpl].node : p.NODE_ID;
        if (!v) v = sub[tpl].VAR;
        tplName = tpl;
      }
      if (!node) continue;
      if (/#ID#/.test(node) && !local.ID) continue; // template definition body
      node = subst(node, local); v = subst(v, local);
      const btn = subst(p.BTN_VAR, local);
      const inc = subst(p.VAR_INC || p.INC_CODE, local), dec = subst(p.VAR_DEC || p.DEC_CODE, local);
      const rd = subst(p.VAR_READ, local), wr = subst(p.VAR_WRITE || p.CODE, local);
      let [kind, pos] = kindOf(tplName === 'A220_ACP_Knob' ? 'A220_KnobPotentiometer' : tplName, p);
      if (tplName === 'A220_ACP_Knob') kind = 'volume knob + push (Rx toggle)';
      if (sub[tpl] && sub[tpl].base && tplName !== 'A220_ACP_Knob' && tplName !== 'A220_Breaker') { kind = 'momentary pushbutton'; pos = 'momentary'; }
      if (tplName === 'A220_Breaker') { kind = 'circuit breaker'; pos = '2 (in/pulled)'; }
      const guarded = p.IS_GUARDED ? 'guarded' : '';
      const lamp = [p.SEQ1_CODE, p.SEQ2_CODE].map(s => subst(s, local)).filter(s => s && s !== '0').join(' | ');
      controls.push({ file: rel, area, node, template: tplName, kind, positions: pos, guarded,
        var: v, read: rd, write: wr, inc, dec, btn, lamp });
    }
  }
}
// Oxygen (ASOBO components)
for (const side of ['CPT', 'FO']) {
  controls.push({ file: 'Pedestal/Oxygen.xml', area: 'Pedestal', node: side + '_OXY_FLOW_TEST', template: 'ASOBO_GT_Push_Button', kind: 'momentary (held)', positions: 'momentary', guarded: '', var: 'L:INI_OXYTEST_BLINKER_' + side, read: '', write: '', inc: '', dec: '', btn: '', lamp: '' });
  controls.push({ file: 'Pedestal/Oxygen.xml', area: 'Pedestal', node: side + '_OXY_N1', template: 'ASOBO_GT_Switch_Code', kind: 'toggle (N/100%)', positions: '2', guarded: '', var: 'L:INI_OXYTEST_' + side + '_N', read: '', write: '', inc: '', dec: '', btn: '', lamp: '' });
  controls.push({ file: 'Pedestal/Oxygen.xml', area: 'Pedestal', node: side + '_OXY_MASK_TEST', template: 'ASOBO_GT_Push_Button', kind: 'momentary (held) EMER test', positions: 'momentary', guarded: '', var: 'L:INI_OXYTEST_EMER_' + side, read: '', write: '', inc: '', dec: '', btn: '', lamp: '' });
}

// ---------- 2. glTF positions ------------------------------------------------
const g = JSON.parse(fs.readFileSync(gltfPath, 'utf8'));
const nodes = g.nodes;
const mul = (a, b) => { const r = new Array(16).fill(0); for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) for (let k = 0; k < 4; k++) r[j * 4 + i] += a[k * 4 + i] * b[j * 4 + k]; return r; };
const trs = n => {
  if (n.matrix) return n.matrix.slice();
  const [tx, ty, tz] = n.translation || [0, 0, 0];
  const [qx, qy, qz, qw] = n.rotation || [0, 0, 0, 1];
  const [sx, sy, sz] = n.scale || [1, 1, 1];
  const r = [1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy + qz * qw), 2 * (qx * qz - qy * qw), 0,
    2 * (qx * qy - qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz + qx * qw), 0,
    2 * (qx * qz + qy * qw), 2 * (qy * qz - qx * qw), 1 - 2 * (qx * qx + qy * qy), 0, 0, 0, 0, 1];
  r[0] *= sx; r[1] *= sx; r[2] *= sx; r[4] *= sy; r[5] *= sy; r[6] *= sy; r[8] *= sz; r[9] *= sz; r[10] *= sz;
  r[12] = tx; r[13] = ty; r[14] = tz; return r;
};
const xf = (m, p) => [m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12], m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13], m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14]];
const world = new Array(nodes.length);
const parent = new Array(nodes.length).fill(-1);
nodes.forEach((n, i) => (n.children || []).forEach(c => (parent[c] = i)));
const roots = (g.scenes && g.scenes[g.scene || 0] ? g.scenes[g.scene || 0].nodes : nodes.map((_, i) => i).filter(i => parent[i] < 0));
const walk = (i, pm) => { world[i] = mul(pm, trs(nodes[i])); (nodes[i].children || []).forEach(c => walk(c, world[i])); };
const I = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
roots.forEach(r => walk(r, I));
const meshCenter = (i) => {
  const n = nodes[i];
  if (n.mesh === undefined) return null;
  let mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity], ok = false;
  for (const pr of g.meshes[n.mesh].primitives) {
    const a = g.accessors[pr.attributes.POSITION];
    if (!a || !a.min) continue; ok = true;
    for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], a.min[k]); mx[k] = Math.max(mx[k], a.max[k]); }
  }
  if (!ok) return null;
  return xf(world[i], [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2]);
};
const centerOf = (i, depth = 0) => {
  const c = meshCenter(i); if (c) return c;
  const kids = (nodes[i].children || []).map(k => centerOf(k, depth + 1)).filter(Boolean);
  if (kids.length) return [0, 1, 2].map(k => kids.reduce((s, p) => s + p[k], 0) / kids.length);
  return world[i] ? xf(world[i], [0, 0, 0]) : null;
};
const byName = new Map();
nodes.forEach((n, i) => { if (n.name && !byName.has(n.name)) byName.set(n.name, i); });
for (const c of controls) {
  const i = byName.get(c.node);
  const p = i !== undefined ? centerOf(i) : null;
  c.x = p ? p[0].toFixed(3) : ''; c.y = p ? p[1].toFixed(3) : ''; c.z = p ? p[2].toFixed(3) : '';
}

// ---------- 3. MSFSBA cross-reference ------------------------------------------
// MSFSBA panel -> [key, matching cockpit node(s) or var, note]
const msfsba = {
  'Overhead/Electrical': [['A22X_BATTERY_1', 'OVH_BATT_1'], ['A22X_BATTERY_2', 'OVH_BATT_2'], ['A22X_EXT_PWR', 'OVH_EXT_PWR'], ['A22X_GPU_AVAIL', null, 'EFB ground equipment (INI_GPU_AVAIL), not a cockpit control'], ['A22X_L_GEN_OFF', 'OVH_L_GEN'], ['A22X_R_GEN_OFF', 'OVH_R_GEN'], ['A22X_APU_GEN_OFF', 'OVH_APU_GEN'], ['A22X_CABIN_POWER_OFF', 'OVH_ELEC_CABIN_PWR'], ['A22X_BUS_ISOLATION', 'OVH_ELEC_BUS_ISOL'], ['A22X_RAT_GEN', 'OVH_ELEC_RAT_GEN'], ['A22X_L_GEN_DISC', 'OVH_L_DISC'], ['A22X_R_GEN_DISC', 'OVH_R_DISC']],
  'Overhead/APU': [['A22X_APU_SWITCH', 'OVH_APU_START']],
  'Overhead/Hydraulics': [['A22X_PTU', 'OVH_HYD_PTU'], ['A22X_ACMP_2B', 'OVH_HYD_2B'], ['A22X_ACMP_3A', 'OVH_HYD_3A'], ['A22X_ACMP_3B', 'OVH_HYD_3B'], ['A22X_HYD_1_SOV', 'OVH_HYD_1_SOV'], ['A22X_HYD_2_SOV', 'OVH_HYD_2_SOV']],
  'Overhead/Fuel': [['A22X_L_BOOST_PUMP', 'OVH_FUEL_L_BOOST'], ['A22X_R_BOOST_PUMP', 'OVH_FUEL_R_BOOST'], ['A22X_MANUAL_TRANSFER', 'OVH_FUEL_XFR'], ['A22X_GRAVITY_TRANSFER', 'OVH_FUEL_GRAV']],
  'Overhead/Air Conditioning and Bleed': [['A22X_L_BLEED_OFF', 'OVH_AIR_L_BLEED'], ['A22X_APU_BLEED_OFF', 'OVH_AIR_APU_BLEED'], ['A22X_R_BLEED_OFF', 'OVH_AIR_R_BLEED'], ['A22X_CROSSBLEED', 'OVH_AIR_XBLEED'], ['A22X_L_PACK_OFF', 'OVH_AIR_L_PACK'], ['A22X_R_PACK_OFF', 'OVH_AIR_R_PACK'], ['A22X_PACK_FLOW', 'OVH_AIR_PACK_FLOW'], ['A22X_RAM_AIR', 'OVH_AIR_RAM'], ['A22X_TRIM_AIR_OFF', 'OVH_AIR_TRIM'], ['A22X_RECIRC_OFF', 'OVH_AIR_RECIRC'], ['A22X_MANUAL_TEMP', 'OVH_AIR_MAN_TEMP'], ['A22X_COCKPIT_AIR', 'OVH_AIR_COCKPIT'], ['A22X_FWD_CABIN_AIR', 'OVH_AIR_CABIN_FWD'], ['A22X_AFT_CABIN_AIR', 'OVH_AIR_CABIN_AFT'], ['A22X_FWD_CARGO_AIR', 'OVH_AIR_CARGO_FWD'], ['A22X_AFT_CARGO_AIR', 'OVH_AIR_CARGO_AFT']],
  'Overhead/Anti-Ice and Heat': [['A22X_L_COWL_AI', 'OVH_ICE_L_COWL'], ['A22X_R_COWL_AI', 'OVH_ICE_R_COWL'], ['A22X_WING_AI', 'OVH_ICE_WING'], ['A22X_PROBE_HEAT', 'OVH_PROBE_HEAT', 'cockpit pb is MOMENTARY (no IS_LATCHED: ground test pulse); MSFSBA shows it as an Auto/On combo'], ['A22X_L_WINDOW_HEAT_OFF', 'OVH_HEAT_L_SIDE'], ['A22X_R_WINDOW_HEAT_OFF', 'OVH_HEAT_R_SIDE'], ['A22X_L_WSHLD_HEAT_OFF', 'OVH_HEAT_L_WSHLD'], ['A22X_R_WSHLD_HEAT_OFF', 'OVH_HEAT_R_WSHLD']],
  'Overhead/Pressurization': [['A22X_MAN_PRESS', 'OVH_AUTO_PRESS'], ['A22X_MANUAL_RATE', 'OVH_MAN_RATE'], ['A22X_EMER_DEPRESS', 'OVH_EMER_PRESS'], ['A22X_DITCHING', 'OVH_DITCHING'], ['A22X_PAX_OXYGEN', 'OVH_PAX_OXY']],
  'Overhead/Fire and Emergency': [['A22X_L_ENG_FIRE', 'OVH_FIRE_L', 'cockpit pb is MOMENTARY (no IS_LATCHED); MSFSBA shows Normal/Pushed combo'], ['A22X_R_ENG_FIRE', 'OVH_FIRE_R', 'same'], ['A22X_ELT', 'OVH_ELT'], ['A22X_EVAC', 'OVH_EVAC_CMD']],
  'Overhead/Flight Controls and TAWS': [['A22X_PFCC_1_OFF', 'OVH_PFCC_1'], ['A22X_PFCC_2_OFF', 'OVH_PFCC_2'], ['A22X_PFCC_3_OFF', 'OVH_PFCC_3'], ['A22X_AURAL_INHIBIT', 'OVH_AURAL_WARN'], ['A22X_TAWS_GEAR_INHIBIT', 'OVH_TAWS_GEAR'], ['A22X_TAWS_TERRAIN_INHIBIT', 'OVH_TAWS_TERR'], ['A22X_TAWS_FLAPS_INHIBIT', 'OVH_TAWS_FLAP'], ['A22X_TAWS_GS_INHIBIT', 'OVH_TAWS_GS']],
  'Eyebrow/Exterior Lights': [['A22X_NAV_LIGHTS', 'OVH_LTS_NAV'], ['A22X_BEACON_LIGHTS', 'OVH_LTS_BEACON'], ['A22X_STROBE_LIGHTS', 'OVH_LTS_STROBE'], ['A22X_LOGO_LIGHTS', 'OVH_LTS_LOGO'], ['A22X_WING_INSP_LIGHTS', 'OVH_LTS_WINGS'], ['A22X_TAXI_LIGHTS', 'OVH_LTS_TAXI'], ['A22X_L_LANDING_LIGHTS', 'OVH_LTS_LDG_L'], ['A22X_R_LANDING_LIGHTS', 'OVH_LTS_LDG_R'], ['A22X_NOSE_LANDING_LIGHTS', 'OVH_LTS_LDG_NOSE']],
  'Eyebrow/Signs and Interior': [['A22X_EMERGENCY_LIGHTS', 'OVH_EMER_LTS'], ['A22X_SEAT_BELT_LIGHTS', 'OVH_LTS_PAX'], ['A22X_NO_PED_LIGHTS', 'OVH_LTS_NO_PED'], ['A22X_DOME_LIGHTS', 'OVH_KORRY_DOME'], ['A22X_ANNUN_LIGHTS', 'OVH_ANNUN_BRT'], ['A22X_LAMP_TEST', null, 'L:A22X Lamp Test is read by every lamp emissive but NO cockpit node writes it']],
  'Glareshield/CTP Captain': [['A22X_L_BARO_STD', 'CTP_BARO_STD_1'], ['A22X_L_NAV_SOURCE', 'CTP_NAV_1'], ['A22X_L_CROSSTUNE', 'CTP_ACROSS_1']],
  'Glareshield/CTP First Officer': [['A22X_R_BARO_STD', 'CTP_BARO_STD_2'], ['A22X_R_NAV_SOURCE', 'CTP_NAV_2'], ['A22X_R_CROSSTUNE', 'CTP_ACROSS_2']],
  'Glareshield/Warnings and Chrono': [['A22X_MASTER_CW_CANCEL', 'FCU_MASTER_CAUTION_1', 'cockpit press WRITES 1; MSFSBA writes 0'], ['A22X_L_CHRONO', 'FCU_CHRONO_1'], ['A22X_R_CHRONO', 'FCU_CHRONO_2']],
  'Glareshield/Flight Directors': [['A22X_L_FD', 'FCU_FD_1'], ['A22X_R_FD', 'FCU_FD_2']],
  'Pedestal/Engines and Ignition': [['A22X_ENG_MASTER_1', 'ENG_MASTER_1'], ['A22X_ENG_MASTER_2', 'ENG_MASTER_2'], ['A22X_ENG_START_MODE', 'ENG_MASTER_MODE'], ['A22X_CONT_IGNITION', 'CONT_IGNITION'], ['A22X_PARKING_BRAKE', 'PARKING_BRAKE']],
  'Pedestal/Trim': [['A22X_STAB_TRIM_SET', null, 'no cockpit node (sidestick trim switches are not in the XML); ELEV_TRIM_UP/DN walk'], ['RUDDER_TRIM_LEFT', 'Rudder_Trim'], ['RUDDER_TRIM_RIGHT', 'Rudder_Trim']],
  'Main Panel/Gear and Brakes': [['A22X_GEAR_LEVER', 'LDG_LEVER'], ['A22X_ALTERNATE_GEAR', 'LDG_ALTN_GEAR'], ['A22X_AUTOBRAKE', 'LDG_AUTO_BRK', 'cockpit knob has 5 states; MSFSBA labels 4 (no RTO)'], ['A22X_NOSE_STEER_OFF', 'NOSE_STEER'], ['A22X_ALTERNATE_BRAKE', 'ALTN_BRAKE'], ['A22X_GEAR_AURAL_CANCEL', 'LDG_AURAL']],
  'Main Panel/Flaps and Speedbrake': [['A22X_FLAP_LEVER', 'FCTL_FLAPS_LEVER'], ['A22X_ALTERNATE_FLAP', 'ALT_FLAP_TOGGLE'], ['A22X_SPOILERS_RETRACT', 'FCTL_SPD_BRK_LEVER'], ['A22X_SPOILERS_DEPLOY', 'FCTL_SPD_BRK_LEVER']],
  'Radios/COM Radios': [['A22X_COM1_STBY_SET', 'CTP_TUNE_LARGE_1', 'stock event (the CTP tunes via H: knobs)'], ['A22X_COM1_SWAP', null, 'stock event; cockpit swap is a CTP soft key'], ['A22X_COM2_STBY_SET', null, 'stock event'], ['A22X_COM2_SWAP', null, 'stock event']],
  'Radios/NAV Radios': [['A22X_NAV1_STBY_SET', null, 'stock event'], ['A22X_NAV2_STBY_SET', null, 'stock event']],
  'Radios/Transponder': [['TRANSPONDER_CODE_SET', null, 'stock XPNDR_SET'], ['A22X_XPDR_IDENT', 'CTP_IDENT_1']],
  'Utilities/Simulation Options': [['A22X_TOD_PAUSE', null, 'sim option'], ['A22X_TOD_DISTANCE_SET', null, 'sim option']],
};
const nodeToMsfsba = new Map();
for (const [panel, rows] of Object.entries(msfsba)) for (const [key, node, note] of rows) if (node) {
  if (!nodeToMsfsba.has(node)) nodeToMsfsba.set(node, []);
  nodeToMsfsba.get(node).push(panel + ':' + key);
}
// coverage outside panels (dialogs / windows / hotkeys / FMS form)
const elsewhere = {
  FCU_AP: 'Ctrl+P window + AP hotkey', FCU_AT: 'Ctrl+P window + A/T hotkey', FCU_XFR: 'Ctrl+P window (FG Source Transfer)', FCU_FD_1: 'panel', FCU_FD_2: 'panel',
  SIDE_STICK_DISCO_CPT: 'Ctrl+P AP Disconnect (AUTOPILOT_OFF)', SIDE_STICK_DISCO_FO: 'Ctrl+P AP Disconnect (AUTOPILOT_OFF)',
  FCU_SPD_PUSH: 'Ctrl+S dialog (walk + Kt/Mach)', FCU_SPD_MODE: 'Ctrl+S dialog (FMS/Manual ring)', FCU_HDG_SYNC: 'Ctrl+H dialog (walk + sync)', FCU_HDG: 'Ctrl+H/Ctrl+P', FCU_NAV: 'Ctrl+H/Ctrl+P',
  FCU_APPR: 'Ctrl+H/Ctrl+P', FCU_BANK: 'Ctrl+H dialog (half bank)', FCU_ALT_PUSH: 'Ctrl+A dialog (walk + fine/coarse)', FCU_ALT_MODE: 'Ctrl+A dialog (ft/m ring)', FCU_FLC: 'Ctrl+A/Ctrl+P', FCU_ALT: 'Ctrl+A/Ctrl+P',
  FCU_VNAV: 'Ctrl+A/Ctrl+P', FCU_VS_SEL: 'Ctrl+V dialog', FCU_VS: 'Ctrl+V/Ctrl+P', FCU_FPA: 'Ctrl+V/Ctrl+P',
  CTP_BARO_STD_1: 'Ctrl+B dialog + panel', CTP_BARO_MODE_1: 'read-only (HPA) — no write', CTP_FMS_1: 'FMS window opens via H:A220_CTP_FMS_1',
  ENG_THROTTLE_LEFT_TOGA: 'Ctrl+P TOGA', ENG_THROTTLE_RIGHT_TOGA: 'Ctrl+P TOGA', TOGA_BIND_1: 'Ctrl+P TOGA', TOGA_BIND_2: 'Ctrl+P TOGA',
  ENG_THROTTLE_ATHR_DISC_LEFT: 'Ctrl+P A/T Disconnect', ENG_THROTTLE_ATHR_DISC_RIGHT: 'Ctrl+P A/T Disconnect',
  KBD_EXEC_1: 'FMS window', KBD_CNC_1: 'FMS window',
};
for (const c of controls) {
  const hit = nodeToMsfsba.get(c.node);
  c.msfsba = hit ? hit.join(' ; ') : '';
  if (!hit) {
    if (elsewhere[c.node]) c.msfsba = '(not a panel) ' + elsewhere[c.node];
    else if (/^KBD_/.test(c.node)) c.msfsba = '(not a panel) FMS window drives H:A220_KBD_*_1';
  }
}

// ---------- output -------------------------------------------------------------
const cols = ['area', 'file', 'node', 'kind', 'positions', 'guarded', 'var', 'read', 'write', 'inc', 'dec', 'btn', 'lamp', 'x', 'y', 'z', 'msfsba'];
const esc = s => '"' + String(s === undefined ? '' : s).replace(/"/g, '""') + '"';
fs.writeFileSync(path.join(outDir, 'controls.csv'), [cols.join(',')].concat(controls.map(c => cols.map(k => esc(c[k])).join(','))).join('\n'));
// MSFSBA keys with no cockpit node
const nodeSet = new Set(controls.map(c => c.node));
const orphan = [];
for (const [panel, rows] of Object.entries(msfsba)) for (const [key, node, note] of rows) if (!node || !nodeSet.has(node)) orphan.push([panel, key, node || '', note || '']);
fs.writeFileSync(path.join(outDir, 'msfsba_no_cockpit_node.csv'), orphan.map(r => r.map(esc).join(',')).join('\n'));
console.log('controls', controls.length, 'located', controls.filter(c => c.x !== '').length);
console.log('unlocated:', controls.filter(c => c.x === '').map(c => c.node).join(' '));
// emit a compact positional dump for analysis
fs.writeFileSync(path.join(outDir, 'positions.json'), JSON.stringify(controls.map(c => ({ n: c.node, f: c.file, x: +c.x, y: +c.y, z: +c.z, k: c.kind, v: c.var || c.read || c.write || c.inc, m: c.msfsba })), null, 0));
