(function(){ try {
  var A=window.__MSFSBA_A380; if(!A) return 'no agent';
  var pages=['fms/sec/index','fms/sec1/init','fms/sec1/f-pln','fms/sec1/perf'];
  var out=[];
  for (var p=0;p<pages.length;p++){
    var r=A.navigateUri(pages[p]);
    var o=JSON.parse(A.scrape());
    var els=(o.elements||[]);
    var txt=[];
    for (var i=0;i<els.length && i<22;i++){ var e=els[i]; txt.push('['+(e.kind||'?')+'] '+(e.text!=null?e.text:'')); }
    out.push('### '+pages[p]+' nav='+r+' title="'+o.title+'" n='+els.length+'\n   '+txt.join('\n   '));
  }
  return out.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()