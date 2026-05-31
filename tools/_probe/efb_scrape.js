(function(){ try {
  var A=window.__MSFSBA_FLYPAD; if(!A) return 'no flypad agent';
  var raw=A.scrape(); var o=JSON.parse(raw);
  var els=o.items||o.elements||[];
  var lines=['keys='+Object.keys(o).join(',')+' n='+els.length];
  for(var i=0;i<els.length && i<70;i++){ var e=els[i];
    var t=(e.text!=null?e.text:(e.label!=null?e.label:''));
    lines.push(i+': ['+(e.kind||e.role||e.type||'?')+'] '+t);
  }
  return lines.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()