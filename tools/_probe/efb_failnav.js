(function(){ try {
  var A=window.__MSFSBA_FLYPAD; if(!A) return 'no flypad agent';
  var o=JSON.parse(A.scrape()); var els=o.items||o.elements||[];
  for(var i=0;i<els.length;i++){ var e=els[i]; var t=(e.text||e.label||'');
    if(t==='Failures' && (e.kind==='link'||e.role==='link'||(''+t).toLowerCase()==='failures')){
      var ix=(e.aidx!=null?e.aidx:(e.idx!=null?e.idx:i));
      var r=A.clickElement(ix);
      return 'clicked Failures aidx='+ix+' -> '+r;
    }
  }
  return 'Failures link not found';
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()