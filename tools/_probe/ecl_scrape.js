(function(){ try {
  var A=window.__MSFSBA_ECL; if(!A) return 'no ECL agent';
  var o=JSON.parse(A.scrape());
  if(!o.ok) return 'scrape err '+o.error;
  var out=['shown='+o.shown+' rows='+o.rows.length];
  for(var i=0;i<o.rows.length && i<20;i++){ var r=o.rows[i]; out.push(i+': ['+r.type+(r.selected?'/SEL':'')+(r.checked?'/CHK':'')+'] "'+r.text+'"'); }
  // Also dump raw classes of first few EclLine to see filter hits
  var lines=document.querySelectorAll('.EclLine');
  out.push('--- raw classes (first 6) ---');
  for(var j=0;j<lines.length && j<6;j++){ out.push(j+': "'+(lines[j].getAttribute('class')||'')+'" txt="'+((lines[j].textContent||'').replace(/\s+/g,' ').substring(0,40))+'"'); }
  return out.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()