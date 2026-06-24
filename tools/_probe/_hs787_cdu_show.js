(function(){
  if(!window.__MSFSBA_HS787) return 'AGENT MISSING';
  var s = window.__MSFSBA_HS787.scrape();
  if(!s || !s.ok) return 'scrape not ok: '+JSON.stringify(s);
  if(!s.visible) return 'CDU NOT VISIBLE (rows='+(s.rows?s.rows.length:0)+')';
  var out = [];
  for(var i=0;i<s.rows.length;i++){ out.push((i<10?' ':'')+i+'|'+s.rows[i]); }
  out.push('SP|'+(s.scratchpad||''));
  return out.join('\n');
})()
