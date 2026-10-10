// Prints every interaction node the Skyward Citation Sovereign+ model declares — the NODE_ID
// (else ID / ANIM_NAME) of every UseTemplate of an interaction template in SOV_Logic.xml,
// SOV_Animations.xml and SOV_Circuit_Breakers.xml — one per line, sorted. The interaction-
// surface test compares this list with the definition's coverage map.
// Run: node tools/c680-gen/enumerate-controls.js   (C680_PKG overrides the package folder)
const fs = require('fs'), path = require('path');
const pkg = process.env.C680_PKG || path.join(process.env.LOCALAPPDATA,
  'Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache/Packages/Community/skyward-cessna-citation-c680');
const model = path.join(pkg, 'SimObjects/Airplanes/cessna-citation-c680/model');
const INTERACTION = /^(ASOBO_GT_Switch|ASOBO_GT_Push_Button|ASOBO_GT_Toggle|ASOBO_Interaction_Base_Template|SW_SOV_.*(Push|Switch|Knob|Lever|Button|Handle|Toggle)|SW_SOV_GT_Switch|SKYWARD_GT_Circuit_Breaker|ASOBO_ELECTRICAL_|ASOBO_HANDLING_|ASOBO_AUTOPILOT_|ASOBO_SAFETY_|ASOBO_FUEL_|ASOBO_ENGINE_|ASOBO_LIGHTING_|ASOBO_INSTRUMENT_|WT_G3000_|SW_Sovereign_)/;
const nodes = new Set();
for (const file of ['SOV_Logic.xml', 'SOV_Animations.xml', 'SOV_Circuit_Breakers.xml']) {
  const xml = fs.readFileSync(path.join(model, file), 'utf8');
  for (const m of xml.matchAll(/<UseTemplate Name="([^"]+)"\s*(\/>|>([\s\S]*?)<\/UseTemplate>)/g)) {
    const name = m[1]; if (!INTERACTION.test(name)) continue;
    const body = m[3] || '';
    const id = (body.match(/<NODE_ID>([^<#]+)<\/NODE_ID>/) || body.match(/<ANIM_NAME>([^<#]+)<\/ANIM_NAME>/) || body.match(/<ID>([^<#]+)<\/ID>/) || [])[1];
    nodes.add(id ? id.trim() : '(' + name + ')');
  }
}
for (const n of [...nodes].sort()) console.log(n);
console.error('nodes', nodes.size);
