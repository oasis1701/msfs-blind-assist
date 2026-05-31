(function(){ try {
  var A=window.__MSFSBA_A380; A.navigateUri('fms/active/f-pln');
  var o=JSON.parse(A.scrape()); var els=o.elements||[];
  // does the scrape capture the ACTIVE/POSITION/SEC INDEX/DATA header tabs?
  var tabs=[];
  for(var i=0;i<els.length;i++){ var t=(els[i].text||'').toUpperCase();
    if(/^(ACTIVE|POSITION|SEC INDEX|DATA|FMS|ATCCOM|SURV|FCU BKUP)$/.test(t)) tabs.push('['+els[i].kind+'] '+els[i].text);
  }
  // also count header dropdown DOM elements directly
  var hdr=document.querySelectorAll('.mfd-header-page-select-row .mfd-page-selector, [id*="_MFD_pageSelector"]');
  return 'scraped header-tab elements: '+(tabs.length?tabs.join(' | '):'NONE')+'\nDOM pageSelector els='+hdr.length+' total scrape els='+els.length;
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()