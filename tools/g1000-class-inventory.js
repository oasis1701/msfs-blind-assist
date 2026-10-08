// G1000 CLASS INVENTORY - find a marking BEFORE it causes a bug.
//
// Every reading fault on this aeroplane has had the same shape: the aeroplane marks
// something with a class the agent does not know, and the agent then reads a hidden copy,
// welds two layouts together, or reports a live cursor as absent. Reading the markup you
// already know cannot find those; only enumerating what the instrument ACTUALLY uses can.
//
// Run it against a live display and it lists every class token the instrument carries,
// flagging the state-ish ones the agent has never heard of.
//
//   node tools/g1000-class-inventory.js MFD ../MSFSBlindAssist/Resources/coherent-da40-g1000-agent.js
//
// Found this way, each before it was reported: hidden-element beside hide-element (one
// letter apart, both meaning not-shown); highlight-active beside highlight-select (the
// difference between "cursor here" and "field open for editing"); input-component-value
// and number-input-active as two more cursor markings on other page families.
//
// TIP: run it once, then again after pressing a key, and diff the counts. That is how the
// cursor's second state was found - no amount of reading the DOM revealed it, because the
// class only exists while a field is open.
const {findPage} = require('./g1000-page.js');
const {evalOn} = require('./g1000-cdp.js');
const fs = require('fs');

const INV = `(function(){
 var el=document.querySelector('wtg1000-mfd')||document.querySelector('wtg1000-pfd');
 if(!el) return '{}';
 var n=el.querySelectorAll('*'),seen={};
 for(var i=0;i<n.length;i++){
   var c=n[i].className; if(typeof c!=='string')continue;
   var p=c.split(' ');
   for(var j=0;j<p.length;j++){var t=p[j].trim(); if(t) seen[t]=(seen[t]||0)+1;}
 }
 return JSON.stringify(seen);})()`;

(async () => {
  const which = process.argv[2] || 'MFD';
  const agentPath = process.argv[3];
  const p = await findPage(which);
  const seen = JSON.parse(await evalOn(p.ws, INV));
  const agent = agentPath ? fs.readFileSync(agentPath, 'utf8') : '';

  const stateish = Object.keys(seen).filter(c =>
    /highlight|select|active|cursor|focus|edit|hide|hidden|invisible|disabled/i.test(c));

  console.log(`${which}: ${Object.keys(seen).length} distinct class tokens`);
  console.log('\nSTATE-ish classes (these change what a reading MEANS):');
  for (const c of stateish.sort()) {
    const known = agent && agent.indexOf(c) >= 0;
    console.log(`   ${known ? 'known  ' : 'UNKNOWN'}  ${c}  (x${seen[c]})`);
  }
})().catch(e => console.log('ERR', e.message));
