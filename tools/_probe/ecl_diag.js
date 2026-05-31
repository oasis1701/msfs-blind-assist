(function(){ try {
  var out=[];
  out.push('EclLine count='+document.querySelectorAll('.EclLine').length);
  // C/L overlay state
  try { out.push('A32NX_BTN_CL='+SimVar.GetSimVarValue('L:A32NX_BTN_CL','number')); } catch(e){}
  try { out.push('CHECKLIST_SHOWN?='+SimVar.GetSimVarValue('L:A32NX_FWS_NORMAL_CHECKLISTS_VISIBLE','number')); } catch(e){}
  // Search the DOM for any class containing Ecl / hecklist / hklist
  var all=document.getElementsByTagName('*'); var seen={};
  for (var i=0;i<all.length;i++){
    var c=(all[i].getAttribute&&all[i].getAttribute('class'))||'';
    if(/ecl|hecklist|checklist|ChecklistItem|Cl_/i.test(c)){
      c.split(/\s+/).forEach(function(k){ if(/ecl|hecklist|cklist/i.test(k)) seen[k]=(seen[k]||0)+1; });
    }
  }
  var ks=Object.keys(seen); out.push('classes w/ ecl/checklist: '+(ks.length?ks.slice(0,30).map(function(k){return k+'('+seen[k]+')';}).join(', '):'NONE'));
  // Root custom elements present
  var roots=['ewd-main','a380x-ewd','ewd','vcockpit'];
  var found=[]; for(var r=0;r<roots.length;r++){ if(document.querySelector(roots[r])) found.push(roots[r]); }
  out.push('roots: '+found.join(',')+' | body firstEl tags: '+(function(){var b=document.body,a=[],n=b?b.children:[];for(var j=0;j<n.length&&j<6;j++)a.push(n[j].tagName.toLowerCase());return a.join(',');})());
  return out.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()