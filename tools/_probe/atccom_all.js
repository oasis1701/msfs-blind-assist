(function(){ try {
  var A=window.__MSFSBA_A380; if(!A) return 'no agent';
  if(!A.navigateUri) return 'no navigateUri on agent';
  var pages=['atccom/connect','atccom/request','atccom/report-modify/position','atccom/msg-record','atccom/d-atis/list','atccom/emer'];
  var out=[];
  for (var p=0;p<pages.length;p++){
    var r=A.navigateUri(pages[p]);
    var o=JSON.parse(A.scrape());
    var els=(o.elements||[]);
    var txt=[];
    for (var i=0;i<els.length && i<12;i++){ var e=els[i]; txt.push('['+(e.kind||'?')+'] '+(e.text!=null?e.text:'')); }
    out.push('### '+pages[p]+' nav='+r+' title="'+o.title+'" n='+els.length+'\n   '+txt.join(' | '));
  }
  return out.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()