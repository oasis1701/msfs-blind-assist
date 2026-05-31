(function(){ try {
  var A=window.__MSFSBA_A380;
  A.navigateUri('surv/controls');
  var o=JSON.parse(A.scrape()); var els=o.elements||[];
  function findRadio(txt){ for(var i=0;i<els.length;i++){ var t=(els[i].text||''); if(t.indexOf(txt)===0 && els[i].kind==='radio') return els[i].aidx!=null?els[i].aidx:els[i].idx; } return -1; }
  var elevn=findRadio('ELEVN');
  if(elevn<0) return 'ELEVN not found';
  A.clickElement(elevn);
  // small spin to let React commit
  var t0=Date.now? 0:0; for(var s=0;s<200000;s++){}
  var o2=JSON.parse(A.scrape()); var e2=o2.elements||[];
  var after=[];
  for (var j=0;j<e2.length;j++){ var t2=e2[j].text||''; if(t2.indexOf('AUTO')===0||t2.indexOf('ELEVN')===0||t2.indexOf('TILT')===0) after.push('['+e2[j].kind+'] '+t2); }
  return 'clicked ELEVN (idx '+elevn+')\nafter: '+after.join(' | ');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()