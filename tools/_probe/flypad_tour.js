(function(){
  if(!window.__MSFSBA_FLYPAD) return 'NO_AGENT';
  var p = __MSFSBA_FLYPAD.ping();
  var s;
  try { s = JSON.parse(__MSFSBA_FLYPAD.scrape()); }
  catch(e){ return JSON.stringify({ping:p, scrapeError:String(e)}); }
  var els = (s.elements||[]).map(function(e){
    return { i:e.idx, k:e.kind, tag:e.tag, role:e.role, t:e.text, v:e.value,
             ct:e.controlType, cl:e.clickable?1:0, lv:e.level, dis:e.disabled?1:0,
             live:e.live||'', opt:(e.options&&e.options.length)?e.options:undefined };
  });
  return JSON.stringify({ping:p, ok:s.ok, page:s.page, count:els.length, els:els});
})()
