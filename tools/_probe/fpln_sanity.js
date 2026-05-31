(function(){ try {
  var A=window.__MSFSBA_A380; if(!A) return 'no agent';
  if(!A.buildFplnLines) return 'no buildFplnLines';
  A.navigateUri('fms/active/f-pln');
  var o=JSON.parse(A.scrape());
  if(!o.ok) return 'scrape err '+o.error;
  var els=o.elements||[]; var lines=['title='+o.title+' n='+els.length];
  for(var i=0;i<els.length && i<14;i++){ var e=els[i]; lines.push('['+(e.kind||'?')+'] '+(e.text!=null?e.text:'')); }
  return lines.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()