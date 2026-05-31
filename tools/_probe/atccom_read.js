(function(){ try {
  var A=window.__MSFSBA_A380; if(!A) return 'no agent';
  var raw=A.scrape(); var o=JSON.parse(raw);
  if(!o.ok) return 'scrape err '+o.error;
  var els=o.elements||[];
  var lines=['TITLE: '+o.title+'  SCRATCH: '+o.scratchpad+'  count='+els.length];
  for (var i=0;i<els.length && i<70;i++){
    var e=els[i];
    lines.push(i+': ['+(e.kind||'?')+(e.role?'/'+e.role:'')+'] '+(e.text!=null?e.text:''));
  }
  return lines.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()