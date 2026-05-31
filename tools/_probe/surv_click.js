(function(){ try {
  var A=window.__MSFSBA_A380;
  A.navigateUri('surv/controls');
  var o=JSON.parse(A.scrape()); var els=o.elements||[];
  // find the ABV radio (TCAS display range above/below) which currently is not selected
  var target=-1, label='';
  for (var i=0;i<els.length;i++){ var t=(els[i].text||''); if(t.indexOf('ABV')===0){ target=els[i].aidx!=null?els[i].aidx:els[i].idx; label=t; break; } }
  if(target<0) return 'ABV not found';
  A.clickElement(target);
  var o2=JSON.parse(A.scrape()); var e2=o2.elements||[];
  var after=[];
  for (var j=0;j<e2.length;j++){ var t2=e2[j].text||''; if(t2.indexOf('ABV')===0||t2.indexOf('BLW')===0||t2.indexOf('STBY')===0) after.push('['+e2[j].kind+'] '+t2); }
  return 'clicked '+label+' (idx '+target+')\nafter: '+after.join(' | ');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()