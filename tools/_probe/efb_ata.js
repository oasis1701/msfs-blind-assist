(function(){ try {
  var A=window.__MSFSBA_FLYPAD; if(!A) return 'no flypad agent';
  var o=JSON.parse(A.scrape()); var els=o.items||o.elements||[];
  for(var i=0;i<els.length;i++){ var e=els[i]; var t=(e.text||e.label||'');
    if(t.indexOf('Electrical Power')>=0 && (e.kind==='link'||e.role==='link')){
      var ix=(e.aidx!=null?e.aidx:(e.idx!=null?e.idx:i));
      return 'ATA24 aidx='+ix+' -> '+A.clickElement(ix);
    }
  }
  return 'ATA24 not found';
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()