(function(){ try {
  var mfd=document.querySelector('a380x-mfd'); if(!mfd||!mfd.fsInstrument) return 'no mfd';
  var fi=mfd.fsInstrument;
  var u=fi.uiService; if(!u) return 'no uiService';
  // Walk each ATCCOM page, navigate, and report what the scrape agent returns.
  var pages=['atccom/connect','atccom/request','atccom/d-atis/list','atccom/msg-record'];
  var out=[];
  for (var i=0;i<pages.length;i++){
    try { u.navigateTo(pages[i]); } catch(e){ out.push(pages[i]+' NAV-ERR '+e); continue; }
    var au=(u.activeUri&&u.activeUri.get)?u.activeUri.get():null;
    out.push('=== '+pages[i]+' -> active='+(au?au.uri:'?'));
  }
  return out.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()