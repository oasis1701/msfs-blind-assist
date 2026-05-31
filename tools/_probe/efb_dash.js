(function(){ try {
  var A=window.__MSFSBA_FLYPAD; if(!A) return 'no flypad agent';
  var o=JSON.parse(A.scrape()); var els=o.items||o.elements||[];
  for(var i=0;i<els.length;i++){ var e=els[i]; var t=(e.text||e.label||'');
    if(t==='Dashboard' && (e.kind==='link'||e.role==='link')){
      var ix=(e.aidx!=null?e.aidx:(e.idx!=null?e.idx:i));
      return 'back to Dashboard aidx='+ix+' -> '+A.clickElement(ix);
    }
  }
  return 'Dashboard link not found';
} catch(e){ return 'ERR '+e; } })()